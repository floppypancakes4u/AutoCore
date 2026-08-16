using Microsoft.Extensions.Configuration;

namespace AutoCore.Launcher.Bootstrap;

using AutoCore.Auth.Config;
using AutoCore.Global.Config;
using AutoCore.Sector.Config;

/// <summary>
/// Loads Launcher host configuration from JSON files without starting any servers.
/// </summary>
public static class LauncherConfigLoader
{
    public const string AuthConfigFileName = "appsettings.auth.json";
    public const string AuthEnvConfigFileName = "appsettings.auth.env.json";
    public const string GlobalConfigFileName = "appsettings.global.json";
    public const string GlobalEnvConfigFileName = "appsettings.global.env.json";
    public const string SectorConfigFileName = "appsettings.sector.json";
    public const string SectorEnvConfigFileName = "appsettings.sector.env.json";
    public const string BotBridgeConfigFileName = "appsettings.botbridge.json";
    public const string BotBridgeEnvConfigFileName = "appsettings.botbridge.env.json";

    public static AuthConfig LoadAuthConfig(string? contentRoot = null)
        => LoadConfig<AuthConfig>(AuthConfigFileName, AuthEnvConfigFileName, contentRoot);

    public static GlobalConfig LoadGlobalConfig(string? contentRoot = null)
        => LoadConfig<GlobalConfig>(GlobalConfigFileName, GlobalEnvConfigFileName, contentRoot);

    public static SectorConfig LoadSectorConfig(string? contentRoot = null)
        => LoadConfig<SectorConfig>(SectorConfigFileName, SectorEnvConfigFileName, contentRoot);

    /// <summary>
    /// Loads the optional bridge config to the external Auto Assault Crash Bot. Missing primary
    /// file yields defaults (both bridge features disabled) so existing deployments keep working.
    /// </summary>
    public static BotBridgeConfig LoadBotBridgeConfig(string? contentRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(contentRoot)
            ? Directory.GetCurrentDirectory()
            : contentRoot;

        var primaryPath = Path.Combine(root, BotBridgeConfigFileName);
        if (!File.Exists(primaryPath))
            return new BotBridgeConfig();

        return LoadConfig<BotBridgeConfig>(BotBridgeConfigFileName, BotBridgeEnvConfigFileName, contentRoot);
    }

    /// <summary>
    /// Loads and binds a config type from a required primary JSON file and an optional env overlay.
    /// Throws <see cref="FileNotFoundException"/> when the primary file is missing.
    /// </summary>
    public static T LoadConfig<T>(string primaryFileName, string optionalEnvFileName, string? contentRoot = null)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionalEnvFileName);

        var root = string.IsNullOrWhiteSpace(contentRoot)
            ? Directory.GetCurrentDirectory()
            : contentRoot;

        var primaryPath = Path.Combine(root, primaryFileName);
        if (!File.Exists(primaryPath))
            throw new FileNotFoundException($"Required configuration file was not found: {primaryFileName}", primaryPath);

        var builder = new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile(primaryFileName, optional: false, reloadOnChange: false)
            .AddJsonFile(optionalEnvFileName, optional: true, reloadOnChange: false);

        var config = new T();
        builder.Build().Bind(config);
        return config;
    }
}
