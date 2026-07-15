/*
 * Purpose: C2S InventoryDrop 0x2036 size 0x20 — HARDPOINT type=2 (equip-via-drop).
 * Stable ID: aa_exe_00863430  Address: 0x00863430
 */

#include <cstdint>

extern int   DAT_00d1b6d8;
extern int*  DAT_00d1b1f8; // UI selection
extern void  Client_SendSectorPacket(void* game, uint32_t size, void* payload);
extern char  FUN_00862860(); // precheck
extern int   FUN_004fabc0(int* item, int);
extern void  FUN_00931db0();
extern char  FUN_004ce5f0(int vehicle); // in town?
extern void  FUN_008012f0();
extern void  FUN_00931440(int);
extern void  FUN_007a69d0();
extern void* FUN_007a6de0(const char*, int);
extern void  FUN_007fdfb0(void*, void*, int, int, int);
extern void* DAT_00d1a840;

uint8_t Client_SendInventoryDrop_Hardpoint()
{
    if (DAT_00d1b6d8 == 0)
        return 0;

    auto** vt = (void**)*DAT_00d1b1f8;
    int* item = ((int* (*)(int*))vt[0x3ac / 4])(DAT_00d1b1f8);
    if (item == nullptr)
        return 0;

    if (FUN_00862860() == 0)
        return 0;

    int vehicle = *(int*)(DAT_00d1b6d8 + 0x250);
    if (FUN_004fabc0(item, 0) != 0) {
        FUN_00931db0();
        return 1;
    }

    // clone type at item[0x2a]+0x38
    int cloneType = *(int*)(item[0x2a] + 0x38);
    if (cloneType == 0xe) {
        // type 0xe: only in town or when player+0x6b4 > 0
        if (FUN_004ce5f0(vehicle) != 0 || *(int*)(DAT_00d1b6d8 + 0x6b4) > 0) {
            FUN_008012f0();
            FUN_00931440(1);
            return 1;
        }
        goto town_only_error;
    }

    if (FUN_004ce5f0(vehicle) == 0) {
        int secondary = ((int (*)(int*))((void**)*item)[0x1f0 / 4])(item);
        if (secondary != 0 && *(int*)(DAT_00d1b6d8 + 0x6b4) < 1)
            goto town_only_error;
    }

    // Build drop packet
    {
        uint8_t pkt[0x20] = {};
        *(uint32_t*)pkt = 0x2036;
        *(int32_t*)(pkt + 8) = item[0x58];
        *(int32_t*)(pkt + 0xC) = item[0x59];
        pkt[0x10] = (uint8_t)item[0x5a];
        pkt[0x18] = 0xff;
        pkt[0x19] = 0xff;
        pkt[0x1A] = 2; // HARDPOINT drop type
        Client_SendSectorPacket(&DAT_00d1a840, 0x20, pkt);
        return 1;
    }

town_only_error:
    FUN_007a69d0();
    void* msg = FUN_007a6de0("This item can only be changed in town.", -1);
    FUN_007fdfb0(&DAT_00d1a840, msg, -1, 1, 0);
    return 1; // binary returns 1 even on error message path
}

enum : uint32_t {
    kInventoryDropOpcode = 0x2036,
    kInventoryDropSize = 0x20,
    kInventoryDropTypeHardpoint = 2,
};
