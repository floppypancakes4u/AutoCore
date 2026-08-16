namespace AutoCore.Launcher.Bootstrap;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using AutoCore.Game.Diagnostics;

/// <summary>
/// Delivers player bug reports to the external Auto Assault Crash Bot's loopback HTTP
/// endpoint (<c>POST /bugreport</c> with <c>X-Api-Key</c>), which posts them to Discord.
/// Replaces the in-process Discord uploader removed with the AutoCore.Discord project.
/// </summary>
public sealed class HttpBugReportUploader : IBugReportUploader, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly string _bugReportUrl;
    private readonly string _apiKey;

    public HttpBugReportUploader(BotBridgeConfig config, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        _bugReportUrl = $"http://127.0.0.1:{config.BugReportPort}/bugreport";
        _apiKey = config.BugReportSharedSecret ?? string.Empty;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
    }

    /// <summary>Endpoint is configured; delivery failures surface as failed results.</summary>
    public bool IsAvailable => true;

    public async Task<BugReportSubmitResult> UploadAsync(
        BugReportPackage package,
        CancellationToken cancellationToken = default)
    {
        if (package is null)
            return BugReportSubmitResult.Fail("No bug report package.", "package is null");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _bugReportUrl);
            request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(package, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return BugReportSubmitResult.Ok($"Bug report {package.ReportId} delivered.");

            return BugReportSubmitResult.Fail(
                "Bug report could not be delivered to Discord.",
                $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return BugReportSubmitResult.Fail(
                "Bug report could not be delivered to Discord.",
                ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();
}
