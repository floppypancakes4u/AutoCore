using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Unit tests for handbrake rear ×0.5 (calcWheelTorque 0x598040) and
/// ticked <c>hkDefaultBrake_update</c> (0x64e6f0) service brake + lock (C8).
/// </summary>
[TestClass]
public class HkVehicleBrakeTests
{
    private const float Epsilon = 1e-6f;

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_RearAndHandbrake_HalvesTorque()
    {
        const float torque = 400f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: true, handbrakeActive: true);
        Assert.AreEqual(200f, scaled, Epsilon);
        Assert.AreEqual(torque * HkPhysicsConstants.HandbrakeRearTorqueScale, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_FrontAndHandbrake_Unchanged()
    {
        const float torque = 400f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: false, handbrakeActive: true);
        Assert.AreEqual(torque, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_RearWithoutHandbrake_Unchanged()
    {
        const float torque = 400f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: true, handbrakeActive: false);
        Assert.AreEqual(torque, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_FrontWithoutHandbrake_Unchanged()
    {
        const float torque = 123.5f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: false, handbrakeActive: false);
        Assert.AreEqual(torque, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_ZeroTorque_StaysZero()
    {
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(0f, isRear: true, handbrakeActive: true);
        Assert.AreEqual(0f, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_UsesConstantHalf()
    {
        Assert.AreEqual(0.5f, HkPhysicsConstants.HandbrakeRearTorqueScale, Epsilon);
        const float torque = 1000f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: true, handbrakeActive: true);
        Assert.AreEqual(500f, scaled, Epsilon);
    }

    [TestMethod]
    public void ComputeServiceBrakeTorque_FullPedal_EqualsMaxTorque()
    {
        Assert.AreEqual(100f, HkVehicleBrake.ComputeServiceBrakeTorque(100f, 1f), Epsilon);
    }

    [TestMethod]
    public void ComputeServiceBrakeTorque_HalfPedal_HalfTorque()
    {
        Assert.AreEqual(40f, HkVehicleBrake.ComputeServiceBrakeTorque(80f, 0.5f), Epsilon);
    }

    [TestMethod]
    public void ComputeServiceBrakeTorque_ZeroPedal_ZeroTorque()
    {
        Assert.AreEqual(0f, HkVehicleBrake.ComputeServiceBrakeTorque(200f, 0f), Epsilon);
    }

    [TestMethod]
    public void DeriveBrakePedal_ReverseComponentOnly()
    {
        // Accel=-1 / Reverse=+1 → pedal = max(0, thr)
        Assert.AreEqual(0f, HkVehicleBrake.DeriveBrakePedal(-1f), Epsilon);
        Assert.AreEqual(0f, HkVehicleBrake.DeriveBrakePedal(-0.25f), Epsilon);
        Assert.AreEqual(0f, HkVehicleBrake.DeriveBrakePedal(0f), Epsilon);
        Assert.AreEqual(0.4f, HkVehicleBrake.DeriveBrakePedal(0.4f), Epsilon);
        Assert.AreEqual(1f, HkVehicleBrake.DeriveBrakePedal(1f), Epsilon);
    }

    [TestMethod]
    public void ComputeIsBlocked_HandbrakeConnected_Locks()
    {
        Assert.IsTrue(HkVehicleBrake.ComputeIsBlocked(
            handbrakeConnected: true, handbrakeActive: true, pedalInput: 0f, minPedalInputToBlock: 0.5f));
        Assert.IsFalse(HkVehicleBrake.ComputeIsBlocked(
            handbrakeConnected: true, handbrakeActive: false, pedalInput: 0f, minPedalInputToBlock: 0.5f));
        Assert.IsFalse(HkVehicleBrake.ComputeIsBlocked(
            handbrakeConnected: false, handbrakeActive: true, pedalInput: 0f, minPedalInputToBlock: 0.5f));
    }

    [TestMethod]
    public void ComputeIsBlocked_PedalAtOrAboveMin_Locks()
    {
        Assert.IsFalse(HkVehicleBrake.ComputeIsBlocked(
            false, false, pedalInput: 0.09f, minPedalInputToBlock: 0.1f));
        Assert.IsTrue(HkVehicleBrake.ComputeIsBlocked(
            false, false, pedalInput: 0.1f, minPedalInputToBlock: 0.1f));
        Assert.IsTrue(HkVehicleBrake.ComputeIsBlocked(
            false, false, pedalInput: 1f, minPedalInputToBlock: 0.1f));
    }

    [TestMethod]
    public void ComputeOpposingSpinBrakeTorque_ClampsToPeak()
    {
        // Large spin → |raw| >> peak → ±peak
        float t = HkVehicleBrake.ComputeOpposingSpinBrakeTorque(
            spin: 10f, radius: 0.5f, wheelsMass: 15f, invDt: 60f, peak: 100f);
        Assert.AreEqual(-100f, t, Epsilon);
    }

    [TestMethod]
    public void ComputeOpposingSpinBrakeTorque_PassesRawWhenInsidePeak()
    {
        float t = HkVehicleBrake.ComputeOpposingSpinBrakeTorque(
            spin: 2f, radius: 0.5f, wheelsMass: 15f, invDt: 60f, peak: 500f);
        Assert.AreEqual(-450f, t, Epsilon);
    }

    [TestMethod]
    public void ComputeOpposingSpinBrakeTorque_ZeroPeak_IsZero()
    {
        float t = HkVehicleBrake.ComputeOpposingSpinBrakeTorque(
            spin: 10f, radius: 0.5f, wheelsMass: 15f, invDt: 60f, peak: 0f);
        Assert.AreEqual(0f, t, Epsilon);
    }

    [TestMethod]
    public void BrakeTorqueToFrictionForce_DividesByRadius()
    {
        Assert.AreEqual(-200f, HkVehicleBrake.BrakeTorqueToFrictionForce(-100f, 0.5f), Epsilon);
        Assert.AreEqual(0f, HkVehicleBrake.BrakeTorqueToFrictionForce(-100f, 0f), Epsilon);
    }

    [TestMethod]
    public void WheelsMassScale_IsFifteen()
    {
        Assert.AreEqual(15f, HkPhysicsConstants.WheelsMassScale, Epsilon);
        Assert.AreEqual(HkPhysicsConstants.LowSpeedTractionCutoff, HkPhysicsConstants.WheelsMassScale, Epsilon);
    }

    [TestMethod]
    public void ApplyAction_ReverseThrottle_WritesBrakeTorqueAndBlocksWhenAboveMinPedal()
    {
        var inst = CreateGroundedInstance();
        // Seed spin so opposing-spin torque is nonzero.
        for (var i = 0; i < inst.Wheels.Length; i++)
            inst.Wheels[i].Spin = 8f;

        const float dt = 1f / 60f;
        VehicleActionSim.ApplyAction(
            inst,
            throttleInput: 1f, // reverse component → full pedal
            steerInput: 0f,
            handbrake: false,
            dt: dt,
            query: new AlwaysHitQuery(0.5f));

        float pedal = HkVehicleBrake.DeriveBrakePedal(1f);
        Assert.AreEqual(1f, pedal, Epsilon);

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            var setup = inst.Data.Wheels[i];
            // After ApplyAction, brake update ran with spin at the start of brake stage
            // (post collide integrate). With thr=1 and minPedal=0.1, all wheels block.
            Assert.IsTrue(inst.Wheels[i].IsBlocked, $"wheel {i} should lock at full pedal");
            // Peak = 1 * MaxBrakingTorque; raw kill-spin is large → torque = -peak (forward spin).
            // Spin may have been zeroed if IsBlocked was already set from a prior concept;
            // first substep: IsBlocked was false during spin integrate, then brake sets it.
            float peak = setup.MaxBrakingTorque * pedal;
            Assert.IsTrue(
                MathF.Abs(inst.Wheels[i].BrakeTorque) <= peak + Epsilon,
                $"wheel {i} |BrakeTorque| should be ≤ peak {peak}");
            // Full pedal + nonzero spin before brake → non-zero torque or locked-zero path.
            Assert.IsTrue(
                inst.Wheels[i].BrakeTorque != 0f || inst.Wheels[i].IsBlocked,
                $"wheel {i} should produce brake torque or lock");
        }
    }

    [TestMethod]
    public void ApplyAction_ForwardThrottle_NoServiceBrakePedal()
    {
        var inst = CreateGroundedInstance();
        for (var i = 0; i < inst.Wheels.Length; i++)
            inst.Wheels[i].Spin = 8f;

        VehicleActionSim.ApplyAction(
            inst,
            throttleInput: -1f, // forward accel
            steerInput: 0f,
            handbrake: false,
            dt: 1f / 60f,
            query: new AlwaysHitQuery(0.5f));

        Assert.AreEqual(0f, HkVehicleBrake.DeriveBrakePedal(-1f), Epsilon);
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.AreEqual(0f, inst.Wheels[i].BrakeTorque, Epsilon, $"wheel {i}");
            Assert.IsFalse(inst.Wheels[i].IsBlocked, $"wheel {i}");
        }
    }

    [TestMethod]
    public void ApplyAction_Handbrake_LocksConnectedWheels_ZerosSpinNextSubstep()
    {
        var inst = CreateGroundedInstance();
        // Rear wheels are handbrake-connected by setup default.
        for (var i = 0; i < inst.Wheels.Length; i++)
            inst.Wheels[i].Spin = 12f;

        const float dt = 1f / 60f;
        var query = new AlwaysHitQuery(0.5f);

        // Substep 1: handbrake asserts → IsBlocked on connected wheels.
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: true,
            dt: dt, query: query);

        bool anyConnectedBlocked = false;
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            if (inst.Data.Wheels[i].HandbrakeConnected)
            {
                Assert.IsTrue(inst.Wheels[i].IsBlocked, $"connected wheel {i}");
                anyConnectedBlocked = true;
            }
        }
        Assert.IsTrue(anyConnectedBlocked, "fixture should have handbrake-connected wheels");

        // Substep 2: preUpdate sees IsBlocked → spin forced to 0.
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: true,
            dt: dt, query: query);

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            if (inst.Data.Wheels[i].HandbrakeConnected)
                Assert.AreEqual(0f, inst.Wheels[i].Spin, Epsilon, $"locked wheel {i} spin");
        }
    }

    [TestMethod]
    public void ApplyAction_ReverseThrottle_DoesNotInjectReverseDrivePack()
    {
        // Double-decel guard: thr>0 is service-brake pedal only — engine pack sign stays 0.
        // (Forward drive still uses thr&lt;0 → throttleSign=-1.)
        var inst = CreateGroundedInstance();
        inst.Body.LinVelZ = 5f;

        // Warm one substep so DriveTorque is populated for the next friction pass.
        var query = new AlwaysHitQuery(0.5f);
        VehicleActionSim.ApplyAction(
            inst, throttleInput: -1f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: query);

        float speedBefore = MathF.Abs(inst.Body.LinVelZ);

        // Full reverse thr = full brake pedal; must not also apply reverse engine pack.
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 1f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: query);

        // Still moving forward or slowed — not accelerated in reverse by drive pack.
        // (Brake + slip cancel may reduce speed; should not flip to large reverse speed in one tick.)
        Assert.IsTrue(inst.Body.LinVelZ > -1f,
            $"reverse thr must not inject reverse drive; LinVelZ={inst.Body.LinVelZ}, before={speedBefore}");
    }

    private static VehiclePhysicsInstance CreateGroundedInstance()
    {
        var vs = new VehicleSpecific
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
        return new VehiclePhysicsInstance(HkVehicleData.FromVehicleSpecific(vs, cbid: 9101));
    }

    private sealed class AlwaysHitQuery : IVehicleCollisionQuery
    {
        private readonly float _fraction;
        public AlwaysHitQuery(float fraction) => _fraction = fraction;

        public bool CastRay(
            float ox, float oy, float oz,
            float dx, float dy, float dz,
            float maxDistance,
            out VehicleRayHit hit)
        {
            float dist = maxDistance * _fraction;
            hit = new VehicleRayHit(
                fraction: _fraction,
                pointX: ox + dx * dist,
                pointY: oy + dy * dist,
                pointZ: oz + dz * dist,
                normalX: 0f,
                normalY: 1f,
                normalZ: 0f,
                isTerrain: true);
            return true;
        }
    }
}
