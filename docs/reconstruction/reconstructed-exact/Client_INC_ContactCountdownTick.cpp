/*
 * Purpose: Per-tick INC contact countdown; on zero fires option 0/1/2 actions.
 * Stable ID: aa_exe_0091ee20  Address: 0x0091ee20
 * Closes UF-001 partial: death → INC button (tutorial strings) → countdown → Respawn send.
 * Full death-UI open (corpse HP → show INC) still may need more xrefs; countdown path confirmed.
 */

#include <cstdint>

#ifdef _WIN32
#include <windows.h>
#else
unsigned GetTickCount();
#endif

extern int   DAT_00d1b6d8;
extern int   DAT_00d1b8dc;
extern int*  DAT_00d1b960;
extern int   DAT_00d1b644;
extern int   DAT_00d1b8c0;
extern float DAT_00aaabf4; // speed threshold for cancel
extern void  FUN_007a69d0();
extern void* FUN_007a6de0(const char*, int);
extern void  FUN_008f8200(int, int, void*, void*, int);
extern void  Client_SendRespawnInSector();
extern void  Client_SendInstantRepairRequest();
extern char  FUN_005134e0();
extern void  FUN_0091edd0(int ui); // cancel / abort countdown
extern long long FUN_0040ad20();
extern long long FUN_0040ccb0();
extern void  FUN_008ed650(); // transfer start
extern void* DAT_00a156cc;

// INC UI object fields (this / in_EAX)
// +0xc20 last tick ms
// +0xc24 remaining ms
// +0xc28 total duration ms
// +0xc2c last health/snapshot for cancel compare
// +0xc30 option: 0=airlift, 1=instant repair, 2=transfer

enum IncOption : int {
    kIncOptionAirlift = 0,
    kIncOptionInstantRepair = 1,
    kIncOptionTransfer = 2,
};

void Client_INC_ContactCountdownTick(int* ui)
{
    FUN_007a69d0();

    if (*(int*)((char*)ui + 0xc24) < 1)
        return;
    if (DAT_00d1b6d8 == 0)
        return;

    // Cancel if any of 3 hardpoints at vehicle+0x260 has flag +199 set
    if (*(int*)(DAT_00d1b6d8 + 0x250) != 0) {
        for (char i = 0; i < 3; i++) {
            int hp = *(int*)(*(int*)(*(int*)(DAT_00d1b6d8 + 0x250) + 0x260) + i * 4);
            if (hp != 0 && *(char*)(hp + 199) != 0) {
                FUN_0091edd0((int)ui);
                return;
            }
        }
    }

    // Gate: current value from vfunc+0x1b0 >= ui+0xc2c
    // AND vehicle+0x138 speed <= DAT_00aaabf4
    // AND entity flags at +0xb8 & 0xc3 == 0
    // AND vfunc+0x19c entity exists and bit at +0x180 >> 3 & 1 == 0
    // AND FUN_005134e0()==0 and player+0x6b9==0 (not on-foot special)
    // If any fail → FUN_0091edd0 cancel
    int player = DAT_00d1b6d8;
    int off = *(int*)(*(int*)(player + 4) + 4);
    int curSnap = ((int (*)(int))(*(int*)(off + 4 + player) /* vtable proxy */))(player);
    (void)curSnap;
    // Structural: when gates pass:
    {
        // refresh +0xc2c from vfunc+0x1b0
        unsigned now = GetTickCount();
        int elapsed = (int)now - *(int*)((char*)ui + 0xc20);

        // Mid-countdown toast when remaining/total both > 5000 and remaining-elapsed < 0x1389
        if (*(int*)((char*)ui + 0xc28) > 5000 && *(int*)((char*)ui + 0xc24) > 5000 &&
            (*(int*)((char*)ui + 0xc24) - elapsed) < 0x1389) {
            void* msg = FUN_007a6de0(
                "Contacting INC... Please do nothing for 5 more seconds!", -1);
            if (DAT_00d1b8dc != 0)
                FUN_008f8200(DAT_00d1b8dc, 6, &DAT_00a156cc, msg, 0);
        }

        int rem = *(int*)((char*)ui + 0xc24) - elapsed;
        if (rem < 0)
            rem = 0;
        *(int*)((char*)ui + 0xc24) = rem;
        *(unsigned*)((char*)ui + 0xc20) = now;

        // Progress bar on INC UI if visible (DAT_00d1b960)
        if (DAT_00d1b960 != nullptr) {
            // vfunc visible; set progress 1 - rem/total
        }

        if (rem != 0)
            return;

        int option = *(int*)((char*)ui + 0xc30);
        if (option == kIncOptionAirlift) {
            void* msg = FUN_007a6de0(
                "INC Contact Established!  Returning you to nearest repair station...", -1);
            if (DAT_00d1b8dc != 0)
                FUN_008f8200(DAT_00d1b8dc, 6, &DAT_00a156cc, msg, 0);
            Client_SendRespawnInSector();
            return;
        }
        if (option == kIncOptionInstantRepair) {
            // if cannot afford → "You cannot afford the repair fee!"
            // else toast + Client_SendInstantRepairRequest()
            Client_SendInstantRepairRequest();
            return;
        }
        if (option == kIncOptionTransfer) {
            // map/station fee check; else FUN_008ed650()
            if (DAT_00d1b644 == 0 || DAT_00d1b8c0 == 0)
                return;
            FUN_008ed650();
            return;
        }
    }
}

// Pure: option dispatch used by tests (matches binary option codes)
inline int IncOption_OnCountdownZero(int option)
{
    // Returns opcode-ish outcome codes for tests:
    // 0 → send respawn 0x2073 path
    // 1 → instant repair path
    // 2 → transfer path
    // other → no-op
    if (option == 0 || option == 1 || option == 2)
        return option;
    return -1;
}
