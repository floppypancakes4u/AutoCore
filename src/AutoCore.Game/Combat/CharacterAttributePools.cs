namespace AutoCore.Game.Combat;

/// <summary>
/// Retail character attribute pool clamp shared by combat and vehicle pool formulas.
/// Mirrors <c>Character_GetTechForPoolCalcs</c> @ 0x004C3FF0 (cap raw 200, total [1, 250]).
/// </summary>
public static class CharacterAttributePools
{
    public const short MinSpent = 1;
    public const int RawCap = 200;
    public const int PoolMax = 250;

    /// <summary>Spent attribute for storage/display: never below 1.</summary>
    public static short NormalizeSpent(short spent) =>
        spent < MinSpent ? MinSpent : spent;

    /// <summary>
    /// Pool value used in combat / max heat / max power / max HP formulas.
    /// </summary>
    public static int GetForCombat(short spent, short bonus = 0)
    {
        var capped = Math.Min(spent, (short)RawCap);
        var sum = capped + bonus;
        if (sum < PoolMax)
            return sum < 2 ? 1 : sum;
        return PoolMax;
    }
}
