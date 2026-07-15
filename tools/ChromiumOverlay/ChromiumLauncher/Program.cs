using System.Diagnostics;

namespace ChromiumOverlay;

/// <summary>
/// Launches autoassault.exe with -developer, injects ChromiumBridge.dll, and starts the CEF overlay host.
/// Order is deliberate: settle game → inject → prove alive → start CEF → prove alive.
/// </summary>
internal static class Program
{
    private const string DefaultProcessName = "autoassault";

    private static int Main(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help" or "help"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            ChromiumLauncher — start Auto Assault with -developer, inject bridge, open CEF overlay

            Usage:
              ChromiumLauncher.exe [options]

            Options:
              --game-path <dir>   Install root (contains exe\autoassault.exe)
              --exe <path>        Full path to autoassault.exe
              --pid <n>           Attach to running process (skip launch)
              --no-launch         Do not start the game (require running client or --pid)
              --skip-inject       Skip ChromiumBridge injection
              --skip-host         Skip starting ChromiumHost (test inject alone)
              --settle-ms <n>     Wait after game window before inject (default 3000)
              --bridge <path>     Path to ChromiumBridge.dll
              --host <path>       Path to ChromiumHost.exe
              -h, --help          Show this help

            Sequence: launch → settle → inject → alive? → CEF host → alive?
            Default client prefers Auto Assault.bak when present.
            """);
    }

    private static int Run(string[] args)
    {
        string? gamePath = null;
        string? exePath = null;
        int? pid = null;
        var noLaunch = false;
        var skipInject = false;
        var skipHost = false;
        var settleMs = 3000;
        string? bridgePath = null;
        string? hostPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + a);

            switch (a)
            {
                case "--game-path": gamePath = Next(); break;
                case "--exe": exePath = Next(); break;
                case "--pid": pid = int.Parse(Next()); break;
                case "--no-launch": noLaunch = true; break;
                case "--skip-inject": skipInject = true; break;
                case "--skip-host": skipHost = true; break;
                case "--settle-ms": settleMs = int.Parse(Next()); break;
                case "--bridge": bridgePath = Next(); break;
                case "--host": hostPath = Next(); break;
                case "--no-click-through":
                case "--click-through":
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + a);
            }
        }

        Process game;
        if (pid is { } existingPid)
        {
            game = Process.GetProcessById(existingPid);
            Console.WriteLine($"[1/4] Attached to pid={existingPid}");
        }
        else if (noLaunch)
        {
            var matches = Process.GetProcessesByName(DefaultProcessName);
            if (matches.Length == 0)
            {
                Console.Error.WriteLine($"No process '{DefaultProcessName}' and --no-launch was set.");
                return 1;
            }
            game = matches[0];
            if (matches.Length > 1)
                Console.WriteLine($"WARNING: {matches.Length} clients; using pid={game.Id}");
            Console.WriteLine($"[1/4] Using running client pid={game.Id}");
        }
        else
        {
            var options = GameLaunchOptions.FromEnvironment(exePath, gamePath);
            var resolvedExe = GamePathResolver.ResolveExePath(options);
            Console.WriteLine($"[1/4] Launching: {resolvedExe} {GamePathResolver.DeveloperArgument}");
            if (resolvedExe.Contains("Auto Assault.bak", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("      Using RE/patched install (.bak).");
            game = GameLauncher.Start(resolvedExe);
            Console.WriteLine($"      Started pid={game.Id}");
            if (!GameLauncher.WaitForMainWindow(game, TimeSpan.FromSeconds(30)))
                Console.WriteLine("      WARNING: main window not ready within 30s; continuing.");
            else
                Console.WriteLine("      Main window ready.");
        }

        if (!EnsureAlive(game, "after launch"))
            return 3;

        if (settleMs > 0)
        {
            Console.WriteLine($"[1b] Settling {settleMs}ms (let client finish boot)…");
            Thread.Sleep(settleMs);
            if (!EnsureAlive(game, "after settle"))
                return 3;
        }

        var baseDir = AppContext.BaseDirectory;

        // Inject BEFORE CEF so we can tell which step kills the client.
        if (!skipInject)
        {
            bridgePath ??= OverlayHostProcess.DefaultBridgePath(baseDir);
            Console.WriteLine($"[2/4] Injecting bridge: {bridgePath}");
            var inject = new BridgeInjector().Inject(game.Id, bridgePath);
            Console.WriteLine(inject.Success ? "      " + inject.Message : "      INJECT FAILED: " + inject.Message);
            if (!inject.Success)
                return 1;

            // Give publisher thread a moment; fail loud if inject killed the process.
            Thread.Sleep(500);
            if (!EnsureAlive(game, "after inject"))
            {
                Console.Error.WriteLine("      Bridge log: %TEMP%\\AutoCoreChromium\\bridge.log");
                Console.Error.WriteLine("      Game died during/after inject — CEF was not started yet.");
                return 4;
            }
            Console.WriteLine("      Game still alive after inject.");
            Console.WriteLine("      Bridge log: %TEMP%\\AutoCoreChromium\\bridge.log");
        }
        else
        {
            Console.WriteLine("[2/4] Inject skipped (--skip-inject).");
        }

        Process? host = null;
        if (!skipHost)
        {
            hostPath ??= OverlayHostProcess.DefaultHostPath(baseDir);
            Console.WriteLine($"[3/4] Starting CEF host: {hostPath}");
            host = OverlayHostProcess.Start(hostPath, game.Id);
            Console.WriteLine($"      ChromiumHost pid={host.Id}");
            Console.WriteLine("      Host log: %TEMP%\\AutoCoreChromium\\host.log");

            // Host paints a layered TOPMOST window; that has killed exclusive-FS clients.
            for (var i = 0; i < 15; i++)
            {
                Thread.Sleep(300);
                if (host.HasExited)
                {
                    Console.Error.WriteLine($"[FAIL] ChromiumHost exited early code={host.ExitCode}.");
                    Console.Error.WriteLine("       Read %TEMP%\\AutoCoreChromium\\host.log (look for Cef.Initialize).");
                    return 6;
                }
                if (!IsAlive(game))
                {
                    Console.Error.WriteLine($"[FAIL] Game exited {300 * (i + 1)}ms after CEF host start (exit={TryExitCode(game)}).");
                    Console.Error.WriteLine("       Retry with: --skip-host  (prove inject alone is safe)");
                    try { host.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    return 5;
                }
            }
            Console.WriteLine("      Game + host still alive ~4.5s after CEF host start.");
        }
        else
        {
            Console.WriteLine("[3/4] CEF host skipped (--skip-host).");
        }

        Console.WriteLine("[4/4] Ready.");
        if (host != null)
            Console.WriteLine("      Overlay should show Hello World (transparent, click-through).");
        Console.WriteLine("      Leave this window open while testing. Ctrl+C when done.");

        if (host != null)
        {
            // Watch both: if game dies, stop waiting on host quietly.
            while (!host.HasExited)
            {
                if (!IsAlive(game))
                {
                    Console.Error.WriteLine($"[FAIL] Game exited while overlay was running (exit={TryExitCode(game)}).");
                    try { host.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    return 5;
                }
                Thread.Sleep(250);
            }
            Console.WriteLine("ChromiumHost exited with " + host.ExitCode);
            if (!IsAlive(game))
                Console.Error.WriteLine($"Game is also dead (exit={TryExitCode(game)}).");
            return host.ExitCode;
        }

        return 0;
    }

    private static bool IsAlive(Process game)
    {
        try
        {
            game.Refresh();
            return !game.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string TryExitCode(Process game)
    {
        try
        {
            game.Refresh();
            return game.HasExited ? game.ExitCode.ToString() : "n/a";
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool EnsureAlive(Process game, string when)
    {
        if (IsAlive(game))
            return true;
        Console.Error.WriteLine($"[FAIL] Game not running {when} (exit={TryExitCode(game)}).");
        return false;
    }
}
