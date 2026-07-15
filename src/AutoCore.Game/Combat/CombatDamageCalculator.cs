namespace AutoCore.Game.Combat;

/// <summary>
/// Weapon damage channels, class scalar, primary level bonus, Theory-penetrated armor mit, crit apply.
/// Client anchors: OnHit @ 0x515520, class table DAT_009cdf9c, Theory DAT_009cdf80 = 0.004.
/// </summary>
public static class CombatDamageCalculator
{
    public static readonly float[] ClassDamageBalance = { 1.35f, 1.15f, 1.0f, 1.23f };
    public const float ArmorMitigationScale = 0.1f;
    public const float TheoryPenetrationPerPoint = 0.004f;

    public readonly record struct Result(int Damage, bool IsCrit);

    public static int PrimaryDamageType(short[] max)
    {
        if (max == null || max.Length == 0)
            return 0;
        var best = 0;
        var bestVal = int.MinValue;
        for (var t = 0; t < max.Length && t < 6; t++)
        {
            if (max[t] > bestVal)
            {
                bestVal = max[t];
                best = t;
            }
        }
        return best;
    }

    public static float GetClassMultiplier(int attackerClass)
    {
        if (attackerClass >= 0 && attackerClass < ClassDamageBalance.Length)
            return ClassDamageBalance[attackerClass];
        return 1f;
    }

    /// <summary>
    /// Effective mitigation after Theory: mitRoll − trunc(mitRoll × Theory × 0.004), floored at 0.
    /// </summary>
    public static int ApplyTheoryToMitigation(int mitRoll, int theoryPool)
    {
        if (mitRoll <= 0)
            return 0;
        var pen = (int)(mitRoll * theoryPool * TheoryPenetrationPerPoint);
        return Math.Max(0, mitRoll - pen);
    }

    public static Result Compute(
        int attackerLevel,
        int attackerClass,
        short attackerTheory,
        short attackerPerception,
        short[] minDamage,
        short[] maxDamage,
        int dmgMinMin,
        int dmgMaxMax,
        float damageBonusPerLevel,
        float damageScalar,
        short[] resists,
        Random rng,
        bool? forceCrit = null)
    {
        var level = Math.Max(1, attackerLevel);
        var classMul = GetClassMultiplier(attackerClass);
        var theoryPool = CharacterAttributePools.GetForCombat(attackerTheory);
        var levelBonus = (int)MathF.Round(damageBonusPerLevel * level);
        var primary = PrimaryDamageType(maxDamage);

        var baseTotal = 0;
        if (minDamage != null && maxDamage != null && minDamage.Length >= 6 && maxDamage.Length >= 6)
        {
            for (var t = 0; t < 6; t++)
            {
                var lo = (int)MathF.Round(minDamage[t] * classMul);
                var hi = (int)MathF.Round(maxDamage[t] * classMul);
                if (t == primary)
                {
                    lo += levelBonus;
                    hi += levelBonus;
                }
                if (hi < lo)
                    (lo, hi) = (hi, lo);

                var roll = hi > lo ? rng.Next(lo, hi + 1) : lo;
                if (roll > 0 && resists != null && t < resists.Length && resists[t] > 0)
                {
                    var cap = (int)MathF.Ceiling(resists[t] * ArmorMitigationScale);
                    if (cap > 0)
                    {
                        var mit = rng.Next(1, cap + 1);
                        roll -= ApplyTheoryToMitigation(mit, theoryPool);
                    }
                }

                if (roll > 0)
                    baseTotal += roll;
            }
        }

        if (baseTotal <= 0)
        {
            var lo = (int)MathF.Round(dmgMinMin * classMul);
            var hi = (int)MathF.Round(dmgMaxMax * classMul);
            if (hi < lo)
                (lo, hi) = (hi, lo);
            baseTotal = (hi > lo ? rng.Next(lo, hi + 1) : Math.Max(1, lo)) + levelBonus;
        }

        float dmg = Math.Max(1, baseTotal);
        var isCrit = forceCrit ?? (rng.NextDouble() <= CombatCritCalculator.CalculateChance(level, attackerPerception));
        if (isCrit)
            dmg *= CombatCritCalculator.GetMultiplier(level);

        var scalar = damageScalar > 0f ? damageScalar : 1f;
        var final = (int)MathF.Round(dmg * scalar);
        return new Result(Math.Max(1, final), isCrit);
    }
}
