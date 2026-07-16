using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Gear-selection boundaries from <c>hkDefaultTransmission_update</c> @ <c>0x64f510</c>.
/// Strict thresholds (rpm &lt; downshift / rpm &gt; upshift); reverse suppresses auto-shift.
/// </summary>
[TestClass]
public class HkVehicleTransmissionTests
{
    private const float Epsilon = 1e-6f;

    private static HkVehicleTransmission MakeFiveSpeed(
        float upshift = 5000f,
        float downshift = 2000f)
        => new(
            gearRatios: new[] { 3.5f, 2.1f, 1.4f, 1.0f, 0.8f },
            primaryTransmissionRatio: 3.5f,
            reverseGearRatio: 3.2f,
            upshiftRpm: upshift,
            downshiftRpm: downshift,
            clutchDelayTime: 0.15f,
            numberOfGears: 5);

    // --- storage ---

    [TestMethod]
    public void Constructor_StoresGearRatiosAndShiftPoints()
    {
        var t = MakeFiveSpeed(upshift: 4800f, downshift: 1800f);
        Assert.AreEqual(5, t.NumberOfGears);
        Assert.AreEqual(5, t.GearRatios.Count);
        Assert.AreEqual(3.5f, t.GearRatios[0], Epsilon);
        Assert.AreEqual(0.8f, t.GearRatios[4], Epsilon);
        Assert.AreEqual(3.5f, t.PrimaryTransmissionRatio, Epsilon);
        Assert.AreEqual(3.2f, t.ReverseGearRatio, Epsilon);
        Assert.AreEqual(4800f, t.UpshiftRpm, Epsilon);
        Assert.AreEqual(1800f, t.DownshiftRpm, Epsilon);
        Assert.AreEqual(0.15f, t.ClutchDelayTime, Epsilon);
    }

    [TestMethod]
    public void Constructor_NumberOfGearsZero_UsesRatioCount()
    {
        var t = new HkVehicleTransmission(
            gearRatios: new[] { 4f, 2f, 1f },
            primaryTransmissionRatio: 3f,
            reverseGearRatio: 3f,
            upshiftRpm: 5000f,
            downshiftRpm: 2000f,
            numberOfGears: 0);
        Assert.AreEqual(3, t.NumberOfGears);
    }

    // --- SelectGear: upshift boundary ---

    [TestMethod]
    public void SelectGear_RpmAboveUpshift_AdvancesOneGear()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(1, t.SelectGear(currentGear: 0, engineRpm: 5000.1f));
        Assert.AreEqual(2, t.SelectGear(currentGear: 1, engineRpm: 6000f));
    }

    [TestMethod]
    public void SelectGear_RpmExactlyUpshift_NoChange()
    {
        // Retail: upshift when upshiftRpm <= rpm AND rpm != upshiftRpm  → strict >
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(2, t.SelectGear(currentGear: 2, engineRpm: 5000f));
    }

    [TestMethod]
    public void SelectGear_RpmBelowUpshift_NoUpshift()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(1, t.SelectGear(currentGear: 1, engineRpm: 4999.9f));
    }

    [TestMethod]
    public void SelectGear_AtTopGear_NoUpshiftEvenWhenRpmHigh()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        // last gear index = NumberOfGears - 1 = 4; gear+1 < numGears fails
        Assert.AreEqual(4, t.SelectGear(currentGear: 4, engineRpm: 9000f));
    }

    // --- SelectGear: downshift boundary ---

    [TestMethod]
    public void SelectGear_RpmBelowDownshift_DropsOneGear()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(1, t.SelectGear(currentGear: 2, engineRpm: 1999.9f));
        Assert.AreEqual(0, t.SelectGear(currentGear: 1, engineRpm: 0f));
    }

    [TestMethod]
    public void SelectGear_RpmExactlyDownshift_NoChange()
    {
        // Retail: downshift when rpm <= downshift AND downshift != rpm → strict <
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(3, t.SelectGear(currentGear: 3, engineRpm: 2000f));
    }

    [TestMethod]
    public void SelectGear_RpmAboveDownshift_NoDownshift()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(2, t.SelectGear(currentGear: 2, engineRpm: 2000.1f));
    }

    [TestMethod]
    public void SelectGear_AtBottomGear_NoDownshiftEvenWhenRpmLow()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(0, t.SelectGear(currentGear: 0, engineRpm: 100f));
    }

    // --- reverse / clamp / mid band ---

    [TestMethod]
    public void SelectGear_Reverse_NoAutoShift()
    {
        var t = MakeFiveSpeed();
        Assert.AreEqual(0, t.SelectGear(currentGear: 0, engineRpm: 9000f, isReverse: true));
        Assert.AreEqual(2, t.SelectGear(currentGear: 2, engineRpm: 100f, isReverse: true));
    }

    [TestMethod]
    public void SelectGear_MidBand_HoldsGear()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        Assert.AreEqual(2, t.SelectGear(currentGear: 2, engineRpm: 3500f));
    }

    [TestMethod]
    public void SelectGear_ClampsOutOfRangeGearBeforeShift()
    {
        var t = MakeFiveSpeed(upshift: 5000f, downshift: 2000f);
        // gear 99 clamped to 4; high rpm still cannot upshift past top
        Assert.AreEqual(4, t.SelectGear(currentGear: 99, engineRpm: 9000f));
        // gear -3 clamped to 0; low rpm cannot downshift further
        Assert.AreEqual(0, t.SelectGear(currentGear: -3, engineRpm: 100f));
    }

    [TestMethod]
    public void SelectGear_SingleGear_NeverShifts()
    {
        var t = new HkVehicleTransmission(
            gearRatios: new[] { 2.5f },
            primaryTransmissionRatio: 3f,
            reverseGearRatio: 3f,
            upshiftRpm: 1000f,
            downshiftRpm: 500f,
            numberOfGears: 1);
        Assert.AreEqual(0, t.SelectGear(0, engineRpm: 50f));
        Assert.AreEqual(0, t.SelectGear(0, engineRpm: 9000f));
    }

    // --- ratio lookup ---

    [TestMethod]
    public void GetGearRatio_Forward_UsesIndexedRatio()
    {
        var t = MakeFiveSpeed();
        Assert.AreEqual(3.5f, t.GetGearRatio(0), Epsilon);
        Assert.AreEqual(1.4f, t.GetGearRatio(2), Epsilon);
        Assert.AreEqual(0.8f, t.GetGearRatio(4), Epsilon);
    }

    [TestMethod]
    public void GetGearRatio_Reverse_ReturnsNegatedReverseRatio()
    {
        // update @ 0x64f510: gearRatio = 0 - reverseGearRatio when reverse flag set
        var t = MakeFiveSpeed();
        Assert.AreEqual(-3.2f, t.GetGearRatio(0, isReverse: true), Epsilon);
    }

    [TestMethod]
    public void FromVehicleData_CopiesTransmissionFields()
    {
        var data = HkVehicleData.FromVehicleSpecific(new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelExistance = 0b001111,
            WheelAxle = 2,
            WheelHardPoints = new[]
            {
                new AutoCore.Game.Structures.Vector3(0.8f, -0.2f, 1.2f),
                new AutoCore.Game.Structures.Vector3(-0.8f, -0.2f, 1.2f),
                new AutoCore.Game.Structures.Vector3(0.8f, -0.2f, -1.2f),
                new AutoCore.Game.Structures.Vector3(-0.8f, -0.2f, -1.2f),
                default,
                default,
            },
            WheelRadius = new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0f, 0f },
            NumberOfGears = 4,
            GearRatios = new[] { 3f, 2f, 1.2f, 0.9f },
            TransmissionRatio = 4.1f,
            ReverseGearRation = 3.3f,
            UpshiftRPM = 5500,
            DownshiftRPM = 2200,
            ClutchDelayTime = 0.2f,
            SuspensionLength = new AutoCore.Game.Structures.FrontRear { Front = 0.3f, Rear = 0.3f },
            SuspensionStrength = new AutoCore.Game.Structures.FrontRear { Front = 40f, Rear = 40f },
            SuspensionDampeningCoefficientCompression = new AutoCore.Game.Structures.FrontRear { Front = 3f, Rear = 3f },
            SuspensionDampeningCoefficientExtension = new AutoCore.Game.Structures.FrontRear { Front = 2f, Rear = 2f },
            BrakesMaxTorque = new AutoCore.Game.Structures.FrontRear { Front = 100f, Rear = 80f },
            BrakesPedalInput = new AutoCore.Game.Structures.FrontRear { Front = 0.1f, Rear = 0.1f },
            WheelTorqueRatios = new AutoCore.Game.Structures.FrontRear { Front = 0f, Rear = 1f },
            RVInertiaRoll = 1f,
            RVInertiaPitch = 1f,
            RVInertiaYaw = 1f,
        });

        var t = HkVehicleTransmission.FromVehicleData(data);
        Assert.AreEqual(4, t.NumberOfGears);
        Assert.AreEqual(4.1f, t.PrimaryTransmissionRatio, Epsilon);
        Assert.AreEqual(3.3f, t.ReverseGearRatio, Epsilon);
        Assert.AreEqual(5500f, t.UpshiftRpm, Epsilon);
        Assert.AreEqual(2200f, t.DownshiftRpm, Epsilon);
        Assert.AreEqual(0.2f, t.ClutchDelayTime, Epsilon);
        Assert.AreEqual(3f, t.GearRatios[0], Epsilon);
        Assert.AreEqual(0.9f, t.GearRatios[3], Epsilon);
    }
}
