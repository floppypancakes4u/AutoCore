/*
 * Purpose: S2C CharacterLevel (0x2017) absolute snapshot of level/XP/currency/pools.
 * Original address / stable ID: 0x00810f00 / aa_exe_00810f00
 * Original symbol: Client_RecvCharacterLevel
 * Behavioral summary:
 *   Lookup object by packet TFID (lo@+8, hi@+0xc, global@+0x10).
 *   If found, vfunc+0xcc → CVOGCharacter_ApplyCharacterLevelPacket (absolute apply).
 *   If TFID matches local player at game+0xe98: refresh level UI + optional HUD panels.
 *   Always refresh open mission windows; optional secondary UI at game+0x10b0.
 * Known callers: Client_PacketDispatch case 0x2017
 * Known callees: Client_LookupObjectByTfid_Inferred; ApplyCharacterLevelPacket;
 *   Client_RefreshLocalCharacterLevelUi; Client_RefreshOpenMissionUiWindows
 * Confidence: high (static)
 * Verification: raw/aa_exe_00810f00.md + Apply capture aa_exe_00531e90.md
 * Prior docs: docs/XP.md (authority formulas); packet AutoCore CharacterLevelPacket.cs
 */

#include <cstdint>

struct Packet_CharacterLevelView {
    uint32_t opcode;       // +0x00
    int32_t  unknown_hdr;  // +0x04
    int32_t  coid_lo;      // +0x08
    int32_t  coid_hi;      // +0x0c
    uint8_t  b_global;     // +0x10
    // pad to +0x18
    uint8_t  level;        // +0x18
    // ... currency +0x20, experience +0x28, health +0x2c, mana +0x34, points ...
};

extern int*  Client_LookupObjectByTfid_Inferred(uint8_t bGlobal, uint32_t lo, uint32_t hi);
extern void  Client_RefreshLocalCharacterLevelUi();
extern void  Client_RefreshOpenMissionUiWindows(void* game);
extern void  FUN_008a05a0(); // secondary HUD refresh when panel visible

// vfunc +0xcc on character object → CVOGCharacter_ApplyCharacterLevelPacket
// Dispatch often leaves packet pointer in EAX; game client in ECX (this).

void Client_RecvCharacterLevel(void* pGameClient, Packet_CharacterLevelView* pPacket /* often EAX */)
{
    // Decompiler names packet-in-EAX as pPacketInEax; model as pPacket.
    int* pObject = Client_LookupObjectByTfid_Inferred(
        pPacket->b_global,
        (uint32_t)pPacket->coid_lo,
        (uint32_t)pPacket->coid_hi);

    if (pObject != nullptr) {
        // (**(code **)(*pObject + 0xcc))();  // thiscall ApplyCharacterLevelPacket
        using ApplyFn = void(__thiscall*)(int* self, Packet_CharacterLevelView*);
        auto* vtbl = *reinterpret_cast<void***>(pObject);
        reinterpret_cast<ApplyFn>(vtbl[0xcc / sizeof(void*)])(pObject, pPacket);
    }

    int pLocalPlayerCtx = *reinterpret_cast<int*>(reinterpret_cast<char*>(pGameClient) + 0xe98);
    if (pLocalPlayerCtx != 0) {
        int nLocalPlayerOff = *reinterpret_cast<int*>(
            *reinterpret_cast<int*>(pLocalPlayerCtx + 4) + 4);
        int nLocalTfidBase = nLocalPlayerOff + 0x164 + pLocalPlayerCtx;
        int local_lo = *reinterpret_cast<int*>(nLocalPlayerOff + 0x164 + pLocalPlayerCtx);
        int local_hi = *reinterpret_cast<int*>(nLocalTfidBase + 4);
        char local_g = *reinterpret_cast<char*>(nLocalTfidBase + 8);

        if (pPacket->coid_lo == local_lo &&
            pPacket->coid_hi == local_hi &&
            (char)pPacket->b_global == local_g) {
            Client_RefreshLocalCharacterLevelUi();
            int* pUi = *reinterpret_cast<int**>(reinterpret_cast<char*>(pGameClient) + 0x1034);
            if (pUi != nullptr) {
                using IsVisibleFn = char(__thiscall*)(int*);
                auto* uiVtbl = *reinterpret_cast<void***>(pUi);
                char bUiVisible = reinterpret_cast<IsVisibleFn>(uiVtbl[0x3d8 / sizeof(void*)])(pUi);
                if (bUiVisible != 0) {
                    using UiFn = void(__thiscall*)(int*);
                    reinterpret_cast<UiFn>(uiVtbl[0x448 / sizeof(void*)])(pUi);
                    reinterpret_cast<UiFn>(uiVtbl[0x34c / sizeof(void*)])(pUi);
                }
            }
        }
    }

    Client_RefreshOpenMissionUiWindows(pGameClient);

    int* pUi2 = *reinterpret_cast<int**>(reinterpret_cast<char*>(pGameClient) + 0x10b0);
    if (pUi2 != nullptr) {
        using IsVisibleFn = char(__thiscall*)(int*);
        auto* uiVtbl = *reinterpret_cast<void***>(pUi2);
        char bUiVisible = reinterpret_cast<IsVisibleFn>(uiVtbl[0x3d8 / sizeof(void*)])(pUi2);
        if (bUiVisible != 0)
            FUN_008a05a0();
    }
}
