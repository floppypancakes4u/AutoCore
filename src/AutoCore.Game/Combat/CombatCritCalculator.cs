namespace AutoCore.Game.Combat;

/// <summary>
/// Crit chance and magnitude from Perception + level
/// (client <c>GetBaseCriticalHitChance</c> @ 0x4c4dd0, <c>GetCriticalHitMultiplier</c> @ 0x4cd550).
/// </summary>
public static class CombatCritCalculator
{
    public const float CritChanceBase = 0.02f;
    /// <summary>0.001 * 0.125 from client FPU constants.</summary>
    public const float CritChancePerLevelOrPerception = 0.000125f;
    public const float CritChanceFloor = 0.05f;
    public const float CritMultBase = 1.2f;
    public const float CritMultPerLevel = 0.01f;

    public static float CalculateChance(
        int attackerLevel,
        short perception,
        float critOffense = 0f,
        float critDefense = 0f)
    {
        var level = Math.Max(1, attackerLevel);
        var perceptionPool = CharacterAttributePools.GetForCombat(perception);
        var chance = CritChanceBase
            + CritChancePerLevelOrPerception * (level + perceptionPool)
            + critOffense
            - critDefense;
        if (chance < 0f)
            return CritChanceFloor;
        return chance;
    }

    public static float GetMultiplier(int attackerLevel)
    {
        var level = Math.Max(0, attackerLevel);
        return level * CritMultPerLevel + CritMultBase;
    }
}
