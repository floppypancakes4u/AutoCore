namespace AutoCore.Game.Combat;

/// <summary>
/// Retail client <c>Vehicle_CalcMaxHitPoints</c> @ 0x005002D0 for player-owned vehicles.
/// Chassis <c>SimpleObjectSpecific.MaxHitPoint</c> is almost always 1 in clonebase.wad;
/// live max HP is derived from armor factor, chassis ArmorAdd, Tech, level, race, and class.
/// </summary>
public static class VehicleHitPointCalculator
{
    /// <summary>Client DAT_009cd0a0 — constant added inside the level term.</summary>
    public const float LevelBase = 60f;

    /// <summary>Client DAT_00a1330c — Tech contribution scale.</summary>
    public const float TechScale = 3f;

    /// <summary>
    /// Client DAT_009cd0a8 — per-race level scale (race 0=Human, 1=Mutant, 2=Bot).
    /// </summary>
    public static readonly float[] RaceLevelScale = { 9.5f, 8.5f, 8.0f };

    /// <summary>
    /// Client DAT_009cd0b8 — per-class multiplier (0=Commando, 1=Engineer, 2=Operative, 3=Raider).
    /// </summary>
    public static readonly float[] ClassScale = { 1.0f, 1.1f, 1.2f, 2.0f };

    /// <summary>
    /// Client <c>Character_GetTechForPoolCalcs</c> @ 0x004C3FF0.
    /// Tech is capped at 200 before adding <paramref name="techBonus"/>; total clamped to [1, 250].
    /// </summary>
    public static int GetTechForPoolCalcs(short tech, short techBonus = 0)
    {
        var cappedTech = Math.Min(tech, (short)200);
        var sum = cappedTech + techBonus;
        if (sum < 250)
            return sum < 2 ? 1 : sum;
        return 250;
    }

    /// <summary>
    /// Player-vehicle max HP matching client recalculation after armor equip
    /// (<c>Vehicle_RecalcCombatPools</c> @ 0x00501F60 → <c>Vehicle_CalcMaxHitPoints</c>).
    /// </summary>
    public static int CalculatePlayerMaxHp(
        byte race,
        byte classId,
        int level,
        short tech,
        short armorFactor,
        short chassisArmorAdd,
        float vehicleHpPercentBonus = 0f,
        int vehicleHpAddBonus = 0,
        short techBonus = 0)
    {
        var techPool = GetTechForPoolCalcs(tech, techBonus);
        var raceIdx = Math.Clamp((int)race, 0, RaceLevelScale.Length - 1);
        var classIdx = Math.Clamp((int)classId, 0, ClassScale.Length - 1);
        var safeLevel = Math.Max(1, level);

        var levelScale = safeLevel * RaceLevelScale[raceIdx] + LevelBase;
        var basePool = techPool * TechScale + ClassScale[classIdx] * levelScale;

        var total = armorFactor
            + basePool
            + basePool * vehicleHpPercentBonus
            + vehicleHpAddBonus
            + chassisArmorAdd;

        return Math.Max(1, (int)Math.Ceiling(total));
    }
}
