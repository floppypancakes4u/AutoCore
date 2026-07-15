/*
 * Purpose: Attempt secondary weapon fire with heat and connection gates.
 * Stable ID: aa_exe_0091a550
 * Address: 0x0091a550
 */

#include <cstdint>

extern int   DAT_00d1b6d8; // local player entity
extern void* g_pSectorNetConnection_INFERRED;
extern char  HeatOk_FUN_004f52e0();
extern void  FireSecondary_FUN_004f5110();
extern void  Log_FUN_007a4480(int, const char*);
extern void  UiRefresh_FUN_0089ff80();
extern int*  DAT_00d1b8f0;

void Input_TryFireSecondaryWeapons()
{
    if (DAT_00d1b6d8 == 0)
        return;

    int off = *(int*)(*(int*)(DAT_00d1b6d8 + 4) + 4);
    uint8_t flags = *(uint8_t*)(off + 0xb8 + DAT_00d1b6d8);
    // Gate: (flags & 0xd2) == 0
    if ((flags & 0xd2) != 0)
        return;
    if (g_pSectorNetConnection_INFERRED == nullptr)
        return;
    // connection vfunc +8 must return non-zero (connected)
    // if vehicle child +0x250 == 0 return

    if (HeatOk_FUN_004f52e0() == 0) {
        Log_FUN_007a4480(0, "Failed to fire secondary weapons due to heat.\n");
        return;
    }
    FireSecondary_FUN_004f5110();

    if ((*(char*)(DAT_00d1b6d8 + 0x6b8) != 0 || *(char*)(DAT_00d1b6d8 + 0x6b9) != 0)
        && DAT_00d1b8f0 != nullptr /* && visible */) {
        UiRefresh_FUN_0089ff80();
    }
}
