using System.Diagnostics;

namespace ChromiumOverlay;

/// <summary>Options for locating autoassault.exe and building a ProcessStartInfo.</summary>
public sealed class GameLaunchOptions
{
    public string? ExplicitExePath { get; init; }
    public string? GamePath { get; init; }
    public string? EnvironmentInstall { get; init; }

    public static GameLaunchOptions FromEnvironment(
        string? explicitExe = null,
        string? gamePath = null,
        string? environmentInstall = null) =>
        new()
        {
            ExplicitExePath = explicitExe,
            GamePath = gamePath,
            EnvironmentInstall = environmentInstall ?? Environment.GetEnvironmentVariable("AA_INSTALL"),
        };
}

/// <summary>
/// Resolves the Auto Assault client executable and builds a start info with -developer.
/// Prefers the local RE / patched install (<c>Auto Assault.bak</c>) when present — the stock
/// 2007 client still hard-requires MSXML 4.0 and shows a legacy parser dialog; the .bak build
/// is the one DevTool / hooks already treat as the working client.
/// </summary>
public static class GamePathResolver
{
    public const string DefaultInstall = @"C:\Program Files (x86)\NetDevil\Auto Assault";
    public const string DefaultBakInstall = @"C:\Program Files (x86)\NetDevil\Auto Assault.bak";
    public const string ExeFileName = "autoassault.exe";
    public const string DeveloperArgument = "-developer";

    public static string ResolveExePath(GameLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.ExplicitExePath))
            return Path.GetFullPath(options.ExplicitExePath);

        var root = FirstNonEmpty(options.GamePath, options.EnvironmentInstall, null);
        root ??= ResolveDefaultInstallRoot();
        return Path.GetFullPath(Path.Combine(root, "exe", ExeFileName));
    }

    /// <summary>
    /// Default install root when neither --game-path nor AA_INSTALL is set.
    /// Prefers <see cref="DefaultBakInstall"/> if it contains autoassault.exe.
    /// </summary>
    public static string ResolveDefaultInstallRoot()
    {
        var bakExe = Path.Combine(DefaultBakInstall, "exe", ExeFileName);
        if (File.Exists(bakExe))
            return DefaultBakInstall;
        return DefaultInstall;
    }

    public static ProcessStartInfo BuildStartInfo(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Executable path is required.", nameof(exePath));

        var full = Path.GetFullPath(exePath);
        var dir = Path.GetDirectoryName(full)
                  ?? throw new ArgumentException("Executable path has no directory.", nameof(exePath));

        return new ProcessStartInfo
        {
            FileName = full,
            Arguments = DeveloperArgument,
            WorkingDirectory = dir,
            UseShellExecute = false,
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }
}
