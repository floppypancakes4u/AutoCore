namespace AutoCore.Game.Combat;

using System;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Entities;

/// <summary>
/// Authentic server-side weapon-damage computation, reverse-engineered from the retail client and
/// verified against it. The client is DISPLAY-ONLY for player damage (live-confirmed 2026-07-07:
/// killing an Ostrake tripped neither OnFire nor CalculateCriticalHit), so the SERVER owns this whole
/// formula — the client just applies the 0x2023 int we send. Full RE + addresses in memory
/// combat-damage-formula-re.md; the client anchors are:
///   CVOGWeapon::OnFire            @0x56e000  (per-target orchestrator: OnHit -> falloff -> crit)
///   CVOGClonedObjectBase::OnHit   @0x515520  (per-type roll [min,max] + level bonus + mitigation)
///   CVOGWeapon::GetTotalDamageLevelBonus @0x56b340  (perLevelDmg * level, additive to the primary type)
///   CVOGSectorMap::GetCriticalHitChance  @0x4cef70  (base(Perception)+offense-defense, floor 0.05)
///   CVOGSectorMap::CalculateCriticalHit  @0x4cf080  (d100 <= chance; mult = level*0.01 + 1.2)
///   CVOGSectorMap::CalculateHitChance    @0x4ceba0  (inanimate victim -> 1.0 "AutoHit"; else Combat vs
///                                                    Perception + offense, |levelDelta|>9 gate, clamp .05/.95)
///
/// Pipeline (per target): hit/miss (inanimate ALWAYS hits) -> per-class attacker scalar + per damage-type
/// roll + level bonus + per-type armor mitigation -> spray distance falloff -> crit -> global scalar.
///
/// The RE pins the STRUCTURE exactly; a few MAGNITUDES decompile out of messy x87 FPU code and are
/// exposed as the Tunables block for the live-tuning pass. Everything else is verbatim from the client.
/// </summary>
public static class DamageCalculator
{
    // ===================== Tunables (RE structure exact; magnitudes live-tuned) =====================
    // Crit multiplier — SOLID (GetCriticalHitMultiplier @0x4cd550: level*0.01 + 1.2).
    public const float CritMultBase = 1.2f;
    public const float CritMultPerLevel = 0.01f;

    // Crit chance — base derives from Perception (GetBaseCriticalHitChance @0x4c4dd0, FPU-fuzzy) plus
    // attacker crit-offense minus victim crit-defense (the "Criticals"/"Critical Def" stats AutoCore does
    // not model yet -> 0 for now), floored at 5% (DAT_009cbf80, SOLID).
    public const float CritChanceFloor = 0.05f;
    public const float CritChanceBase = 0.02f;
    public const float CritChancePerPerception = 0.0018f;  // ~ Perception * 0.001 * 1.775
    public const float CritChanceCap = 0.95f;

    // Hit chance — the |levelDelta|>9 anti-farm gate is SOLID (pin 0.95/0.05). The base curve is fuzzy;
    // it's built from the authentic inputs (attacker Combat + weapon offense vs victim defense).
    public const int HitLevelDeltaGate = 9;
    public const float HitChanceMax = 0.95f;
    public const float HitChanceMin = 0.05f;
    public const float HitChanceBase = 0.75f;              // even-matched baseline
    public const float HitChancePerRating = 1f / 200f;     // (attackRating - defenseRating) scaling

    // Armor mitigation — authentic OnHit subtracts ~rand(1, armor*0.1) per damage type.
    public const float ArmorMitigationScale = 0.1f;

    // Theory penetration — the attacker's Theory attribute cuts the victim's effective resistance before
    // mitigation ("Enemy Resistance Reduction %" per the in-game attribute tooltip; client OnHit @0x515520
    // sets the per-hit combat penetration = attacker GetAttribTheory @0x4c4140, overwriting the weapon's own
    // penetration for attribute-bearing attackers). Structure is RE-confirmed; the per-point magnitude is a
    // live tunable — 0.4%/point => Theory 100 trims 40% of resistance, capped at 90%. (1.2) Overturns the old
    // "attributes are pure equip gates" note, which the in-game tooltips disproved.
    public const float TheoryPenetrationPerPoint = 0.004f;
    public const float TheoryPenetrationMax = 0.9f;

    // Spray falloff — authentic OnFire: secondary cone targets take base*(1.05 - dist/range); primary = 1.0.
    public const float SprayFalloffBase = 1.05f;

    // Global damage scalar — retail's setplayerdamageglobal knob (default 1.0).
    public const float GlobalDamageScalar = 1.0f;

    // Per-class outgoing-damage multiplier — SOLID (client OnHit @0x515520 indexes DAT_009cdf9c by the
    // ATTACKER's Class and scales every raw damage channel by it before mitigation). The index is
    // CharacterSpecific.Class (enumClassType 0-3), NOT any weapon field — proven via GetClassString
    // @0x51f940 / GetFileNameLetters @0x51f550 (character-specific +0x531 = Class, +0x532 = Race). The
    // 4 careers (per race): 0 tank-bruiser (Commando/Champion/Terminator) hits hardest; 1 healer/buffer
    // (Engineer/Shaman/Constructor); 2 summoner (Lieutenant/Archon/MasterMind) is baseline 1.0 since its
    // damage lives in pets/skills; 3 ranged DPS (Bounty Hunter/Avenger/Agent). NPC creature attackers
    // have no Class -> factor 1.0.
    public static readonly float[] ClassDamageBalance = { 1.35f, 1.15f, 1.0f, 1.23f };
    // ================================================================================================

    public readonly record struct Result(int Damage, bool IsCrit, bool Miss);

    /// <summary>
    /// Compute one weapon hit against one target. <paramref name="isSprayTarget"/> is true for secondary
    /// cone/spray targets (distance falloff applies); false for the primary/hard target.
    /// </summary>
    public static Result Compute(
        Character attackerChar, int attackerLevel, ClonedObjectBase target,
        WeaponSpecific weapon, Random rng, bool isSprayTarget, float dist,
        BattleModeEffect battleMode = default)
    {
        var atk = ReadAttacker(attackerChar, attackerLevel);
        var vic = ReadVictim(target);

        // 1) HIT / MISS. Inanimate targets (buildings, props, mission objects) are ALWAYS hit — the client
        //    short-circuits to 1.0 "Victim inanimate (AutoHit)"; only Creatures/Vehicles roll to-hit.
        //    Battle-mode accuracy (Frenzy -5%, Sharpshooter +33%) folds into the to-hit AFTER the anti-farm
        //    level gate so it can't push past the 0.05/0.95 pins.
        if (target is Creature || target is Vehicle)
        {
            var hitChance = ComputeHitChance(atk, vic, weapon, battleMode.HitChanceAdd);
            if (rng.NextDouble() > hitChance)
                return new Result(0, false, true);
        }

        // 2) BASE DAMAGE: roll each of the 6 damage-type channels in [min,max], add the per-level bonus to
        //    the primary type, and subtract per-type armor mitigation.
        var min = weapon.MinMin.Damage;
        var max = weapon.MaxMax.Damage;
        var resist = ReadResistances(target);
        int primary = PrimaryDamageType(max);
        int levelBonus = (int)MathF.Round(weapon.DamageBonusPerLevel * attackerLevel); // perLevelDmg * level
        // Battle-mode flat physical bonus (Sniper's dmgadd_equip_physical = +(1 + 0.1*level) to physical
        // min & max). Lands on channel 0 (physical) regardless of the weapon's own type, matching the skill.
        int physFlat = (int)MathF.Round(battleMode.PhysFlatAdd);

        // Per-class scalar (see ClassDamageBalance): the client scales each raw channel by the attacker's
        // class factor BEFORE the level bonus and mitigation. 1.0 for non-character attackers (NPC creatures
        // have no Class) and out-of-range values. The client also wraps this in a weapon SubType==0 check
        // (excludes deployables, SubType 0x11); deployables don't route through this cone path in AutoCore,
        // so no explicit SubType gate is needed here yet.
        float classMul = 1f;
        int attackerClass = (attackerChar?.CloneBaseObject as CloneBaseCharacter)?.CharacterSpecific.Class ?? -1;
        if (attackerClass >= 0 && attackerClass < ClassDamageBalance.Length)
            classMul = ClassDamageBalance[attackerClass];

        int baseTotal = 0;
        if (min != null && max != null && min.Length >= 6 && max.Length >= 6)
        {
            for (int t = 0; t < 6; t++)
            {
                int lo = (int)MathF.Round(min[t] * classMul);
                int hi = (int)MathF.Round(max[t] * classMul);
                if (t == 0 && physFlat > 0) { lo += physFlat; hi += physFlat; } // battle-mode flat physical (Sniper)
                if (t == primary) { lo += levelBonus; hi += levelBonus; }
                if (hi < lo) (lo, hi) = (hi, lo);
                int roll = hi > lo ? rng.Next(lo, hi + 1) : lo;

                if (roll > 0 && resist != null && t < resist.Length && resist[t] > 0)
                {
                    // Theory penetration: attacker Theory shaves the victim's effective resistance before
                    // mitigation (in-game "Enemy Resistance Reduction %"). At stats=1 this is ~0.4% (a no-op);
                    // it bites on high-Theory attackers vs high-armor targets. (1.2)
                    float pen = Math.Min(TheoryPenetrationMax, atk.Theory * TheoryPenetrationPerPoint);
                    float effResist = resist[t] * (1f - pen);
                    int cap = (int)MathF.Ceiling(effResist * ArmorMitigationScale);
                    if (cap > 0) roll -= rng.Next(1, cap + 1);
                }

                if (roll > 0) baseTotal += roll;
            }
        }

        if (baseTotal <= 0)
        {
            // Fallback: the scalar corner values when the 6-channel arrays are empty (some legacy weapons).
            int lo = (int)MathF.Round(weapon.DmgMinMin * classMul);
            int hi = (int)MathF.Round(weapon.DmgMaxMax * classMul);
            if (hi < lo) (lo, hi) = (hi, lo);
            baseTotal = (hi > lo ? rng.Next(lo, hi + 1) : Math.Max(1, lo)) + levelBonus + physFlat;
        }

        // 3) SPRAY DISTANCE FALLOFF (secondary cone targets only).
        float dmg = baseTotal;
        if (isSprayTarget && weapon.RangeMax > 0f)
            // Client OnFire has NO upper clamp: factor = 1.05 - dist/range, so a point-blank spray secondary
            // takes up to a 1.05x bonus (the old Math.Clamp(...,0,1) capped it at 1.0). (1.6 fix)
            dmg *= Math.Max(0f, SprayFalloffBase - dist / weapon.RangeMax);

        // 4) CRIT: roll against crit chance; on a crit scale by (level*0.01 + 1.2). Sniper battle-mode adds
        //    +33% crit chance (its criticals_vs_vehicles/creatures elements).
        bool isCrit = false;
        if (rng.NextDouble() <= ComputeCritChance(atk, vic) + battleMode.CritChanceAdd)
        {
            isCrit = true;
            dmg *= attackerLevel * CritMultPerLevel + CritMultBase;
        }

        // 5) GLOBAL + weapon scalar.
        float wscalar = weapon.DamageScalar > 0f ? weapon.DamageScalar : 1f;
        int final = (int)MathF.Round(dmg * wscalar * GlobalDamageScalar);
        return new Result(Math.Max(1, final), isCrit, false);
    }

    /// <summary>
    /// Fixed-amount, single-channel damage routed through the SAME per-type armor/resistance mitigation as
    /// weapon fire, with NO hit-roll (deterministic — for the /sd self-damage test command). Lets us verify
    /// resistances / resist buffs / defense modules against a known input. <paramref name="attackerTheory"/>
    /// shaves the victim's effective resistance ("Enemy Resistance Reduction") exactly as Compute does — pass
    /// 0 to see raw resistance. Returns post-mitigation damage (>= 0). The shield/power-pool stages are
    /// applied later by the caller once those pools exist (client order: armor/resist -> shield -> power -> HP).
    /// </summary>
    public static int ComputeFixed(ClonedObjectBase target, int amount, int damageType, Random rng, int attackerTheory = 0)
    {
        if (amount <= 0) return 0;
        if (damageType < 0 || damageType > 5) return amount;

        var resist = ReadResistances(target);
        int roll = amount;
        if (resist != null && damageType < resist.Length && resist[damageType] > 0)
        {
            float pen = Math.Min(TheoryPenetrationMax, attackerTheory * TheoryPenetrationPerPoint);
            float effResist = resist[damageType] * (1f - pen);
            int cap = (int)MathF.Ceiling(effResist * ArmorMitigationScale);
            if (cap > 0) roll -= rng.Next(1, cap + 1);
        }
        return Math.Max(0, roll);
    }

    // -------------------------------------------------------------------------------------------------

    private static float ComputeHitChance(in Combatant atk, in Combatant vic, in WeaponSpecific weapon, float modeHitAdd)
    {
        // Anti-farm level gate — SOLID (CalculateHitChance: |atkLvl - vicLvl| > 9 pins the outcome). The
        // battle-mode accuracy add is applied AFTER this gate (below), so it never breaks the pins.
        int delta = atk.Level - vic.Level;
        if (delta > HitLevelDeltaGate) return HitChanceMax;
        if (delta < -HitLevelDeltaGate) return HitChanceMin;

        // Attacker Combat + weapon offense (+ per-level) vs victim defense. Attributes are now LIVE inputs
        // (they were inert before) — the authentic model reads attacker GetAttribCombat vs victim defense.
        // modeHitAdd = battle-mode accuracy (Frenzy -0.05, Sharpshooter +0.33).
        float attackRating = atk.Combat + weapon.OffenseBonus + weapon.HitBonusPerLevel * atk.Level;
        float defenseRating = vic.DefenseBonus + vic.Perception;
        float chance = HitChanceBase + (attackRating - defenseRating) * HitChancePerRating + modeHitAdd;
        if (weapon.AccucaryModifier > 0f) chance *= weapon.AccucaryModifier;
        return Math.Clamp(chance, HitChanceMin, HitChanceMax);
    }

    private static float ComputeCritChance(in Combatant atk, in Combatant vic)
    {
        // base(Perception) + attacker crit-offense - victim crit-defense. Client GetCriticalHitChance
        // @0x4cef70 clamps ONLY negatives to 0.05 (DAT_009cbf80) and has NO upper cap — so a low-Perception
        // char correctly stays below 5% (the old Math.Clamp floor ~doubled their crit rate) and a high-crit
        // build can exceed 0.95. (1.5 fix; CritChanceCap is now dead, kept for reference.)
        float chance = CritChanceBase + atk.Perception * CritChancePerPerception
                       + atk.CritOffense - vic.CritDefense;
        if (chance < 0f) chance = CritChanceFloor;
        return chance;
    }

    /// <summary>Index of the primary damage type = the channel with the largest max damage (level bonus lands here).</summary>
    private static int PrimaryDamageType(short[] max)
    {
        if (max == null) return 0;
        int best = 0, bestVal = int.MinValue;
        for (int t = 0; t < max.Length && t < 6; t++)
            if (max[t] > bestVal) { bestVal = max[t]; best = t; }
        return best;
    }

    /// <summary>Per-type armor/resistance (short[6]) on any object; null if the target has none.</summary>
    private static short[] ReadResistances(ClonedObjectBase target)
    {
        // Equipped vehicle armor takes precedence (its Resistances are the driver's actual mitigation).
        if (target is Vehicle v && v.Armor?.CloneBaseArmor?.ArmorSpecific?.Resistances?.Damage is { } vres)
            return vres;
        return target.CloneBaseObject?.SimpleObjectSpecific.DamageArmor?.Damage;
    }

    private readonly record struct Combatant(
        int Level, int Combat, int Perception, int Theory,
        int DefenseBonus, float CritOffense, float CritDefense);

    private static Combatant ReadAttacker(Character attackerChar, int attackerLevel)
    {
        var s = attackerChar?.Stats;
        return new Combatant(
            Level: attackerLevel,
            Combat: s?.AttributeCombat ?? 1,
            Perception: s?.AttributePerception ?? 1,
            Theory: s?.AttributeTheory ?? 1,
            DefenseBonus: 0,
            CritOffense: 0f,   // TODO: "Criticals vs Creatures/Vehicles" stat (not modeled yet)
            CritDefense: 0f);
    }

    private static Combatant ReadVictim(ClonedObjectBase target)
    {
        int level = (target as Creature)?.GetLevel()
                    ?? (target as Vehicle)?.Owner?.GetAsCreature()?.GetLevel()
                    ?? 1;

        // Player vehicle victim: attributes from the owning character's stat block.
        var vs = (target as Vehicle)?.Owner?.GetAsCharacter()?.Stats;
        if (vs != null)
            return new Combatant(level, vs.AttributeCombat, vs.AttributePerception, vs.AttributeTheory,
                (target as Vehicle)?.Armor?.CloneBaseArmor?.ArmorSpecific?.DefenseBonus ?? 0, 0f, 0f);

        // NPC creature victim: attributes/defense from the creature clonebase (CreatureSpecific), if present.
        // Per-creature enhancement bonuses (tCreatureEnhancement) ride on the Creature entity, so an elite's
        // boosted Combat/Perception raise its effective to-hit/crit defense above the shared clonebase base.
        var cs = (target?.CloneBaseObject as CloneBaseCreature)?.CreatureSpecific;
        if (cs != null)
        {
            var cre = target as Creature;
            return new Combatant(level,
                cs.AttributeCombat + (cre?.AttributeCombatBonus ?? 0),
                cs.AttributePerception + (cre?.AttributePerceptionBonus ?? 0),
                cs.AttributeTheory, cs.DefensiveBonus, 0f, 0f);
        }

        // Inanimate / unknown: to-hit is skipped for these anyway; return neutral defaults.
        return new Combatant(level, 1, 1, 1, 0, 0f, 0f);
    }
}
