using System.Diagnostics;

namespace ChromiumOverlay;

/// <summary>Starts ChromiumHost.exe and points it at the game process.</summary>
public static class OverlayHostProcess
{
    public static Process Start(string hostExePath, int gamePid)
    {
        if (!File.Exists(hostExePath))
            throw new FileNotFoundException("ChromiumHost.exe not found.", hostExePath);

        var info = new ProcessStartInfo
        {
            FileName = hostExePath,
            Arguments = $"--pid {gamePid}",
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(hostExePath)) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
        };

        return Process.Start(info)
               ?? throw new InvalidOperationException("Failed to start ChromiumHost.");
    }

    public static string DefaultHostPath(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "host", "ChromiumHost.exe"),
            Path.Combine(baseDirectory, "ChromiumHost.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "ChromiumHost", "bin", "Debug", "net8.0-windows", "win-x64", "ChromiumHost.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "ChromiumHost", "bin", "Release", "net8.0-windows", "win-x64", "ChromiumHost.exe")),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public static string DefaultBridgePath(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "ChromiumBridge.dll"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "ChromiumBridge", "ChromiumBridge.dll")),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
