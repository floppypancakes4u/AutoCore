namespace AutoCore.Dev;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

public sealed class ClientInventoryGrabBreakpointWatcher
{
    private const ulong ImageBase = 0x00400000;
    private const ulong ClientRecvInventoryGrabAddress = 0x00811be0;
    private const uint ExceptionDebugEvent = 1;
    private const uint OutputDebugStringEvent = 8;
    private const uint ContinueStatusHandled = 0x00010002;
    private const uint ContinueStatusNotHandled = 0x80010001;
    private const uint ExceptionBreakpoint = 0x80000003;
    private const uint ProcessAllAccess = 0x001F0FFF;
    private const uint ThreadGetContext = 0x0008;
    private const uint ThreadSetContext = 0x0010;
    private const uint Wow64ContextFull = 0x00010007;

    public ClientInventoryGrabBreakpointHit WaitForHit(
        int processId,
        TimeSpan timeout,
        TaskCompletionSource<ClientInventoryGrabBreakpointReady>? ready = null,
        Action<string>? trace = null)
    {
        using var process = Process.GetProcessById(processId);
        var moduleBase = (ulong)process.MainModule!.BaseAddress.ToInt64();
        var breakpointAddress = moduleBase + (ClientRecvInventoryGrabAddress - ImageBase);
        var processHandle = OpenProcess(ProcessAllAccess, false, processId);
        if (processHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open process {processId}.");

        var attached = false;
        var armed = false;
        var postHitDeadline = DateTimeOffset.MinValue;
        byte originalByte = 0;
        ClientInventoryGrabBreakpointHit? hit = null;
        var debugStrings = new List<string>();

        try
        {
            if (!DebugActiveProcess((uint)processId))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to attach debugger to process {processId}.");

            attached = true;
            DebugSetProcessKillOnExit(false);
            trace?.Invoke($"Debugger attached to PID {processId}; draining attach events.");

            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var activeDeadline = hit == null ? deadline : postHitDeadline;
                var waitMs = (uint)Math.Min(1000, Math.Max(1, (activeDeadline - DateTimeOffset.UtcNow).TotalMilliseconds));
                if (!WaitForDebugEvent(out var debugEvent, waitMs))
                {
                    if (hit != null && DateTimeOffset.UtcNow >= postHitDeadline)
                        return hit with { DebugStrings = debugStrings.ToArray() };

                    continue;
                }

                var signalReadyAfterContinue = false;

                var continueStatus = ContinueStatusHandled;

                try
                {
                    if (debugEvent.dwDebugEventCode == OutputDebugStringEvent)
                    {
                        var debugString = ReadDebugString(processHandle, debugEvent.u.DebugString);
                        if (!string.IsNullOrWhiteSpace(debugString))
                            debugStrings.Add(debugString.TrimEnd('\0', '\r', '\n'));
                        continue;
                    }

                    if (debugEvent.dwDebugEventCode != ExceptionDebugEvent)
                        continue;

                    var exception = debugEvent.u.Exception;
                    if (exception.ExceptionRecord.ExceptionCode != ExceptionBreakpoint)
                        continue;

                    var exceptionAddress = exception.ExceptionRecord.ExceptionAddress.ToUInt64();
                    if (!armed)
                    {
                        originalByte = ArmBreakpoint(processHandle, breakpointAddress);
                        armed = true;
                        signalReadyAfterContinue = true;
                        continue;
                    }

                    var context = GetWow64Context(debugEvent.dwThreadId);
                    var hitOurBreakpoint = exceptionAddress == breakpointAddress
                        || exceptionAddress == breakpointAddress + 1
                        || context.Eip == breakpointAddress
                        || context.Eip == breakpointAddress + 1;
                    if (!hitOurBreakpoint)
                    {
                        trace?.Invoke($"Ignoring breakpoint at exception=0x{exceptionAddress:X}, eip=0x{context.Eip:X}.");
                        continueStatus = ContinueStatusNotHandled;
                        continue;
                    }

                    trace?.Invoke($"Client_RecvInventoryGrab breakpoint hit at exception=0x{exceptionAddress:X}, eip=0x{context.Eip:X}.");

                    var packetAddress = context.Ebx;
                    var bytes = ReadProcessBytes(processHandle, packetAddress, 0x40);

                    RestoreBreakpoint(processHandle, breakpointAddress, originalByte);
                    armed = false;

                    context.Eip = (uint)breakpointAddress;
                    SetWow64Context(debugEvent.dwThreadId, context);

                    hit = new ClientInventoryGrabBreakpointHit(
                        breakpointAddress,
                        packetAddress,
                        bytes,
                        DecodeResponse(bytes),
                        []);
                    postHitDeadline = DateTimeOffset.UtcNow.AddSeconds(3);
                }
                finally
                {
                    ContinueDebugEvent(debugEvent.dwProcessId, debugEvent.dwThreadId, continueStatus);

                    if (signalReadyAfterContinue)
                    {
                        trace?.Invoke($"Breakpoint armed at 0x{breakpointAddress:X}; client continued.");
                        ready?.TrySetResult(new ClientInventoryGrabBreakpointReady(breakpointAddress));
                    }
                }

                if (hit != null && DateTimeOffset.UtcNow >= postHitDeadline)
                    return hit with { DebugStrings = debugStrings.ToArray() };
            }

            if (hit != null)
                return hit with { DebugStrings = debugStrings.ToArray() };

            throw new TimeoutException($"Timed out waiting for Client_RecvInventoryGrab at 0x{breakpointAddress:X}.");
        }
        finally
        {
            if (armed)
            {
                RestoreBreakpoint(processHandle, breakpointAddress, originalByte);
                trace?.Invoke($"Breakpoint restored at 0x{breakpointAddress:X}.");
            }

            if (attached)
            {
                DebugActiveProcessStop((uint)processId);
                trace?.Invoke($"Debugger detached from PID {processId}.");
            }

            CloseHandle(processHandle);
        }
    }

    private static InventoryGrabResponseSnapshot DecodeResponse(byte[] bytes)
    {
        return new InventoryGrabResponseSnapshot
        {
            Opcode = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0,
            ItemCoid = bytes.Length >= 16 ? BitConverter.ToInt64(bytes, 8) : -1,
            ItemGlobal = bytes.Length > 0x10 && bytes[0x10] != 0,
            InventoryType = bytes.Length > 0x18 ? bytes[0x18] : (byte)0,
            Quantity = bytes.Length >= 0x20 ? BitConverter.ToInt32(bytes, 0x1c) : 0,
            AddToExistingItem = bytes.Length > 0x20 && bytes[0x20] != 0,
            InventoryPositionX = bytes.Length >= 0x2c ? BitConverter.ToInt32(bytes, 0x28) : 0,
            InventoryPositionY = bytes.Length >= 0x30 ? BitConverter.ToInt32(bytes, 0x2c) : 0,
            WasSuccessful = bytes.Length > 0x38 && bytes[0x38] != 0
        };
    }

    private static byte ArmBreakpoint(IntPtr processHandle, ulong address)
    {
        var original = ReadProcessBytes(processHandle, address, 1)[0];
        WriteProcessBytes(processHandle, address, [0xCC]);
        return original;
    }

    private static void RestoreBreakpoint(IntPtr processHandle, ulong address, byte originalByte)
    {
        WriteProcessBytes(processHandle, address, [originalByte]);
    }

    private static byte[] ReadProcessBytes(IntPtr processHandle, ulong address, int length)
    {
        var buffer = new byte[length];
        if (!ReadProcessMemory(processHandle, (UIntPtr)address, buffer, (UIntPtr)buffer.Length, out var bytesRead))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to read process memory at 0x{address:X}.");

        var read = (int)bytesRead.ToUInt64();
        if (read == buffer.Length)
            return buffer;

        Array.Resize(ref buffer, read);
        return buffer;
    }

    private static string ReadDebugString(IntPtr processHandle, OutputDebugStringInfo info)
    {
        var length = Math.Min((int)info.nDebugStringLength, 4096);
        if (length == 0 || info.lpDebugStringData == UIntPtr.Zero)
            return string.Empty;

        var bytes = ReadProcessBytes(processHandle, info.lpDebugStringData.ToUInt64(), length);
        return info.fUnicode == 0
            ? System.Text.Encoding.Default.GetString(bytes)
            : System.Text.Encoding.Unicode.GetString(bytes);
    }

    private static void WriteProcessBytes(IntPtr processHandle, ulong address, byte[] bytes)
    {
        if (!WriteProcessMemory(processHandle, (UIntPtr)address, bytes, (UIntPtr)bytes.Length, out var bytesWritten)
            || bytesWritten.ToUInt64() != (ulong)bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to write process memory at 0x{address:X}.");

        FlushInstructionCache(processHandle, (UIntPtr)address, (UIntPtr)bytes.Length);
    }

    private static Wow64Context GetWow64Context(uint threadId)
    {
        var thread = OpenThread(ThreadGetContext, false, threadId);
        if (thread == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open thread {threadId}.");

        try
        {
            var context = new Wow64Context
            {
                ContextFlags = Wow64ContextFull,
                FloatSave = new FloatingSaveArea { RegisterArea = new byte[80] },
                ExtendedRegisters = new byte[512]
            };
            if (!Wow64GetThreadContext(thread, ref context))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to read WOW64 context for thread {threadId}.");

            return context;
        }
        finally
        {
            CloseHandle(thread);
        }
    }

    private static void SetWow64Context(uint threadId, Wow64Context context)
    {
        var thread = OpenThread(ThreadSetContext, false, threadId);
        if (thread == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open thread {threadId}.");

        try
        {
            if (!Wow64SetThreadContext(thread, ref context))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to write WOW64 context for thread {threadId}.");
        }
        finally
        {
            CloseHandle(thread);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DebugEvent
    {
        [FieldOffset(0)]
        public uint dwDebugEventCode;

        [FieldOffset(4)]
        public uint dwProcessId;

        [FieldOffset(8)]
        public uint dwThreadId;

        [FieldOffset(16)]
        public DebugEventUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DebugEventUnion
    {
        [FieldOffset(0)]
        public ExceptionDebugInfo Exception;

        [FieldOffset(0)]
        public OutputDebugStringInfo DebugString;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionDebugInfo
    {
        public ExceptionRecord ExceptionRecord;
        public uint dwFirstChance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionRecord
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public IntPtr ExceptionRecordPointer;
        public UIntPtr ExceptionAddress;
        public uint NumberParameters;
        public UIntPtr ExceptionInformation0;
        public UIntPtr ExceptionInformation1;
        public UIntPtr ExceptionInformation2;
        public UIntPtr ExceptionInformation3;
        public UIntPtr ExceptionInformation4;
        public UIntPtr ExceptionInformation5;
        public UIntPtr ExceptionInformation6;
        public UIntPtr ExceptionInformation7;
        public UIntPtr ExceptionInformation8;
        public UIntPtr ExceptionInformation9;
        public UIntPtr ExceptionInformation10;
        public UIntPtr ExceptionInformation11;
        public UIntPtr ExceptionInformation12;
        public UIntPtr ExceptionInformation13;
        public UIntPtr ExceptionInformation14;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputDebugStringInfo
    {
        public UIntPtr lpDebugStringData;
        public ushort fUnicode;
        public ushort nDebugStringLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FloatingSaveArea
    {
        public uint ControlWord;
        public uint StatusWord;
        public uint TagWord;
        public uint ErrorOffset;
        public uint ErrorSelector;
        public uint DataOffset;
        public uint DataSelector;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
        public byte[] RegisterArea;

        public uint Cr0NpxState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Wow64Context
    {
        public uint ContextFlags;
        public uint Dr0;
        public uint Dr1;
        public uint Dr2;
        public uint Dr3;
        public uint Dr6;
        public uint Dr7;
        public FloatingSaveArea FloatSave;
        public uint SegGs;
        public uint SegFs;
        public uint SegEs;
        public uint SegDs;
        public uint Edi;
        public uint Esi;
        public uint Ebx;
        public uint Edx;
        public uint Ecx;
        public uint Eax;
        public uint Ebp;
        public uint Eip;
        public uint SegCs;
        public uint EFlags;
        public uint Esp;
        public uint SegSs;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] ExtendedRegisters;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, UIntPtr lpBaseAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DebugActiveProcess(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DebugActiveProcessStop(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DebugSetProcessKillOnExit(bool killOnExit);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WaitForDebugEvent(out DebugEvent lpDebugEvent, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ContinueDebugEvent(uint dwProcessId, uint dwThreadId, uint dwContinueStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Wow64GetThreadContext(IntPtr hThread, ref Wow64Context lpContext);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Wow64SetThreadContext(IntPtr hThread, ref Wow64Context lpContext);
}

public sealed record ClientInventoryGrabBreakpointHit(
    ulong BreakpointAddress,
    ulong PacketAddress,
    byte[] PacketBytes,
    InventoryGrabResponseSnapshot Response,
    string[] DebugStrings);

public sealed record ClientInventoryGrabBreakpointReady(ulong BreakpointAddress);

public sealed class InventoryGrabResponseSnapshot
{
    public uint Opcode { get; set; }
    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; }
    public byte InventoryType { get; set; }
    public int Quantity { get; set; }
    public bool AddToExistingItem { get; set; }
    public int InventoryPositionX { get; set; }
    public int InventoryPositionY { get; set; }
    public bool WasSuccessful { get; set; }
}
