// ChromiumBridge — injected x86 DLL for Auto Assault CEF overlay experiment.
// Publishes demo JSON state via MMF (Local\AutoCoreChromium_State) for ChromiumHost.
//
// Setup is intentionally light: NO MinHook until we have real detours (MH_Initialize
// on a CreateRemoteThread has been a crash risk). SEH wraps setup so exceptions
// return 0 instead of taking down the client.
//
// Layout MUST match ChromiumOverlay.Core GameStateChannel.cs:
//   +0x00 int Magic 'CEF1' (0x31464543)
//   +0x04 int Version (1)
//   +0x08 int Seq
//   +0x0C int JsonLength
//   +0x10 bytes JSON (max 4096)

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

    volatile LONG g_setupState = 0; // 0 idle, 1 installing, 2 ready, -1 failed
    volatile LONG g_stop = 0;
    HANDLE g_thread = nullptr;
    HANDLE g_map = nullptr;
    void* g_view = nullptr;

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

        const int next = ReadInt(g_view, SeqOffset) + 1;
        WriteInt(g_view, SeqOffset, next);
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
        WriteInt(g_view, SeqOffset, 0);
        WriteInt(g_view, JsonLengthOffset, 0);
        return true;
    }

    void CloseChannel()
    {
        if (g_view)
        {
            UnmapViewOfFile(g_view);
            g_view = nullptr;
        }
        if (g_map)
        {
            CloseHandle(g_map);
            g_map = nullptr;
        }
    }

    DWORD WINAPI PublisherThread(LPVOID)
    {
        Log("Publisher thread started");
        const DWORD pid = GetCurrentProcessId();
        LONG tick = 0;

        while (InterlockedCompareExchange(&g_stop, 0, 0) == 0)
        {
            ++tick;
            char json[512];
            sprintf_s(json,
                "{\"pid\":%lu,\"tick\":%ld,\"message\":\"hello from bridge\"}",
                static_cast<unsigned long>(pid),
                static_cast<long>(tick));
            PublishJson(json);
            Sleep(200);
        }

        Log("Publisher thread stopping");
        return 0;
    }

    bool StartPublisher()
    {
        g_thread = CreateThread(nullptr, 0, PublisherThread, nullptr, 0, nullptr);
        return g_thread != nullptr;
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

        Log("SetupChromiumBridge enter (no MinHook; MMF + publisher only)");

        if (!OpenChannel())
        {
            Log("OpenChannel failed");
            InterlockedExchange(&g_setupState, -1);
            return 0;
        }

        if (!StartPublisher())
        {
            Log("StartPublisher failed");
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
    // Never let an exception on the remote thread take down autoassault.
    __try
    {
        return SetupImpl();
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        // Best-effort log if lock already exists.
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        StopPublisher();
        CloseChannel();
    }
    return TRUE;
}
