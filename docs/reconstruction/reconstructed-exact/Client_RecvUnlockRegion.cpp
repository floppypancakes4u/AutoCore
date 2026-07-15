/*
 * Purpose: S2C UnlockRegion 0x205B — continent unlock / explored bitmask.
 * Stable ID: aa_exe_00809550  Address: 0x00809550
 */

#include <cstdint>

extern void  CVOGReaction_RelockContinentObject(int continentId);
extern void  CVOGReaction_UnlockContinentObject(void* character, uint32_t id);
extern void* CNDHash_LookupByKey(void* hash, uint32_t key);
extern void  CVOGCharacter_SetAreaExploredBit(void* character, int areaIndex /*1..32*/);

// Packet view (param holds opcode at +0 in dispatch; decompiler used pPacket offsets)
// +4 ContinentId (int)
// +8 UnlockFlag (byte) — 0 = relock
// +0xC ExploredBits (uint)

void Client_RecvUnlockRegion(void* game, const uint8_t* packet)
{
    void* character = *(void**)((char*)game + 0xe98);
    if (character == nullptr)
        return;

    int continentId = *(int*)(packet + 4);
    uint8_t unlockFlag = packet[8];
    uint32_t exploredBits = *(uint32_t*)(packet + 0xC);

    if (unlockFlag == 0) {
        CVOGReaction_RelockContinentObject(continentId);
        return;
    }

    // If no local USContinentUnlocked entry: UnlockContinentObject only (bits ignored → 0)
    // Decompiler: lookup entry; if missing, unlock with bits=0
    // If entry exists and bits differ: for areas 1..32, if bit set, SetAreaExploredBit

    // Structural reconstruction of bit walk (evidence: plate comment + truncated decompile):
    void* entry = nullptr; // CNDHash_LookupByKey(unlockedHash, continentId)
    if (entry == nullptr) {
        CVOGReaction_UnlockContinentObject(character, (uint32_t)continentId);
        return;
    }

    uint32_t prevBits = 0; // from entry
    if (prevBits == exploredBits)
        return;

    for (int area = 1; area <= 32; area++) {
        uint32_t mask = 1u << (area - 1);
        if ((exploredBits & mask) != 0 && (prevBits & mask) == 0)
            CVOGCharacter_SetAreaExploredBit(character, area);
    }
}

enum : uint32_t {
    kUnlockRegionOpcode = 0x205B,
};
