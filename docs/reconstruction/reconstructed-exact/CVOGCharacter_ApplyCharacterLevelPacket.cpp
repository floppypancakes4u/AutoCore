/*
 * Purpose: Absolute apply of Packet_CharacterLevel fields onto CVOGCharacter.
 * Original address / stable ID: 0x00531e90 / aa_exe_00531e90
 * Original symbol: CVOGCharacter_ApplyCharacterLevelPacket
 * Behavioral summary:
 *   If level changes: vehicle hook + level-up UI packet.
 *   HP applied only when game gate + vehicle present (vtbl max/cur).
 *   Always: Level, Currency (int64 absolute), Experience (int32 absolute),
 *   skill/attrib/research pools, current/max mana; optional skill-rank loop.
 * Known callers: Client_RecvCharacterLevel via vfunc +0xcc
 * Known callees: Client_SendLogicUiPacket; Skill_SetRankAndReevaluate; FUN_00531330
 * Confidence: high on absolute fields; skill-rank tail partially inferred
 * Verification: raw/aa_exe_00531e90.md; CharacterLevelPacket.cs
 */

#include <cstdint>

struct Packet_CharacterLevel {
    // full packet including opcode
    uint32_t opcode;        // +0x00
    int32_t  unknown_hdr;   // +0x04
    // TFID +0x08.. (coid lo/hi, global)
    uint8_t  bLevel;        // +0x18
    // pad
    int64_t  llCurrency;    // +0x20
    int32_t  nExperience;   // +0x28
    int32_t  nHealth;       // +0x2C
    int32_t  nHealthMax;    // +0x30
    int16_t  nCurrentMana;  // +0x34
    int16_t  nMaxMana;      // +0x36
    // attributes...
    int16_t  nAttributePoints; // +0x40 (AutoCore layout)
    int16_t  nSkillPoints;     // +0x42
    // Unknown7 +0x44
    int16_t  nResearchPoints;  // +0x46
    // nSkillRankCount_Inferred + trailing rank pairs
};

void CVOGCharacter_ApplyCharacterLevelPacket(void* self, Packet_CharacterLevel* pPacket)
{
    char* base = reinterpret_cast<char*>(self);

    if (static_cast<uint32_t>(pPacket->bLevel) !=
        *reinterpret_cast<uint32_t*>(base + 0x6c8)) {
        int vehicle = *reinterpret_cast<int*>(base + 0x250);
        if (vehicle != 0) {
            // vehicle vfunc +0x244 pre-level change
        }
        // ceil(raceUiScalar + baseUiScalar); Client_SendLogicUiPacket();
    }

    // HP gate: object+0xa8 present, +0xf5 != 0, vehicle at +0x250
    //   vtbl+0x248 set max; vtbl+0x240 set current = pPacket->nHealth

    *reinterpret_cast<uint32_t*>(base + 0x6c8) = pPacket->bLevel;
    *reinterpret_cast<int64_t*>(base + 0x720) = pPacket->llCurrency; // absolute money
    *reinterpret_cast<int32_t*>(base + 0x730) = pPacket->nExperience; // absolute XP

    // FUN_004c2ee0/ef0/f00/f10 attribute-side refresh helpers

    *reinterpret_cast<int16_t*>(base + 0x6cc) = pPacket->nSkillPoints;
    *reinterpret_cast<int16_t*>(base + 0x6ce) = pPacket->nAttributePoints;
    *reinterpret_cast<int16_t*>(base + 0x12c) = pPacket->nCurrentMana;
    *reinterpret_cast<int16_t*>(base + 0x12e) = pPacket->nMaxMana;
    *reinterpret_cast<int16_t*>(base + 0x580) = pPacket->nResearchPoints;

    // if skill rank count > 0: loop set ranks via vfunc +0x234 + Skill_SetRankAndReevaluate
    // FUN_00531330();
}
