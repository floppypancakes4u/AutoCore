namespace AutoCore.Game.AgentDebug;

using System.Text.Json;

internal static class TossDebugLogger
{
    private static readonly string LogPath = ResolveLogPath();
    private const string SessionId = "c5f9cf";

    public static void Log(string hypothesisId, string location, string message, object? data = null, string runId = "pre-fix")
    {
        try
        {
            var entry = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["runId"] = runId,
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            File.AppendAllText(LogPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
            // ignore debug logging failures
        }
    }

    private static string ResolveLogPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return Path.Combine(dir, "debug-c5f9cf.log");

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Path.Combine(AppContext.BaseDirectory, "debug-c5f9cf.log");
    }
}
