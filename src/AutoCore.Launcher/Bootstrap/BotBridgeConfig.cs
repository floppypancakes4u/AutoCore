namespace AutoCore.Launcher.Bootstrap;

/// <summary>
/// Optional bridge to the external Auto Assault Crash Bot (AA-DevBot), which now owns all
/// Discord functionality: forwards in-game <c>/reportbug</c> uploads over HTTP and exposes
/// the online player count the bot shows in its Discord activity.
/// </summary>
public sealed class BotBridgeConfig
{
    public bool BugReportUploadEnabled { get; set; }
    public int BugReportPort { get; set; } = 8787;
    public string BugReportSharedSecret { get; set; } = string.Empty;

    public bool PlayerCountEndpointEnabled { get; set; }
    public int PlayerCountPort { get; set; } = 8788;
}
