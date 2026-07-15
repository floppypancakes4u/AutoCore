/*
 * Purpose: S2C GiveCredits (0x205E) — additive currency delta + credits floater/sound.
 * Original address / stable ID: 0x0080cac0 / aa_exe_0080cac0
 * Original symbol: Client_RecvGiveCredits
 * Behavioral summary:
 *   Require local character game+0xe98.
 *   CVOGCharacter_AddCredits(char, int64 amount @ packet+0x08) → money at char+0x720.
 *   If amount > 0: play "credits" interface sound.
 *   If vehicle: floater type 4 when char+0xd6c == 0; refresh money HUD panel.
 * Known callers: Client_PacketDispatch case 0x205e
 * Known callees: CVOGCharacter_AddCredits; Client_EnqueueCombatFloater_INFERRED;
 *   Client_PlayNamedInterfaceSound
 * Confidence: high (static)
 * Verification: raw/aa_exe_0080cac0.md; AutoCore GiveCreditsPacket.cs
 *
 * Wire:
 *   +0x04 pad int32
 *   +0x08 amount int64 (signed delta)
 *
 * Relation to XP path:
 *   Sibling economy notify. Do NOT send after mission CompleteObjective already added
 *   credits client-side (double-count). Login restore uses absolute 0x2017 CharacterLevel.
 */

#include <cstdint>

extern void CVOGCharacter_AddCredits(void* character, int64_t amount);
extern void Client_EnqueueCombatFloater_INFERRED(void* floaterBundle);
extern void Client_GetMissionCompleteAudioTable(...);
extern void Client_PlayNamedInterfaceSound(...);
extern void FUN_007a4480(int, const char*);

// Dispatch: ESI = game, EDI = packet (plate convention for this handler).
void Client_RecvGiveCredits_Explicit(void* game, const uint8_t* packet)
{
    void* character = *reinterpret_cast<void**>(reinterpret_cast<char*>(game) + 0xe98);
    if (character == nullptr) {
        FUN_007a4480(0, "VOG_DEBUG_STOP");
        return;
    }

    int64_t amount = *reinterpret_cast<const int64_t*>(packet + 8);
    CVOGCharacter_AddCredits(character, amount);

    int32_t amount_hi = *reinterpret_cast<const int32_t*>(packet + 0xc);
    int32_t amount_lo = *reinterpret_cast<const int32_t*>(packet + 8);
    if (amount_hi >= 0 && (amount_hi > 0 || amount_lo != 0)) {
        // play "credits" named UI sound (mission-complete audio table lookup)
        (void)"credits";
    }

    int vehicle = *reinterpret_cast<int*>(reinterpret_cast<char*>(character) + 0x250);
    if (vehicle != 0) {
        if (*reinterpret_cast<int*>(reinterpret_cast<char*>(character) + 0xd6c) == 0) {
            // floater type 4; amount lo dword at packet+8
            // Client_EnqueueCombatFloater_INFERRED(...)
        }
        // money HUD: game+0x1040 → panel +0x50c → vfunc +0x448 refresh
    }
}
