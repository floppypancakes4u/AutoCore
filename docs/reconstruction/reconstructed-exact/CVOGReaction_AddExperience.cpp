/*
 * Purpose: Client XP apply kernel — scale, soft-cap, mutate total XP, level up/down.
 * Original address / stable ID: 0x00533c30 / aa_exe_00533c30
 * Original symbol: CVOGReaction_AddExperience
 * Behavioral summary: See algorithm below; server GiveXp should mirror rules for MP.
 * Known callers:
 *   Client_AwardKillExperience (S2C 0x205F, PacketOrNonKill)
 *   CVOGCombat_CalculateAndAwardKillXP (KillPath)
 *   CVOGReaction_CompleteObjective (mission final, PacketOrNonKill)
 *   CVOGCharacter_ApplyCreateFromPacket; CVOGCharacter_CheckMissionPrerequisites
 * Known callees:
 *   Experience_GetCumulativeThreshold; CVOGCharacter_LevelUp/LevelDown;
 *   CVOGCharacter_WeaponAllowsKillXpBonus; GetTickCount
 * Confidence: high (static); kill-path weapon-bonus table index residual (see review)
 * Verification: raw/aa_exe_00533c30.md; docs/XP.md
 *
 * Character layout (verified by use):
 *   +0x6b4 specialMode  +0x6c8 level  +0x6cc skillPts  +0x6ce attribPts
 *   +0x730 totalXp      +0x734 lastKillTick  +0x738 spree/hint byte
 *   +0xc50 maxLevel     +0xc54 personalXpGain float
 */

#include <cstdint>
#include <cmath>

enum XpIsKillPath : int {
    PacketOrNonKill = 0,
    KillPath = 1,
};

extern unsigned long GetTickCount();
extern bool CVOGCharacter_WeaponAllowsKillXpBonus(/* character context */);
extern uint32_t Experience_GetCumulativeThreshold(uint16_t level);
extern void CVOGCharacter_LevelUp(void* self, bool bNotifyUi);
extern void CVOGCharacter_LevelDown(void* self);
extern int GetCharacterLevel_Vfunc27c(void* self); // vtbl+0x27c

// Approximate float constants from image (read_memory):
// DAT_00aaa7b8 ≈ 0.02f; DAT_00aaa8f4 ≈ 0.04f; DAT_00aaa8f0 ≈ 0.06f
// g_flOne = 1.0f

bool CVOGReaction_AddExperience(void* self, int nAmount, XpIsKillPath isKillPath)
{
    int nLevelLoopGuard = 0;
    char* base = reinterpret_cast<char*>(self);

    if (isKillPath != PacketOrNonKill) {
        uint32_t nowTick = GetTickCount();
        uint32_t last = *reinterpret_cast<uint32_t*>(base + 0x734);
        if (nowTick - last < 5000) {
            int8_t spree = *reinterpret_cast<int8_t*>(base + 0x738) + 1;
            uint8_t clamped = static_cast<uint8_t>(spree);
            if (clamped > 4)
                clamped = 5;
            *reinterpret_cast<uint8_t*>(base + 0x738) = clamped;
        } else {
            *reinterpret_cast<uint8_t*>(base + 0x738) = 0;
        }
        *reinterpret_cast<uint32_t*>(base + 0x734) = nowTick;

        if (CVOGCharacter_WeaponAllowsKillXpBonus()) {
            // Index from nested game object +0xe818 (NOT the +0x738 spree byte).
            // Table: [0]=0, [1]=0, [2]≈0.02, [3]≈0.04, [4..15]≈0.06; clamp index 0..15.
            // nAmount = ROUND((table[i] + 1.0f) * nAmount);
        }
    }

    int nMaxLevel = *reinterpret_cast<int*>(base + 0xc50);
    float personal = *reinterpret_cast<float*>(base + 0xc54);
    int nScaledAmount = static_cast<int>(static_cast<float>(nAmount) * personal);
    int nPlayerLevel = GetCharacterLevel_Vfunc27c(self);
    int specialMode = *reinterpret_cast<int*>(base + 0x6b4);

    // Soft cap when at/above max level and not specialMode
    if ((nMaxLevel < nPlayerLevel + 1) && (specialMode < 1)) {
        int total = *reinterpret_cast<int*>(base + 0x730);
        uint16_t wLevel = static_cast<uint16_t>(GetCharacterLevel_Vfunc27c(self));
        uint32_t nThreshold = Experience_GetCumulativeThreshold(wLevel);
        int room = static_cast<int>(nThreshold - total) - 1;
        if (room < nScaledAmount)
            nScaledAmount = room;
    }

    if (nScaledAmount == 0)
        return false;

    *reinterpret_cast<int*>(base + 0x730) += nScaledAmount;

    // Level mutation only when local-player gate bit at game/object +0x7e is set
    // (exact gate path: *(char*)(*(int*)(...+0xa8 + this)+0x7e) != 0)
    bool localGate = true; // decompiler condition — see raw
    if (!localGate)
        return true;

    if (nScaledAmount < 1) {
        // negative XP → LevelDown loop or clamp total to 0 at level < 2
        int level = *reinterpret_cast<int*>(base + 0x6c8);
        if (level < 2) {
            if (*reinterpret_cast<int*>(base + 0x730) < 0) {
                *reinterpret_cast<int*>(base + 0x730) = 0;
                return true;
            }
        } else {
            uint32_t thrPrev = Experience_GetCumulativeThreshold(
                static_cast<uint16_t>(*reinterpret_cast<int*>(base + 0x6c8) - 1));
            if (*reinterpret_cast<int*>(base + 0x730) < static_cast<int>(thrPrev)) {
                do {
                    nLevelLoopGuard++;
                    if (nLevelLoopGuard > 300)
                        return true;
                    uint32_t thr = Experience_GetCumulativeThreshold(
                        *reinterpret_cast<uint16_t*>(base + 0x6c8));
                    if (thr == 0x7fffffff)
                        return true;
                    if (*reinterpret_cast<int*>(base + 0x6c8) < 1)
                        return true;
                    CVOGCharacter_LevelDown(self);
                    thrPrev = Experience_GetCumulativeThreshold(
                        static_cast<uint16_t>(*reinterpret_cast<int*>(base + 0x6c8) - 1));
                } while (*reinterpret_cast<int*>(base + 0x730) < static_cast<int>(thrPrev));
            }
        }
    } else if ((nMaxLevel < *reinterpret_cast<int*>(base + 0x6c8) + 1) && specialMode < 1) {
        // already at max: pin total to threshold(level)-1 if overshot
        uint32_t thr = Experience_GetCumulativeThreshold(
            *reinterpret_cast<uint16_t*>(base + 0x6c8));
        if (static_cast<int>(thr) < *reinterpret_cast<int*>(base + 0x730)) {
            *reinterpret_cast<uint32_t*>(base + 0x730) = thr - 1;
            return true;
        }
    } else {
        // positive XP level-up loop
        uint32_t thr = Experience_GetCumulativeThreshold(
            *reinterpret_cast<uint16_t*>(base + 0x6c8));
        if (static_cast<int>(thr) <= *reinterpret_cast<int*>(base + 0x730)) {
            while (((*reinterpret_cast<int*>(base + 0x6c8) < nMaxLevel) ||
                    (specialMode > 0)) &&
                   (++nLevelLoopGuard < 0x12d)) {
                thr = Experience_GetCumulativeThreshold(
                    *reinterpret_cast<uint16_t*>(base + 0x6c8));
                if (thr == 0x7fffffff)
                    break;
                CVOGCharacter_LevelUp(self, true);
                thr = Experience_GetCumulativeThreshold(
                    *reinterpret_cast<uint16_t*>(base + 0x6c8));
                if (*reinterpret_cast<int*>(base + 0x730) < static_cast<int>(thr))
                    return true;
            }
        }
    }
    return true;
}
