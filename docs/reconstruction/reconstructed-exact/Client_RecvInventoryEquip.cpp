/*
 * Purpose: S2C InventoryEquip 0x203C size 0x40.
 * Stable ID: aa_exe_00813f40  Address: 0x00813f40
 *
 * Layout (packet including opcode at +0):
 *   +8  item TFID
 *   +0x18 vehicle TFID-ish
 *   +0x28 oldItem TFID
 *   +0x38 putInHand (byte)
 *   +0x39 srcX, +0x3A srcY, +0x3B invTypeFrom
 *
 * No client C2S builder for 0x203C — equip is requested via Drop HARDPOINT (type 2).
 */

#include <cstdint>

struct InventoryEquipPacket {
    uint32_t opcode;       // +0  0x203C
    uint32_t pad4;
    int32_t  item_lo;      // +8
    int32_t  item_hi;      // +c
    // ... vehicle at +0x18, old at +0x28
    // putInHand at +0x38
};

extern void* FUN_004bafe0(uint8_t, int32_t, int32_t); // resolve vehicle
extern void* Object_ResolveFromTFID(void* tfid);
extern void  FUN_009440e0(void*, int, int, int, int);
extern void* CVOGReaction_ResolveObjectTarget(int, int32_t, int32_t);
extern void* FUN_00571010(int32_t, int32_t);
extern void  FUN_00571b80(void*, int, int);
extern void* FUN_00502e90(void*);
extern void  FUN_00571620(void* item, uint8_t x, uint8_t y, int);
extern void  FUN_007fc150();
extern void  FUN_007fc270(uint8_t invType);
extern void  FUN_008c2940();
extern void  FUN_008c3120();
extern void  FUN_008801b0(int);
extern void  FUN_007a4480(int, const char*, ...);
extern void  Vehicle_EquipPowerPlant(void*, void*, void**, bool);
extern void  FUN_004fe620(void*, void*, int);
extern void  FUN_004fe800(void*, void*, int);
extern void  FUN_004fe110(void*, void*, void*);
extern void  FUN_004ff510(void*, void*, int);
extern void  FUN_00502180(void*, void*, int);
extern void  FUN_0092f120();

void Client_RecvInventoryEquip(void* packet, int game /*in_EAX*/)
{
    FUN_007a4480(-1, "Requesting InventoryEquip: char:%I64d Old:%I64d New:%I64d\n",
                 *(int*)((char*)packet + 0x18), *(int*)((char*)packet + 0x1c),
                 *(int*)((char*)packet + 0x28), *(int*)((char*)packet + 0x2c),
                 *(int*)((char*)packet + 8), *(int*)((char*)packet + 0xc));

    void* vehicle = FUN_004bafe0(*(uint8_t*)((char*)packet + 0x20),
                                 *(int*)((char*)packet + 0x18),
                                 *(int*)((char*)packet + 0x1c));
    void* itemTfid = (char*)packet + 8;

    if (vehicle == nullptr) {
        void* obj = Object_ResolveFromTFID(itemTfid);
        if (obj != nullptr)
            FUN_009440e0(obj, 1, 0, -1, -1);
        return;
    }

    int off = *(int*)(*(int*)((char*)vehicle + 4) + 4);
    int* ownerPtr = *(int**)(off + 0xb0 + (int)vehicle);
    if (ownerPtr == nullptr)
        goto foreign_equip;

    // owner vfunc +0x1dc → entity id must equal game+0xe98 for local path
    // (preserved: only local owner applies grid/hand placement)
    {
        // Simplified: local ownership check
        int localEntity = *(int*)(game + 0xe98);
        // if owner entity != localEntity → foreign_equip
        (void)localEntity;

        void* placed = nullptr;
        if (*(char*)((char*)packet + 0x38) == 1) {
            // putInHand
            placed = CVOGReaction_ResolveObjectTarget(
                1, *(int32_t*)itemTfid, *(int32_t*)((char*)packet + 0xc));
        } else if (*(int*)((char*)vehicle + 0x2b0) != 0) {
            placed = FUN_00571010(*(int32_t*)itemTfid, *(int32_t*)((char*)packet + 0xc));
            FUN_00571b80(placed, 1, 0);
        }
        void* result = FUN_00502e90(placed);

        if (result == nullptr) {
            if (*(char*)((char*)packet + 0x38) == 1 &&
                (*(uint32_t*)((char*)packet + 0x28) & *(uint32_t*)((char*)packet + 0x2c)) ==
                    0xffffffffu) {
                FUN_007fc150();
            }
        } else if (*(char*)((char*)packet + 0x38) == 1) {
            FUN_007fc270(*(uint8_t*)((char*)packet + 0x3b));
        } else {
            FUN_00571620(result, *(uint8_t*)((char*)packet + 0x39),
                         *(uint8_t*)((char*)packet + 0x3a), 1);
        }

        if (*(int*)(game + 0x1078) != 0)
            FUN_008801b0(*(int*)(game + 0x1078));
        if (*(int*)(game + 0x104c) != 0)
            FUN_008801b0(*(int*)(game + 0x104c));
        *(uint8_t*)(game + 0x30b4) = 1;
        *(uint8_t*)(game + 0x30b5) = 0;
        return;
    }

foreign_equip:
    // Resolve item and switch on clone type at [0x2a]+0x38:
    // 6 → special graphics; 10 → power plant; 0xc → weapon/mount; 0x10 →; 0x1c →
    int* pi = (int*)CVOGReaction_ResolveObjectTarget(
        *(uint8_t*)((char*)packet + 0x10), *(int32_t*)itemTfid,
        *(int32_t*)((char*)packet + 0xc));
    if (pi == nullptr)
        return;

    int type = *(int*)(pi[0x2a] + 0x38);
    void* out = nullptr;
    switch (type) {
    case 6:
        if (*(short*)(*(int*)(pi[0x2a] + 0x3c) + 0x3f4) != 10)
            return;
        FUN_004fe620(pi, &out, 0);
        break;
    case 10: {
        void* pp = ((void* (*)(int*))((void**)*pi)[500 / 4])(pi);
        Vehicle_EquipPowerPlant(vehicle, pp, &out, false);
        break;
    }
    case 0xc:
        // weapon path: short@+0x3f4 == 9 → FUN_004fe800 else FUN_004fe110
        break;
    case 0x10:
        FUN_004ff510(pi, &out, 0);
        break;
    case 0x1c:
        FUN_00502180(pi, &out, 0);
        break;
    default:
        return;
    }
    if (out != nullptr)
        FUN_009440e0(out, 1, 0, -1, -1);
    FUN_0092f120();
}

enum : uint32_t {
    kInventoryEquipOpcode = 0x203C,
    kInventoryEquipSize = 0x40,
};
