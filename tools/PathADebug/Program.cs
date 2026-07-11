using System.Diagnostics;
using System.Text;
using AutoLoginInjector;

namespace PathADebug;

/// <summary>
/// Non-freezing Path A debugger: injects PathAHook.dll (MinHook) into autoassault.exe.
/// Does not use cdb — no attach break, no DirectInput freeze.
/// </summary>
internal static class Program
{
    private const string DefaultProcessName = "autoassault";
    private const uint RequiredAccess = DllInjector.RequiredProcessAccess;
    private const int ThreadTimeoutMs = 15_000;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "arm" => Arm(args),
                "status" => Status(),
                "hits" or "check" => Hits(copyToDocs: true),
                "tail" => Tail(),
                "disarm" => DisarmHint(),
                _ => Unknown(cmd),
            };
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
            PathADebug — non-freezing CreateVehicle equip capture (MinHook inject)

            Commands:
              arm [--process autoassault] [--dll path\to\PathAHook.dll]
                  Inject PathAHook into the running client. No debugger attach freeze.
              status
                  Show process + hit log path + last lines.
              hits | check
                  Print hit summary and copy jsonl into docs/debugger-hits/path-a-hits.jsonl
              tail
                  Follow the hit log (Ctrl+C to stop).
              disarm
                  Instructions (DLL unloads on client exit; no forced detach API yet).

            Workflow:
              1. Start a healthy client (not frozen).
              2. pathadebug arm
              3. Login / enter Ark Bay (foreign CreateVehicle).
              4. pathadebug check
            """);
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command '{cmd}'. Use help.");
        return 1;
    }

    private static string RepoRoot()
    {
        // tools/PathADebug/bin/... -> repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AutoCore.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string DefaultDllPath()
    {
        var root = RepoRoot();
        var candidates = new[]
        {
            Path.Combine(root, "tools", "PathAHook", "PathAHook.dll"),
            Path.Combine(AppContext.BaseDirectory, "PathAHook.dll"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string HitsPath()
    {
        var temp = Path.Combine(Path.GetTempPath(), "AutoCorePathA", "hits.jsonl");
        return temp;
    }

    private static int Arm(string[] args)
    {
        var processName = DefaultProcessName;
        var dllPath = DefaultDllPath();
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--process" && i + 1 < args.Length)
                processName = args[++i];
            else if (args[i] == "--dll" && i + 1 < args.Length)
                dllPath = args[++i];
        }

        if (!File.Exists(dllPath))
        {
            Console.Error.WriteLine($"PathAHook.dll not found at {dllPath}");
            Console.Error.WriteLine("Build: powershell -File tools\\PathAHook\\build.ps1");
            return 1;
        }

        var clients = Process.GetProcessesByName(processName);
        if (clients.Length == 0)
        {
            Console.Error.WriteLine($"No process named '{processName}' is running.");
            return 1;
        }
        if (clients.Length > 1)
            Console.WriteLine($"WARNING: {clients.Length} processes named {processName}; using pid={clients[0].Id}");
        var pid = clients[0].Id;

        var exePath = TryGetMainModulePath(pid);
        var verifier = new ClientBuildVerifier();
        if (!string.IsNullOrEmpty(exePath))
        {
            var v = verifier.Verify(exePath);
            if (!v.Success)
            {
                Console.Error.WriteLine("Client build check failed: " + v.Message);
                return 1;
            }
            Console.WriteLine("Client build verified: " + exePath);
        }
        else
        {
            Console.WriteLine("WARNING: could not resolve client path; skipping hash check.");
        }

        var platform = new WindowsInjectionPlatform();
        var process = platform.OpenProcess(RequiredAccess, pid);
        if (process == IntPtr.Zero)
        {
            Console.Error.WriteLine("Could not open game process.");
            return 1;
        }

        try
        {
            var kernel32 = platform.FindRemoteModule(process, "kernel32.dll");
            if (kernel32 == IntPtr.Zero)
            {
                Console.Error.WriteLine("kernel32.dll not found in game process.");
                return 1;
            }

            var loadLibrary = platform.ResolveRemoteProcAddress(process, kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                Console.Error.WriteLine("LoadLibraryW not resolved.");
                return 1;
            }

            var fullPath = Path.GetFullPath(dllPath);
            var pathBytes = Encoding.Unicode.GetBytes(fullPath + '\0');
            var remotePath = platform.Allocate(process, pathBytes.Length);
            if (remotePath == IntPtr.Zero)
            {
                Console.Error.WriteLine("Remote alloc failed.");
                return 1;
            }

            uint remoteModule;
            try
            {
                if (!platform.Write(process, remotePath, pathBytes))
                {
                    Console.Error.WriteLine("Remote write failed.");
                    return 1;
                }

                var load = platform.RunThread(process, loadLibrary, remotePath, ThreadTimeoutMs);
                if (!load.Success)
                {
                    Console.Error.WriteLine("LoadLibrary thread failed: " + load.Message);
                    return 1;
                }
                if (load.ExitCode == 0)
                {
                    Console.Error.WriteLine("PathAHook.dll failed to load in the game process.");
                    return 1;
                }
                remoteModule = load.ExitCode;
            }
            finally
            {
                platform.Free(process, remotePath);
            }

            var exportRva = platform.GetExportRva(fullPath, "SetupPathAHook");
            if (exportRva <= 0)
            {
                Console.Error.WriteLine("SetupPathAHook export not found.");
                return 1;
            }

            var setup = new IntPtr(unchecked((long)remoteModule + exportRva));
            var setupResult = platform.RunThread(process, setup, IntPtr.Zero, ThreadTimeoutMs);
            if (!setupResult.Success)
            {
                Console.Error.WriteLine("SetupPathAHook thread failed: " + setupResult.Message);
                return 1;
            }
            if (setupResult.ExitCode == 0)
            {
                Console.Error.WriteLine("SetupPathAHook returned 0 (prologue mismatch or MinHook install failed).");
                Console.Error.WriteLine("Check " + HitsPath() + " for SetupPathAHook_FAILED.");
                return 1;
            }

            Console.WriteLine("PathAHook installed (pid={0}).", pid);
            Console.WriteLine("Hit log: {0}", HitsPath());
            Console.WriteLine("Reproduce Ark Bay, then: PathADebug check");
            return 0;
        }
        finally
        {
            platform.CloseProcess(process);
        }
    }

    private static string? TryGetMainModulePath(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int Status()
    {
        var procs = Process.GetProcessesByName(DefaultProcessName);
        Console.WriteLine(procs.Length == 0
            ? "Client: not running"
            : $"Client: running pid={string.Join(",", procs.Select(p => p.Id))}");

        var path = HitsPath();
        Console.WriteLine("Hit log: " + path);
        if (!File.Exists(path))
        {
            Console.WriteLine("Hit log does not exist yet (arm + reproduce first).");
            return 0;
        }

        var info = new FileInfo(path);
        Console.WriteLine($"Size={info.Length} LastWrite={info.LastWriteTime}");
        var lines = File.ReadAllLines(path);
        Console.WriteLine($"Lines={lines.Length}");
        foreach (var line in lines.TakeLast(8))
            Console.WriteLine(line);
        return 0;
    }

    private static int Hits(bool copyToDocs)
    {
        var path = HitsPath();
        if (!File.Exists(path))
        {
            Console.WriteLine("No hits file at " + path);
            return 1;
        }

        var lines = File.ReadAllLines(path);
        var byEv = lines
            .Select(l =>
            {
                var i = l.IndexOf("\"ev\":\"", StringComparison.Ordinal);
                if (i < 0) return "?";
                var s = i + 6;
                var e = l.IndexOf('"', s);
                return e < 0 ? "?" : l[s..e];
            })
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count());

        Console.WriteLine($"Total lines: {lines.Length}");
        foreach (var g in byEv)
            Console.WriteLine($"  {g.Key}: {g.Count()}");

        var nullish = lines.Count(l =>
            l.Contains("\"v258_after\":\"0x00000000\"", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("\"v258_after\":\"0x0\"", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("\"v258_after\":\"00000000\"", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"Lines with v258_after null-ish: {nullish}");

        if (copyToDocs)
        {
            var destDir = Path.Combine(RepoRoot(), "docs", "debugger-hits");
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, "path-a-hits.jsonl");
            File.Copy(path, dest, overwrite: true);
            Console.WriteLine("Copied to " + dest);
        }

        Console.WriteLine("--- last 20 ---");
        foreach (var line in lines.TakeLast(20))
            Console.WriteLine(line);
        return 0;
    }

    private static int Tail()
    {
        var path = HitsPath();
        Console.WriteLine("Tailing " + path + " (Ctrl+C to stop)");
        long pos = 0;
        if (File.Exists(path))
            pos = new FileInfo(path).Length;

        while (true)
        {
            if (File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length > pos)
                {
                    fs.Seek(pos, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                        Console.WriteLine(line);
                    pos = fs.Position;
                }
            }
            Thread.Sleep(250);
        }
    }

    private static int DisarmHint()
    {
        Console.WriteLine("PathAHook has no remote unload export yet.");
        Console.WriteLine("Exit the client to clear hooks, or restart the client before re-arming.");
        Console.WriteLine("Hits remain at: " + HitsPath());
        return 0;
    }
}
