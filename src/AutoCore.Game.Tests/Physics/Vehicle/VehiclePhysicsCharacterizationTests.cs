using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Phase 4 characterization tests for <see cref="VehiclePhysicsInstance"/> over the full
/// substep + <see cref="VehicleActionSim"/> path. Documents current behavior with synthetic
/// <see cref="VehicleSpecific"/> data and either <see cref="NullVehicleCollisionQuery"/> or a
/// flat-ground <see cref="TerrainHeightfieldCollisionQuery"/> callback.
/// </summary>
[TestClass]
public class VehiclePhysicsCharacterizationTests
{
    private const float Frame60 = 1f / 60f;
    private const float GroundY = 0f;

    /// <summary>Four-wheel synthetic car (matches other vehicle physics unit fixtures).</summary>
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

    private static VehiclePhysicsInstance CreateInstance(int cbid = 7001)
        => new(HkVehicleData.FromVehicleSpecific(SyntheticCar(), cbid));

    private static TerrainHeightfieldCollisionQuery FlatGround(float y = GroundY)
        => new((float x, float z, out float h) =>
        {
            h = y;
            return true;
        });

    private static void StepMany(
        VehiclePhysicsInstance inst,
        int frames,
        float throttle,
        float steer,
        bool handbrake,
        float frameDt,
        IVehicleCollisionQuery query)
    {
        for (var i = 0; i < frames; i++)
            inst.Step(throttle, steer, handbrake, frameDt, query);
    }

    // ── 1. Free fall ─────────────────────────────────────────────────────────

    [TestMethod]
    public void FreeFall_NullQuery_PosYDecreasesUnderGravity()
    {
        var inst = CreateInstance();
        inst.SetPose(0f, 50f, 0f, 0f, 0f, 0f, 1f);

        float y0 = inst.Body.PosY;
        inst.Step(0f, 0f, false, Frame60, NullVehicleCollisionQuery.Instance);

        Assert.IsTrue(inst.AllWheelsAirborne, "null query must keep all wheels airborne");
        Assert.IsTrue(inst.Body.PosY < y0, $"PosY should drop under gravity: {inst.Body.PosY} vs start {y0}");
        Assert.IsTrue(inst.Body.LinVelY < 0f, "LinVelY should be negative under DefaultGravityY");

        // Multi-frame free fall continues downward (no ground support).
        float yAfterOne = inst.Body.PosY;
        StepMany(inst, frames: 30, throttle: 0f, steer: 0f, handbrake: false,
            frameDt: Frame60, query: NullVehicleCollisionQuery.Instance);
        Assert.IsTrue(inst.Body.PosY < yAfterOne, "continued free fall must keep lowering PosY");
        Assert.IsTrue(inst.Body.LinVelY < HkPhysicsConstants.DefaultGravityY * Frame60,
            "downward speed should accumulate past one frame of gravity");
    }

    // ── 2. Flat ground / suspension support ──────────────────────────────────

    [TestMethod]
    public void FlatGround_SuspensionSupports_DoesNotInfiniteFall()
    {
        var inst = CreateInstance();
        // Start above contact range so the car falls onto the plane, then settles.
        // Hardpoint local Y = -0.2, max cast ≈ 0.7 → contact once PosY ≲ 0.9.
        inst.SetPose(0f, 2.0f, 0f, 0f, 0f, 0f, 1f);
        var ground = FlatGround(GroundY);

        // Free-fall baseline over the same horizon for comparison.
        var free = CreateInstance(cbid: 7002);
        free.SetPose(0f, 2.0f, 0f, 0f, 0f, 0f, 1f);

        const int frames = 180; // 3 s @ 60 Hz
        var sawContact = false;
        float minPosY = float.PositiveInfinity;
        for (var i = 0; i < frames; i++)
        {
            inst.Step(0f, 0f, false, Frame60, ground);
            if (!inst.AllWheelsAirborne)
                sawContact = true;
            for (var w = 0; w < inst.Wheels.Length; w++)
                sawContact |= inst.Wheels[w].InContact;
            if (inst.Body.PosY < minPosY)
                minPosY = inst.Body.PosY;
        }

        StepMany(free, frames, throttle: 0f, steer: 0f, handbrake: false, Frame60,
            NullVehicleCollisionQuery.Instance);

        // Supported body stays near the plane; free-fall drops many meters.
        Assert.IsTrue(inst.Body.PosY > GroundY - 1f,
            $"supported PosY should remain above ground with margin, got {inst.Body.PosY}");
        Assert.IsTrue(inst.Body.PosY > free.Body.PosY + 5f,
            $"supported PosY ({inst.Body.PosY}) must be well above free-fall ({free.Body.PosY})");
        Assert.IsTrue(minPosY > free.Body.PosY + 5f,
            $"supported min PosY ({minPosY}) must not track free-fall ({free.Body.PosY})");
        Assert.IsTrue(sawContact, "at least one wheel should contact the flat heightfield while settling");

        // Vertical speed is bounded (not free-fall |v| ≈ |g|·t ≈ 29 after 3 s).
        Assert.IsTrue(MathF.Abs(inst.Body.LinVelY) < 15f,
            $"settled |LinVelY| should be moderate, got {inst.Body.LinVelY}");
    }

    // ── 3. Forward throttle on ground ────────────────────────────────────────

    [TestMethod]
    public void ForwardThrottle_RetailNegativeSign_ProducesHorizontalMotion()
    {
        var inst = CreateInstance();
        // Spawn already in the contact band so wheels engage quickly.
        inst.SetPose(0f, 0.85f, 0f, 0f, 0f, 0f, 1f);
        var ground = FlatGround(GroundY);

        // Settle without drive.
        StepMany(inst, frames: 90, throttle: 0f, steer: 0f, handbrake: false, Frame60, ground);

        float x0 = inst.Body.PosX;
        float z0 = inst.Body.PosZ;

        // Retail drive-controller convention: negative throttle = forward base (VehicleDriveController).
        // Friction signs the axle drive pack from Throttle; |thr| itself is not the torque curve factor.
        const float retailForwardThrottle = -1f;
        float maxPlanarSpeed = 0f;
        const int driveFrames = 240; // ~4 s @ 60 Hz (allows one-substep torque lag + settle)
        for (var i = 0; i < driveFrames; i++)
        {
            inst.Step(retailForwardThrottle, 0f, false, Frame60, ground);
            float spd = MathF.Sqrt(
                inst.Body.LinVelX * inst.Body.LinVelX + inst.Body.LinVelZ * inst.Body.LinVelZ);
            if (spd > maxPlanarSpeed)
                maxPlanarSpeed = spd;
        }

        float dx = inst.Body.PosX - x0;
        float dz = inst.Body.PosZ - z0;
        float planarDisp = MathF.Sqrt(dx * dx + dz * dz);

        Assert.AreEqual(retailForwardThrottle, inst.Throttle, 1e-5f);
        Assert.IsTrue(
            planarDisp > 0.02f || maxPlanarSpeed > 0.05f,
            $"expected non-zero horizontal motion under retail-forward throttle; "
            + $"disp={planarDisp}, maxSpeed={maxPlanarSpeed}, pos=({inst.Body.PosX},{inst.Body.PosZ})");
    }

    // ── 4. Substep N=2 for frameDt=0.05 ──────────────────────────────────────

    [TestMethod]
    public void Substep_FrameDt005_ProducesN2_MatchesDoubleGravityIntegrate()
    {
        const float frameDt = 0.05f;
        var (n, subDt) = HkVehicleSubstep.Compute(frameDt);
        Assert.AreEqual(2, n, "frameDt=0.05 must split into N=2 substeps");
        Assert.AreEqual(frameDt / 2f, subDt, 1e-6f);

        var inst = CreateInstance();
        inst.SetPose(0f, 100f, 0f, 0f, 0f, 0f, 1f);

        inst.Step(0f, 0f, false, frameDt, NullVehicleCollisionQuery.Instance);

        // Two semi-implicit gravity substeps from rest (same contract as smoke suite).
        float g = HkPhysicsConstants.DefaultGravityY;
        float v1 = g * subDt;
        float y1 = 100f + v1 * subDt;
        float v2 = v1 + g * subDt;
        float y2 = y1 + v2 * subDt;

        Assert.AreEqual(v2, inst.Body.LinVelY, 1e-3f);
        Assert.AreEqual(y2, inst.Body.PosY, 1e-2f);
        Assert.IsTrue(inst.AllWheelsAirborne);
    }

    // ── 5. AVD damps large angular velocity ──────────────────────────────────

    [TestMethod]
    public void Avd_LargeAngularVelocity_DampsAfterSteps()
    {
        var inst = CreateInstance();
        inst.SetPose(0f, 20f, 0f, 0f, 0f, 0f, 1f);

        // Spin above AVD collision threshold (synthetic car thr=4) but below
        // MaxAngularSpeed clamp so closed-form AVD prediction is not polluted by Integrate clamp.
        const float largeWy = 6f;
        inst.Body.AngVelX = 2f;
        inst.Body.AngVelY = largeWy;
        inst.Body.AngVelZ = -3f;
        // |ω| = sqrt(4+36+9)=7 < MaxAngularSpeed(8), > AVD thr(4)

        float mag0 = MathF.Sqrt(
            inst.Body.AngVelX * inst.Body.AngVelX
            + inst.Body.AngVelY * inst.Body.AngVelY
            + inst.Body.AngVelZ * inst.Body.AngVelZ);
        Assert.IsTrue(mag0 <= HkPhysicsConstants.MaxAngularSpeed,
            "test seed must stay under ang clamp for closed-form AVD");

        // Several frames airborne so AVD runs without suspension torque noise dominating.
        StepMany(inst, frames: 30, throttle: 0f, steer: 0f, handbrake: false, Frame60,
            NullVehicleCollisionQuery.Instance);

        float mag1 = MathF.Sqrt(
            inst.Body.AngVelX * inst.Body.AngVelX
            + inst.Body.AngVelY * inst.Body.AngVelY
            + inst.Body.AngVelZ * inst.Body.AngVelZ);

        Assert.IsTrue(mag1 < mag0,
            $"AVD should reduce |ω|: before={mag0}, after={mag1}");
        Assert.IsTrue(mag1 < mag0 * 0.5f,
            $"large ω should decay substantially under collision/normal AVD rates: {mag1} vs {mag0}");

        // Airborne closed-form: AVD scales ω each substep; branch switches from collision→normal
        // once |ω| ≤ threshold (no wheel torque when null query).
        float expectedMag = mag0;
        float thr = inst.Data.AvdCollisionThreshold;
        for (var i = 0; i < 30; i++)
        {
            float rate = expectedMag * expectedMag <= thr * thr
                ? inst.Data.AvdNormalSpinDamping
                : inst.Data.AvdCollisionSpinDamping;
            float f = MathF.Max(0f, 1f - rate * Frame60);
            expectedMag *= f;
        }

        Assert.AreEqual(expectedMag, mag1, 1e-2f,
            "airborne AVD damping should match repeated scale with thr branch (no wheel torque)");
    }
}
