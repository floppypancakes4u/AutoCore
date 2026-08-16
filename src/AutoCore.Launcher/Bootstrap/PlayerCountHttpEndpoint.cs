namespace AutoCore.Launcher.Bootstrap;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AutoCore.Utils;
using AutoCore.Utils.Logging;

/// <summary>
/// Minimal loopback HTTP endpoint exposing <c>{"players": N}</c> for the external Auto
/// Assault Crash Bot's Discord activity presence. Deliberately dependency-free (no ASP.NET
/// Core in the launcher). Binding failure is non-fatal: the bot falls back to a static 0.
/// </summary>
public sealed class PlayerCountHttpEndpoint : IDisposable
{
    private readonly Func<int> _countProvider;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _disposed;

    private PlayerCountHttpEndpoint(int port, Func<int> countProvider)
    {
        _countProvider = countProvider ?? throw new ArgumentNullException(nameof(countProvider));
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Starts the endpoint, or returns null (and logs) when the port is unavailable.</summary>
    public static PlayerCountHttpEndpoint? TryStart(int port, Func<int> countProvider)
    {
        try
        {
            return new PlayerCountHttpEndpoint(port, countProvider);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or ArgumentOutOfRangeException)
        {
            Logger.WriteException(LogType.Warning, $"Player-count endpoint on port {port}", ex);
            return null;
        }
    }

    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return; // listener stopped during Dispose
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var requestLine = await ReadRequestAsync(stream).ConfigureAwait(false);

                if (requestLine is not null &&
                    (requestLine.StartsWith("GET / HTTP/1.1", StringComparison.Ordinal) ||
                     requestLine.StartsWith("GET /players HTTP/1.1", StringComparison.Ordinal)))
                {
                    await WriteJsonAsync(stream, 200, JsonSerializer.Serialize(new { players = _countProvider() }))
                        .ConfigureAwait(false);
                }
                else
                {
                    await WriteJsonAsync(stream, 404, "{\"error\":\"not found\"}").ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            Logger.WriteException(LogType.Warning, "Player-count endpoint client", ex);
        }
    }

    /// <summary>
    /// Reads the request line and consumes the headers (up to the blank line) in one buffered
    /// pass, returning the request line. Bytes read beyond the request line are preserved, so
    /// a request arriving in a single packet is not truncated.
    /// </summary>
    private static async Task<string?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[16384];
        var read = 0;
        string? requestLine = null;

        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read)).ConfigureAwait(false);
            if (n == 0)
                return requestLine; // client closed before sending a full request

            read += n;

            var start = 0;
            var headersComplete = false;
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\n')
                    continue;

                var line = Encoding.ASCII.GetString(buffer, start, i - start).TrimEnd('\r');
                if (requestLine is null)
                    requestLine = line;
                else if (line.Length == 0)
                {
                    headersComplete = true;
                    break;
                }

                start = i + 1;
            }

            if (headersComplete)
                return requestLine;

            if (start > 0)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, read - start);
                read -= start;
            }
        }

        return requestLine;
    }

    private static async Task WriteJsonAsync(NetworkStream stream, int statusCode, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {(statusCode == 200 ? "OK" : "Not Found")}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(payload).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        _listener.Stop();
        _listener.Dispose();
        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Best-effort stop; nothing to recover.
        }
        _cts.Dispose();
    }
}
