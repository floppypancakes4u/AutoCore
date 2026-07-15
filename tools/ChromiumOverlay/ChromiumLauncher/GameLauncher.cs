using System.Diagnostics;

namespace ChromiumOverlay;

/// <summary>Starts autoassault.exe with -developer and waits for a main window.</summary>
public static class GameLauncher
{
    public static Process Start(string exePath)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Game executable not found.", exePath);

        var info = GamePathResolver.BuildStartInfo(exePath);
        var process = Process.Start(info)
                      ?? throw new InvalidOperationException("Process.Start returned null for " + exePath);
        return process;
    }

    public static bool WaitForMainWindow(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                return false;
            if (process.MainWindowHandle != IntPtr.Zero)
                return true;
            Thread.Sleep(100);
        }
        process.Refresh();
        return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
    }
}
