#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstring>

#pragma comment(lib, "minhook.x32.lib")
#include "minhook.h"

// Path A (CreateVehicle equip → SetWheelset → optional Havok activate) non-freezing capture.
// Also GhostObject waiting-bind apply (FUN_005b0ed0 @ 0x005B0ED0) for AV 0x005B0EFF.
// Injected MinHook detours: log only, never break/suspend the process.
// Client build: autoassault 0.0.14.117.2007.2.1.11 image base 0x400000.

namespace
{
    constexpr uintptr_t EquipFromCreateRva = 0x00104480; // 0x00504480
    constexpr uintptr_t SetWheelsetRva = 0x000FEA90;      // 0x004FEA90
    constexpr uintptr_t CreateVehicleActionRva = 0x000FB660; // 0x004FB660
    constexpr uintptr_t ActivateEnterWorldRva = 0x00103F30;  // 0x00503F30
    constexpr uintptr_t RecvCreateVehicleRva = 0x0040A4B0;   // 0x0080A4B0
    // GhostObject apply after "Assigned a ghost to waiting" (crash 0x005B0EFF).
    constexpr uintptr_t GhostApplyRva = 0x001B0ED0;         // 0x005B0ED0 FUN_005b0ed0
    constexpr uintptr_t GhostOnAddRva = 0x001B0D70;         // 0x005B0D70 GhostObject_OnGhostAdd

    // First 8 bytes at each VA (Ghidra / client PE).
    constexpr unsigned char EquipPrologue[] = { 0x51, 0x53, 0x8b, 0x5c, 0x24, 0x0c, 0x55, 0x8b };
    constexpr unsigned char SetWheelsetPrologue[] = { 0x56, 0x8b, 0xf1, 0x8b, 0x46, 0x04, 0x8b, 0x48 };
    constexpr unsigned char CreateActionPrologue[] = { 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00, 0x6a, 0xff };
    constexpr unsigned char ActivatePrologue[] = { 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00, 0x6a, 0xff };
    constexpr unsigned char RecvPrologue[] = { 0x56, 0x57, 0x8b, 0xf8, 0x8b, 0x43, 0x04, 0x6a };
    constexpr unsigned char GhostApplyPrologue[] = { 0x55, 0x8b, 0xec, 0x83, 0xe4, 0xf0, 0x83, 0xec };
    constexpr unsigned char GhostOnAddPrologue[] = { 0x8b, 0xc1, 0x83, 0x78, 0x50, 0x00, 0x74, 0x11 };

    // GhostObject layout (client RE)
    constexpr size_t GhostBoundObjectOffset = 0x50;   // game object pointer
    constexpr size_t GhostCreateBufOffset = 0x5C;    // pending create/state blob
    constexpr size_t GhostTfidCoidOffset = 0x40;     // int64 coid (low/high dwords at +0x40/+0x44)
    constexpr size_t GhostGlobalFlagOffset = 0x48;   // bool at ghost+0x48 (param_1[0x12] as int*)
    // Create blob (FUN_005b0e30): opcode @0, cbid @+8
    constexpr size_t BufOpcodeOffset = 0x00;
    constexpr size_t BufCbidOffset = 0x08;
    // Game object: COID pair often at +0x160/+0x164 (logged after assign)
    constexpr size_t ObjectCoidLoOffset = 0x160;
    constexpr size_t ObjectCoidHiOffset = 0x164;
    // vtable slot used by FUN_005b0ed0 before crash
    constexpr size_t ObjectVfuncIfaceOffset = 0x1C8;

    constexpr size_t VehicleWheelsetOffset = 0x258;
    // CreateVehicle fixed message layout (FUN_005F5AD0 / EquipFromCreate param_2)
    constexpr size_t PacketRootOpcodeOffset = 0x00;       // 0x201D CreateVehicle / 0x201E Extended
    constexpr size_t PacketRootCbidOffset = 0x04;
    constexpr size_t PacketIsInInventoryOffset = 0xA2;    // byte
    constexpr size_t PacketIsInventoryOffset = 0x151;     // byte (gates equip if set on vehicle)
    constexpr size_t PacketIsActiveOffset = 0x152;        // byte
    constexpr size_t PacketWheelsetOpcodeOffset = 0x458;  // 0x201B
    constexpr size_t PacketWheelsetCbidOffset = 0x45C;
    constexpr size_t PacketWheelsetTfidCoidOffset = 0x4E8; // nested ObjectId COID (8)
    constexpr size_t PacketWheelsetTfidGlobalOffset = 0x4F0; // bool + pad

    volatile LONG g_hookState = 0; // 0 idle, 1 installing, 2 installed
    volatile LONG g_hitCount = 0;
    constexpr LONG MaxHits = 5000;

    CRITICAL_SECTION g_logLock;
    bool g_logLockInit = false;
    char g_logPath[MAX_PATH] = {};

    void InitLogPath()
    {
        char temp[MAX_PATH];
        GetTempPathA(MAX_PATH, temp);
        sprintf_s(g_logPath, "%sAutoCorePathA", temp);
        CreateDirectoryA(g_logPath, nullptr);
        strcat_s(g_logPath, "\\hits.jsonl");
    }

    bool IsReadable(const void* address, size_t size)
    {
        MEMORY_BASIC_INFORMATION info = {};
        if (VirtualQuery(address, &info, sizeof(info)) == 0)
            return false;
        if (info.State != MEM_COMMIT || (info.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0)
            return false;
        const auto regionEnd = reinterpret_cast<uintptr_t>(info.BaseAddress) + info.RegionSize;
        return reinterpret_cast<uintptr_t>(address) + size <= regionEnd;
    }

    void* ReadPtr(void* base, size_t offset)
    {
        auto* p = reinterpret_cast<unsigned char*>(base) + offset;
        if (!IsReadable(p, sizeof(void*)))
            return reinterpret_cast<void*>(static_cast<uintptr_t>(-1));
        return *reinterpret_cast<void**>(p);
    }

    int ReadInt(void* base, size_t offset)
    {
        auto* p = reinterpret_cast<unsigned char*>(base) + offset;
        if (!IsReadable(p, sizeof(int)))
            return 0x7FFFFFFF;
        return *reinterpret_cast<int*>(p);
    }

    long long ReadLong(void* base, size_t offset)
    {
        auto* p = reinterpret_cast<unsigned char*>(base) + offset;
        if (!IsReadable(p, sizeof(long long)))
            return 0x7FFFFFFFFFFFFFFFLL;
        return *reinterpret_cast<long long*>(p);
    }

    unsigned char ReadByte(void* base, size_t offset)
    {
        auto* p = reinterpret_cast<unsigned char*>(base) + offset;
        if (!IsReadable(p, 1))
            return 0xFF;
        return *p;
    }

    void HexDump(void* base, size_t offset, size_t len, char* out, size_t outChars)
    {
        out[0] = 0;
        auto* p = reinterpret_cast<unsigned char*>(base) + offset;
        if (!IsReadable(p, len) || outChars < len * 2 + 1)
            return;
        for (size_t i = 0; i < len; ++i)
            sprintf_s(out + i * 2, outChars - i * 2, "%02X", p[i]);
    }

    void AppendJsonLine(const char* line)
    {
        if (!g_logLockInit)
            return;

        const LONG n = InterlockedIncrement(&g_hitCount);
        if (n > MaxHits)
            return;

        EnterCriticalSection(&g_logLock);
        HANDLE file = CreateFileA(g_logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE)
        {
            SetFilePointer(file, 0, nullptr, FILE_END);
            DWORD written = 0;
            WriteFile(file, line, static_cast<DWORD>(strlen(line)), &written, nullptr);
            WriteFile(file, "\r\n", 2, &written, nullptr);
            CloseHandle(file);
        }
        LeaveCriticalSection(&g_logLock);
    }

    void LogEvent(const char* ev, void* vehicle, void* packetOrWs, void* v258Before, void* v258After, int cbid)
    {
        char line[512];
        const DWORD tick = GetTickCount();
        sprintf_s(line,
            "{\"t\":%lu,\"ev\":\"%s\",\"veh\":\"%p\",\"arg\":\"%p\",\"v258_before\":\"%p\",\"v258_after\":\"%p\",\"cbid\":%d,\"hit\":%ld}",
            static_cast<unsigned long>(tick),
            ev,
            vehicle,
            packetOrWs,
            v258Before,
            v258After,
            cbid,
            static_cast<long>(g_hitCount + 1));
        AppendJsonLine(line);
    }

    bool VerifyAndHook(uintptr_t imageBase, uintptr_t rva, const unsigned char* expected, size_t len,
        LPVOID detour, LPVOID* target, LPVOID* original)
    {
        *target = reinterpret_cast<LPVOID>(imageBase + rva);
        MEMORY_BASIC_INFORMATION memory = {};
        if (VirtualQuery(*target, &memory, sizeof(memory)) == 0 || memory.State != MEM_COMMIT ||
            (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0)
            return false;
        if (memcmp(*target, expected, len) != 0)
            return false;
        if (MH_CreateHook(*target, detour, original) != MH_OK)
            return false;
        if (MH_EnableHook(*target) != MH_OK)
        {
            MH_RemoveHook(*target);
            return false;
        }
        return true;
    }

    // --- SetWheelset (thiscall): this=vehicle, arg1=wheelset object ---
    using SetWheelsetFn = void(__fastcall*)(void* vehicle, void* edx, void* wheelset);
    SetWheelsetFn pOrigSetWheelset = nullptr;
    LPVOID pTargetSetWheelset = nullptr;

    void __fastcall HookedSetWheelset(void* vehicle, void* edx, void* wheelset)
    {
        void* before = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        pOrigSetWheelset(vehicle, edx, wheelset);
        void* after = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        LogEvent("SetWheelset", vehicle, wheelset, before, after, -1);
    }

    // --- EquipFromCreate (thiscall): this=vehicle, packet, mode, ... ---
    using EquipFn = void(__fastcall*)(void* vehicle, void* edx, void* packet, int mode, int p4, int p5, int p6);
    EquipFn pOrigEquip = nullptr;
    LPVOID pTargetEquip = nullptr;

    void __fastcall HookedEquip(void* vehicle, void* edx, void* packet, int mode, int p4, int p5, int p6)
    {
        void* before = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        const int rootOpcode = packet ? ReadInt(packet, PacketRootOpcodeOffset) : 0;
        const int rootCbid = packet ? ReadInt(packet, PacketRootCbidOffset) : 0x7FFFFFFF;
        const int wheelCbid = packet ? ReadInt(packet, PacketWheelsetCbidOffset) : 0x7FFFFFFF;
        const int wheelOpcode = packet ? ReadInt(packet, PacketWheelsetOpcodeOffset) : 0;
        const long long wheelTfid = packet ? ReadLong(packet, PacketWheelsetTfidCoidOffset) : 0;
        const int wheelGlobal = packet ? static_cast<int>(ReadByte(packet, PacketWheelsetTfidGlobalOffset)) : -1;
        const int isInv = packet ? static_cast<int>(ReadByte(packet, PacketIsInventoryOffset)) : -1;
        const int isActive = packet ? static_cast<int>(ReadByte(packet, PacketIsActiveOffset)) : -1;
        const int isInInv = packet ? static_cast<int>(ReadByte(packet, PacketIsInInventoryOffset)) : -1;

        char nestHex[48] = {};
        if (packet && wheelCbid <= 0)
            HexDump(packet, PacketWheelsetOpcodeOffset, 20, nestHex, sizeof(nestHex));

        pOrigEquip(vehicle, edx, packet, mode, p4, p5, p6);
        void* after = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;

        char line[960];
        const DWORD tick = GetTickCount();
        sprintf_s(line,
            "{\"t\":%lu,\"ev\":\"EquipFromCreate\",\"veh\":\"%p\",\"pkt\":\"%p\","
            "\"v258_before\":\"%p\",\"v258_after\":\"%p\","
            "\"root_opcode\":%d,\"root_cbid\":%d,"
            "\"wheel_cbid\":%d,\"wheel_opcode\":%d,\"wheel_tfid\":%lld,\"wheel_global\":%d,"
            "\"isInventory\":%d,\"isActive\":%d,\"isInInventory\":%d,"
            "\"mode\":%d,\"p4\":%d,\"p5\":%d,\"p6\":%d,"
            "\"nest_hex\":\"%s\",\"hit\":%ld}",
            static_cast<unsigned long>(tick), vehicle, packet, before, after,
            rootOpcode, rootCbid, wheelCbid, wheelOpcode,
            static_cast<long long>(wheelTfid), wheelGlobal,
            isInv, isActive, isInInv,
            mode, p4, p5, p6,
            nestHex,
            static_cast<long>(g_hitCount + 1));
        AppendJsonLine(line);
    }

    // --- CreateVehicleAction (thiscall) ---
    using CreateActionFn = void(__fastcall*)(void* vehicle, void* edx);
    CreateActionFn pOrigCreateAction = nullptr;
    LPVOID pTargetCreateAction = nullptr;

    void __fastcall HookedCreateAction(void* vehicle, void* edx)
    {
        void* before = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        LogEvent("CreateVehicleAction_enter", vehicle, nullptr, before, before, -1);
        pOrigCreateAction(vehicle, edx);
        void* after = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        LogEvent("CreateVehicleAction_exit", vehicle, nullptr, before, after, -1);
    }

    // --- Activate enter-world FUN_00503F30 (thiscall) ---
    using ActivateFn = void(__fastcall*)(void* vehicle, void* edx);
    ActivateFn pOrigActivate = nullptr;
    LPVOID pTargetActivate = nullptr;

    void __fastcall HookedActivate(void* vehicle, void* edx)
    {
        void* before = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        LogEvent("ActivateEnterWorld_enter", vehicle, nullptr, before, before, -1);
        pOrigActivate(vehicle, edx);
        void* after = vehicle ? ReadPtr(vehicle, VehicleWheelsetOffset) : nullptr;
        LogEvent("ActivateEnterWorld_exit", vehicle, nullptr, before, after, -1);
    }

    // --- Recv CreateVehicle 0x201D: packet in EBX, context in EAX (register convention) ---
    // MinHook trampoline preserves registers when calling original; our detour is entered as
    // a normal function with the same entry registers. We capture EBX/EAX via naked helper.
    using RecvFn = void(__cdecl*)();
    RecvFn pOrigRecv = nullptr;
    LPVOID pTargetRecv = nullptr;

    // Compiler helper: read current EBX/EAX after MinHook transfers control.
    // The original is called with matching registers via a small asm thunk.
    void HookedRecvBody(void* packet, void* context)
    {
        int rootCbid = packet ? ReadInt(packet, PacketRootCbidOffset) : 0x7FFFFFFF;
        int wheelCbid = packet ? ReadInt(packet, PacketWheelsetCbidOffset) : 0x7FFFFFFF;
        int wheelOpcode = packet ? ReadInt(packet, PacketWheelsetOpcodeOffset) : 0;
        char line[512];
        const DWORD tick = GetTickCount();
        sprintf_s(line,
            "{\"t\":%lu,\"ev\":\"RecvCreateVehicle\",\"pkt\":\"%p\",\"ctx\":\"%p\","
            "\"root_cbid\":%d,\"wheel_cbid\":%d,\"wheel_opcode\":%d,\"hit\":%ld}",
            static_cast<unsigned long>(tick), packet, context, rootCbid, wheelCbid, wheelOpcode,
            static_cast<long>(g_hitCount + 1));
        AppendJsonLine(line);
    }

    // Naked-style via __declspec(naked) not available in x64; we are x86.
    __declspec(naked) void HookedRecv()
    {
        __asm {
            pushad
            pushfd
            // packet was in EBX, context in EAX on entry to original
            push eax
            push ebx
            call HookedRecvBody
            add esp, 8
            popfd
            popad
            // jump to original trampoline (pointer)
            jmp dword ptr [pOrigRecv]
        }
    }

    // --- GhostObject apply (thiscall): this=ghost — AV 0x005B0EFF when iface null ---
    using GhostApplyFn = void(__fastcall*)(void* ghost, void* edx);
    GhostApplyFn pOrigGhostApply = nullptr;
    LPVOID pTargetGhostApply = nullptr;

    void* CallObjectIface(void* object)
    {
        if (!object || !IsReadable(object, sizeof(void*)))
            return reinterpret_cast<void*>(static_cast<uintptr_t>(-2));
        void* vtable = *reinterpret_cast<void**>(object);
        if (!vtable || !IsReadable(vtable, ObjectVfuncIfaceOffset + sizeof(void*)))
            return reinterpret_cast<void*>(static_cast<uintptr_t>(-3));
        auto* slot = reinterpret_cast<void**>(reinterpret_cast<unsigned char*>(vtable) + ObjectVfuncIfaceOffset);
        auto fn = reinterpret_cast<void*(__fastcall*)(void*, void*)>(*slot);
        if (!fn)
            return reinterpret_cast<void*>(static_cast<uintptr_t>(-4));
        // Call carefully: if object is corrupt this may still AV, but only after we logged enter.
        return fn(object, nullptr);
    }

    void __fastcall HookedGhostApply(void* ghost, void* edx)
    {
        void* bound = ghost ? ReadPtr(ghost, GhostBoundObjectOffset) : nullptr;
        void* buf = ghost ? ReadPtr(ghost, GhostCreateBufOffset) : nullptr;
        const long long tfidLo = ghost ? ReadLong(ghost, GhostTfidCoidOffset) : 0;
        const int globalFlag = ghost ? static_cast<int>(ReadByte(ghost, GhostGlobalFlagOffset)) : -1;
        const int bufOpcode = buf && buf != reinterpret_cast<void*>(static_cast<uintptr_t>(-1))
            ? ReadInt(buf, BufOpcodeOffset) : 0x7FFFFFFF;
        const int bufCbid = buf && buf != reinterpret_cast<void*>(static_cast<uintptr_t>(-1))
            ? ReadInt(buf, BufCbidOffset) : 0x7FFFFFFF;
        const int objCoidLo = bound && bound != reinterpret_cast<void*>(static_cast<uintptr_t>(-1))
            ? ReadInt(bound, ObjectCoidLoOffset) : 0x7FFFFFFF;
        const int objCoidHi = bound && bound != reinterpret_cast<void*>(static_cast<uintptr_t>(-1))
            ? ReadInt(bound, ObjectCoidHiOffset) : 0x7FFFFFFF;

        char line[768];
        // Log raw fields first (before any speculative vcall) so a bad object still leaves a trail.
        sprintf_s(line,
            "{\"t\":%lu,\"ev\":\"GhostApply_enter\",\"ghost\":\"%p\",\"bound\":\"%p\",\"buf\":\"%p\","
            "\"tfid\":%lld,\"global\":%d,\"buf_opcode\":%d,\"buf_cbid\":%d,"
            "\"obj_coid_lo\":%d,\"obj_coid_hi\":%d,\"hit\":%ld}",
            static_cast<unsigned long>(GetTickCount()),
            ghost,
            bound,
            buf,
            static_cast<long long>(tfidLo),
            globalFlag,
            bufOpcode,
            bufCbid,
            objCoidLo,
            objCoidHi,
            static_cast<long>(g_hitCount + 1));
        AppendJsonLine(line);

        // FUN_005b0ed0 only touches iface when both bound+buf non-null.
        const bool willProbe = bound
            && bound != reinterpret_cast<void*>(static_cast<uintptr_t>(-1))
            && buf
            && buf != reinterpret_cast<void*>(static_cast<uintptr_t>(-1));

        if (willProbe)
        {
            void* iface = CallObjectIface(bound);
            const int ifaceOk = (iface != nullptr
                && iface != reinterpret_cast<void*>(static_cast<uintptr_t>(-2))
                && iface != reinterpret_cast<void*>(static_cast<uintptr_t>(-3))
                && iface != reinterpret_cast<void*>(static_cast<uintptr_t>(-4))) ? 1 : 0;

            sprintf_s(line,
                "{\"t\":%lu,\"ev\":\"%s\",\"ghost\":\"%p\",\"iface\":\"%p\",\"iface_ok\":%d,"
                "\"tfid\":%lld,\"buf_opcode\":%d,\"buf_cbid\":%d,\"obj_coid_lo\":%d,\"hit\":%ld}",
                static_cast<unsigned long>(GetTickCount()),
                ifaceOk ? "GhostApply_iface" : "GhostApply_CRASH_IMMINENT",
                ghost,
                iface,
                ifaceOk,
                static_cast<long long>(tfidLo),
                bufOpcode,
                bufCbid,
                objCoidLo,
                static_cast<long>(g_hitCount + 1));
            AppendJsonLine(line);

            // Skip original when we know it will AV — keeps the client alive for more repros.
            if (ifaceOk == 0)
            {
                sprintf_s(line,
                    "{\"t\":%lu,\"ev\":\"GhostApply_SKIPPED_NULL_IFACE\",\"ghost\":\"%p\",\"hit\":%ld}",
                    static_cast<unsigned long>(GetTickCount()),
                    ghost,
                    static_cast<long>(g_hitCount + 1));
                AppendJsonLine(line);
                return;
            }
        }

        pOrigGhostApply(ghost, edx);

        sprintf_s(line,
            "{\"t\":%lu,\"ev\":\"GhostApply_exit\",\"ghost\":\"%p\",\"hit\":%ld}",
            static_cast<unsigned long>(GetTickCount()),
            ghost,
            static_cast<long>(g_hitCount + 1));
        AppendJsonLine(line);
    }

    // --- GhostObject_OnGhostAdd (thiscall): this=ghost ---
    using GhostOnAddFn = int(__fastcall*)(void* ghost, void* edx);
    GhostOnAddFn pOrigGhostOnAdd = nullptr;
    LPVOID pTargetGhostOnAdd = nullptr;

    int __fastcall HookedGhostOnAdd(void* ghost, void* edx)
    {
        void* bound = ghost ? ReadPtr(ghost, GhostBoundObjectOffset) : nullptr;
        const long long tfidLo = ghost ? ReadLong(ghost, GhostTfidCoidOffset) : 0;
        char line[320];
        sprintf_s(line,
            "{\"t\":%lu,\"ev\":\"GhostOnAdd\",\"ghost\":\"%p\",\"bound\":\"%p\",\"tfid\":%lld,\"hit\":%ld}",
            static_cast<unsigned long>(GetTickCount()),
            ghost,
            bound,
            static_cast<long long>(tfidLo),
            static_cast<long>(g_hitCount + 1));
        AppendJsonLine(line);
        return pOrigGhostOnAdd(ghost, edx);
    }

    void RemoveAllHooks()
    {
        if (pTargetSetWheelset) { MH_DisableHook(pTargetSetWheelset); MH_RemoveHook(pTargetSetWheelset); pTargetSetWheelset = nullptr; }
        if (pTargetEquip) { MH_DisableHook(pTargetEquip); MH_RemoveHook(pTargetEquip); pTargetEquip = nullptr; }
        if (pTargetCreateAction) { MH_DisableHook(pTargetCreateAction); MH_RemoveHook(pTargetCreateAction); pTargetCreateAction = nullptr; }
        if (pTargetActivate) { MH_DisableHook(pTargetActivate); MH_RemoveHook(pTargetActivate); pTargetActivate = nullptr; }
        if (pTargetRecv) { MH_DisableHook(pTargetRecv); MH_RemoveHook(pTargetRecv); pTargetRecv = nullptr; }
        if (pTargetGhostApply) { MH_DisableHook(pTargetGhostApply); MH_RemoveHook(pTargetGhostApply); pTargetGhostApply = nullptr; }
        if (pTargetGhostOnAdd) { MH_DisableHook(pTargetGhostOnAdd); MH_RemoveHook(pTargetGhostOnAdd); pTargetGhostOnAdd = nullptr; }
        pOrigSetWheelset = nullptr;
        pOrigEquip = nullptr;
        pOrigCreateAction = nullptr;
        pOrigActivate = nullptr;
        pOrigRecv = nullptr;
        pOrigGhostApply = nullptr;
        pOrigGhostOnAdd = nullptr;
    }
}

extern "C" __declspec(dllexport) DWORD WINAPI SetupPathAHook(LPVOID /*unused*/)
{
    const LONG prior = InterlockedCompareExchange(&g_hookState, 1, 0);
    if (prior == 2)
        return 1;
    if (prior != 0)
        return 0;

    if (!g_logLockInit)
    {
        InitializeCriticalSection(&g_logLock);
        g_logLockInit = true;
    }
    InitLogPath();

    char boot[320];
    sprintf_s(boot, "{\"t\":%lu,\"ev\":\"SetupPathAHook\",\"path\":\"%s\"}",
        static_cast<unsigned long>(GetTickCount()), g_logPath);
    AppendJsonLine(boot);

    const auto imageBase = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (imageBase == 0)
    {
        InterlockedExchange(&g_hookState, 0);
        return 0;
    }

    const MH_STATUS st = MH_Initialize();
    if (st != MH_OK && st != MH_ERROR_ALREADY_INITIALIZED)
    {
        InterlockedExchange(&g_hookState, 0);
        return 0;
    }

    bool ok = true;
    ok = VerifyAndHook(imageBase, SetWheelsetRva, SetWheelsetPrologue, sizeof(SetWheelsetPrologue),
        &HookedSetWheelset, &pTargetSetWheelset, reinterpret_cast<LPVOID*>(&pOrigSetWheelset)) && ok;
    ok = VerifyAndHook(imageBase, EquipFromCreateRva, EquipPrologue, sizeof(EquipPrologue),
        &HookedEquip, &pTargetEquip, reinterpret_cast<LPVOID*>(&pOrigEquip)) && ok;
    ok = VerifyAndHook(imageBase, CreateVehicleActionRva, CreateActionPrologue, sizeof(CreateActionPrologue),
        &HookedCreateAction, &pTargetCreateAction, reinterpret_cast<LPVOID*>(&pOrigCreateAction)) && ok;
    ok = VerifyAndHook(imageBase, ActivateEnterWorldRva, ActivatePrologue, sizeof(ActivatePrologue),
        &HookedActivate, &pTargetActivate, reinterpret_cast<LPVOID*>(&pOrigActivate)) && ok;
    ok = VerifyAndHook(imageBase, GhostApplyRva, GhostApplyPrologue, sizeof(GhostApplyPrologue),
        &HookedGhostApply, &pTargetGhostApply, reinterpret_cast<LPVOID*>(&pOrigGhostApply)) && ok;
    ok = VerifyAndHook(imageBase, GhostOnAddRva, GhostOnAddPrologue, sizeof(GhostOnAddPrologue),
        &HookedGhostOnAdd, &pTargetGhostOnAdd, reinterpret_cast<LPVOID*>(&pOrigGhostOnAdd)) && ok;

    // Recv is optional: custom register convention; still try.
    const bool recvOk = VerifyAndHook(imageBase, RecvCreateVehicleRva, RecvPrologue, sizeof(RecvPrologue),
        &HookedRecv, &pTargetRecv, reinterpret_cast<LPVOID*>(&pOrigRecv));

    if (!ok)
    {
        char fail[200];
        sprintf_s(fail, "{\"t\":%lu,\"ev\":\"SetupPathAHook_FAILED\",\"recv\":%d,\"ghost_apply\":%d,\"ghost_onadd\":%d}",
            static_cast<unsigned long>(GetTickCount()),
            recvOk ? 1 : 0,
            pOrigGhostApply ? 1 : 0,
            pOrigGhostOnAdd ? 1 : 0);
        AppendJsonLine(fail);
        RemoveAllHooks();
        if (st == MH_OK)
            MH_Uninitialize();
        InterlockedExchange(&g_hookState, 0);
        return 0;
    }

    char done[280];
    sprintf_s(done,
        "{\"t\":%lu,\"ev\":\"SetupPathAHook_OK\",\"recv\":%d,\"ghost_apply\":1,\"ghost_onadd\":1,\"base\":\"0x%p\"}",
        static_cast<unsigned long>(GetTickCount()), recvOk ? 1 : 0, reinterpret_cast<void*>(imageBase));
    AppendJsonLine(done);

    InterlockedExchange(&g_hookState, 2);
    return 1;
}

BOOL APIENTRY DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_DETACH && g_hookState == 2)
        RemoveAllHooks();
    return TRUE;
}
