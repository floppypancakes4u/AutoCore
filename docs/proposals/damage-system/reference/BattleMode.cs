namespace AutoCore.Game.Combat;

using System;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Entities;

/// <summary>
/// Combat Modes — the class-specific tactical battle-modes the HUD calls "Combat Modes" but the wire calls
/// BattleMode (opcode ChangeBattleMode 0x20BB/0x20BC). NOT the ChangeCombatMode 0x20B9/0x20BA Power-
/// Distribution triangle (that already works). Each class has 3 modes = self-cast skills from
/// tConfigNewCharacters IDSkillBattleMode1/2/3; selecting one applies its status effects. The client is
/// display-only for player damage, so the SERVER owns the effect. Magnitudes below are dumped verbatim from
/// the clonebase skill Elements (2026-07-09); the ElementType→stat mapping was RE-verified via the client's
/// decodeSkillElements/calculate/description-tag chain. See memory combat-modes-vs-power-distribution.md.
///
/// Class 3 (ranged DPS — Bounty Hunter/Avenger/Agent) is implemented. The other classes' modes are RE'd
/// (tank last-stand, engineer convoy-sustain, lieutenant pet/group) but need machinery we don't have yet
/// (heat/HP pools, convoy-wide buffs, pets), so they resolve to None for now — the mode is still tracked
/// and acked so the HUD button stays in sync; it just has no combat effect.
/// </summary>
public readonly record struct BattleModeEffect(
    float RefireMultiplier,  // multiplies the weapon fire-cooldown: 0.9 = 10% faster, 2.0 = half rate. 1.0 = none.
    float HitChanceAdd,      // additive to hit chance: -0.05 = -5%, +0.33 = +33%.
    float CritChanceAdd,     // additive to crit chance: +0.33 = +33%.
    float PhysFlatAdd)       // flat physical damage added to the weapon's physical min & max (already level-resolved).
{
    /// <summary>No active mode: fire at normal rate, no hit/crit/damage modifier.</summary>
    public static readonly BattleModeEffect None = new(1f, 0f, 0f, 0f);

    /// <summary>Resolve the active mode's effect for a Character (looks up class + selected index).</summary>
    public static BattleModeEffect ForCharacter(Character attacker, int level)
    {
        if (attacker == null || attacker.ActiveBattleMode < 0)
            return None;
        int cls = (attacker.CloneBaseObject as CloneBaseCharacter)?.CharacterSpecific.Class ?? -1;
        return Resolve(cls, attacker.ActiveBattleMode, level);
    }

    /// <summary>
    /// Resolve (class, mode-index 0/1/2, level) → effect. Values verbatim from the clonebase skill Elements.
    /// Returns None for unmodeled class/index combos.
    /// </summary>
    public static BattleModeEffect Resolve(int attackerClass, int modeIndex, int level)
    {
        if (modeIndex < 0 || modeIndex > 2)
            return None;

        // Class 3 — ranged DPS (skills 5170/5178/5181 human, race-invariant effects). Index order matches the
        // HUD left→right: 0 Frenzy, 1 Sharpshooter, 2 Sniper.
        if (attackerClass == 3)
        {
            return modeIndex switch
            {
                0 => new BattleModeEffect(0.9f, -0.05f, 0f, 0f),                    // Frenzy:       refire x0.9, -5% accuracy
                1 => new BattleModeEffect(2.0f, +0.33f, 0f, 0f),                    // Sharpshooter: refire x2,   +33% hit
                2 => new BattleModeEffect(2.0f, 0f, +0.33f, 1f + 0.1f * level),     // Sniper:       refire x2,   +33% crit, +(1+0.1*lvl) flat physical
                _ => None,
            };
        }

        // Class 0 tank (Attrition/Tanker/Siege), Class 1 engineer (AutoCooldown/Power/Repair), Class 2
        // lieutenant (Horde/PetFocus/GroupFocus): RE'd, not yet modeled server-side. See memory.
        return None;
    }
}
