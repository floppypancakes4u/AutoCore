using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AbandonProbe;

/// <summary>External ReadProcessMemory attach — does not freeze the client (no debugger break).</summary>
public sealed class ClientMemory : IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly IntPtr _handle;

    public int Pid { get; }
    public string ProcessName { get; }
    public uint ModuleBase { get; }
    public string? ModulePath { get; }

    private ClientMemory(int pid, string processName, IntPtr handle, uint moduleBase, string? modulePath)
    {
        Pid = pid;
        ProcessName = processName;
        _handle = handle;
        ModuleBase = moduleBase;
        ModulePath = modulePath;
    }

    public static ClientMemory Attach(string processName = "autoassault")
    {
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
            throw new InvalidOperationException($"No process named '{processName}' is running.");

        var proc = processes[0];
        if (processes.Length > 1)
            Console.Error.WriteLine($"WARNING: {processes.Length} '{processName}' processes; using pid={proc.Id}");

        var access = ProcessVmRead | ProcessQueryInformation | ProcessQueryLimitedInformation;
        var handle = OpenProcess(access, false, proc.Id);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed — run elevated?");

        try
        {
            if (!TryGetMainModule(handle, proc.Id, out var baseAddr, out var path))
                throw new InvalidOperationException("Could not resolve autoassault.exe module base.");

            return new ClientMemory(proc.Id, processName, handle, baseAddr, path);
        }
        catch
        {
            CloseHandle(handle);
            throw;
        }
    }

    /// <summary>
    /// Resolve a Ghidra static VA (as if image base were 0x400000) to the live process address.
    /// </summary>
    public uint Resolve(uint staticVaAssumingBase400000)
    {
        const uint analyzedBase = 0x00400000;
        var rva = staticVaAssumingBase400000 - analyzedBase;
        return ModuleBase + rva;
    }

    public uint ReadUInt32(uint address)
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(address, buf);
        return BitConverter.ToUInt32(buf);
    }

    public int ReadInt32(uint address) => unchecked((int)ReadUInt32(address));

    public byte ReadByte(uint address)
    {
        Span<byte> buf = stackalloc byte[1];
        ReadExact(address, buf);
        return buf[0];
    }

    public void ReadExact(uint address, Span<byte> buffer)
    {
        var tmp = new byte[buffer.Length];
        if (!ReadProcessMemory(_handle, new IntPtr(address), tmp, tmp.Length, out var read) ||
            read != tmp.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"ReadProcessMemory failed at 0x{address:X8} size={buffer.Length}");
        }

        tmp.CopyTo(buffer);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
            CloseHandle(_handle);
    }

    private static bool TryGetMainModule(IntPtr process, int pid, out uint baseAddr, out string? path)
    {
        baseAddr = 0;
        path = null;

        // Prefer Toolhelp — works for 32-bit targets from 64-bit hosts more reliably than MainModule.
        // TH32CS_SNAPMODULE (0x8) | TH32CS_SNAPMODULE32 (0x10) = 0x18 for WOW64 targets.
        var snap = CreateToolhelp32Snapshot(0x00000018, (uint)pid);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            snap = CreateToolhelp32Snapshot(0x00000008, (uint)pid);

        if (snap != IntPtr.Zero && snap != new IntPtr(-1))
        {
            try
            {
                var me = new ModuleEntry32
                {
                    DwSize = (uint)Marshal.SizeOf<ModuleEntry32>()
                };
                if (Module32First(snap, ref me))
                {
                    do
                    {
                        var name = me.SzModule ?? "";
                        if (name.Equals("autoassault.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            baseAddr = (uint)me.ModBaseAddr.ToInt64();
                            path = me.SzExePath;
                            return baseAddr != 0;
                        }
                    } while (Module32Next(snap, ref me));
                }
            }
            finally
            {
                CloseHandle(snap);
            }
        }

        // Fallback: Process.MainModule (may throw if WOW64 mismatch).
        try
        {
            using var p = Process.GetProcessById(pid);
            var mod = p.MainModule;
            if (mod != null)
            {
                baseAddr = (uint)mod.BaseAddress.ToInt64();
                path = mod.FileName;
                return baseAddr != 0;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        int dwSize,
        out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Module32First(IntPtr hSnapshot, ref ModuleEntry32 lpme);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref ModuleEntry32 lpme);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry32
    {
        public uint DwSize;
        public uint Th32ModuleId;
        public uint Th32ProcessId;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr ModBaseAddr;
        public uint ModBaseSize;
        public IntPtr HModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string SzExePath;
    }
}
