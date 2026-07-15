// ChromiumBridge — injected x86 DLL for Auto Assault CEF overlay experiment.
// Publishes player combat pools (HP/Power/Shield) via MMF Local\AutoCoreChromium_State.
//
// Chain (offsets/bak.json + vehicle_combat_pool RE):
//   module + 0x91A840 + 0xE98 -> local player
//   player + 0x250             -> vehicle
//   vehicle + 0x144/0x148      -> shield cur/max (int)
//   vehicle + 0x12C/0x12E      -> power cur/max (int16, creature base)
//   HP via vtable +0x248 / +0x240 (thiscall, SEH-guarded)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstring>

namespace
{
    constexpr int MagicCef1 = 0x31464543; // 'CEF1' LE
    constexpr int ChannelVersion = 1;
    constexpr int MagicOffset = 0x00;
    constexpr int VersionOffset = 0x04;
    constexpr int SeqOffset = 0x08;
    constexpr int JsonLengthOffset = 0x0C;
    constexpr int JsonPayloadOffset = 0x10;
    constexpr int MaxJsonBytes = 4096;
    constexpr int ChannelByteSize = JsonPayloadOffset + MaxJsonBytes;
    constexpr wchar_t MappingName[] = L"Local\\AutoCoreChromium_State";

    constexpr uintptr_t VogClientBaseRva = 0x91A840;
    constexpr size_t LocalPlayerOffset = 0xE98;
    constexpr size_t PlayerVehicleOffset = 0x250;
    constexpr size_t CurrentShieldOffset = 0x144;
    constexpr size_t MaxShieldOffset = 0x148;
    constexpr size_t CurrentPowerOffset = 0x12C;
    constexpr size_t MaxPowerOffset = 0x12E;
    constexpr size_t HpGetVtableByteOffset = 0x248;
    constexpr size_t HpMaxVtableByteOffset = 0x240;

    volatile LONG g_setupState = 0;
    volatile LONG g_stop = 0;
    HANDLE g_thread = nullptr;
    HANDLE g_map = nullptr;
    void* g_view = nullptr;
    HMODULE g_gameModule = nullptr;

    CRITICAL_SECTION g_logLock;
    bool g_logLockInit = false;
    char g_logPath[MAX_PATH] = {};

    void InitLogPath()
    {
        char temp[MAX_PATH];
        GetTempPathA(MAX_PATH, temp);
        sprintf_s(g_logPath, "%sAutoCoreChromium", temp);
        CreateDirectoryA(g_logPath, nullptr);
        strcat_s(g_logPath, "\\bridge.log");
    }

    void Log(const char* msg)
    {
        if (!g_logLockInit)
            return;
        EnterCriticalSection(&g_logLock);
        FILE* f = nullptr;
        if (fopen_s(&f, g_logPath, "a") == 0 && f)
        {
            SYSTEMTIME st;
            GetLocalTime(&st);
            fprintf(f, "%04d-%02d-%02d %02d:%02d:%02d.%03d %s\n",
                st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, msg);
            fclose(f);
        }
        LeaveCriticalSection(&g_logLock);
    }

    void WriteInt(void* base, int offset, int value)
    {
        *reinterpret_cast<int*>(static_cast<char*>(base) + offset) = value;
    }

    int ReadInt(void* base, int offset)
    {
        return *reinterpret_cast<int*>(static_cast<char*>(base) + offset);
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

    bool ReadInt32(const void* base, size_t offset, int* out)
    {
        auto* p = reinterpret_cast<const unsigned char*>(base) + offset;
        if (!IsReadable(p, 4) || !out)
            return false;
        *out = *reinterpret_cast<const int*>(p);
        return true;
    }

    bool ReadInt16(const void* base, size_t offset, short* out)
    {
        auto* p = reinterpret_cast<const unsigned char*>(base) + offset;
        if (!IsReadable(p, 2) || !out)
            return false;
        *out = *reinterpret_cast<const short*>(p);
        return true;
    }

    bool ReadPtr(const void* base, size_t offset, void** out)
    {
        auto* p = reinterpret_cast<const unsigned char*>(base) + offset;
        if (!IsReadable(p, sizeof(void*)) || !out)
            return false;
        *out = *reinterpret_cast<void* const*>(p);
        return true;
    }

    // MSVC virtual-base this adjustment used throughout AA:
    //   thisAdj = *(*(obj+4)+4) + obj + 4
    bool ResolveAdjustedThis(void* obj, void** thisAdj, void*** vtable)
    {
        if (!obj || !thisAdj || !vtable)
            return false;
        void* mid = nullptr;
        if (!ReadPtr(obj, 4, &mid) || !mid)
            return false;
        int adj = 0;
        if (!ReadInt32(mid, 4, &adj))
            return false;
        auto* adjThis = reinterpret_cast<unsigned char*>(obj) + 4 + adj;
        if (!IsReadable(adjThis, sizeof(void*)))
            return false;
        *thisAdj = adjThis;
        *vtable = *reinterpret_cast<void***>(adjThis);
        return *vtable != nullptr && IsReadable(*vtable, HpGetVtableByteOffset + sizeof(void*));
    }

    int CallThiscallNoArgs(void* thisPtr, void* func)
    {
        int result = -1;
        __try
        {
            using Fn = int(__thiscall*)(void*);
            result = reinterpret_cast<Fn>(func)(thisPtr);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            result = -1;
        }
        return result;
    }

    bool TryReadHp(void* vehicle, int* cur, int* max)
    {
        *cur = -1;
        *max = -1;
        void* thisAdj = nullptr;
        void** vtable = nullptr;
        if (!ResolveAdjustedThis(vehicle, &thisAdj, &vtable))
            return false;

        void* getHp = nullptr;
        void* getMax = nullptr;
        if (!IsReadable(reinterpret_cast<char*>(vtable) + HpGetVtableByteOffset, sizeof(void*)))
            return false;
        getHp = *reinterpret_cast<void**>(reinterpret_cast<char*>(vtable) + HpGetVtableByteOffset);
        getMax = *reinterpret_cast<void**>(reinterpret_cast<char*>(vtable) + HpMaxVtableByteOffset);
        if (!getHp || !getMax)
            return false;

        *cur = CallThiscallNoArgs(thisAdj, getHp);
        *max = CallThiscallNoArgs(thisAdj, getMax);
        // Sanity: garbage vfuncs often return huge values
        if (*cur < 0 || *cur > 1000000)
            *cur = -1;
        if (*max < 0 || *max > 1000000)
            *max = -1;
        return *cur >= 0 || *max >= 0;
    }

    bool TryReadCombatPools(int* hp, int* maxHp, int* power, int* maxPower, int* shield, int* maxShield, bool* hasVehicle)
    {
        *hp = *maxHp = -1;
        *power = *maxPower = *shield = *maxShield = 0;
        *hasVehicle = false;

        if (!g_gameModule)
            g_gameModule = GetModuleHandleW(L"autoassault.exe");
        if (!g_gameModule)
            g_gameModule = GetModuleHandleW(nullptr);
        if (!g_gameModule)
            return false;

        auto* imageBase = reinterpret_cast<unsigned char*>(g_gameModule);
        void* vogField = imageBase + VogClientBaseRva + LocalPlayerOffset;
        void* player = nullptr;
        if (!ReadPtr(vogField, 0, &player) || !player)
        {
            // Alternate: VogClientBaseRva is a pointer to the client object.
            void* vogObj = nullptr;
            if (!ReadPtr(imageBase + VogClientBaseRva, 0, &vogObj) || !vogObj)
                return false;
            if (!ReadPtr(vogObj, LocalPlayerOffset, &player) || !player)
                return false;
        }

        void* vehicle = nullptr;
        if (!ReadPtr(player, PlayerVehicleOffset, &vehicle) || !vehicle)
            return false;

        *hasVehicle = true;

        int sh = 0, msh = 0;
        short pow = 0, mpow = 0;
        if (!ReadInt32(vehicle, CurrentShieldOffset, &sh))
            sh = 0;
        if (!ReadInt32(vehicle, MaxShieldOffset, &msh))
            msh = 0;
        if (!ReadInt16(vehicle, CurrentPowerOffset, &pow))
            pow = 0;
        if (!ReadInt16(vehicle, MaxPowerOffset, &mpow))
            mpow = 0;

        *shield = sh < 0 ? 0 : sh;
        *maxShield = msh < 0 ? 0 : msh;
        *power = pow < 0 ? 0 : pow;
        *maxPower = mpow < 0 ? 0 : mpow;

        TryReadHp(vehicle, hp, maxHp);
        return true;
    }

    void PublishJson(const char* json)
    {
        if (!g_view || !json)
            return;
        const int len = static_cast<int>(strlen(json));
        if (len < 0 || len > MaxJsonBytes)
            return;
        WriteInt(g_view, JsonLengthOffset, len);
        if (len > 0)
            memcpy(static_cast<char*>(g_view) + JsonPayloadOffset, json, static_cast<size_t>(len));
        WriteInt(g_view, SeqOffset, ReadInt(g_view, SeqOffset) + 1);
        WriteInt(g_view, MagicOffset, MagicCef1);
        WriteInt(g_view, VersionOffset, ChannelVersion);
    }

    bool OpenChannel()
    {
        g_map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, ChannelByteSize, MappingName);
        if (!g_map)
            return false;
        g_view = MapViewOfFile(g_map, FILE_MAP_ALL_ACCESS, 0, 0, ChannelByteSize);
        if (!g_view)
        {
            CloseHandle(g_map);
            g_map = nullptr;
            return false;
        }
        ZeroMemory(g_view, ChannelByteSize);
        WriteInt(g_view, MagicOffset, MagicCef1);
        WriteInt(g_view, VersionOffset, ChannelVersion);
        return true;
    }

    void CloseChannel()
    {
        if (g_view) { UnmapViewOfFile(g_view); g_view = nullptr; }
        if (g_map) { CloseHandle(g_map); g_map = nullptr; }
    }

    DWORD WINAPI PublisherThread(LPVOID)
    {
        Log("Publisher thread started (combat pools)");
        const DWORD pid = GetCurrentProcessId();
        LONG tick = 0;

        while (InterlockedCompareExchange(&g_stop, 0, 0) == 0)
        {
            ++tick;
            int hp = -1, maxHp = -1, power = 0, maxPower = 0, shield = 0, maxShield = 0;
            bool hasVehicle = false;
            __try
            {
                TryReadCombatPools(&hp, &maxHp, &power, &maxPower, &shield, &maxShield, &hasVehicle);
            }
            __except (EXCEPTION_EXECUTE_HANDLER)
            {
                hasVehicle = false;
            }

            char json[512];
            sprintf_s(json,
                "{\"pid\":%lu,\"tick\":%ld,\"hasVehicle\":%s,"
                "\"hp\":%d,\"maxHp\":%d,\"power\":%d,\"maxPower\":%d,"
                "\"shield\":%d,\"maxShield\":%d}",
                static_cast<unsigned long>(pid),
                static_cast<long>(tick),
                hasVehicle ? "true" : "false",
                hp, maxHp, power, maxPower, shield, maxShield);
            PublishJson(json);

            if (tick <= 3 || (tick % 25) == 0)
            {
                char line[256];
                sprintf_s(line, "pools tick=%ld veh=%d hp=%d/%d pow=%d/%d sh=%d/%d",
                    static_cast<long>(tick), hasVehicle ? 1 : 0, hp, maxHp, power, maxPower, shield, maxShield);
                Log(line);
            }
            Sleep(200);
        }
        Log("Publisher thread stopping");
        return 0;
    }

    void StopPublisher()
    {
        InterlockedExchange(&g_stop, 1);
        if (g_thread)
        {
            WaitForSingleObject(g_thread, 2000);
            CloseHandle(g_thread);
            g_thread = nullptr;
        }
    }

    int SetupImpl()
    {
        const LONG prev = InterlockedCompareExchange(&g_setupState, 1, 0);
        if (prev == 2)
            return 1;
        if (prev != 0)
            return 0;

        InitLogPath();
        if (!g_logLockInit)
        {
            InitializeCriticalSection(&g_logLock);
            g_logLockInit = true;
        }
        Log("SetupChromiumBridge enter (combat pool publisher)");

        if (!OpenChannel())
        {
            Log("OpenChannel failed");
            InterlockedExchange(&g_setupState, -1);
            return 0;
        }

        InterlockedExchange(&g_stop, 0);
        g_thread = CreateThread(nullptr, 0, PublisherThread, nullptr, 0, nullptr);
        if (!g_thread)
        {
            Log("CreateThread failed");
            CloseChannel();
            InterlockedExchange(&g_setupState, -1);
            return 0;
        }

        Log("SetupChromiumBridge OK");
        InterlockedExchange(&g_setupState, 2);
        return 1;
    }
}

extern "C" __declspec(dllexport) int __stdcall SetupChromiumBridge()
{
    __try
    {
        return SetupImpl();
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(hModule);
    else if (reason == DLL_PROCESS_DETACH)
    {
        StopPublisher();
        CloseChannel();
    }
    return TRUE;
}
