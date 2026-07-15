/*
 * Purpose: C2S StoreTransactionRequest (0x2027) size 0x40 — buy and sell entry points.
 * Stable IDs:
 *   buy  — aa_exe_0088e180 @ 0x0088e180 (FUN_0088e180)
 *   sell — aa_exe_00860a50 @ 0x00860a50 (Client_UI_InventoryDropToGrid store branch)
 * Confidence: high for packet layout + IsBuy flag; high for local afford/space gates
 */

#include <cstdint>

struct StoreTransactionRequestPacket {
    uint32_t opcode;       // +0x00 0x2027
    uint32_t pad04[5];     // +0x04 .. +0x17
    int32_t  itemTfid[4];  // +0x18
    int32_t  storeTfid[4]; // +0x28 (store COID/TFID)
    uint8_t  isBuy;        // +0x38 1=buy 0=sell
    uint8_t  pad39[3];
    int32_t  quantity;     // +0x3c
}; // 0x40

extern int   DAT_00d1b6d8;
extern char  DAT_00d1a840[];
extern void  Client_SendSectorPacket(void* game, short size, void* buffer);
extern void  FUN_007fdfb0(void* game, const char* msg, int, int, int);
extern char  FUN_005714e0(void* item, void*, void*, int, int);
extern char  FUN_00513770(); // store will-accept-sell predicate
extern int*  /*vt*/ ;

// store UI object: this+0x5a0 holds store entity
// item object: param_2; TFID at item dword indices 0x58..0x5b (object+0x160)

uint32_t Client_SendStoreTransactionBuy(void* storeUi, int* itemObj)
{
    if (*(int*)((char*)storeUi + 0x5a0) == 0 || DAT_00d1b6d8 == 0)
        return 0;
    int character = DAT_00d1b6d8;
    int vehicle = *(int*)(character + 0x250);
    if (vehicle == 0 || *(int*)(vehicle + 0x2b0) == 0)
        return 0;

    // price = item->vt+0x168()
    using PriceFn = uint32_t (*)(int*);
    auto** ivt = reinterpret_cast<void**>(*itemObj);
    uint32_t price = ((PriceFn)ivt[0x168 / 4])(itemObj);

    uint32_t feeLo = *(uint32_t*)(character + 0xce8);
    int feeHi = *(int*)(character + 0xcec);
    // total = price + fee (64-bit)
    // available = (credits - reserved) at +0x720/+0x728
    uint32_t creditsLo = *(uint32_t*)(character + 0x720);
    int creditsHi = *(int*)(character + 0x724);
    uint32_t reservedLo = *(uint32_t*)(character + 0x728);
    int reservedHi = *(int*)(character + 0x72c);
    // if cannot afford → toast "You cannot afford this!" return 1

    uint8_t z0 = 0, z1 = 0;
    if (FUN_005714e0(itemObj, &z0, &z1, 1, -1) == 0) {
        FUN_007fdfb0(&DAT_00d1a840, "Your Inventory is too full to buy this item.", -1, 1, 0);
        return 1;
    }

    StoreTransactionRequestPacket pkt{};
    pkt.opcode = 0x2027;
    pkt.quantity = 1;
    pkt.isBuy = 1;

    // item TFID from itemObj[0x58..]
    pkt.itemTfid[0] = itemObj[0x58];
    pkt.itemTfid[1] = itemObj[0x59];
    pkt.itemTfid[2] = itemObj[0x5a];
    pkt.itemTfid[3] = itemObj[0x5b];

    int storeObj = *(int*)((char*)storeUi + 0x5a0);
    int sbase = *(int*)(*(int*)(storeObj + 4) + 4);
    int32_t* stfid = (int32_t*)(sbase + 0x164 + storeObj);
    pkt.storeTfid[0] = stfid[0];
    pkt.storeTfid[1] = stfid[1];
    pkt.storeTfid[2] = stfid[2];
    pkt.storeTfid[3] = stfid[3];

    Client_SendSectorPacket(&DAT_00d1a840, 0x40, &pkt);
    return 1;
}

// Sell path excerpt from Client_UI_InventoryDropToGrid when dest inv type == 4
uint32_t Client_SendStoreTransactionSell_FromDrop(void* cursorItem /* char+0xcd0 */)
{
    if (FUN_00513770() == 0) {
        FUN_007fdfb0(&DAT_00d1a840, "The store does not want that item.", -1, 1, 0);
        return 0;
    }

    StoreTransactionRequestPacket pkt{};
    pkt.opcode = 0x2027;
    pkt.isBuy = 0;

    // quantity via cursor item vtable+0x25c
    using QtyFn = int (*)(int*);
    auto** vt = *(void***)cursorItem;
    pkt.quantity = ((QtyFn)vt[0x25c / 4])((int*)cursorItem);

    int32_t* tfid = (int32_t*)((char*)cursorItem + 0x160);
    pkt.itemTfid[0] = tfid[0];
    pkt.itemTfid[1] = tfid[1];
    pkt.itemTfid[2] = tfid[2];
    pkt.itemTfid[3] = tfid[3];
    // storeTfid may be zero on sell live captures

    Client_SendSectorPacket(&DAT_00d1a840, 0x40, &pkt);
    return 1;
}
