using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AutoCore.Game.Diagnostics;
using AutoCore.Launcher.Bootstrap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Launcher.Tests;

[TestClass]
public class HttpBugReportUploaderTests
{
    [TestMethod]
    public async Task UploadAsync_PostsJsonWithApiKey_ReturnsOk()
    {
        using var server = new StubHttpServer(statusCode: 200);
        server.Start();

        using var uploader = new HttpBugReportUploader(new BotBridgeConfig
        {
            BugReportPort = server.Port,
            BugReportSharedSecret = "sekret",
        });

        var result = await uploader.UploadAsync(MakePackage());

        Assert.IsTrue(result.Success, result.Detail);
        Assert.AreEqual("POST /bugreport HTTP/1.1", server.RequestLine);
        Assert.AreEqual("sekret", server.ApiKeyHeader);

        using var json = JsonDocument.Parse(server.Body!);
        var root = json.RootElement;
        Assert.AreEqual("rid-1", root.GetProperty("reportId").GetString());
        Assert.AreEqual("AQID", root.GetProperty("zipBytes").GetString()); // base64 of {1,2,3}
        Assert.AreEqual("Tester", root.GetProperty("characterName").GetString());
        Assert.AreEqual(42, root.GetProperty("characterId").GetInt64());
    }

    [TestMethod]
    public async Task UploadAsync_ServerRejects_ReturnsFailWithHttpCode()
    {
        using var server = new StubHttpServer(statusCode: 401);
        server.Start();

        using var uploader = new HttpBugReportUploader(new BotBridgeConfig
        {
            BugReportPort = server.Port,
            BugReportSharedSecret = "sekret",
        });

        var result = await uploader.UploadAsync(MakePackage());

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Detail, "401");
    }

    [TestMethod]
    public async Task UploadAsync_ServerUnreachable_ReturnsFailWithoutThrowing()
    {
        var freePort = GetFreePort();

        using var uploader = new HttpBugReportUploader(new BotBridgeConfig { BugReportPort = freePort });

        var result = await uploader.UploadAsync(MakePackage());

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Detail);
    }

    [TestMethod]
    public void Ctor_NullConfig_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new HttpBugReportUploader(null!));
    }

    [TestMethod]
    public void IsAvailable_WhenConfigured_IsTrue()
    {
        using var uploader = new HttpBugReportUploader(new BotBridgeConfig());
        Assert.IsTrue(uploader.IsAvailable);
    }

    private static BugReportPackage MakePackage() => new()
    {
        ReportId = "rid-1",
        FileName = "bugreport.zip",
        ZipBytes = new byte[] { 1, 2, 3 },
        DiscordMessage = "Player report",
        CharacterName = "Tester",
        CharacterId = 42,
        SessionId = "s-1",
    };

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Minimal loopback HTTP server that captures one request and replies with a fixed status.</summary>
    private sealed class StubHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly int _statusCode;
        private Task? _acceptTask;

        public StubHttpServer(int statusCode)
        {
            _statusCode = statusCode;
            _listener = new TcpListener(IPAddress.Loopback, 0);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public string? RequestLine { get; private set; }
        public string? ApiKeyHeader { get; private set; }
        public string? Body { get; private set; }

        public void Start()
        {
            _listener.Start();
            _acceptTask = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                RequestLine = await reader.ReadLineAsync();

                var contentLength = 0;
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null && line.Length > 0)
                {
                    if (line.StartsWith("X-Api-Key:", StringComparison.OrdinalIgnoreCase))
                        ApiKeyHeader = line["X-Api-Key:".Length..].Trim();
                    else if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                }

                var bodyBuffer = new char[contentLength];
                if (contentLength > 0)
                    await reader.ReadAsync(bodyBuffer.AsMemory(0, contentLength));
                Body = new string(bodyBuffer, 0, contentLength);

                var payload = Encoding.UTF8.GetBytes("{}");
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {_statusCode} X\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(payload);
                await stream.FlushAsync();
            }
            catch (Exception)
            {
                // Test server is best-effort; failures surface in assertions.
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Dispose();
            try { _acceptTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        }
    }
}
