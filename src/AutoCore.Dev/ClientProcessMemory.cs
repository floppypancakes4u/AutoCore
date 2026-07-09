namespace AutoCore.Dev;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

public sealed class ClientProcessMemory : IDisposable
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;

    private readonly IntPtr _handle;

    public ClientProcessMemory(int processId)
    {
        ProcessId = processId;
        _handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (_handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open process {processId} for memory read.");
    }

    public int ProcessId { get; }

    public static ClientProcessInfo Open(string processName)
    {
        var normalized = Path.GetFileNameWithoutExtension(processName);
        var processes = Process.GetProcessesByName(normalized);

        return processes.Length switch
        {
            1 => new ClientProcessInfo(processes[0].Id, processes[0].ProcessName),
            0 => throw new InvalidOperationException($"Process '{processName}' was not found."),
            _ => throw new InvalidOperationException($"Multiple '{processName}' processes were found.")
        };
    }

    public ClientCargoMemoryVerification VerifyCargoItems(long firstCoid, long secondCoid)
    {
        var firstAddress = 0UL;
        var secondAddress = 0UL;
        var cargoAddress = 0UL;

        foreach (var region in EnumerateReadableRegions())
        {
            const int chunkSize = 2 * 1024 * 1024;
            for (var chunkOffset = 0UL; chunkOffset < region.Size; chunkOffset += chunkSize)
            {
                var readSize = (int)Math.Min((ulong)chunkSize, region.Size - chunkOffset);
                var bytes = Read(region.BaseAddress + chunkOffset, readSize);
                if (bytes.Length == 0)
                    continue;

                if (firstAddress == 0)
                {
                    var firstOffset = ClientCargoMemoryScanner.FindFirst(bytes, firstCoid);
                    if (firstOffset >= 0)
                        firstAddress = region.BaseAddress + chunkOffset + (ulong)firstOffset;
                }

                if (secondAddress == 0)
                {
                    var secondOffset = ClientCargoMemoryScanner.FindFirst(bytes, secondCoid);
                    if (secondOffset >= 0)
                        secondAddress = region.BaseAddress + chunkOffset + (ulong)secondOffset;
                }

                if (cargoAddress == 0)
                {
                    var cargoOffset = ClientCargoMemoryScanner.FindCargoBlock(bytes, firstCoid, secondCoid);
                    if (cargoOffset >= 0)
                        cargoAddress = region.BaseAddress + chunkOffset + (ulong)cargoOffset;
                }

                if (firstAddress != 0 && secondAddress != 0 && cargoAddress != 0)
                {
                    return new ClientCargoMemoryVerification
                    {
                        FirstCoidAddress = firstAddress,
                        SecondCoidAddress = secondAddress,
                        CargoBlockAddress = cargoAddress
                    };
                }
            }
        }

        if (firstAddress == 0)
            throw new InvalidOperationException($"Client memory does not contain first COID {firstCoid}.");

        if (secondAddress == 0)
            throw new InvalidOperationException($"Client memory does not contain second COID {secondCoid}.");

        throw new InvalidOperationException("Client memory contains both COIDs, but no cargo block matched slot 0,0 and 1,0.");
    }

    public ClientCargoMemoryVerification VerifyCargoItem(long coid, byte x, byte y)
    {
        var firstAddress = 0UL;
        var cargoAddress = 0UL;

        foreach (var region in EnumerateReadableRegions())
        {
            const int chunkSize = 2 * 1024 * 1024;
            for (var chunkOffset = 0UL; chunkOffset < region.Size; chunkOffset += chunkSize)
            {
                var readSize = (int)Math.Min((ulong)chunkSize, region.Size - chunkOffset);
                var bytes = Read(region.BaseAddress + chunkOffset, readSize);
                if (bytes.Length == 0)
                    continue;

                if (firstAddress == 0)
                {
                    var firstOffset = ClientCargoMemoryScanner.FindFirst(bytes, coid);
                    if (firstOffset >= 0)
                        firstAddress = region.BaseAddress + chunkOffset + (ulong)firstOffset;
                }

                if (cargoAddress == 0)
                {
                    var cargoOffset = ClientCargoMemoryScanner.FindCargoSlot(bytes, coid, x, y);
                    if (cargoOffset >= 0)
                        cargoAddress = region.BaseAddress + chunkOffset + (ulong)cargoOffset;
                }

                if (firstAddress != 0 && cargoAddress != 0)
                {
                    return new ClientCargoMemoryVerification
                    {
                        FirstCoidAddress = firstAddress,
                        ItemCoidAddress = firstAddress,
                        CargoBlockAddress = cargoAddress
                    };
                }
            }
        }

        if (firstAddress == 0)
            throw new InvalidOperationException($"Client memory does not contain COID {coid}.");

        throw new InvalidOperationException($"Client memory contains COID {coid}, but no cargo slot matched {x},{y}.");
    }

    private IEnumerable<MemoryRegion> EnumerateReadableRegions()
    {
        var address = 0UL;
        var maxAddress = Environment.Is64BitOperatingSystem ? 0x00007ffffffeffffUL : 0xffffffffUL;

        while (address < maxAddress)
        {
            if (VirtualQueryEx(_handle, (UIntPtr)address, out var info, (UIntPtr)Marshal.SizeOf<MemoryBasicInformation>()) == UIntPtr.Zero)
            {
                address += 0x10000;
                continue;
            }

            var baseAddress = info.BaseAddress.ToUInt64();
            var size = info.RegionSize.ToUInt64();
            var nextAddress = baseAddress + Math.Max(size, 0x1000);

            if (info.State == MemCommit
                && (info.Protect & PageNoAccess) == 0
                && (info.Protect & PageGuard) == 0
                && size > 0)
            {
                yield return new MemoryRegion(baseAddress, size);
            }

            if (nextAddress <= address)
                yield break;

            address = nextAddress;
        }
    }

    private byte[] Read(ulong address, int size)
    {
        var buffer = new byte[size];
        if (!ReadProcessMemory(_handle, (UIntPtr)address, buffer, (UIntPtr)buffer.Length, out var bytesRead))
            return [];

        var read = (int)bytesRead.ToUInt64();
        if (read == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, read);
        return buffer;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
            CloseHandle(_handle);
    }

    private readonly record struct MemoryRegion(ulong BaseAddress, ulong Size);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public UIntPtr BaseAddress;
        public UIntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(IntPtr processHandle, UIntPtr address, out MemoryBasicInformation buffer, UIntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr processHandle, UIntPtr baseAddress, byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
}

public sealed record ClientProcessInfo(int ProcessId, string ProcessName);
