/*
 * Purpose: C2S InventoryUnequip (0x203E) size 0x30.
 * Stable ID: aa_exe_00862c00  Address: 0x00862c00
 * Confidence: high
 */

#include <cstdint>

extern int   DAT_00d1b6d8; // local player
extern void  Client_SendSectorPacket(void* game, uint32_t size, void* payload);
extern char  FUN_005714e0(void* item, uint8_t* outX, uint8_t* outY, int, int); // find free
extern char  FUN_004ce5c0(int player); // alternate inventory space path
extern int   FUN_004f6a80(void* item); // blocked unequip?
extern void  FUN_00931db0();
extern void  FUN_007a69d0();
extern void* FUN_007a6de0(const char*, int);
extern void  FUN_007fdfb0(void*, void*, int, int, int);
extern void* DAT_00d1a840; // game client

// in_EAX = UI item widget with vfunc +0x3ac → item object
uint32_t Client_SendInventoryUnequip(int* itemUi)
{
    if (DAT_00d1b6d8 == 0)
        return 0;
    if (*(int*)(DAT_00d1b6d8 + 0x250) == 0)
        return 0;

    // vfunc +0x3ac → item*
    using VFn = int (*)(int*);
    auto** vt = (void**)*itemUi;
    int item = ((int (*)(int*))vt[0x3ac / 4])(itemUi);
    if (item == 0)
        return 0;

    item = ((int (*)(int*))vt[0x3ac / 4])(itemUi);
    if (FUN_004f6a80((void*)item) != 0) {
        FUN_00931db0();
        return 0;
    }

    uint8_t destX = 0, destY = 0;
    item = ((int (*)(int*, uint8_t*, uint8_t*, int, int))vt[0x3ac / 4])(
        itemUi, &destX, &destY, 1, -1);
    char found = FUN_005714e0((void*)item, &destX, &destY, 1, -1);
    if (found == 0) {
        // retry with alternate cargo if FUN_004ce5c0(player)
        if (FUN_004ce5c0(DAT_00d1b6d8) != 0) {
            item = ((int (*)(int*, uint8_t*, uint8_t*, int, int))vt[0x3ac / 4])(
                itemUi, &destX, &destY, 1, -1);
            found = FUN_005714e0((void*)item, &destX, &destY, 1, -1);
            if (found != 0)
                goto send_packet;
        }
        FUN_007a69d0();
        void* msg = FUN_007a6de0(
            "There is not enough space in your inventory for this equipment.", -1);
        FUN_007fdfb0(&DAT_00d1a840, msg, -1, 1, 0);
        return 0;
    }

send_packet:
    // UI refresh vfunc +0x34c
    ((void (*)(int*))vt[0x34c / 4])(itemUi);

    // Packet 0x203E size 0x30
    uint8_t pkt[0x30] = {};
    *(uint32_t*)pkt = 0x203E;
    item = ((int (*)(int*))vt[0x3ac / 4])(itemUi);
    pkt[0x10] = *(uint8_t*)(item + 0x168); // uStack_20 region
    *(uint32_t*)(pkt + 8) = *(uint32_t*)(item + 0x160);
    *(uint32_t*)(pkt + 0xC) = *(uint32_t*)(item + 0x164);
    pkt[0x28] = destX; // uStack_8
    pkt[0x29] = destY; // uStack_7

    Client_SendSectorPacket(&DAT_00d1a840, 0x30, pkt);
    return 1;
}

// Pure layout constants for tests / docs
enum : uint32_t {
    kInventoryUnequipOpcode = 0x203E,
    kInventoryUnequipSize = 0x30,
};
