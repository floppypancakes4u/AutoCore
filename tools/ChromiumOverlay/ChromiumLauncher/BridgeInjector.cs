using System.Diagnostics;
using System.Text;
using AutoLoginInjector;

namespace ChromiumOverlay;

/// <summary>
/// Injects ChromiumBridge.dll into autoassault.exe and calls SetupChromiumBridge.
/// Mirrors tools/PathADebug and SpeedCheatService injection flow.
/// </summary>
public sealed class BridgeInjector
{
    private const uint RequiredAccess = DllInjector.RequiredProcessAccess;
    private const int ThreadTimeoutMs = 15_000;
    public const string SetupExportName = "SetupChromiumBridge";

    private readonly IInjectionPlatform _platform;

    public BridgeInjector(IInjectionPlatform? platform = null)
    {
        _platform = platform ?? new WindowsInjectionPlatform();
    }

    public sealed record Result(bool Success, string Message, int? ProcessId = null);

    public Result Inject(int processId, string dllPath)
    {
        if (!File.Exists(dllPath))
            return new Result(false, $"ChromiumBridge.dll not found at {dllPath}. Build: tools\\ChromiumOverlay\\ChromiumBridge\\build.ps1");

        var process = _platform.OpenProcess(RequiredAccess, processId);
        if (process == IntPtr.Zero)
            return new Result(false, "Could not open the game process (run elevated?).", processId);

        try
        {
            var kernel32 = _platform.FindRemoteModule(process, "kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                return new Result(false, "kernel32.dll not found in the game process.", processId);

            var loadLibrary = _platform.ResolveRemoteProcAddress(process, kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
                return new Result(false, "LoadLibraryW not resolved in the game process.", processId);

            var fullPath = Path.GetFullPath(dllPath);
            var pathBytes = Encoding.Unicode.GetBytes(fullPath + '\0');
            var remotePath = _platform.Allocate(process, pathBytes.Length);
            if (remotePath == IntPtr.Zero)
                return new Result(false, "Remote alloc for DLL path failed.", processId);

            uint remoteModule;
            try
            {
                if (!_platform.Write(process, remotePath, pathBytes))
                    return new Result(false, "Remote write of DLL path failed.", processId);

                var load = _platform.RunThread(process, loadLibrary, remotePath, ThreadTimeoutMs);
                if (!load.Success)
                    return new Result(false, "LoadLibrary thread failed: " + load.Message, processId);
                if (load.ExitCode == 0)
                    return new Result(false, "ChromiumBridge.dll failed to load in the game process.", processId);
                remoteModule = load.ExitCode;
            }
            finally
            {
                _platform.Free(process, remotePath);
            }

            var exportRva = _platform.GetExportRva(fullPath, SetupExportName);
            if (exportRva <= 0)
                return new Result(false, $"{SetupExportName} export not found in ChromiumBridge.dll.", processId);

            var setup = new IntPtr(unchecked((long)remoteModule + exportRva));
            var setupResult = _platform.RunThread(process, setup, IntPtr.Zero, ThreadTimeoutMs);
            if (!setupResult.Success)
                return new Result(false, $"{SetupExportName} thread failed: " + setupResult.Message, processId);
            if (setupResult.ExitCode == 0)
                return new Result(false, $"{SetupExportName} returned 0 (see %TEMP%\\AutoCoreChromium\\bridge.log).", processId);

            return new Result(true, $"ChromiumBridge installed (pid={processId}).", processId);
        }
        finally
        {
            _platform.CloseProcess(process);
        }
    }

    public static string? TryGetMainModulePath(int pid)
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
}
