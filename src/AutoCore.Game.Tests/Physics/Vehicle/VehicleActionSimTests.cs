using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Smoke tests for <see cref="VehicleActionSim"/> — one-substep orchestrator
/// matching <c>VehicleAction::applyAction</c> @ <c>0x598650</c> order
/// (axes → tickSubsystems map → stage-1/2 → torque → integrate).
/// </summary>
[TestClass]
public class VehicleActionSimTests
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

    private static VehiclePhysicsInstance CreateInstance()
        => new(HkVehicleData.FromVehicleSpecific(SyntheticCar(), cbid: 9001));

    [TestMethod]
    public void ApplyAction_NullInstance_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            VehicleActionSim.ApplyAction(
                null!,
                throttleInput: 0f,
                steerInput: 0f,
                handbrake: false,
                dt: 1f / 60f,
                query: NullVehicleCollisionQuery.Instance));
    }

    [TestMethod]
    public void ApplyAction_NullQuery_Airborne_IntegratesGravity()
    {
        var inst = CreateInstance();
        inst.Body.PosY = 40f;

        const float dt = 1f / 60f;
        VehicleActionSim.ApplyAction(
            inst,
            throttleInput: 0f,
            steerInput: 0f,
            handbrake: false,
            dt: dt,
            query: null!);

        Assert.IsTrue(inst.AllWheelsAirborne);
        Assert.AreEqual(HkPhysicsConstants.DefaultGravityY * dt, inst.Body.LinVelY, 1e-4f);
        Assert.AreEqual(40f + inst.Body.LinVelY * dt, inst.Body.PosY, 1e-3f);

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsFalse(inst.Wheels[i].InContact);
            Assert.AreEqual(inst.Data.Wheels[i].SuspensionRestLength, inst.Wheels[i].CurrentLength, 1e-5f);
            Assert.AreEqual(0f, inst.Wheels[i].DriveTorque, 1e-5f);
        }
    }

    [TestMethod]
    public void ApplyAction_AcceptsThrottleSteerHandbrake_BeforeDtGate()
    {
        var inst = CreateInstance();

        // Non-positive dt: still stores axes, does not integrate / does not stage-1 ramp.
        float y0 = inst.Body.PosY;
        VehicleActionSim.ApplyAction(
            inst,
            throttleInput: -0.75f,
            steerInput: 0.4f,
            handbrake: true,
            dt: 0f,
            query: NullVehicleCollisionQuery.Instance);

        Assert.AreEqual(-0.75f, inst.Throttle, 1e-5f);
        Assert.AreEqual(0.4f, inst.SteerInput, 1e-5f);
        Assert.AreEqual(0f, inst.SteerRamp, 1e-5f); // stage-1 requires dt > 0
        Assert.IsTrue(inst.Handbrake);
        Assert.AreEqual(y0, inst.Body.PosY, 1e-6f);
        Assert.AreEqual(0f, inst.Body.LinVelY, 1e-6f);
    }

    [TestMethod]
    public void ApplyAction_Stage1SteerRamp_UsesRateTimesDt()
    {
        var inst = CreateInstance();
        const float dt = 1f / 60f;

        // From zero: factor 1 → step = rateBase * dt (not snap-store).
        float expectedRamp = HkPhysicsConstants.SteerStage1RateBase * dt;
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 1f, handbrake: false,
            dt: dt, query: NullVehicleCollisionQuery.Instance);

        Assert.AreEqual(1f, inst.SteerInput, 1e-5f);
        Assert.AreEqual(expectedRamp, inst.SteerRamp, 1e-5f);

        // Off-zero with target inside open rails → factor 2 (target < +1)
        float step2 = HkPhysicsConstants.SteerStage1RateBase * dt * HkPhysicsConstants.SteerStage1OpenBandFactor;
        float expected2 = expectedRamp + step2;
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0.9f, handbrake: false,
            dt: dt, query: NullVehicleCollisionQuery.Instance);
        Assert.AreEqual(0.9f, inst.SteerInput, 1e-5f);
        Assert.AreEqual(expected2, inst.SteerRamp, 1e-5f);
    }

    [TestMethod]
    public void ApplyAction_SteerFinal_RampsWithSpeedFactor()
    {
        var inst = CreateInstance();
        const float dt = 1f / 60f;

        // At rest: speedFactor = 0 → targetFinal = 0 → SteerFinal stays 0.
        // Stage-1 still advances toward steer input.
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 1f, handbrake: false,
            dt: dt, query: NullVehicleCollisionQuery.Instance);
        Assert.AreEqual(HkPhysicsConstants.SteerStage1RateBase * dt, inst.SteerRamp, 1e-5f);
        Assert.AreEqual(0f, inst.SteerFinal, 1e-5f);

        // Seed stage-1 full authority + speed above divisor → factor 1 → stage-2 ±0.05.
        inst.SteerRamp = 1f;
        inst.Body.LinVelZ = 25f;
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 1f, handbrake: false,
            dt: dt, query: NullVehicleCollisionQuery.Instance);
        Assert.AreEqual(HkPhysicsConstants.SteerRampPerTick, inst.SteerFinal, 1e-5f);
    }

    [TestMethod]
    public void ApplyAction_Avd_DampsAngularVelocity()
    {
        var inst = CreateInstance();
        inst.Body.AngVelY = 2f;

        const float dt = 1f / 60f;
        // |w|=2 < threshold 4 → normal damp 1.5: f = max(0, 1 - 1.5*dt)
        float expectedScale = MathF.Max(0f, 1f - inst.Data.AvdNormalSpinDamping * dt);

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: NullVehicleCollisionQuery.Instance);

        Assert.AreEqual(2f * expectedScale, inst.Body.AngVelY, 1e-4f);
    }

    [TestMethod]
    public void ApplyAction_ClampSteerAndThrottleInputs()
    {
        var inst = CreateInstance();

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 3f, steerInput: -2f, handbrake: false,
            dt: 0f, query: NullVehicleCollisionQuery.Instance);

        Assert.AreEqual(1f, inst.Throttle, 1e-5f);
        Assert.AreEqual(-1f, inst.SteerInput, 1e-5f);
        Assert.AreEqual(0f, inst.SteerRamp, 1e-5f);
    }

    [TestMethod]
    public void ApplyAction_ThrottleSign_PreservedNegative()
    {
        // Retail keeps thr sign (AI forward often −1); never flip.
        var inst = CreateInstance();
        VehicleActionSim.ApplyAction(
            inst, throttleInput: -0.5f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: NullVehicleCollisionQuery.Instance);
        Assert.AreEqual(-0.5f, inst.Throttle, 1e-5f);
    }

    [TestMethod]
    public void ApplyAction_OrderSmoke_Airborne_WritesTorqueAndRampsSteer()
    {
        // Order smoke: one airborne substep leaves gravity-integrated body,
        // stage-1 advanced, stage-2 zero at rest, drive torque zero (no contact).
        var inst = CreateInstance();
        inst.Body.PosY = 50f;
        const float dt = 1f / 60f;

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 1f, steerInput: 0.8f, handbrake: false,
            dt: dt, query: NullVehicleCollisionQuery.Instance);

        Assert.IsTrue(inst.AllWheelsAirborne);
        Assert.AreEqual(HkPhysicsConstants.SteerStage1RateBase * dt, inst.SteerRamp, 1e-4f);
        Assert.AreEqual(0f, inst.SteerFinal, 1e-5f);
        Assert.IsTrue(inst.Body.LinVelY < 0f);
        for (var i = 0; i < inst.Wheels.Length; i++)
            Assert.AreEqual(0f, inst.Wheels[i].DriveTorque, 1e-5f);
    }

    [TestMethod]
    public void ApplyAction_Grounded_TorqueCurveFactor_IsMinTorqueNotAbsThrottle()
    {
        // Retail: torqueCurve2D(contact.x, contact.z) OOR → factors[0] = MinTorqueFactor (0.2).
        // Old skeleton used |throttle|; assert we no longer do that.
        var inst = CreateInstance();
        // High speed avoids low-speed μ boost so expected torque = μ * upright * factors[0].
        inst.Body.LinVelZ = 20f;

        const float throttle = 0.75f; // |throttle| must NOT be the curve factor
        VehicleActionSim.ApplyAction(
            inst,
            throttleInput: throttle,
            steerInput: 0f,
            handbrake: false,
            dt: 1f / 60f,
            query: new AlwaysHitQuery(fraction: 0.5f));

        Assert.IsFalse(inst.AllWheelsAirborne);
        float expectedCurve = inst.Data.MinTorqueFactor; // 0.2 from SyntheticCar
        Assert.AreEqual(0.2f, expectedCurve, 1e-5f);
        Assert.AreNotEqual(MathF.Abs(throttle), expectedCurve, 1e-5f);

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsTrue(inst.Wheels[i].InContact, $"wheel {i} should contact");
            float mu = inst.Data.Wheels[i].Friction;
            float tRatio = inst.Data.Wheels[i].TorqueRatio;
            float expected = HkVehicleEngine.ComputeWheelTorque(
                torqueCurveFactor: expectedCurve,
                frictionMu: mu,
                uprightFactor: 1f,
                chassisSpeed: 20f,
                isRear: inst.Data.Wheels[i].IsRear,
                handbrake: false,
                driverMod: 0f,
                torqueRatio: tRatio);
            Assert.AreEqual(expected, inst.Wheels[i].DriveTorque, 1e-4f,
                $"wheel {i}: expected constant-factor × tRatio torque, not |throttle| scale");
            // Front undriven (tRatio=0) must be zero; rear carries the drive.
            if (tRatio == 0f)
                Assert.AreEqual(0f, inst.Wheels[i].DriveTorque, 1e-4f);
            else
                Assert.IsTrue(inst.Wheels[i].DriveTorque > 0f, $"driven wheel {i}");
            // Explicitly not the old |throttle| path (when tRatio would make them equal by chance).
            if (tRatio > 0f)
            {
                float oldWrong = HkVehicleEngine.ComputeWheelTorque(
                    torqueCurveFactor: MathF.Abs(throttle),
                    frictionMu: mu,
                    uprightFactor: 1f,
                    chassisSpeed: 20f,
                    isRear: inst.Data.Wheels[i].IsRear,
                    handbrake: false,
                    torqueRatio: tRatio);
                Assert.AreNotEqual(oldWrong, inst.Wheels[i].DriveTorque, 1e-4f);
            }
        }
    }

    [TestMethod]
    public void ApplyAction_Grounded_ContactXzOor_UsesFactors0()
    {
        var inst = CreateInstance();
        inst.Body.PosX = 500f;
        inst.Body.PosZ = -300f;
        inst.Body.LinVelZ = 20f;

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 1f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: new AlwaysHitQuery(fraction: 0.4f));

        // Contact points are world-space → OOR for trivial 0-bin LUT.
        float f0 = inst.Data.EngineFactors[0];
        Assert.AreEqual(inst.Data.MinTorqueFactor, f0, 1e-5f);

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsTrue(inst.Wheels[i].InContact);
            float curve = HkVehicleEngine.EvaluateTorqueCurveFactor(
                inst.Data,
                inst.Wheels[i].ContactPointX,
                inst.Wheels[i].ContactPointZ);
            Assert.AreEqual(f0, curve, 1e-5f);

            float expected = HkVehicleEngine.ComputeWheelTorque(
                curve, inst.Data.Wheels[i].Friction, 1f, 20f,
                inst.Data.Wheels[i].IsRear, handbrake: false,
                torqueRatio: inst.Data.Wheels[i].TorqueRatio);
            Assert.AreEqual(expected, inst.Wheels[i].DriveTorque, 1e-4f);
        }
    }

    [TestMethod]
    public void AggregateDrivePack_UsesContactGateNotTorqueRatio()
    {
        // Retail postTick: drivePack += wheels+0x28[i] * wheel+0x88 / N
        // wheel+0x88 = contact gate (1 grounded / 0 airborne); tRatio is already in +0x28.
        var torques = new[] { 10f, 10f };
        float grounded = HkVehicleFrictionSolver.AggregateDrivePack(
            torques,
            new[]
            {
                HkVehicleEngine.ComputeContactDriveScale(true),
                HkVehicleEngine.ComputeContactDriveScale(true),
            });
        float oneAir = HkVehicleFrictionSolver.AggregateDrivePack(
            torques,
            new[]
            {
                HkVehicleEngine.ComputeContactDriveScale(true),
                HkVehicleEngine.ComputeContactDriveScale(false),
            });
        float allAir = HkVehicleFrictionSolver.AggregateDrivePack(
            torques,
            new[]
            {
                HkVehicleEngine.ComputeContactDriveScale(false),
                HkVehicleEngine.ComputeContactDriveScale(false),
            });

        Assert.AreEqual(10f, grounded, 1e-5f); // (10*1 + 10*1) / 2
        Assert.AreEqual(5f, oneAir, 1e-5f);    // (10*1 + 10*0) / 2
        Assert.AreEqual(0f, allAir, 1e-5f);
    }

    /// <summary>
    /// Phase 3: chassis linVel is passed into wheel cast so damper gets real
    /// wheel+0xB4 (ClosingSpeed = dot(linVel, contactNormal); &lt;0 when falling).
    /// </summary>
    [TestMethod]
    public void ApplyAction_ApproachingGround_WritesNegativeClosingSpeed()
    {
        var inst = CreateInstance();
        inst.SetPose(0f, 1f, 0f, 0f, 0f, 0f, 1f);
        inst.Body.LinVelX = 0f;
        inst.Body.LinVelY = -3f; // falling onto ground
        inst.Body.LinVelZ = 0f;
        inst.Body.AngVelX = inst.Body.AngVelY = inst.Body.AngVelZ = 0f;

        // Pure unit formula (normal up): ClosingSpeed = dot(v, n) = −3
        Assert.AreEqual(-3f, HkVehicleWheelCollide.ComputeClosingSpeed(0f, -3f, 0f, 0f, 1f, 0f), 1e-5f);

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: new AlwaysHitQuery(fraction: 0.5f));

        Assert.IsFalse(inst.AllWheelsAirborne);
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsTrue(inst.Wheels[i].InContact, $"wheel {i}");
            // AlwaysHit normal is world +Y; cast uses pre-impulse linVel snapshot.
            Assert.IsTrue(inst.Wheels[i].ClosingSpeed < 0f,
                $"wheel {i} expected negative closingSpeed, got {inst.Wheels[i].ClosingSpeed}");
            Assert.AreEqual(-3f, inst.Wheels[i].ClosingSpeed, 1e-3f);
        }
    }

    [TestMethod]
    public void ApplyAction_SeparatingFromGround_WritesPositiveClosingSpeed()
    {
        var inst = CreateInstance();
        inst.SetPose(0f, 1f, 0f, 0f, 0f, 0f, 1f);
        inst.Body.LinVelX = 0f;
        inst.Body.LinVelY = 2.5f; // rising
        inst.Body.LinVelZ = 0f;
        inst.Body.AngVelX = inst.Body.AngVelY = inst.Body.AngVelZ = 0f;

        Assert.AreEqual(2.5f, HkVehicleWheelCollide.ComputeClosingSpeed(0f, 2.5f, 0f, 0f, 1f, 0f), 1e-5f);

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: new AlwaysHitQuery(fraction: 0.5f));

        Assert.IsFalse(inst.AllWheelsAirborne);
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsTrue(inst.Wheels[i].InContact, $"wheel {i}");
            Assert.IsTrue(inst.Wheels[i].ClosingSpeed > 0f,
                $"wheel {i} expected positive closingSpeed, got {inst.Wheels[i].ClosingSpeed}");
            Assert.AreEqual(2.5f, inst.Wheels[i].ClosingSpeed, 1e-3f);
        }
    }

    // --- Phase 4 friction solver integration ---

    [TestMethod]
    public void ApplyAction_GroundedWithThrottle_ProducesForwardAcceleration()
    {
        var inst = CreateInstance();
        // Identity orientation: +Z forward. Always-hit ground.
        // Retail thr base = −1 for forward (VehicleDriveController / AI thr convention).
        inst.SetPose(0f, 1f, 0f, 0f, 0f, 0f, 1f);

        const float dt = 1f / 60f;
        const float thrForward = -1f;
        var query = new AlwaysHitQuery(fraction: 0.5f);

        // Retail lag: friction consumes PRIOR-tick DriveTorque. Warm-up writes torque.
        VehicleActionSim.ApplyAction(
            inst, throttleInput: thrForward, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        // Pin pose/vel so second substep starts from rest (integration drifts).
        // Keep warm-up DriveTorque on wheels (SetPose does not clear wheel state).
        float priorTorqueSum = 0f;
        for (var i = 0; i < inst.Wheels.Length; i++)
            priorTorqueSum += inst.Wheels[i].DriveTorque;
        Assert.IsTrue(priorTorqueSum > 0f, "warm-up should leave prior-tick drive torque");

        inst.SetPose(0f, 1f, 0f, 0f, 0f, 0f, 1f);
        // Restore prior-tick torque after SetPose (pose clear only; re-seed if needed).
        // SetPose does not touch wheels — torque still present.
        Assert.IsTrue(inst.Wheels[0].DriveTorque + inst.Wheels[1].DriveTorque
                      + inst.Wheels[2].DriveTorque + inst.Wheels[3].DriveTorque > 0f);

        VehicleActionSim.ApplyAction(
            inst, throttleInput: thrForward, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        Assert.IsFalse(inst.AllWheelsAirborne);
        Assert.IsTrue(inst.Body.LinVelZ > 1e-4f,
            $"expected +Z (forward) velocity from thr={thrForward}, got Vz={inst.Body.LinVelZ}");
        Assert.AreEqual(0f, inst.Body.LinVelX, 1e-3f);

        bool anyLong = false;
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            if (inst.Data.Wheels[i].IsRear && inst.Wheels[i].InContact
                && MathF.Abs(inst.Wheels[i].LongImpulse) > 1e-6f)
            {
                anyLong = true;
            }
        }
        Assert.IsTrue(anyLong, "expected rear wheels to carry longitudinal friction impulse");
    }

    [TestMethod]
    public void ApplyAction_GroundedLateralSlip_ProducesOpposingSideForce()
    {
        var inst = CreateInstance();
        inst.SetPose(0f, 1f, 0f, 0f, 0f, 0f, 1f);
        // Side slip to the right (+X). Friction should push left (ΔVx < 0).
        const float sideVel = 5f;
        inst.Body.LinVelX = sideVel;

        float vxBefore = inst.Body.LinVelX;
        VehicleActionSim.ApplyAction(
            inst,
            throttleInput: 0f,
            steerInput: 0f,
            handbrake: false,
            dt: 1f / 60f,
            query: new AlwaysHitQuery(fraction: 0.5f));

        Assert.IsFalse(inst.AllWheelsAirborne);
        Assert.IsTrue(inst.Body.LinVelX < vxBefore - 1e-4f,
            $"expected opposing lateral friction (Vx decrease), before={vxBefore} after={inst.Body.LinVelX}");

        bool anyLat = false;
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            if (inst.Wheels[i].InContact && MathF.Abs(inst.Wheels[i].LatImpulse) > 1e-6f)
                anyLat = true;
        }
        Assert.IsTrue(anyLat, "expected grounded wheels to carry lateral friction impulse");
    }

    // --- Wheel spin integration (preUpdate 0x64cf20 Loop 3) ---

    [TestMethod]
    public void ApplyAction_InContact_IntegratesSpinFromLongVelAndChassisSpeed()
    {
        var inst = CreateInstance();
        inst.Body.LinVelZ = 4f; // chassis long vel along +Z forward (identity quat)
        const float longContact = 0.8f;
        const float dt = 1f / 60f;

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            inst.Wheels[i].LongContactVel = longContact;
            inst.Wheels[i].SpinAngle = 0.1f;
        }

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: new AlwaysHitQuery(fraction: 0.5f));

        // forwardSpeed ≈ 4; radius 0.4 → ω = (0.8 + 4) / 0.4 = 12
        const float expectedSpin = (longContact + 4f) / 0.4f;
        const float expectedAngle = 0.1f + dt * expectedSpin;

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsTrue(inst.Wheels[i].InContact, $"wheel {i} should be grounded");
            Assert.AreEqual(expectedSpin, inst.Wheels[i].Spin, 1e-3f, $"wheel {i} spin");
            Assert.AreEqual(expectedAngle, inst.Wheels[i].SpinAngle, 1e-3f, $"wheel {i} angle");
        }
    }

    [TestMethod]
    public void ApplyAction_Airborne_ZerosSpin_LeavesSpinAngle()
    {
        var inst = CreateInstance();
        inst.Body.PosY = 40f;
        inst.Body.LinVelZ = 10f;

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            inst.Wheels[i].Spin = 99f;
            inst.Wheels[i].SpinAngle = 1.5f;
            inst.Wheels[i].LongContactVel = 5f;
        }

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: 1f / 60f, query: NullVehicleCollisionQuery.Instance);

        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsFalse(inst.Wheels[i].InContact);
            Assert.AreEqual(0f, inst.Wheels[i].Spin, 1e-6f);
            Assert.AreEqual(1.5f, inst.Wheels[i].SpinAngle, 1e-6f);
        }
    }

    [TestMethod]
    public void ApplyAction_InContact_MultipleSteps_AccumulatesSpinAngle()
    {
        var inst = CreateInstance();
        const float dt = 1f / 60f;
        const float longVel = 0.4f; // ω = 0.4/0.4 = 1 rad/s (LongContact=0)
        var query = new AlwaysHitQuery(fraction: 0.5f);

        for (var step = 0; step < 60; step++)
        {
            // Full pose reset each step so chassisLongVel = LinVel · +Z stays stable
            // (suspension torque would otherwise rotate the body and flip spin sign).
            inst.SetPose(0f, 1f, 0f, 0f, 0f, 0f, 1f);
            inst.Body.LinVelX = 0f;
            inst.Body.LinVelY = 0f;
            inst.Body.LinVelZ = longVel;
            inst.Body.AngVelX = 0f;
            inst.Body.AngVelY = 0f;
            inst.Body.AngVelZ = 0f;
            for (var i = 0; i < inst.Wheels.Length; i++)
                inst.Wheels[i].LongContactVel = 0f;
            // Preserve SpinAngle across steps (SetPose does not clear wheels).
            VehicleActionSim.ApplyAction(
                inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
                dt: dt, query: query);
        }

        // 60 * dt * 1 ≈ 1 rad
        for (var i = 0; i < inst.Wheels.Length; i++)
        {
            Assert.IsTrue(inst.Wheels[i].InContact, $"wheel {i}");
            Assert.AreEqual(1f, inst.Wheels[i].Spin, 1e-2f);
            Assert.AreEqual(1f, inst.Wheels[i].SpinAngle, 5e-2f);
        }
    }

    /// <summary>
    /// postTickApplyForces @ 0x64bc70 / suspImpulse (C2): per grounded wheel,
    /// I = F · dt · n̂ applied via <see cref="HkRigidBody.ApplyPointImpulse"/> at the wheel
    /// <b>contact point</b> (wheel+0x20) — NOT the COM. Known spring force + dt must produce
    /// Δv = ΣI · invMass (+ gravity from integrate) AND the r×J angular response: this fixture's
    /// front/rear spring mismatch leaves a small but exactly predictable pitch (ωx) while the
    /// left/right symmetry cancels yaw/roll.
    /// </summary>
    [TestMethod]
    public void ApplyAction_KnownSuspensionForceAndDt_ProducesExpectedDeltaV()
    {
        // r×J path is opt-in (live default is COM-only for stability).
        ServerConfig.ChassisPointImpulsesEnabled = true;
        try
        {
        var inst = CreateInstance();
        var body = inst.Body;
        var data = inst.Data;

        // Chassis above flat ground so each hardpoint compresses to a known length.
        // hardpoint world Y = PosY + localHardpointY; local Y = -0.2 for all wheels.
        const float bodyY = 0.75f;
        const float terrainY = 0f;
        const float hardpointLocalY = -0.2f;
        float hardpointWorldY = bodyY + hardpointLocalY; // 0.55
        float travel = hardpointWorldY - terrainY;       // 0.55

        inst.SetPose(0f, bodyY, 0f, 0f, 0f, 0f, 1f);

        // Flat Y=0 terrain — contact normal = (0,1,0).
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = terrainY; return true; });

        // Expected spring force per wheel (v=0 → closingSpeed=0 → damper out).
        const float dt = 1f / 60f;
        float expectedForceSum = 0f;
        float expectedAngVelX = 0f; // pitch from r×J at contact points (r rel. COM; Jz=0 → tx=−rz·Jy)
        for (var i = 0; i < data.WheelCount; i++)
        {
            var setup = data.Wheels[i];
            // length = (radius + rest) * frac − radius = travel − radius  (vertical cast)
            float currentLength = travel - setup.Radius;
            Assert.IsTrue(currentLength < setup.SuspensionRestLength,
                $"wheel {i} should be compressed (len={currentLength}, rest={setup.SuspensionRestLength})");

            float scaling = setup.SuspensionRestLength > 1e-6f
                ? 1f / setup.SuspensionRestLength
                : 1f;
            float force = HkVehicleSuspension.ComputeForce(
                inContact: true,
                restLength: setup.SuspensionRestLength,
                strength: setup.SuspensionStrength,
                dampCompression: setup.DampingCompression,
                dampExtension: setup.DampingExtension,
                currentLength: currentLength,
                scalingFactor: scaling,
                closingSpeed: 0f,
                invMass: body.InvMass);
            Assert.IsTrue(force > 0f, $"wheel {i} expected positive spring force");
            expectedForceSum += force;

            // Contact point = hardpoint XZ projected onto the ground plane (vertical cast),
            // so r = contact − COM has rz = hardpointLocalZ (identity quat, PosZ=0);
            // J = (0, F·dt, 0) → Δωx = (−rz · Jy) · invIxx per wheel.
            expectedAngVelX += -setup.HardpointZ * (force * dt) * body.InvInertiaX;
        }

        // AVD (normal branch, |ω| under threshold) scales the suspension pitch once per substep.
        expectedAngVelX *= MathF.Max(0f, 1f - data.AvdNormalSpinDamping * dt);

        // I = F * dt * n̂  with n̂ = (0,1,0) → I_y = sum(F) * dt
        float expectedImpulseY = expectedForceSum * dt;

        float vY0 = body.LinVelY;
        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        Assert.IsFalse(inst.AllWheelsAirborne);

        // Δv = I · invMass (point impulse) + g·dt from Integrate gravity.
        float expectedDeltaVy =
            expectedImpulseY * body.InvMass + data.GravityY * dt;
        Assert.AreEqual(expectedDeltaVy, body.LinVelY - vY0, 1e-3f,
            "suspension must apply I=F·dt·n (postTick) consistently with HkRigidBody");
        Assert.AreEqual(0f, body.LinVelX, 1e-3f);
        Assert.AreEqual(0f, body.LinVelZ, 1e-3f);

        // C2 hardpoint impulse: the front/rear spring mismatch MUST show up as pitch (r×J);
        // a COM-only application would leave ωx = 0.
        Assert.IsTrue(MathF.Abs(expectedAngVelX) > 1e-3f,
            "fixture must predict a non-zero pitch response (front/rear force mismatch)");
        Assert.AreEqual(expectedAngVelX, body.AngVelX, 2e-4f,
            "suspension point impulses must produce the r×J pitch response at the contact points");

        // Left/right hardpoint symmetry cancels yaw and roll exactly.
        Assert.AreEqual(0f, body.AngVelY, 1e-3f);
        Assert.AreEqual(0f, body.AngVelZ, 1e-3f);
        }
        finally
        {
            ServerConfig.ChassisPointImpulsesEnabled = ServerConfig.DefaultChassisPointImpulsesEnabled;
        }
    }

    /// <summary>
    /// C2 focused contract: equal springs at mirrored hardpoints → the four contact-point
    /// impulses sum to pure lift; every r×J torque cancels (no pitch/roll/yaw).
    /// </summary>
    [TestMethod]
    public void ApplyAction_SymmetricEqualSuspension_LiftsWithoutAngularVelocity()
    {
        ServerConfig.ChassisPointImpulsesEnabled = true;
        try
        {
        var spec = SyntheticCar();
        spec.SuspensionLength = new FrontRear { Front = 0.3f, Rear = 0.3f };
        spec.SuspensionStrength = new FrontRear { Front = 40f, Rear = 40f };
        var inst = new VehiclePhysicsInstance(HkVehicleData.FromVehicleSpecific(spec, cbid: 9002));
        inst.SetPose(0f, 0.75f, 0f, 0f, 0f, 0f, 1f);

        const float dt = 1f / 60f;
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return true; });

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        Assert.IsFalse(inst.AllWheelsAirborne);
        // Net Δv up: 4 equal compressed springs outweigh one frame of gravity here.
        Assert.IsTrue(inst.Body.LinVelY > 0f,
            $"expected net upward Δv from symmetric suspension, got Vy={inst.Body.LinVelY}");
        // Mirrored contact points with equal impulses — torques cancel to ~zero.
        Assert.AreEqual(0f, inst.Body.AngVelX, 1e-5f, "pitch must cancel (equal front/rear)");
        Assert.AreEqual(0f, inst.Body.AngVelY, 1e-5f, "yaw must cancel");
        Assert.AreEqual(0f, inst.Body.AngVelZ, 1e-5f, "roll must cancel (equal left/right)");
        }
        finally
        {
            ServerConfig.ChassisPointImpulsesEnabled = ServerConfig.DefaultChassisPointImpulsesEnabled;
        }
    }

    /// <summary>
    /// C2 focused contract: one axle pushing harder (front springs stiffer) must produce a
    /// weight-transfer pitch — non-zero ωx about the lateral axis — impossible under the old
    /// COM-force application.
    /// </summary>
    [TestMethod]
    public void ApplyAction_AsymmetricSuspension_ProducesPitchAngularVelocity()
    {
        ServerConfig.ChassisPointImpulsesEnabled = true;
        try
        {
        var spec = SyntheticCar();
        spec.SuspensionLength = new FrontRear { Front = 0.3f, Rear = 0.3f };
        spec.SuspensionStrength = new FrontRear { Front = 80f, Rear = 20f };
        var inst = new VehiclePhysicsInstance(HkVehicleData.FromVehicleSpecific(spec, cbid: 9003));
        inst.SetPose(0f, 0.75f, 0f, 0f, 0f, 0f, 1f);

        const float dt = 1f / 60f;
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return true; });

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        Assert.IsFalse(inst.AllWheelsAirborne);
        // Stronger front springs at +Z contact points → net torque about −X (r×J pitch).
        Assert.IsTrue(inst.Body.AngVelX < -0.1f,
            $"expected strong nose-back pitch (ωx < -0.1) from front-heavy suspension, got {inst.Body.AngVelX}");
        // Left/right symmetry still cancels yaw and roll.
        Assert.AreEqual(0f, inst.Body.AngVelY, 1e-4f);
        Assert.AreEqual(0f, inst.Body.AngVelZ, 1e-4f);
        }
        finally
        {
            ServerConfig.ChassisPointImpulsesEnabled = ServerConfig.DefaultChassisPointImpulsesEnabled;
        }
    }

    /// <summary>
    /// Live default: COM-only susp must not inject pitch from spring asymmetry
    /// (the tumble path seen in client recordings when full point impulses were always on).
    /// </summary>
    [TestMethod]
    public void ApplyAction_DefaultComForces_AsymmetricSuspension_NoPitchFromRxF()
    {
        Assert.IsFalse(ServerConfig.ChassisPointImpulsesEnabled,
            "test assumes live default COM-only path");
        var spec = SyntheticCar();
        spec.SuspensionLength = new FrontRear { Front = 0.3f, Rear = 0.3f };
        spec.SuspensionStrength = new FrontRear { Front = 80f, Rear = 20f };
        var inst = new VehiclePhysicsInstance(HkVehicleData.FromVehicleSpecific(spec, cbid: 9004));
        inst.SetPose(0f, 0.75f, 0f, 0f, 0f, 0f, 1f);

        const float dt = 1f / 60f;
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return true; });

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        Assert.AreEqual(0f, inst.Body.AngVelX, 1e-4f, "COM susp path must not create r×J pitch");
        Assert.AreEqual(0f, inst.Body.AngVelZ, 1e-4f, "COM susp path must not create r×J roll");
    }

    /// <summary>
    /// Steered tire frames + yaw-only contact friction: constant steer at speed must build
    /// yaw rate (turn-in). Pre-fix used chassis-right for both axles so F/R yaw cancelled.
    /// </summary>
    [TestMethod]
    public void ApplyAction_SteeredFriction_AtSpeed_BuildsYawWithoutPitchTumble()
    {
        Assert.IsFalse(ServerConfig.ChassisPointImpulsesEnabled);
        var inst = CreateInstance();
        inst.SetPose(0f, 0.9f, 0f, 0f, 0f, 0f, 1f);
        const float dt = 1f / 60f;
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return true; });

        // Settle then drive with steer held.
        for (var i = 0; i < 30; i++)
            VehicleActionSim.ApplyAction(inst, throttleInput: -1f, steerInput: 0f, handbrake: false, dt: dt, query: query);
        inst.Body.LinVelZ = 8f; // face +Z with speed
        inst.Body.LinVelX = 0f;

        float maxAbsPitch = 0f;
        float maxAbsYaw = 0f;
        for (var i = 0; i < 90; i++)
        {
            VehicleActionSim.ApplyAction(inst, throttleInput: -1f, steerInput: 1f, handbrake: false, dt: dt, query: query);
            maxAbsPitch = MathF.Max(maxAbsPitch, MathF.Abs(inst.Body.AngVelX));
            maxAbsYaw = MathF.Max(maxAbsYaw, MathF.Abs(inst.Body.AngVelY));
        }

        Assert.IsTrue(maxAbsYaw > 0.05f,
            $"expected yaw from steered front friction, max |ωy|={maxAbsYaw}");
        Assert.IsTrue(maxAbsPitch < 1.5f,
            $"pitch should stay bounded without full r×F tumble, max |ωx|={maxAbsPitch}");
        Assert.IsTrue(MathF.Abs(inst.Body.LinVelX) + MathF.Abs(inst.Body.LinVelZ) > 1f,
            "should still be moving after steered run");
    }

    // --- Anti-sink (retail 0x598650 step 3 / applyAction §5) ---

    /// <summary>
    /// Curb-penetration scenario: a very low cast fraction drives every wheel's current
    /// suspension length (wheel+0xB0) negative. Retail scans all wheels' <c>+0xB0</c> each
    /// substep and, when the minimum is negative, raises chassis Y by exactly <c>-min</c> —
    /// a position-only write (see <c>fn_00598650_applyAction.md</c> §5,
    /// <c>fn_offsets_vehicleAction.md</c> §8 step 3, <c>0.4-suspension.md</c>,
    /// <c>0.5-wheel-collide.md</c>). It must NOT touch LinVelY.
    /// </summary>
    [TestMethod]
    public void ApplyAction_CurbPenetration_LiftsChassisByMinCompression_PreservesLinVelY()
    {
        var inst = CreateInstance();
        inst.SetPose(0f, 5f, 0f, 0f, 0f, 0f, 1f);
        inst.Body.LinVelY = -2f; // falling; anti-sink must not zero/clamp this.

        // dt tiny so integrate (vel*dt) and impulse-driven Δv (F*dt) contributions are
        // negligible next to the raw, non-dt-scaled position lift under test.
        const float dt = 1e-6f;
        var query = new AlwaysHitQuery(fraction: 0.1f);

        float posYBefore = inst.Body.PosY;
        float linVelYBefore = inst.Body.LinVelY;

        // Expected retail current-length scan: (radius + restLen) * fraction - radius per wheel.
        float minLength = float.PositiveInfinity;
        for (var i = 0; i < inst.Data.WheelCount; i++)
        {
            var setup = inst.Data.Wheels[i];
            float len = (setup.Radius + setup.SuspensionRestLength) * 0.1f - setup.Radius;
            if (len < minLength)
                minLength = len;
        }

        Assert.IsTrue(minLength < 0f, "test setup must produce curb penetration (min length < 0)");
        float expectedLift = -minLength;

        VehicleActionSim.ApplyAction(
            inst, throttleInput: 0f, steerInput: 0f, handbrake: false,
            dt: dt, query: query);

        Assert.IsFalse(inst.AllWheelsAirborne);

        // (a) position-only lift by exactly -min this substep.
        Assert.AreEqual(posYBefore + expectedLift, inst.Body.PosY, 1e-3f,
            "anti-sink must raise chassis by exactly -min(wheel current length) this substep");

        // (b) LinVelY must be preserved — no downward-velocity zeroing in the retail form.
        Assert.AreEqual(linVelYBefore, inst.Body.LinVelY, 1e-2f,
            "anti-sink is position-only per retail decompile; it must not modify LinVelY");
    }

    /// <summary>Hits every cast at a fixed fraction with upward normal.</summary>
    private sealed class AlwaysHitQuery : IVehicleCollisionQuery
    {
        private readonly float _fraction;

        public AlwaysHitQuery(float fraction) => _fraction = fraction;

        public bool CastRay(
            float originX, float originY, float originZ,
            float dirX, float dirY, float dirZ,
            float maxDistance,
            out VehicleRayHit hit)
        {
            var t = maxDistance * _fraction;
            hit = new VehicleRayHit(
                _fraction,
                originX + dirX * t,
                originY + dirY * t,
                originZ + dirZ * t,
                0f, 1f, 0f,
                isTerrain: true);
            return true;
        }
    }
}

