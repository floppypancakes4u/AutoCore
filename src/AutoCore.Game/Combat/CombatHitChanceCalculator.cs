namespace AutoCore.Game.Combat;

/// <summary>
/// Server hit chance from Combat vs Perception (client <c>CalculateHitChance</c> @ 0x4ceba0 structure).
/// Magnitudes: base 0.70, slope 1/200 per rating, clamp 0.05/0.95, hard ±9 level gate.
/// </summary>
public static class CombatHitChanceCalculator
{
    public const int LevelDeltaGate = 9;
    public const float HitChanceMax = 0.95f;
    public const float HitChanceMin = 0.05f;
    public const float HitChanceBase = 0.70f;
    public const float HitChancePerRating = 1f / 200f;

    public static float Calculate(
        int attackerLevel,
        short combat,
        int offenseBonus,
        float hitBonusPerLevel,
        float accuracyModifier,
        int victimLevel,
        short perception,
        int defenseBonus)
    {
        var atkLevel = Math.Max(1, attackerLevel);
        var vicLevel = Math.Max(1, victimLevel);
        var delta = atkLevel - vicLevel;
        if (delta > LevelDeltaGate)
            return HitChanceMax;
        if (delta < -LevelDeltaGate)
            return HitChanceMin;

        var combatPool = CharacterAttributePools.GetForCombat(combat);
        var perceptionPool = CharacterAttributePools.GetForCombat(perception);
        var hitPerLevel = (int)MathF.Round(hitBonusPerLevel * atkLevel);

        var attackRating = combatPool + atkLevel + offenseBonus + hitPerLevel;
        var defenseRating = perceptionPool + vicLevel + defenseBonus;
        var chance = HitChanceBase + (attackRating - defenseRating) * HitChancePerRating;

        if (accuracyModifier > 0f)
            chance *= accuracyModifier;

        return Math.Clamp(chance, HitChanceMin, HitChanceMax);
    }
}
