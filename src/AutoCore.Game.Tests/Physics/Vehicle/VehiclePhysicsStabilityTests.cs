using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Live-failure regressions: no fly-through terrain, no unbounded flip/speed under unit-mass sim.
/// </summary>
[TestClass]
public class VehiclePhysicsStabilityTests
{
    private static VehicleSpecific SyntheticCar() => new()
    {
        WheelExistance = 0b001111,
        WheelAxle = 2,
        VehicleFlags = (short)(1 << 2),
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

    private static IVehicleCollisionQuery FlatGround(float y)
        => new TerrainHeightfieldCollisionQuery((float x, float z, out float h) =>
        {
            h = y;
            return true;
        });

    [TestMethod]
    public void SuspensionForce_IsClamped()
    {
        // Huge compression would otherwise produce enormous force * mass scale.
        float f = HkVehicleSuspension.ComputeForce(
            inContact: true,
            restLength: 0.3f,
            strength: 4000f,
            dampCompression: 0f,
            dampExtension: 0f,
            currentLength: -10f,
            scalingFactor: 10f,
            closingSpeed: -50f,
            invMass: 1f);
        Assert.IsTrue(MathF.Abs(f) <= HkPhysicsConstants.MaxSuspensionForce + 1e-3f);
    }

    [TestMethod]
    public void Integrate_ClampsLinearAndAngularSpeed()
    {
        var body = new HkRigidBody();
        body.SetMass(1f);
        body.LinVelX = 1000f;
        body.AngVelY = 500f;
        body.Integrate(1f / 60f, applyGravity: false);
        float lin = MathF.Sqrt(body.LinVelX * body.LinVelX + body.LinVelY * body.LinVelY + body.LinVelZ * body.LinVelZ);
        float ang = MathF.Sqrt(body.AngVelX * body.AngVelX + body.AngVelY * body.AngVelY + body.AngVelZ * body.AngVelZ);
        Assert.IsTrue(lin <= HkPhysicsConstants.MaxLinearSpeed + 1e-3f, $"lin={lin}");
        Assert.IsTrue(ang <= HkPhysicsConstants.MaxAngularSpeed + 1e-3f, $"ang={ang}");
    }

    [TestMethod]
    public void GroundedDrive_StaysNearTerrain_NoFlipExplosion()
    {
        const float groundY = 0f;
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        var inst = new VehiclePhysicsInstance(data);
        // Chassis slightly above ground so wheels can rest.
        inst.SetPose(0f, 0.9f, 0f, 0f, 0f, 0f, 1f);
        var ground = FlatGround(groundY);

        // Warm torque lag then drive with retail-forward thr.
        for (var i = 0; i < 180; i++)
            inst.Step(-1f, 0f, false, 1f / 60f, ground);

        // Must not fall through ground.
        Assert.IsTrue(inst.Body.PosY > groundY - 0.5f,
            $"fell through terrain: Y={inst.Body.PosY}");
        // Must not launch to the stratosphere.
        Assert.IsTrue(inst.Body.PosY < groundY + 8f,
            $"launched: Y={inst.Body.PosY}");

        float ang = MathF.Sqrt(
            inst.Body.AngVelX * inst.Body.AngVelX +
            inst.Body.AngVelY * inst.Body.AngVelY +
            inst.Body.AngVelZ * inst.Body.AngVelZ);
        Assert.IsTrue(ang <= HkPhysicsConstants.MaxAngularSpeed + 0.1f, $"angVel explode {ang}");

        float lin = MathF.Sqrt(
            inst.Body.LinVelX * inst.Body.LinVelX +
            inst.Body.LinVelY * inst.Body.LinVelY +
            inst.Body.LinVelZ * inst.Body.LinVelZ);
        Assert.IsTrue(lin <= HkPhysicsConstants.MaxLinearSpeed + 0.1f, $"linVel explode {lin}");
    }

    [TestMethod]
    public void SoftPullPlanar_ClampsDrift()
    {
        float x = 100f, z = 0f;
        AutoCore.Game.Npc.NpcVehiclePhysicsController.SoftPullPlanarToward(0f, 0f, ref x, ref z);
        float d = MathF.Sqrt(x * x + z * z);
        Assert.AreEqual(HkPhysicsConstants.PathSoftPullMaxDrift, d, 1e-3f);
    }

    [TestMethod]
    public void SoftPullVertical_ClampsLaunchAndSink()
    {
        float y = 50f, vy = 20f;
        AutoCore.Game.Npc.NpcVehiclePhysicsController.SoftPullVerticalToward(
            supportY: 1f, ref y, ref vy);
        Assert.AreEqual(1f + HkPhysicsConstants.PathSoftPullMaxVerticalDrift, y, 1e-3f);
        Assert.AreEqual(0f, vy, 1e-5f);

        y = -10f;
        vy = -5f;
        AutoCore.Game.Npc.NpcVehiclePhysicsController.SoftPullVerticalToward(
            supportY: 1f, ref y, ref vy);
        Assert.AreEqual(1f - HkPhysicsConstants.PathSoftPullMaxVerticalDrift, y, 1e-3f);
        Assert.AreEqual(0f, vy, 1e-5f);
    }

    [TestMethod]
    public void SoftPullPlanarVelocity_ClampsExcess()
    {
        float vx = 100f, vz = 0f;
        AutoCore.Game.Npc.NpcVehiclePhysicsController.SoftPullPlanarVelocityToward(
            0f, 0f, ref vx, ref vz);
        float d = MathF.Sqrt(vx * vx + vz * vz);
        Assert.AreEqual(HkPhysicsConstants.PathSoftPullMaxPlanarVelDrift, d, 1e-3f);
    }

    [TestMethod]
    public void BuildCollisionQuery_NullMap_ProvidesPlanarGround()
    {
        var q = AutoCore.Game.Npc.NpcVehiclePhysicsController.BuildCollisionQuery(
            map: null, fallbackGroundY: 5f);
        // Flat plane at the supplied chassis Y (ride default is 0 — do not float NPCs).
        Assert.IsTrue(q.CastRay(0f, 5.5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.AreEqual(5f, hit.PointY, 1e-3f);
        Assert.IsTrue(hit.IsTerrain);
    }

    [TestMethod]
    public void ResolveRideHeight_DefaultsNearZero_NotFloatingConstant()
    {
        // Without clonebase metrics, must not invent a large float height.
        var v = new AutoCore.Game.Entities.Vehicle();
        float ride = AutoCore.Game.Npc.NpcVehiclePhysicsController.ResolveRideHeight(v, data: null);
        Assert.IsTrue(ride < 0.15f, $"expected near-zero ride, got {ride}");
    }

    [TestMethod]
    public void GroundedDrive_ProducesForwardMotion_NotLaunch()
    {
        const float groundY = 0f;
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        var inst = new VehiclePhysicsInstance(data);
        inst.SetPose(0f, 0.9f, 0f, 0f, 0f, 0f, 1f);
        var ground = FlatGround(groundY);

        // Warm torque lag then several drive frames (retail thr=-1 → +Z).
        for (var i = 0; i < 30; i++)
            inst.Step(-1f, 0f, false, 1f / 60f, ground);

        Assert.IsTrue(inst.Body.LinVelZ > 1e-3f || inst.Body.PosZ > 1e-3f,
            $"expected +Z from thr=-1, PosZ={inst.Body.PosZ} Vz={inst.Body.LinVelZ}");
        Assert.IsTrue(inst.Body.PosY < groundY + 5f, $"launched Y={inst.Body.PosY}");
        Assert.IsTrue(inst.Body.PosY > groundY - 0.5f, $"sank Y={inst.Body.PosY}");
        // Reduced model is soft on peak speed; path soft-pull owns route tracking.
        Assert.IsTrue(inst.Body.LinVelZ < HkPhysicsConstants.MaxLinearSpeed);
    }
}
