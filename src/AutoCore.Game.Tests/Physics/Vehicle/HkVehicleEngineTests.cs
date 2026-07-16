using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// <c>VehicleAction::calcWheelTorque</c> @ <c>0x598040</c> — per-wheel drive torque assembly.
/// Covers clamp, handbrake rear-only cut, and low-speed μ boost.
/// </summary>
[TestClass]
public class HkVehicleEngineTests
{
    private const float Epsilon = 1e-5f;

    // Baseline: t=1, μ=1, upright=1, speed>=15 → torque = 1
    private static float Baseline(
        float torqueCurveFactor = 1f,
        float frictionMu = 1f,
        float uprightFactor = 1f,
        float chassisSpeed = 20f,
        bool isRear = false,
        bool handbrake = false,
        float driverMod = 0f)
        => HkVehicleEngine.ComputeWheelTorque(
            torqueCurveFactor, frictionMu, uprightFactor, chassisSpeed, isRear, handbrake, driverMod);

    // --- clamp [0, 1000] ---

    [TestMethod]
    public void ComputeWheelTorque_Clamp_Max1000()
    {
        // μ * upright * t = 50 * 1 * 50 = 2500 → clamp to 1000
        var torque = Baseline(torqueCurveFactor: 50f, frictionMu: 50f, chassisSpeed: 20f);
        Assert.AreEqual(HkPhysicsConstants.TorqueClampMax, torque, Epsilon);
        Assert.AreEqual(1000f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_Clamp_NegativeToZero()
    {
        // Negative driver mod can drive t (and torque) below 0 → clamp to 0
        // m=-0.5 front: t = (1 + m) * t = 0.5 * 1 = 0.5 → not negative
        // m=-1.5 front: t = (1 - 1.5) * 1 = -0.5; μ=1, upright=1 → -0.5 → 0
        var torque = Baseline(torqueCurveFactor: 1f, frictionMu: 1f, driverMod: -1.5f);
        Assert.AreEqual(0f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_Clamp_AtBoundary_Unchanged()
    {
        // Exactly 1000 stays 1000; exactly 0 stays 0
        Assert.AreEqual(1000f, Baseline(torqueCurveFactor: 100f, frictionMu: 10f), Epsilon);
        Assert.AreEqual(0f, Baseline(torqueCurveFactor: 0f, frictionMu: 5f), Epsilon);
    }

    // --- handbrake rear only (entity +0x61c, ×0.5) ---

    [TestMethod]
    public void ComputeWheelTorque_Handbrake_Rear_HalvesTorque()
    {
        // μ=2, t=1, upright=1, speed high → 2; rear+handbrake → 1
        var noHb = Baseline(frictionMu: 2f, isRear: true, handbrake: false);
        var withHb = Baseline(frictionMu: 2f, isRear: true, handbrake: true);
        Assert.AreEqual(2f, noHb, Epsilon);
        Assert.AreEqual(1f, withHb, Epsilon);
        Assert.AreEqual(noHb * HkPhysicsConstants.HandbrakeRearTorqueScale, withHb, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_Handbrake_Front_Unchanged()
    {
        var noHb = Baseline(frictionMu: 2f, isRear: false, handbrake: false);
        var withHb = Baseline(frictionMu: 2f, isRear: false, handbrake: true);
        Assert.AreEqual(2f, noHb, Epsilon);
        Assert.AreEqual(2f, withHb, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_Handbrake_RearWithoutFlag_Unchanged()
    {
        var torque = Baseline(frictionMu: 4f, isRear: true, handbrake: false);
        Assert.AreEqual(4f, torque, Epsilon);
    }

    // --- low-speed μ boost: v < 15 → μ *= (15-v)*0.2 + 1 ---

    [TestMethod]
    public void ComputeWheelTorque_LowSpeedBoost_AtZero_QuadMu()
    {
        // v=0: μ *= (15−0)×0.2 + 1 = 4 → torque = 4
        var torque = Baseline(frictionMu: 1f, chassisSpeed: 0f);
        Assert.AreEqual(4f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_LowSpeedBoost_AtHalfCutoff()
    {
        // v=7.5: mu *= (15-7.5)*0.2 + 1 = 1.5 + 1 = 2.5
        var torque = Baseline(frictionMu: 1f, chassisSpeed: 7.5f);
        Assert.AreEqual(2.5f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_LowSpeedBoost_AtCutoff_NoBoost()
    {
        // v == 15: condition is v < 15, so no boost
        var torque = Baseline(frictionMu: 1f, chassisSpeed: HkPhysicsConstants.LowSpeedTractionCutoff);
        Assert.AreEqual(1f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_LowSpeedBoost_AboveCutoff_NoBoost()
    {
        var torque = Baseline(frictionMu: 1f, chassisSpeed: 20f);
        Assert.AreEqual(1f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_LowSpeedBoost_ScalesExistingMu()
    {
        // μ=2, v=0 → μ' = 2 * 4 = 8
        var torque = Baseline(frictionMu: 2f, chassisSpeed: 0f);
        Assert.AreEqual(8f, torque, Epsilon);
    }

    // --- composition / driver mod smoke (keeps formula wired) ---

    [TestMethod]
    public void ComputeWheelTorque_DriverMod_Zero_LeavesCurve()
    {
        Assert.AreEqual(0.4f, Baseline(torqueCurveFactor: 0.4f), Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_DriverMod_Positive_BlendsTowardOne()
    {
        // m=0.5, t=0.4 → 1 - (1-0.5)*(1-0.4) = 1 - 0.5*0.6 = 0.7
        var torque = Baseline(torqueCurveFactor: 0.4f, driverMod: 0.5f);
        Assert.AreEqual(0.7f, torque, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_DriverMod_Negative_RearDoublesMod()
    {
        // m=-0.25 rear → mm=-0.5; t=(1-0.5)*0.8 = 0.4
        var rear = Baseline(torqueCurveFactor: 0.8f, isRear: true, driverMod: -0.25f);
        // front: t=(1-0.25)*0.8 = 0.6
        var front = Baseline(torqueCurveFactor: 0.8f, isRear: false, driverMod: -0.25f);
        Assert.AreEqual(0.4f, rear, Epsilon);
        Assert.AreEqual(0.6f, front, Epsilon);
    }

    [TestMethod]
    public void ComputeWheelTorque_UprightFactor_Multiplies()
    {
        var torque = Baseline(frictionMu: 2f, uprightFactor: 0.5f, torqueCurveFactor: 1f);
        Assert.AreEqual(1f, torque, Epsilon);
    }

    // --- upright |dot|^4 when |dot| < 0.8 (exp @ 0x9d54e8 = 4.0) ---

    [TestMethod]
    public void ComputeUprightFactor_Upright_IsOne()
    {
        Assert.AreEqual(1f, HkVehicleEngine.ComputeUprightFactor(1f), Epsilon);
        Assert.AreEqual(1f, HkVehicleEngine.ComputeUprightFactor(0.8f), Epsilon); // gate is strict <
        Assert.AreEqual(1f, HkVehicleEngine.ComputeUprightFactor(-0.9f), Epsilon);
    }

    [TestMethod]
    public void ComputeUprightFactor_Tilted_IsAbsDotToFourth()
    {
        // 0.5^4 = 0.0625; step discontinuity vs 0.8^4
        Assert.AreEqual(0.0625f, HkVehicleEngine.ComputeUprightFactor(0.5f), Epsilon);
        Assert.AreEqual(0.0625f, HkVehicleEngine.ComputeUprightFactor(-0.5f), Epsilon);
        var justBelow = 0.799f;
        Assert.AreEqual(MathF.Pow(justBelow, 4f), HkVehicleEngine.ComputeUprightFactor(justBelow), 1e-6f);
    }

    [TestMethod]
    public void ComputeWheelTorque_HandbrakeAfterBoost_ThenClamp()
    {
        // μ=1, v=0 → μ'=4; handbrake rear → 2; no clamp
        var torque = Baseline(frictionMu: 1f, chassisSpeed: 0f, isRear: true, handbrake: true);
        Assert.AreEqual(2f, torque, Epsilon);
    }

    // --- torqueCurve2D constant-factor path (retail contact X/Z bins OOR → factors[0]) ---

    [TestMethod]
    public void DefaultConstantFactor_FromMinTorqueFactor_MatchesFactors0()
    {
        Assert.AreEqual(0.2f, HkVehicleEngine.DefaultConstantFactor(0.2f), Epsilon);
        Assert.AreEqual(0.5f, HkVehicleEngine.DefaultConstantFactor(0.5f), Epsilon);
    }

    [TestMethod]
    public void DefaultConstantFactor_FromData_IsEngineFactors0()
    {
        var data = HkVehicleData.FromVehicleSpecific(new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelExistance = 0b001111,
            WheelAxle = 2,
            VehicleFlags = (short)(1 << 2),
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
            WheelWidth = new[] { 0.2f, 0.2f, 0.2f, 0.2f, 0f, 0f },
            SuspensionLength = new AutoCore.Game.Structures.FrontRear { Front = 0.3f, Rear = 0.32f },
            SuspensionStrength = new AutoCore.Game.Structures.FrontRear { Front = 40f, Rear = 38f },
            SuspensionDampeningCoefficientCompression = new AutoCore.Game.Structures.FrontRear { Front = 3f, Rear = 3f },
            SuspensionDampeningCoefficientExtension = new AutoCore.Game.Structures.FrontRear { Front = 2f, Rear = 2f },
            BrakesMaxTorque = new AutoCore.Game.Structures.FrontRear { Front = 100f, Rear = 80f },
            BrakesPedalInput = new AutoCore.Game.Structures.FrontRear { Front = 0.1f, Rear = 0.1f },
            SteeringMaxAngle = 0.6f,
            SteeringFullSpeedLimit = 12f,
            AerodynamicsAirDensity = 1.2f,
            AerodynamicsFrontalArea = 2f,
            AerodynamicsDrag = 0.3f,
            AerodynamicsLift = -0.1f,
            AVDNormalSpinDamping = 1.5f,
            AVDCollisionSpinDamping = 8f,
            AVDCollisionThreshold = 4f,
            RVInertiaRoll = 1.1f,
            RVInertiaPitch = 1.2f,
            RVInertiaYaw = 1.3f,
            RVFrictionEqualizer = 1f,
            WheelTorqueRatios = new AutoCore.Game.Structures.FrontRear { Front = 0f, Rear = 1f },
            RearWheelFrictionScalar = 1f,
            NumberOfGears = 1,
            GearRatios = new[] { 1f },
            TransmissionRatio = 1f,
            ReverseGearRation = 1f,
            TorqueMax = 200,
            MinTorqueFactor = 0.35f,
            MaxTorqueFactor = 1.0f,
            SpeedLimiter = 40f,
            AbsoluteTopSpeed = 50f,
        });

        Assert.AreEqual(0.35f, data.EngineFactors[0], Epsilon);
        Assert.AreEqual(0.35f, HkVehicleEngine.DefaultConstantFactor(data), Epsilon);
        Assert.AreEqual(data.MinTorqueFactor, HkVehicleEngine.DefaultConstantFactor(data), Epsilon);
    }

    [TestMethod]
    public void EvaluateTorqueCurveFactor_ContactXzOor_ReturnsFactors0()
    {
        // Trivial engine setup (rows=0) → bins always OOR → factors[0] = MinTorqueFactor.
        // Retail: contact world X/Z typically far outside bin range (0.7-transmission.md).
        var data = HkVehicleData.FromVehicleSpecific(new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelExistance = 0b001111,
            WheelAxle = 2,
            VehicleFlags = (short)(1 << 2),
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
            WheelWidth = new[] { 0.2f, 0.2f, 0.2f, 0.2f, 0f, 0f },
            SuspensionLength = new AutoCore.Game.Structures.FrontRear { Front = 0.3f, Rear = 0.32f },
            SuspensionStrength = new AutoCore.Game.Structures.FrontRear { Front = 40f, Rear = 38f },
            SuspensionDampeningCoefficientCompression = new AutoCore.Game.Structures.FrontRear { Front = 3f, Rear = 3f },
            SuspensionDampeningCoefficientExtension = new AutoCore.Game.Structures.FrontRear { Front = 2f, Rear = 2f },
            BrakesMaxTorque = new AutoCore.Game.Structures.FrontRear { Front = 100f, Rear = 80f },
            BrakesPedalInput = new AutoCore.Game.Structures.FrontRear { Front = 0.1f, Rear = 0.1f },
            SteeringMaxAngle = 0.6f,
            SteeringFullSpeedLimit = 12f,
            AerodynamicsAirDensity = 1.2f,
            AerodynamicsFrontalArea = 2f,
            AerodynamicsDrag = 0.3f,
            AerodynamicsLift = -0.1f,
            AVDNormalSpinDamping = 1.5f,
            AVDCollisionSpinDamping = 8f,
            AVDCollisionThreshold = 4f,
            RVInertiaRoll = 1.1f,
            RVInertiaPitch = 1.2f,
            RVInertiaYaw = 1.3f,
            RVFrictionEqualizer = 1f,
            WheelTorqueRatios = new AutoCore.Game.Structures.FrontRear { Front = 0f, Rear = 1f },
            RearWheelFrictionScalar = 1f,
            NumberOfGears = 1,
            GearRatios = new[] { 1f },
            TransmissionRatio = 1f,
            ReverseGearRation = 1f,
            TorqueMax = 200,
            MinTorqueFactor = 0.25f,
            MaxTorqueFactor = 1.2f,
            SpeedLimiter = 40f,
            AbsoluteTopSpeed = 50f,
        });

        // World-space contact coords (typical OOR vs trivial 0-row LUT).
        float factor = HkVehicleEngine.EvaluateTorqueCurveFactor(data, contactX: 100f, contactZ: -50f);
        Assert.AreEqual(0.25f, factor, Epsilon);
        Assert.AreEqual(data.EngineFactors[0], factor, Epsilon);

        // Same as direct TorqueCurve2D OOR path.
        float direct = TorqueCurve2D.Evaluate(
            data.EngineEnabled,
            data.EngineRows,
            data.EngineCols,
            data.EngineRangeScale,
            data.EngineFactorsArray,
            data.EngineLutArray,
            100f,
            -50f);
        Assert.AreEqual(0.25f, direct, Epsilon);
    }

    [TestMethod]
    public void EvaluateTorqueCurveFactor_InRangeLut_UsesTableNotFactors0()
    {
        // Non-trivial LUT: rows=1, cols=1, lut[0]=7 → factors[7], not OOR factors[0].
        var factors = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 1.5f };
        var lut = new byte[] { 7 };
        // scale=100 → base=50; rpm=50, thr=50 → xbin=0, ybin=0 in range.
        float r = TorqueCurve2D.Evaluate(true, 1, 1, 100f, factors, lut, 50f, 50f);
        Assert.AreEqual(1.5f, r, Epsilon);
        // Far OOR still factors[0].
        Assert.AreEqual(0.1f, TorqueCurve2D.Evaluate(true, 1, 1, 100f, factors, lut, 999f, 999f), Epsilon);
    }
}
