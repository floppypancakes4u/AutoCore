using System.Diagnostics;
using CefSharp;
using CefSharp.OffScreen;

namespace ChromiumOverlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help" or "help"))
        {
            Console.WriteLine("""
                ChromiumHost — transparent click-through overlay for Auto Assault

                Usage:
                  ChromiumHost.exe --pid <gamePid>
                """);
            return 0;
        }

        int? pid = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pid" && i + 1 < args.Length)
                pid = int.Parse(args[++i]);
            else if (args[i] is "--click-through" or "--no-click-through")
                continue;
        }

        if (pid is null)
        {
            Console.Error.WriteLine("ERROR: --pid is required.");
            return 1;
        }

        // Critical: CEF natives resolve relative to CWD / BaseDirectory.
        var baseDir = AppContext.BaseDirectory;
        try
        {
            Directory.SetCurrentDirectory(baseDir);
        }
        catch (Exception ex)
        {
            HostLog.Write("SetCurrentDirectory failed: " + ex.Message);
        }

        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "AutoCoreChromium"));
        HostLog.Write($"ChromiumHost start pid={pid.Value}");
        HostLog.Write($"baseDir={baseDir}");
        HostLog.Write($"cwd={Environment.CurrentDirectory}");

        var cefOk = TryInitializeCef(baseDir);
        HostLog.Write("cefOk=" + cefOk);

        try
        {
            Application.Run(new OverlayForm(pid.Value, enableCef: cefOk));
            HostLog.Write("Application.Run returned");
            return cefOk ? 0 : 2;
        }
        catch (Exception ex)
        {
            HostLog.Write("FATAL: " + ex);
            throw;
        }
        finally
        {
            if (Cef.IsInitialized == true)
            {
                Cef.Shutdown();
                HostLog.Write("Cef.Shutdown done");
            }
        }
    }

    private static bool TryInitializeCef(string baseDir)
    {
        try
        {
            // Kill stale CEF leftovers from prior failed runs (they can make Init return false).
            foreach (var name in new[] { "CefSharp.BrowserSubprocess", "ChromiumHost" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.Id == Environment.ProcessId)
                            continue;
                        // Only kill subprocess zombies, not ourselves.
                        if (name == "ChromiumHost")
                            continue;
                        p.Kill(entireProcessTree: true);
                        HostLog.Write($"killed stale {name} pid={p.Id}");
                    }
                    catch
                    {
                        // ignore
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }

            var subprocess = Path.Combine(baseDir, "CefSharp.BrowserSubprocess.exe");
            var libcef = Path.Combine(baseDir, "libcef.dll");
            HostLog.Write($"subprocess exists={File.Exists(subprocess)} path={subprocess}");
            HostLog.Write($"libcef exists={File.Exists(libcef)}");

            if (!File.Exists(subprocess) || !File.Exists(libcef))
            {
                HostLog.Write("CEF files missing from host folder — rebuild/restage ChromiumHost.");
                return false;
            }

            if (Cef.IsInitialized == true)
            {
                HostLog.Write("CEF already initialized");
                return true;
            }

            var cache = Path.Combine(
                Path.GetTempPath(),
                "AutoCoreChromium",
                "cef-cache-" + Environment.ProcessId);

            var settings = new CefSettings
            {
                BrowserSubprocessPath = subprocess,
                CachePath = cache,
                RootCachePath = cache,
                LogFile = Path.Combine(Path.GetTempPath(), "AutoCoreChromium", "cef.log"),
                LogSeverity = LogSeverity.Info,
                WindowlessRenderingEnabled = true,
                BackgroundColor = Cef.ColorSetARGB(0, 0, 0, 0),
                MultiThreadedMessageLoop = true,
                ExternalMessagePump = false,
                LocalesDirPath = Path.Combine(baseDir, "locales"),
                ResourcesDirPath = baseDir,
            };

            // Software path — avoids GPU device fights with the game.
            settings.CefCommandLineArgs["disable-gpu"] = "1";
            settings.CefCommandLineArgs["disable-gpu-compositing"] = "1";
            settings.CefCommandLineArgs["enable-transparent-painting"] = "1";
            settings.CefCommandLineArgs["no-sandbox"] = "1";
            // Avoid single-process (often more fragile); keep default multi-process.

            // performDependencyCheck:false — CefSharp's checker is strict and returns false
            // even when the runtime is usable (common on staged host\ folders).
            var ok = Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
            HostLog.Write("Cef.Initialize returned " + ok + " IsInitialized=" + Cef.IsInitialized);
            return ok && Cef.IsInitialized == true;
        }
        catch (Exception ex)
        {
            HostLog.Write("TryInitializeCef exception: " + ex);
            return false;
        }
    }
}
