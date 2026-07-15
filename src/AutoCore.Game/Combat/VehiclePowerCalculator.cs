namespace AutoCore.Game.Combat;

/// <summary>
/// Retail max power (mana) core formula from <c>CalculateMaximumMana</c> @ 0x4f74c0:
/// level × classCoeff + TheoryPool × 2 + powerplant PowerMaximum (gear %/flat omitted this pass).
/// </summary>
public static class VehiclePowerCalculator
{
    /// <summary>Client DAT_009cd0c8 per-class level coefficient.</summary>
    public static readonly float[] PowerLevelCoeff = { 0.6f, 1.0f, 1.0f, 0.75f };

    public const float TheoryScale = 2f;

    public static int CalculatePlayerMaxPower(
        byte classId,
        int level,
        short theory,
        int powerPlantPowerMaximum)
    {
        var classIdx = Math.Clamp((int)classId, 0, PowerLevelCoeff.Length - 1);
        var safeLevel = Math.Max(1, level);
        var theoryPool = CharacterAttributePools.GetForCombat(theory);
        var plant = Math.Max(0, powerPlantPowerMaximum);
        var total = safeLevel * PowerLevelCoeff[classIdx] + theoryPool * TheoryScale + plant;
        return Math.Max(0, (int)Math.Ceiling(total));
    }
}
