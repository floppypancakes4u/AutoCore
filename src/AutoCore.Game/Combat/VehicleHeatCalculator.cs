namespace AutoCore.Game.Combat;

/// <summary>
/// Retail client <c>Vehicle_CalcHeatMaximum</c> @ 0x004F7360 for player-owned vehicles.
/// Tech contributes <see cref="TechScale"/> (0.5) after <see cref="VehicleHitPointCalculator.GetTechForPoolCalcs"/>.
/// </summary>
public static class VehicleHeatCalculator
{
    /// <summary>Client DAT_009cd0d8 — Tech contribution scale for heat cap.</summary>
    public const float TechScale = 0.5f;

    /// <summary>
    /// Client DAT_009cd0dc — per-race level scale. Dump shows 1.0 for starter races;
    /// length/values marked as current retail snapshot, not fully enumerated.
    /// </summary>
    public static readonly float[] RaceLevelScale = { 1.0f, 1.0f, 1.0f };

    /// <summary>
    /// Client DAT_009cd0ec — per-class multiplier. Dump shows 1.0 for starter classes.
    /// </summary>
    public static readonly float[] ClassScale = { 1.0f, 1.0f, 1.0f, 1.0f };

    /// <summary>
    /// Player vehicle max heat matching <c>Vehicle_CalcHeatMaximum</c> after Tech spend / PP equip.
    /// </summary>
    public static int CalculatePlayerMaxHeat(
        byte race,
        byte classId,
        int level,
        short tech,
        int powerPlantHeatMaximum,
        int heatMaxAdd,
        short techBonus = 0)
    {
        var techPool = VehicleHitPointCalculator.GetTechForPoolCalcs(tech, techBonus);
        var raceIdx = Math.Clamp((int)race, 0, RaceLevelScale.Length - 1);
        var classIdx = Math.Clamp((int)classId, 0, ClassScale.Length - 1);
        var safeLevel = Math.Max(1, level);
        var ppHeat = Math.Max(0, powerPlantHeatMaximum);

        var inner = safeLevel * RaceLevelScale[raceIdx]
            + techPool * TechScale
            + ppHeat;
        var total = ClassScale[classIdx] * inner + heatMaxAdd;

        return Math.Max(0, (int)Math.Ceiling(total));
    }
}
