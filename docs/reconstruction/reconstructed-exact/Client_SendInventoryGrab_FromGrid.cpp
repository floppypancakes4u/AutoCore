/*
 * Purpose: C2S InventoryGrab (0x2034) from inventory grid UI.
 * Stable ID: aa_exe_00860e20
 * Address: 0x00860e20
 */

#include <cstdint>

struct InventoryGrabPacket {
    uint32_t opcode;      // 0x2034
    uint32_t pad4;
    int32_t  item_a;      // from UI object +0x160
    int32_t  item_b;      // +0x164
    uint8_t  inv_type;    // window+0x56c → +4
    uint8_t  pad[7];
    int32_t  quantity;    // param_2
};

extern void* g_pSectorNetConnection_INFERRED;
extern char  DAT_00d1a8f6; // reentrancy / in-flight grab

int Client_SendInventoryGrab_FromGrid(int window, int quantity, int* itemUi /*EDI*/)
{
    // Optional: clear prior drag UI state if inventory window id mismatches
    // FUN_007fbbb0()

    if (DAT_00d1a8f6 != 0)
        return 1; // still returns 1; does not double-send

    InventoryGrabPacket pkt{};
    pkt.opcode = 0x2034;
    // item fields from itemUi vfunc +0x3ac → object +0x160/+0x164/+0x168 byte
    // pkt.inv_type = *(uint8_t*)(*(int*)(window + 0x56c) + 4);
    // pkt.quantity = quantity;

    if (g_pSectorNetConnection_INFERRED != nullptr) {
        // send size 0x20
    }
    // DAT_00d1b4b0 = 1; DAT_00d1a8f6 = 1;
    return 1;
}
