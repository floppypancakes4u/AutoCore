namespace AutoCore.Sector.Config;

using AutoCore.Utils;

public class SectorConfig
{
    public GameConfig GameConfig { get; set; } = new();
    public string CharDatabaseConnectionString { get; set; } = string.Empty;
    public string WorldDatabaseConnectionString { get; set; } = string.Empty;
    /// <summary>
    /// Optional. Required for in-game <c>/addplayer</c> account creation when sector runs standalone
    /// (launcher already initializes Auth from auth config).
    /// </summary>
    public string AuthDatabaseConnectionString { get; set; } = string.Empty;
    public string GamePath { get; set; } = string.Empty;
    public Logger.LoggerConfig LoggerConfig { get; set; } = new();
}
