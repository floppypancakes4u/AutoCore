// EXCERPT (READ-ONLY REFERENCE) from AutoCore.Game/Entities/Vehicle.cs (Destroyer140 fork).
// Only the attribute-consuming pool-derivation methods and their constant tables are reproduced
// here for review. Line numbers in the comments refer to the full Vehicle.cs in our tree.
//
// Attributes consumed:
//   * AttributeTech   -> MaxHeat  (level + Tech*0.5 + powerplant.HeatMaximum)
//   * AttributeTheory -> MaxPower (level*PowerLevelCoeff[class] + Theory*2 + powerplant.PowerMaximum)
//   * (Vehicle combat HP is race/class/level only in our fork — Tech is NOT yet added here;
//      see REASONING.md "Power / Heat" section for the honest gap vs SCAR's VehicleHitPointCalculator.)

namespace AutoCore.Game.Entities;

public partial class Vehicle
{
    // ---- Pool constant tables (Vehicle.cs) --------------------------------------------------

    // Vehicle.cs:211 — client CalculateMaximumMana @0x4f74c0 per-class level coefficient.
    private static readonly float[] PowerLevelCoeff = { 0.6f, 1.0f, 1.0f, 0.75f };

    // Vehicle.cs:673-677 — client CVOGVehicle_ComputeMaxHitPoints @0x005002d0 constants
    // (Ghidra DAT_009cd0a0/a8/b8, DAT_00a1330c) + race off-rating @0x00521230.
    private static readonly float[] HpPerLevelByRace   = { 9.5f, 8.5f, 8.0f, 8.0f };
    private static readonly float[] HpClassMultByClass = { 1.0f, 1.1f, 1.2f, 2.0f };
    private static readonly int[]   OffenseSecondaryByRace = { 18, 20, 10, 10 };
    private const float HpBaseConst = 60.0f;
    private const float HpRatingK   = 3.0f;

    // ---- Vehicle.cs:687-709 — max HP (NOTE: no Tech term here yet in our fork) --------------
    public void ComputeAndSetMaxHitPoints()
    {
        var driver = Owner?.GetAsCharacter();
        if (driver?.CloneBaseObject == null)
            return; // no driver/clonebase yet -> keep the chassis fallback HP

        var charSpec = (driver.CloneBaseObject as CloneBaseCharacter)?.CharacterSpecific;
        var race = System.Math.Clamp((int)(charSpec?.Race ?? 0), 0, 3);
        var cls  = System.Math.Clamp((int)(charSpec?.Class ?? 0), 0, 3);
        int level = driver.GetLevel();

        var fVar5 = level * HpPerLevelByRace[race] + HpBaseConst;
        var ratingSum = System.Math.Clamp(System.Math.Min(OffenseSecondaryByRace[race] + (level - 1), 200), 1, 250);
        var fVar6 = ratingSum * HpRatingK + HpClassMultByClass[cls] * fVar5;

        // TODO: armorHP (armor runtime +0xb4) and chassisBonus (vehicle +0x1d8) not yet mapped.
        // SCAR's VehicleHitPointCalculator additionally folds in Tech (techPool*3) here — ours does not yet.
        const float armorHpBonus = 0f;
        const float chassisBonus = 0f;

        var maxHp = (int)System.Math.Ceiling(armorHpBonus + fVar6 + chassisBonus);
        HP = MaxHP = System.Math.Max(1, maxHp);
        ComputeAndSetMaxPools();
    }

    // ---- Vehicle.cs:717-777 — max heat/power pools (THIS is where Tech & Theory feed) --------
    public void ComputeAndSetMaxPools()
    {
        var prevMaxPower  = MaxPower;
        var prevMaxShield = MaxShield;

        var pp       = (PowerPlant?.CloneBaseObject as CloneBasePowerPlant)?.PowerPlantSpecific;
        var driver   = Owner?.GetAsCharacter();
        var charSpec = (driver?.CloneBaseObject as CloneBaseCharacter)?.CharacterSpecific;
        int level  = driver?.GetLevel() ?? 1;
        int theory = driver?.Stats?.AttributeTheory ?? 0;   // <-- AttributeTheory
        int tech   = driver?.Stats?.AttributeTech   ?? 0;   // <-- AttributeTech
        int cls    = System.Math.Clamp((int)(charSpec?.Class ?? 0), 0, PowerLevelCoeff.Length - 1);

        // Max heat = ceil(level + Tech*0.5 + powerplant HeatMaximum) — client CalculateMaximumHeat @0x4f7360.
        MaxHeat = pp != null && pp.HeatMaximum > 0
            ? (int)System.Math.Ceiling(level + tech * 0.5f + pp.HeatMaximum)
            : 10;

        // Max power = ceil(level*classCoeff + Theory*2 + powerplant PowerMaximum) — client
        // CalculateMaximumMana @0x4f74c0 (Theory coeff hard 2.0; level coeff = per-class table).
        MaxPower = pp != null && pp.PowerMaximum > 0
            ? (int)System.Math.Ceiling(level * PowerLevelCoeff[cls] + theory * 2 + pp.PowerMaximum)
            : 0;

        // ... shield-generator sourcing + fill/clamp logic omitted (not attribute-driven) ...
    }
}
