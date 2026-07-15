/*
 * Purpose: S2C StoreTransactionResponse (0x2028) size 0x30.
 * Stable ID: aa_exe_00810670
 * Address: 0x00810670
 * Confidence: high for fail path + credits write; probable for buy sub-branches
 */

#include <cstdint>

// Absolute layout:
//   +0x00 opcode 0x2028
//   +0x04 pad / buy helper
//   +0x08 item coid i64
//   +0x10 related coid i64 (buy)
//   +0x18 related coid i64 (buy)
//   +0x20 credits i64 → character+0x720
//   +0x28 bWasSuccessful
//   +0x29 bIsBuy
//   +0x2c quantity i32

struct StoreTransactionResponseView {
    uint32_t opcode;
    uint32_t pad04;
    int32_t  itemLo, itemHi;       // +0x08
    int32_t  relALo, relAHi;       // +0x10
    int32_t  relBLo, relBHi;       // +0x18
    int32_t  creditsLo, creditsHi; // +0x20
    uint8_t  success;              // +0x28
    uint8_t  isBuy;                // +0x29
    uint8_t  pad2a[2];
    int32_t  quantity;             // +0x2c
};

extern void  FUN_007a69d0();
extern void* FUN_007a6de0(const char*, int);
extern void  FUN_007fdfb0(void* game, void* msg, int, int, int);
extern int   CVOGReaction_ResolveObjectTarget(int g, uint32_t lo, uint32_t hi);
extern char  FUN_00587970(int character, int item);
extern char  FUN_00587c00(int character, int item, void*, void*, int qty);
extern int   FUN_00571d80(uint32_t lo, uint32_t hi, int flag);
extern char  FUN_00571b60(int item);
extern void  FUN_007fee30();
extern void  FUN_007fc150(); // clear hand / cursor
extern void  Client_RefreshOpenMissionUiWindows(void* game);
extern void  Client_GetMissionCompleteAudioTable(const char*, ...);
extern void  Client_PlayNamedInterfaceSound(const char*, ...);

void Client_RecvStoreTransactionResponse(void* game, StoreTransactionResponseView* pkt)
{
    *(uint8_t*)((char*)game + 0xb6) = 0;
    FUN_007a69d0();

    if (pkt->success == 0) {
        const char* text = (pkt->isBuy == 0)
            ? "Unable to sell item!"
            : "This item is no longer available!";
        void* msg = FUN_007a6de0(text, -1);
        FUN_007fdfb0(game, msg, -1, 1, 0);
        return;
    }

    // Require store UI open (game+0x105c / +0x1060 windows)
    // if neither store UI active with session → return

    Client_GetMissionCompleteAudioTable("loot_credits", 0, -1, -1, 0, 0, 0x1e, 0);
    Client_PlayNamedInterfaceSound("loot_credits", 0, -1, -1, 0, 0, 0x1e, 0);

    int character = *(int*)((char*)game + 0xe98);

    if (pkt->isBuy == 0) {
        // ---- SELL success ----
        int item = CVOGReaction_ResolveObjectTarget(1, pkt->itemLo, pkt->itemHi);
        if (item == 0)
            return;
        if (FUN_00587970(character, item) == 0)
            return;

        *(int*)(character + 0x650) = item;
        // optional store UI refresh FUN_0085e890

        *(int32_t*)(character + 0x720) = pkt->creditsLo;
        *(int32_t*)(character + 0x724) = pkt->creditsHi;

        int vehicle = *(int*)(character + 0x250);
        if (vehicle == 0 || *(int*)(vehicle + 0x2b0) == 0)
            return;

        int destroyed = FUN_00571d80(pkt->itemLo, pkt->itemHi, pkt->quantity != 0);
        if (destroyed != 0) {
            FUN_007fee30(); // store UI type-4 refresh
            Client_RefreshOpenMissionUiWindows(game);
            return;
        }
        destroyed = FUN_00571d80(pkt->itemLo, pkt->itemHi, pkt->quantity != 0);
        if (destroyed != 0) {
            Client_RefreshOpenMissionUiWindows(game);
            return;
        }
        // cargo destroy miss → clear hand
        FUN_007fc150();
        Client_RefreshOpenMissionUiWindows(game);
        return;
    }

    // ---- BUY success ----
    // Branch if pkt +0x10/+0x14 equals local character TFID (grant-to-self vs slot update)
    int cbase = *(int*)(*(int*)(character + 4) + 4);
    bool isSelfTfid =
        (pkt->relALo == *(int32_t*)(cbase + 0x164 + character)) &&
        (pkt->relAHi == *(int32_t*)(cbase + 0x168 + character));

    if (!isSelfTfid) {
        // store-slot path: resolve item@+0x18, decrement stack or destroy, UI refresh
        int* slot = (int*)CVOGReaction_ResolveObjectTarget(1, pkt->relBLo, pkt->relBHi);
        if (slot == nullptr)
            return;
        // vtable+0x25c get qty; if >1 decrement via +0x260; else FUN_00571d80 destroy
        // FUN_0085fd20 store refresh; or FUN_007fc150
        Client_RefreshOpenMissionUiWindows(game);
        return;
    }

    int bought = CVOGReaction_ResolveObjectTarget(1, pkt->relBLo, pkt->relBHi);
    // FUN_00587c00 attach grant using item@+0x08 as source reference
    int32_t grantRef[2] = { pkt->itemLo, pkt->itemHi };
    if (FUN_00587c00(character, bought, nullptr, grantRef, pkt->quantity) == 0)
        return;

    *(int32_t*)(character + 0x720) = pkt->creditsLo;
    *(int32_t*)(character + 0x724) = pkt->creditsHi;

    // optional store catalog refresh FUN_0088f790 / FUN_0085fcc0
    int granted = CVOGReaction_ResolveObjectTarget(1, pkt->itemLo, pkt->itemHi);
    if (granted == 0 || FUN_00571b60(granted) == 0) {
        Client_RefreshOpenMissionUiWindows(game);
        return;
    }

    FUN_007fc150();
    Client_RefreshOpenMissionUiWindows(game);
}
