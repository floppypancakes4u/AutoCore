using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Smoke tests for <see cref="VehiclePhysicsInstance"/> orchestration
/// (substep + applyAction skeleton over available subsystems).
/// </summary>
[TestClass]
public class VehiclePhysicsInstanceTests
{
    private static VehicleSpecific SyntheticCar() => new()
    {
        WheelExistance = 0b001111, // 4 wheels
        WheelAxle = 2,
        VehicleFlags = (short)(1 << 2), // front steers
        WheelHardPoints = new[]
        {
            new Vector3(0.8f, -0.2f, 1.2f),
            new Vector3(-0.8f, -0.2f, 1.2f),
            new Vector3(0.8f, -0.2f, -1.2f),
            new Vector3(-0.8f, -0.2f, -1.2f),
            default,
            default,
        },
        WheelRadius = new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0f, 0f },
        WheelWidth = new[] { 0.2f, 0.2f, 0.2f, 0.2f, 0f, 0f },
        SuspensionLength = new FrontRear { Front = 0.3f, Rear = 0.32f },
        SuspensionStrength = new FrontRear { Front = 40f, Rear = 38f },
        SuspensionDampeningCoefficientCompression = new FrontRear { Front = 3f, Rear = 3f },
        SuspensionDampeningCoefficientExtension = new FrontRear { Front = 2f, Rear = 2f },
        BrakesMaxTorque = new FrontRear { Front = 100f, Rear = 80f },
        BrakesPedalInput = new FrontRear { Front = 0.1f, Rear = 0.1f },
        BrakesMinBlockTime = new FrontRear { Front = 0f, Rear = 0f },
        SteeringMaxAngle = 0.6f,
        SteeringFullSpeedLimit = 12f,
        AerodynamicsAirDensity = 1.2f,
        AerodynamicsFrontalArea = 2f,
        AerodynamicsDrag = 0.3f,
        AerodynamicsLift = -0.1f,
        AerodynamicsExtraGravity = new Vector3(0f, 0f, 0f),
        AVDNormalSpinDamping = 1.5f,
        AVDCollisionSpinDamping = 8f,
        AVDCollisionThreshold = 4f,
        RVInertiaRoll = 1.1f,
        RVInertiaPitch = 1.2f,
        RVInertiaYaw = 1.3f,
        RVFrictionEqualizer = 1f,
        WheelTorqueRatios = new FrontRear { Front = 0f, Rear = 1f },
        RearWheelFrictionScalar = 1.05f,
        CenterOfMassModifier = new Vector3(0f, -0.1f, 0.05f),
        NumberOfGears = 5,
        GearRatios = new[] { 3.5f, 2.1f, 1.4f, 1.0f, 0.8f },
        TransmissionRatio = 3.5f,
        ReverseGearRation = 3.2f,
        TorqueMax = 200,
        MinTorqueFactor = 0.2f,
        MaxTorqueFactor = 1.0f,
        SpeedLimiter = 40f,
        AbsoluteTopSpeed = 50f,
    };

    [TestMethod]
    public void Construct_FromSyntheticVehicleSpecific_InitializesBodyAndWheels()
    {
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar(), cbid: 1001);
        var inst = new VehiclePhysicsInstance(data);

        Assert.AreSame(data, inst.Data);
        Assert.IsNotNull(inst.Body);
        Assert.AreEqual(data.Mass, inst.Body.Mass, 1e-6f);
        Assert.AreEqual(data.InvMass, inst.Body.InvMass, 1e-6f);
        Assert.AreEqual(data.WheelCount, inst.Wheels.Length);
        Assert.AreEqual(0f, inst.Throttle, 1e-6f);
        Assert.AreEqual(0f, inst.SteerRamp, 1e-6f);
        Assert.AreEqual(0f, inst.SteerFinal, 1e-6f);
        Assert.IsFalse(inst.Handbrake);
    }

    [TestMethod]
    public void Construct_MapsRVInertiaToRetailChassisAxes()
    {
        // Chassis basis (live-confirmed, B4): front = +Z, up = +Y, lateral = ±X.
        // Body-frame principal inertia therefore pairs geometrically:
        //   Roll  (about forward/Z) → InvInertiaZ
        //   Pitch (about lateral/X) → InvInertiaX
        //   Yaw   (about up/Y)      → InvInertiaY
        // Live proof (docs/reconstruction/physics/0.2-mass-inertia.md §2.1):
        //   rb+0xe0 invInertia = (1/4500, 1/4500, 1/1500) on axes (X,Y,Z) for a car whose
        //   DB def+0x5dc/5e0/5e4 = Roll=1, Pitch=3, Yaw=3 → forward/Z carries Roll (lowest).
        var vs = SyntheticCar();
        vs.RVInertiaRoll = 1.1f;   // distinct so axis swaps are observable
        vs.RVInertiaPitch = 1.2f;
        vs.RVInertiaYaw = 1.3f;

        var data = HkVehicleData.FromVehicleSpecific(vs, cbid: 7);
        var inst = new VehiclePhysicsInstance(data);

        Assert.AreEqual(1f / data.InertiaRoll, inst.Body.InvInertiaZ, 1e-6f, "forward axis Z ← Roll");
        Assert.AreEqual(1f / data.InertiaPitch, inst.Body.InvInertiaX, 1e-6f, "lateral axis X ← Pitch");
        Assert.AreEqual(1f / data.InertiaYaw, inst.Body.InvInertiaY, 1e-6f, "up axis Y ← Yaw");
    }

    [TestMethod]
    public void Step_NullCollisionQuery_RunsWithoutThrow_AndAppliesGravity()
    {
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar(), cbid: 42);
        var inst = new VehiclePhysicsInstance(data);
        inst.Body.PosY = 50f;

        const float frameDt = 1f / 60f;
        inst.Step(
            throttle: 1f,
            steer: 0.5f,
            handbrake: false,
            frameDt: frameDt,
            query: NullVehicleCollisionQuery.Instance);

        // Inputs stored / ramped
        Assert.AreEqual(1f, inst.Throttle, 1e-5f);
        Assert.IsTrue(inst.AllWheelsAirborne, "Null query → all wheels miss → airborne");

        // Free-fall semi-implicit Euler over one 60fps frame (N=1, dt=1/60)
        float expectedVy = HkPhysicsConstants.DefaultGravityY * frameDt;
        float expectedY = 50f + expectedVy * frameDt;
        Assert.AreEqual(expectedVy, inst.Body.LinVelY, 1e-4f);
        Assert.AreEqual(expectedY, inst.Body.PosY, 1e-3f);

        // Wheels updated to miss/rest defaults
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsFalse(inst.Wheels[i].InContact);
            Assert.AreEqual(data.Wheels[i].SuspensionRestLength, inst.Wheels[i].CurrentLength, 1e-5f);
        }
    }

    [TestMethod]
    public void Step_LongFrame_UsesMultipleSubsteps()
    {
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        var inst = new VehiclePhysicsInstance(data);
        inst.Body.PosY = 100f;

        // frameDt 0.05 → N=2, substep 0.025 (see HkVehicleSubstepTests)
        const float frameDt = 0.05f;
        var (n, subDt) = HkVehicleSubstep.Compute(frameDt);
        Assert.AreEqual(2, n);

        inst.Step(0f, 0f, false, frameDt, NullVehicleCollisionQuery.Instance);

        // Two semi-implicit gravity substeps from rest
        float v1 = HkPhysicsConstants.DefaultGravityY * subDt;
        float y1 = 100f + v1 * subDt;
        float v2 = v1 + HkPhysicsConstants.DefaultGravityY * subDt;
        float y2 = y1 + v2 * subDt;

        Assert.AreEqual(v2, inst.Body.LinVelY, 1e-3f);
        Assert.AreEqual(y2, inst.Body.PosY, 1e-2f);
    }

    [TestMethod]
    public void Step_UsesHkVehicleDataGravityY_NotHardcodedDefault()
    {
        // ServerConfig gravity is baked into HkVehicleData at cache build;
        // ActionSim/Integrate must use data.GravityY for free-fall consistency.
        const float customG = -12.5f;
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar(), cbid: 77, gravityY: customG);
        Assert.AreEqual(customG, data.GravityY, 1e-6f);

        var inst = new VehiclePhysicsInstance(data);
        inst.Body.PosY = 80f;

        const float frameDt = 1f / 60f;
        inst.Step(0f, 0f, false, frameDt, NullVehicleCollisionQuery.Instance);

        Assert.IsTrue(inst.AllWheelsAirborne);
        float expectedVy = customG * frameDt;
        float expectedY = 80f + expectedVy * frameDt;
        Assert.AreEqual(expectedVy, inst.Body.LinVelY, 1e-4f);
        Assert.AreEqual(expectedY, inst.Body.PosY, 1e-3f);
        Assert.AreNotEqual(HkPhysicsConstants.DefaultGravityY * frameDt, inst.Body.LinVelY);
    }

    [TestMethod]
    public void Step_SteerInput_RampsFinalSteer()
    {
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        var inst = new VehiclePhysicsInstance(data);
        const float frameDt = 1f / 60f;

        // At rest, mode speed factor is 0 → target final steer 0; stage-1 ramps by rate*dt.
        inst.Step(0f, 1f, false, frameDt, NullVehicleCollisionQuery.Instance);
        Assert.AreEqual(1f, inst.SteerInput, 1e-5f);
        Assert.AreEqual(HkPhysicsConstants.SteerStage1RateBase * frameDt, inst.SteerRamp, 1e-5f);
        Assert.AreEqual(0f, inst.SteerFinal, 1e-5f);

        // Seed stage-1 + forward speed so stage-2 has authority (speedFactor → 1 above 20)
        inst.SteerRamp = 1f;
        inst.Body.LinVelZ = 25f;
        inst.Step(0f, 1f, false, frameDt, NullVehicleCollisionQuery.Instance);

        // Final steer ramps ±0.05 per substep toward 1.0
        Assert.AreEqual(HkPhysicsConstants.SteerRampPerTick, inst.SteerFinal, 1e-5f);
    }
}
