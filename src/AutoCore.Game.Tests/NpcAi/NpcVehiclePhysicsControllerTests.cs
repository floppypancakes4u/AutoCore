using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Npc;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Controller contracts for the physics-tier NPC vehicle mover.
/// D2: sim-authoritative — publish <c>inst.Body</c> verbatim after <c>Step</c>.
/// </summary>
[TestClass]
public class NpcVehiclePhysicsControllerTests
{
    [TestInitialize]
    public void SetUp()
    {
        ServerConfig.ResetToDefaults();
        HkVehicleDataCache.Clear();
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
        NpcVehiclePhysicsController.ResyncDriftThreshold = 8f;
    }

    [TestCleanup]
    public void TearDown()
    {
        ServerConfig.ResetToDefaults();
        HkVehicleDataCache.Clear();
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
        NpcVehiclePhysicsController.ResyncDriftThreshold = 8f;
    }

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

    private static Vehicle CreateNpcVehicle()
    {
        var v = new Vehicle();
        v.SetCbidForTests(90042);
        var cb = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cb.VehicleSpecific = SyntheticCar();
        cb.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 90042 };
        // Representative chassis mass (rlMass); zero/uninitialized falls back to unit mass and
        // under-accelerates the free-running sim for cruise contracts.
        cb.SimpleObjectSpecific = new SimpleObjectSpecific { Mass = 1500f };
        v.AssignCloneBaseForTests(cb);
        v.NpcAi = new NpcAiState();
        // Identity facing +Z; raised slightly for wheel cast.
        v.ApplyServerMove(
            new Vector3(0f, 1f, 0f),
            Quaternion.Default,
            default,
            0f);
        return v;
    }

    private static MapPathTemplate StraightPath()
    {
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(0f, 0f, 0f),
            AcceptDistance = 3f,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(0f, 0f, 40f),
            AcceptDistance = 3f,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(0f, 0f, 80f),
            AcceptDistance = 3f,
        });
        return path;
    }

    private static PathStepResult HardCruiseAlongZ(float z, float hardSpeed, float dt) => new()
    {
        NewPosition = new Vector3(0f, 1f, z + hardSpeed * dt),
        Velocity = new Vector3(0f, 0f, hardSpeed),
        Rotation = Quaternion.Default,
        NewIndex = 1,
        NewDirection = 1,
        Arrived = false,
        WaitUntilMs = 0,
    };

    [TestMethod]
    public void Apply_CreatesPhysicsInstance_AndSetsDriveInputs()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 2f),
            Velocity = new Vector3(0f, 0f, 5f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
            Arrived = false,
            WaitUntilMs = 0,
        };

        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 1000, dt: 1f / 60f, map: null, npcAi: vehicle.NpcAi);

        Assert.IsNotNull(vehicle.PhysicsInstance);
        Assert.IsTrue(result.HasDriveInputs);
        Assert.AreEqual(hard.NewIndex, result.NewIndex);
        // Aim is along +Z; retail forward thr base is negative.
        Assert.IsTrue(result.Throttle <= 0f,
            $"expected retail-forward throttle ≤ 0, got {result.Throttle}");
        Assert.IsTrue(result.AngularVelocity.HasValue);
    }

    [TestMethod]
    public void Apply_WaitHold_ZerosThrottleAndSetsSharp()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        var hard = new PathStepResult
        {
            NewPosition = vehicle.Position,
            Arrived = true,
            WaitUntilMs = 5000,
            NewIndex = 0,
            NewDirection = 1,
        };

        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 1000, dt: 1f / 60f, map: null, npcAi: vehicle.NpcAi);

        Assert.IsTrue(result.HasDriveInputs);
        Assert.AreEqual(0f, result.Throttle, 1e-5f);
        Assert.AreEqual((byte)1, result.SharpTurn);
        Assert.AreEqual(vehicle.Position.X, result.NewPosition.X, 1e-4f);
        Assert.AreEqual(vehicle.Position.Z, result.NewPosition.Z, 1e-4f);
    }

    [TestMethod]
    public void Apply_NoCloneBase_FailClosedReturnsHard()
    {
        var vehicle = new Vehicle();
        vehicle.NpcAi = new NpcAiState();
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(1f, 2f, 3f),
            NewIndex = 2,
            NewDirection = -1,
        };

        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, StraightPath(), nowMs: 0, dt: 0.016f, map: null, npcAi: vehicle.NpcAi);

        Assert.AreEqual(2, result.NewIndex);
        Assert.AreEqual(-1, result.NewDirection);
        Assert.AreEqual(1f, result.NewPosition.X, 1e-5f);
        Assert.IsNull(vehicle.PhysicsInstance);
    }

    [TestMethod]
    public void ExtractBasis_Identity_RightX_ForwardZ()
    {
        NpcVehiclePhysicsController.ExtractBasis(Quaternion.Default, out var right, out var forward);
        Assert.AreEqual(1f, right.X, 1e-4f);
        Assert.AreEqual(0f, right.Y, 1e-4f);
        Assert.AreEqual(0f, right.Z, 1e-4f);
        Assert.AreEqual(0f, forward.X, 1e-4f);
        Assert.AreEqual(0f, forward.Y, 1e-4f);
        Assert.AreEqual(1f, forward.Z, 1e-4f);
    }

    /// <summary>
    /// Sim-driven path cruise: hard navigator advances along +Z at cruise speed; controller
    /// must make forward progress with bounded lateral slip. Tolerances are deliberately loose
    /// (accel ramp + reduced/sim dynamics) — not kinematic hard-speed matching.
    /// </summary>
    [TestMethod]
    public void Apply_PathCruise_AdvancesAlongPathAtNearHardSpeed()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        const float hardSpeed = 12f;
        const float dt = 1f / 60f;
        var pos = vehicle.Position;

        for (var i = 0; i < 60; i++)
        {
            var hard = HardCruiseAlongZ(pos.Z, hardSpeed, dt);

            var result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: 1000 + i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);

            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

            pos = result.NewPosition;
        }

        // ~1s of free-running cruise: clear path progress (accel ramp + suspension seat).
        // Absolute distance is deliberately loose — not kinematic hard-speed matching.
        Assert.IsTrue(pos.Z > 1f,
            $"expected path progress along +Z, got Z={pos.Z}");
        // Bounded lateral slip (path is X=0) — track pull keeps this tighter.
        Assert.IsTrue(MathF.Abs(pos.X) < 2.5f,
            $"lateral slip out of bound: X={pos.X}");

        var speed = MathF.Sqrt(vehicle.Velocity.X * vehicle.Velocity.X + vehicle.Velocity.Z * vehicle.Velocity.Z);
        Assert.IsTrue(speed > 1f,
            $"expected non-trivial planar speed under sim-driven motion, got {speed}");
    }

    /// <summary>
    /// Path track soft-pull: start offset from the hard line; after a few seconds lateral
    /// error to X=0 must shrink (hard navigator stays on the path).
    /// </summary>
    [TestMethod]
    public void Apply_LateralOffset_PullsTowardHardTrack()
    {
        var vehicle = CreateNpcVehicle();
        vehicle.ApplyServerMove(
            new Vector3(6f, 1f, 0f),
            Quaternion.Default,
            new Vector3(0f, 0f, 8f),
            0f);

        var path = StraightPath();
        const float hardSpeed = 10f;
        const float dt = 1f / 60f;
        float x0 = MathF.Abs(vehicle.Position.X);

        for (var i = 0; i < 180; i++) // 3 s
        {
            var hard = HardCruiseAlongZ(vehicle.Position.Z, hardSpeed, dt);
            // Keep hard on the path X=0 (not on the drifted body).
            hard.NewPosition = new Vector3(0f, 1f, hard.NewPosition.Z);
            hard.Velocity = new Vector3(0f, 0f, hardSpeed);

            var result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);
            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);
        }

        float x1 = MathF.Abs(vehicle.Position.X);
        Assert.IsTrue(x1 < x0 * 0.5f,
            $"expected lateral pull toward path X=0, start |X|={x0} end |X|={x1}");
        Assert.IsTrue(vehicle.Position.Z > 5f,
            $"expected forward progress while correcting, Z={vehicle.Position.Z}");
    }

    [TestMethod]
    public void ScaleThrottleForHeadingError_CutsWhenMisaligned()
    {
        var forward = new Vector3(1f, 0f, 0f); // face +X
        var aim = new Vector3(0f, 0f, 20f); // path +Z
        var pos = new Vector3(0f, 0f, 0f);
        float scale = NpcVehiclePhysicsController.ScaleThrottleForHeadingError(forward, aim, pos);
        Assert.IsTrue(scale < 0.95f && scale >= NpcVehiclePhysicsController.PathThrottleCutMinScale - 1e-4f,
            $"expected thr cut when ~90° misaligned, scale={scale}");

        var alignedFwd = new Vector3(0f, 0f, 1f);
        float scaleOk = NpcVehiclePhysicsController.ScaleThrottleForHeadingError(alignedFwd, aim, pos);
        Assert.AreEqual(1f, scaleOk, 1e-4f);
    }

    [TestMethod]
    public void ApplyPathSpeedMatch_BoostsTowardHardSpeedWhenAligned()
    {
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = 1f, PosZ = 0f,
            LinVelZ = 3f, // slow
            QuatW = 1f, // face +Z
        };
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 5f),
            Velocity = new Vector3(0f, 0f, 12f),
        };
        var aim = new Vector3(0f, 1f, 20f);
        const float dt = 1f / 60f;
        for (var i = 0; i < 90; i++)
            NpcVehiclePhysicsController.ApplyPathSpeedMatch(body, hard, aim, dt);

        Assert.IsTrue(body.LinVelZ > 6f,
            $"expected speed match toward hard 12 m/s, got LinVelZ={body.LinVelZ}");
    }

    [TestMethod]
    public void ApplyPathHeadingAssist_MaxYawStep_IsBounded()
    {
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            InvInertiaY = 1f,
            PosX = 0f, PosY = 1f, PosZ = 0f,
            LinVelX = 10f,
            QuatW = 1f,
        };
        // Face +Z, aim far left (+X) → ~90° error
        var aim = new Vector3(50f, 1f, 0f);
        const float dt = 1f / 60f;
        float yaw0 = VehicleDriveInputs.YawFromQuaternion(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW));
        NpcVehiclePhysicsController.ApplyPathHeadingAssist(body, aim, dt);
        float yaw1 = VehicleDriveInputs.YawFromQuaternion(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW));
        float step = MathF.Abs(yaw1 - yaw0);
        // unwrap
        if (step > MathF.PI) step = 2f * MathF.PI - step;
        Assert.IsTrue(step <= NpcVehiclePhysicsController.PathHeadingMaxYawStep + 0.002f,
            $"yaw step {step} exceeded max {NpcVehiclePhysicsController.PathHeadingMaxYawStep}");
    }

    [TestMethod]
    public void ApplyTerrainStanceAssist_PitchesTowardSlope()
    {
        // Rising terrain in +Z: y = 0.2 * z  (~11° slope)
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) =>
            {
                y = 0.2f * z;
                return true;
            });
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = 2f, PosZ = 10f,
            QuatW = 1f, // level, face +Z
        };
        const float dt = 1f / 60f;
        bool grounded = false;
        for (var i = 0; i < 60; i++)
            grounded = NpcVehiclePhysicsController.ApplyTerrainStanceAssist(body, query, dt);

        Assert.IsTrue(grounded, "uniform slope should count as grounded");
        NpcVehiclePhysicsController.ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        // On rising slope, nose should pitch up (forward.Y > 0).
        Assert.IsTrue(forward.Y > 0.08f,
            $"expected clear nose-up pitch on rising slope, forward.Y={forward.Y}");
        Assert.IsTrue(NpcVehiclePhysicsController.BodyUpDotWorldUp(body) > 0.7f,
            "stance must not invert the chassis");
    }

    [TestMethod]
    public void ApplyTerrainStanceAssist_AirborneOverLedge_ReturnsFalseNoYStick()
    {
        // Fully over a void: all samples hit y=-10 while chassis is at y=2.
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) =>
            {
                y = -10f;
                return true;
            });
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = 2f, PosZ = 5.5f,
            LinVelZ = 8f,
            LinVelY = -2f, // falling
            QuatW = 1f,
        };
        float y0 = body.PosY;
        bool grounded = NpcVehiclePhysicsController.ApplyTerrainStanceAssist(body, query, 1f / 60f);
        Assert.IsFalse(grounded, "high over void should be airborne");
        // Must not yank chassis down to the floor in one tick.
        Assert.IsTrue(body.PosY > y0 - 0.2f,
            $"airborne must not Y-stick into void, PosY={body.PosY}");
        // Ballistic pitch: falling → nose should dip (forward.Y < 0).
        NpcVehiclePhysicsController.ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        Assert.IsTrue(forward.Y < -0.01f,
            $"expected nose-down in free fall, forward.Y={forward.Y}");
    }

    [TestMethod]
    public void ApplyBallisticPitch_FallsNoseDown()
    {
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = 10f, PosZ = 0f,
            LinVelZ = 10f,
            LinVelY = -8f,
            QuatW = 1f,
        };
        const float dt = 1f / 60f;
        for (var i = 0; i < 30; i++)
            NpcVehiclePhysicsController.ApplyBallisticPitch(body, yaw: 0f, dt);

        NpcVehiclePhysicsController.ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        Assert.IsTrue(forward.Y < -0.15f,
            $"expected clear nose-down ballistic pitch, forward.Y={forward.Y}");
    }

    [TestMethod]
    public void ApplyRampLaunchBoost_SetsVyFromNoseAndPlanar()
    {
        // Nose-up ~20° on +Z, planar 12 m/s → wantVy ≈ 12 * tan(0.35) ≈ 4.4
        float noseUp = 0.35f;
        var q = TerrainContactPlane.FromYawPitchRoll(yaw: 0f, pitch: -noseUp, roll: 0f);
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = 5f, PosZ = 0f,
            LinVelZ = 12f,
            LinVelY = 0.2f, // flat launch — the regression symptom
            QuatX = q.X, QuatY = q.Y, QuatZ = q.Z, QuatW = q.W,
        };

        NpcVehiclePhysicsController.ApplyRampLaunchBoost(body, yaw: 0f, planarSpd: 12f);

        float want = 12f * MathF.Tan(noseUp);
        Assert.IsTrue(body.LinVelY >= want * 0.9f,
            $"launch boost should restore climb rate ~{want:F2}, got LinVelY={body.LinVelY}");
    }

    [TestMethod]
    public void ApplyTerrainStanceAssist_ClimbingRamp_StaysPlantedWithoutLoft()
    {
        // Continuous climb must stay grounded and not inject lofting climb-vy (live ramp
        // regression: mid-ramp hop → false free-flight).
        const float slope = 0.22f;
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) =>
            {
                y = slope * z;
                return true;
            });
        float z0 = 20f;
        float seatY = slope * z0 + NpcVehiclePhysicsController.TerrainStanceRideHeight;
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = seatY, PosZ = z0,
            LinVelZ = 14f,
            LinVelY = 0f,
            QuatW = 1f,
        };
        const float dt = 1f / 60f;
        bool grounded = false;
        for (var i = 0; i < 45; i++)
            grounded = NpcVehiclePhysicsController.ApplyTerrainStanceAssist(body, query, dt);

        Assert.IsTrue(grounded, "uniform slope should stay grounded");
        float support = slope * body.PosZ;
        float clearance = body.PosY - support;
        Assert.IsTrue(clearance < NpcVehiclePhysicsController.TerrainStanceRideHeight + 0.35f,
            $"must not loft above ride band, clearance={clearance}");
        Assert.IsTrue(body.LinVelY < 6f,
            $"must not inject large climb vy while grounded, LinVelY={body.LinVelY}");
        NpcVehiclePhysicsController.ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        Assert.IsTrue(forward.Y > 0.08f,
            $"expected nose-up pitch on rising slope, forward.Y={forward.Y}");
    }

    [TestMethod]
    public void ApplyTerrainStanceAssist_RampLipOverGap_LaunchesWithClimbVy()
    {
        // Ramp then deep gap: z < 10 → y = 0.25*z; z >= 10 → pit.
        // Body straddles lip (rear on ramp, front over void) — free-flight + launch vy.
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) =>
            {
                if (z < 10f)
                    y = 0.25f * z;
                else
                    y = -12f;
                return true;
            });
        float zBody = 10f - 0.2f;
        float yRamp = 0.25f * zBody;
        float noseUp = MathF.Atan(0.25f);
        var q = TerrainContactPlane.FromYawPitchRoll(yaw: 0f, pitch: -noseUp, roll: 0f);
        float planar = 14f;
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f,
            PosY = yRamp + NpcVehiclePhysicsController.TerrainStanceRideHeight,
            PosZ = zBody,
            LinVelZ = planar,
            LinVelY = 0.1f,
            QuatX = q.X, QuatY = q.Y, QuatZ = q.Z, QuatW = q.W,
        };

        bool grounded = NpcVehiclePhysicsController.ApplyTerrainStanceAssist(body, query, 1f / 60f);

        Assert.IsFalse(grounded, "front over deep gap with rear near must be lip launch");
        float wantVy = planar * MathF.Tan(noseUp);
        Assert.IsTrue(body.LinVelY >= wantVy * 0.85f,
            $"lip launch must restore climb vy (~{wantVy:F2}), got {body.LinVelY}");
    }

    [TestMethod]
    public void ApplyTerrainStanceAssist_FloatingHigh_HardSnapsToRideHeight()
    {
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) =>
            {
                y = 0f;
                return true;
            });
        const float ride = 0.12f;
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f,
            // Hover ~1.4m above flat ground with residual loft velocity.
            PosY = 1.4f,
            PosZ = 0f,
            LinVelZ = 6f,
            LinVelY = 3f,
            QuatW = 1f,
        };

        bool grounded = NpcVehiclePhysicsController.ApplyTerrainStanceAssist(
            body, query, 1f / 60f, rideHeight: ride);

        Assert.IsTrue(grounded, "hover just above flat ground should re-plant");
        Assert.AreEqual(ride, body.PosY, 0.02f,
            $"must hard-snap to ride height {ride}, PosY={body.PosY}");
        Assert.AreEqual(0f, body.LinVelY, 0.05f,
            $"loft velocity must be zeroed when planted, LinVelY={body.LinVelY}");
    }

    [TestMethod]
    public void ResolveStanceRideHeight_UsesWheelRadiusMinusHardpoint()
    {
        // SyntheticCar: radius 0.4, hardpointY -0.2 → clearance 0.6 → clamp to max 0.55
        var data = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        var inst = new VehiclePhysicsInstance(data);
        float h = NpcVehiclePhysicsController.ResolveStanceRideHeight(vehicle: null, inst);
        Assert.AreEqual(
            NpcVehiclePhysicsController.TerrainStanceRideHeightMax, h, 0.02f,
            $"expected clamped wheel clearance, got {h}");
        // Must NOT include suspension rest (~0.3) which would push ~0.9.
        Assert.IsTrue(h < 0.65f, $"ride height must exclude susp rest loft, got {h}");
    }

    [TestMethod]
    public void ApplyTerrainStanceAssist_RollsTowardCrossSlope()
    {
        // Terrain tilts left-high: y = 0.15 * x  (left = +X in body-right when facing +Z)
        // Body right = +X when yaw=0, so left samples (x negative) are lower → right side high
        // → leftUp positive when left is higher. y = 0.15*x means +X higher.
        var query = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) =>
            {
                y = 0.15f * x;
                return true;
            });
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = NpcVehiclePhysicsController.TerrainStanceRideHeight, PosZ = 0f,
            QuatW = 1f,
        };
        const float dt = 1f / 60f;
        for (var i = 0; i < 40; i++)
            NpcVehiclePhysicsController.ApplyTerrainStanceAssist(body, query, dt);

        NpcVehiclePhysicsController.ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out var right, out _);
        // Higher ground on +X (body right) → right.Y > 0 (right side elevated).
        Assert.IsTrue(right.Y > 0.05f,
            $"expected roll onto cross-slope (right elevated), right.Y={right.Y}");
    }

    [TestMethod]
    public void ForceUprightPreserveYaw_UninvertsWhileKeepingHeading()
    {
        // Build an inverted orientation about Z (roll π) while facing +Z.
        var inverted = TerrainContactPlane.FromYawPitchRoll(yaw: 0f, pitch: 0f, roll: MathF.PI);
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 0f, PosY = 1f, PosZ = 0f,
            QuatX = inverted.X, QuatY = inverted.Y, QuatZ = inverted.Z, QuatW = inverted.W,
            AngVelX = 2f, AngVelZ = 2f,
        };
        Assert.IsTrue(NpcVehiclePhysicsController.BodyUpDotWorldUp(body) < 0f,
            "fixture should start inverted");

        NpcVehiclePhysicsController.ForceUprightPreserveYaw(body);

        Assert.IsTrue(NpcVehiclePhysicsController.BodyUpDotWorldUp(body) > 0.9f,
            $"expected upright, upDot={NpcVehiclePhysicsController.BodyUpDotWorldUp(body)}");
        Assert.AreEqual(0f, body.AngVelX, 1e-5f);
        Assert.AreEqual(0f, body.AngVelZ, 1e-5f);
        NpcVehiclePhysicsController.ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        // Still roughly face +Z after uninvert.
        Assert.IsTrue(forward.Z > 0.7f, $"expected preserve +Z heading, forward.Z={forward.Z}");
    }

    [TestMethod]
    public void ApplyPathTrackLateralPull_CorrectsOrthogonalErrorOnly()
    {
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            PosX = 5f, PosY = 1f, PosZ = 10f,
            QuatW = 1f,
        };
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 12f), // ahead on path + slightly forward
            Velocity = new Vector3(0f, 0f, 10f),     // tangent +Z
        };
        const float dt = 1f / 60f;
        for (var i = 0; i < 60; i++)
            NpcVehiclePhysicsController.ApplyPathTrackLateralPull(body, hard, dt);

        Assert.IsTrue(MathF.Abs(body.PosX) < 2f,
            $"expected X pulled toward 0, got X={body.PosX}");
        // Should not be dragged all the way to hard.Z in 1s (only lateral).
        Assert.IsTrue(body.PosZ < 11.5f,
            $"longitudinal should stay mostly free, Z={body.PosZ}");
    }

    /// <summary>
    /// When facing is off-path, path-heading assist + thr must reorient toward +Z aim
    /// while planar velocity stays mostly along the nose (no pure sideways slide / clock-spin).
    /// </summary>
    [TestMethod]
    public void Apply_VelocityAlignsWithFacing_NotLateralSlide()
    {
        var vehicle = CreateNpcVehicle();
        // Face +X (yaw = +π/2); aim still along path +Z so axes must request a turn.
        var yawEast = MathF.PI * 0.5f;
        var eastRot = TerrainContactPlane.YawOnly(yawEast);
        // Seed facing +X with planar speed so heading assist is not speed-gated off.
        vehicle.ApplyServerMove(new Vector3(0f, 1f, 0f), eastRot, new Vector3(8f, 0f, 0f), 0f);

        var path = StraightPath();
        const float dt = 1f / 60f;
        PathStepResult result = default;
        var sawSteer = false;
        for (var i = 0; i < 90; i++)
        {
            var hard = HardCruiseAlongZ(vehicle.Position.Z, hardSpeed: 10f, dt);
            result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);
            if (MathF.Abs(result.Steering) > 0.2f)
                sawSteer = true;
            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);
        }

        Assert.IsTrue(sawSteer,
            "expected non-trivial steer axis while facing off the path aim");

        var yaw = VehicleDriveInputs.YawFromQuaternion(result.Rotation);
        // Path is +Z; heading should have moved off pure east (π/2) toward 0.
        Assert.IsTrue(MathF.Abs(yaw) < MathF.PI * 0.35f || MathF.Abs(yaw - 0f) < 1.2f,
            $"expected reorient toward path +Z, yaw={yaw}");

        var speed = MathF.Sqrt(result.Velocity.X * result.Velocity.X + result.Velocity.Z * result.Velocity.Z);
        if (speed > 0.5f)
        {
            // Forward from yaw: (sin yaw, cos yaw) on XZ.
            var fx = MathF.Sin(yaw);
            var fz = MathF.Cos(yaw);
            var align = (result.Velocity.X * fx + result.Velocity.Z * fz) / speed;
            Assert.IsTrue(align > 0.5f,
                $"velocity should be mostly along facing, align={align} v=({result.Velocity.X},{result.Velocity.Z})");
        }
    }

    [TestMethod]
    public void ApplyPathHeadingAssist_AtSpeed_YawsTowardAim()
    {
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            InvInertiaX = 1f, InvInertiaY = 1f, InvInertiaZ = 1f,
            PosX = 0f, PosY = 1f, PosZ = 0f,
            LinVelZ = 8f, // moving +Z enough for full speed scale... but facing +X
        };
        // Face +X
        var east = TerrainContactPlane.YawOnly(MathF.PI * 0.5f);
        body.QuatX = east.X; body.QuatY = east.Y; body.QuatZ = east.Z; body.QuatW = east.W;
        body.LinVelX = 8f; body.LinVelZ = 0f;

        var aim = new Vector3(0f, 1f, 20f); // along +Z
        const float dt = 1f / 60f;
        float yaw0 = VehicleDriveInputs.YawFromQuaternion(new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW));
        for (var i = 0; i < 45; i++)
            NpcVehiclePhysicsController.ApplyPathHeadingAssist(body, aim, dt);

        float yaw1 = VehicleDriveInputs.YawFromQuaternion(new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW));
        // From +π/2 toward 0 (path +Z).
        Assert.IsTrue(MathF.Abs(yaw1) < MathF.Abs(yaw0) - 0.1f,
            $"expected yaw move toward 0 from {yaw0}, got {yaw1}");
    }

    [TestMethod]
    public void ApplyPathHeadingAssist_NearlyStopped_DoesNotClockSpin()
    {
        var body = new HkRigidBody
        {
            Mass = 1f, InvMass = 1f,
            InvInertiaX = 1f, InvInertiaY = 1f, InvInertiaZ = 1f,
            PosX = 0f, PosY = 1f, PosZ = 0f,
            LinVelX = 0f, LinVelZ = 0.05f, // crawl — below min speed
            AngVelY = 3f, // residual spin
            QuatW = 1f,
        };
        var aim = new Vector3(10f, 1f, 10f);
        const float dt = 1f / 60f;
        for (var i = 0; i < 30; i++)
            NpcVehiclePhysicsController.ApplyPathHeadingAssist(body, aim, dt);

        Assert.IsTrue(MathF.Abs(body.AngVelY) < 0.15f,
            $"expected residual yaw killed when nearly stopped, AngVelY={body.AngVelY}");
    }

    // -------------------------------------------------------------------------
    // D2 sim-authority contracts
    // -------------------------------------------------------------------------

    /// <summary>
    /// Published pose/vel/angVel must equal <c>inst.Body</c> after Step + path-heading assist
    /// (no separate kinematic pose force-restore that diverges from the body).
    /// </summary>
    [TestMethod]
    public void Apply_PublishesSimPoseVerbatim()
    {
        var vehicle = CreateNpcVehicle();
        // Start facing +X so Step + reorient produces non-zero sim angular velocity once free-running.
        vehicle.ApplyServerMove(
            new Vector3(0f, 1f, 0f),
            TerrainContactPlane.YawOnly(MathF.PI * 0.5f),
            default,
            0f);

        var path = StraightPath();
        const float dt = 1f / 60f;
        PathStepResult result = default;
        for (var i = 0; i < 30; i++)
        {
            var hard = HardCruiseAlongZ(vehicle.Position.Z, hardSpeed: 10f, dt);
            result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);
            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);
        }

        Assert.IsNotNull(vehicle.PhysicsInstance);
        var body = vehicle.PhysicsInstance.Body;

        // Pose / linear velocity published verbatim from the sim body (post-assist).
        Assert.AreEqual(body.PosX, result.NewPosition.X, 1e-4f);
        Assert.AreEqual(body.PosY, result.NewPosition.Y, 1e-4f);
        Assert.AreEqual(body.PosZ, result.NewPosition.Z, 1e-4f);
        Assert.AreEqual(body.LinVelX, result.Velocity.X, 1e-4f);
        Assert.AreEqual(body.LinVelY, result.Velocity.Y, 1e-4f);
        Assert.AreEqual(body.LinVelZ, result.Velocity.Z, 1e-4f);
        Assert.AreEqual(body.QuatX, result.Rotation.X, 1e-4f);
        Assert.AreEqual(body.QuatY, result.Rotation.Y, 1e-4f);
        Assert.AreEqual(body.QuatZ, result.Rotation.Z, 1e-4f);
        Assert.AreEqual(body.QuatW, result.Rotation.W, 1e-4f);

        Assert.IsTrue(result.AngularVelocity.HasValue);
        var ang = result.AngularVelocity.Value;
        Assert.AreEqual(body.AngVelX, ang.X, 1e-4f);
        Assert.AreEqual(body.AngVelY, ang.Y, 1e-4f);
        Assert.AreEqual(body.AngVelZ, ang.Z, 1e-4f);
    }

    /// <summary>
    /// D2: first-create of the physics instance must seat the chassis on terrain via ReGround
    /// (not leave the body floating at the entity seed height forever).
    /// </summary>
    [TestMethod]
    public void Apply_SpawnSeatsOnTerrain()
    {
        var vehicle = CreateNpcVehicle();
        // Seed entity well above the flat fallback ground plane used when map is null.
        vehicle.ApplyServerMove(
            new Vector3(0f, 25f, 0f),
            Quaternion.Default,
            default,
            0f);

        var hard = new PathStepResult
        {
            NewPosition = vehicle.Position,
            Velocity = new Vector3(0f, 0f, 5f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };

        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, StraightPath(), nowMs: 0, dt: 1f / 60f, map: null, npcAi: vehicle.NpcAi);

        Assert.IsNotNull(vehicle.PhysicsInstance);
        var body = vehicle.PhysicsInstance.Body;
        // Flat fallback support is hard Y (=25) with map=null today; D2 first-create should
        // ReGround so chassis sits near the collision query ground under the spawn, not stay
        // mid-air if a heightfield is present. With null map the query plane is at supportY —
        // contract: body Y is within a small band of the published support / result Y after seat.
        Assert.AreEqual(result.NewPosition.Y, body.PosY, 1e-3f,
            "body must be seated at the published grounded pose");
        // After seat, vertical velocity must be quiet (no residual freefall from a float seed).
        Assert.IsTrue(MathF.Abs(body.LinVelY) < 1f,
            $"seated body should not keep freefall vy, got {body.LinVelY}");
        Assert.IsTrue(MathF.Abs(result.Velocity.Y) < 1f,
            $"published vy should be quiet after seat, got {result.Velocity.Y}");
    }

    /// <summary>
    /// D2: if the sim body falls out of world (PosY &lt; supportY − 50), recovery teleports to
    /// hard pose and ReGrounds rather than publishing the freefall forever.
    /// </summary>
    [TestMethod]
    public void Apply_RecoversWhenBodyFallsOutOfWorld()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        const float supportY = 1f;
        const float dt = 1f / 60f;

        // One frame to create the physics instance at a sane pose.
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, supportY, 0f),
            Velocity = new Vector3(0f, 0f, 8f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 0, dt: dt, map: null, npcAi: vehicle.NpcAi);
        vehicle.ApplyServerMove(
            result.NewPosition, result.Rotation, result.Velocity, dt,
            result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

        Assert.IsNotNull(vehicle.PhysicsInstance);
        var body = vehicle.PhysicsInstance.Body;

        // Drop the body deep below support (out-of-world threshold: supportY − 50).
        body.PosY = supportY - 80f;
        body.LinVelY = -40f;

        hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, supportY, 5f),
            Velocity = new Vector3(0f, 0f, 8f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 16, dt: dt, map: null, npcAi: vehicle.NpcAi);

        // Recovered body must be near the hard / support plane, not still at Y=support−80.
        Assert.IsTrue(body.PosY > supportY - 10f,
            $"expected out-of-world recovery to re-seat near support, body.Y={body.PosY}");
        Assert.AreEqual(result.NewPosition.Y, body.PosY, 1e-3f);
        Assert.IsTrue(MathF.Abs(body.LinVelY) < 5f,
            $"recovered body should not keep freefall vy, got {body.LinVelY}");
    }

    /// <summary>
    /// D2: when |body.Pos − hard.NewPosition| exceeds <see cref="NpcVehiclePhysicsController.ResyncDriftThreshold"/>,
    /// controller teleports the body to hard and ReGrounds (path divergence recovery).
    /// </summary>
    [TestMethod]
    public void Apply_DivergenceFromPath_TeleportsAndReGrounds()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        const float dt = 1f / 60f;
        const float hardZ = 10f;

        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, hardZ),
            Velocity = new Vector3(0f, 0f, 10f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 0, dt: dt, map: null, npcAi: vehicle.NpcAi);
        vehicle.ApplyServerMove(
            result.NewPosition, result.Rotation, result.Velocity, dt,
            result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

        Assert.IsNotNull(vehicle.PhysicsInstance);
        var body = vehicle.PhysicsInstance.Body;

        // Shove the sim body far off the path (beyond resync threshold).
        float thr = NpcVehiclePhysicsController.ResyncDriftThreshold;
        body.PosX = thr * 3f;
        body.PosZ = hardZ + thr * 3f;
        body.LinVelX = 20f;
        body.LinVelZ = -20f;

        hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, hardZ + 1f),
            Velocity = new Vector3(0f, 0f, 10f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 16, dt: dt, map: null, npcAi: vehicle.NpcAi);

        // After recovery, body must be near hard.NewPosition (teleport + ReGround), not still drifted.
        float dx = body.PosX - hard.NewPosition.X;
        float dz = body.PosZ - hard.NewPosition.Z;
        float planar = MathF.Sqrt(dx * dx + dz * dz);
        Assert.IsTrue(planar < thr,
            $"expected path-divergence teleport near hard, planar err={planar} thr={thr} body=({body.PosX},{body.PosZ})");
        Assert.AreEqual(result.NewPosition.X, body.PosX, 1e-3f);
        Assert.AreEqual(result.NewPosition.Z, body.PosZ, 1e-3f);

        // D3: controller recovery publish must NOT drop the physics instance (body already at new pose).
        vehicle.ApplyServerMove(
            result.NewPosition, result.Rotation, result.Velocity, dt,
            result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);
        Assert.IsNotNull(vehicle.PhysicsInstance,
            "path-divergence recovery publish must keep the instance (body already at hard pose)");
    }

    /// <summary>
    /// D3: continuous streaming ApplyServerMove (small delta) must keep the physics instance.
    /// </summary>
    [TestMethod]
    public void ApplyServerMove_ContinuousMove_KeepsPhysicsInstance()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        const float dt = 1f / 60f;

        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 2f),
            Velocity = new Vector3(0f, 0f, 8f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 0, dt: dt, map: null, npcAi: vehicle.NpcAi);
        vehicle.ApplyServerMove(
            result.NewPosition, result.Rotation, result.Velocity, dt,
            result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

        var before = vehicle.PhysicsInstance;
        Assert.IsNotNull(before);

        // Continuous stream: ~1 unit forward (well under discontinuity threshold).
        var continuous = new Vector3(
            result.NewPosition.X,
            result.NewPosition.Y,
            result.NewPosition.Z + 1f);
        vehicle.ApplyServerMove(
            continuous, result.Rotation, result.Velocity, dt,
            result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

        Assert.AreSame(before, vehicle.PhysicsInstance,
            "continuous ApplyServerMove must not drop the physics instance");
    }

    /// <summary>
    /// D3: discontinuous teleport via ApplyServerMove drops stale sim state so the next
    /// controller Apply recreates + ReGrounds (first-create seat path).
    /// </summary>
    [TestMethod]
    public void Apply_AfterDiscontinuousTeleport_RecreatesAndReGrounds()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        const float dt = 1f / 60f;

        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 2f),
            Velocity = new Vector3(0f, 0f, 10f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        var result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 0, dt: dt, map: null, npcAi: vehicle.NpcAi);
        vehicle.ApplyServerMove(
            result.NewPosition, result.Rotation, result.Velocity, dt,
            result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

        var stale = vehicle.PhysicsInstance;
        Assert.IsNotNull(stale);
        // Pollute stale body so a kept instance would be wrong after teleport.
        stale.Body.PosX = 999f;
        stale.Body.PosY = 50f;
        stale.Body.LinVelY = -40f;

        // Discontinuous external teleport (beyond PhysicsDiscontinuityDistance).
        float jump = Vehicle.PhysicsDiscontinuityDistance * 3f;
        var teleportPos = new Vector3(jump, 25f, jump);
        vehicle.ApplyServerMove(teleportPos, Quaternion.Default, default, 0f);

        Assert.IsNull(vehicle.PhysicsInstance,
            "discontinuous ApplyServerMove must ClearPhysicsInstance");
        Assert.AreEqual(teleportPos.X, vehicle.Position.X, 1e-4f);
        Assert.AreEqual(teleportPos.Z, vehicle.Position.Z, 1e-4f);

        hard = new PathStepResult
        {
            NewPosition = teleportPos,
            Velocity = new Vector3(0f, 0f, 8f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        result = NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 16, dt: dt, map: null, npcAi: vehicle.NpcAi);

        Assert.IsNotNull(vehicle.PhysicsInstance, "next Apply must recreate the physics instance");
        Assert.AreNotSame(stale, vehicle.PhysicsInstance, "must be a fresh instance, not the stale one");
        var body = vehicle.PhysicsInstance.Body;
        Assert.AreEqual(result.NewPosition.Y, body.PosY, 1e-3f,
            "recreated instance must be seated (first-create ReGround path)");
        Assert.IsTrue(MathF.Abs(body.LinVelY) < 1f,
            $"seated body should not keep freefall vy, got {body.LinVelY}");
        // Fresh seed from entity pose at teleport XZ, not the polluted 999.
        Assert.IsTrue(MathF.Abs(body.PosX - jump) < 5f,
            $"expected body near teleport X, got {body.PosX}");
    }

    /// <summary>
    /// D3: SetPosition (direct discontinuous reposition) clears sim state.
    /// </summary>
    [TestMethod]
    public void SetPosition_Discontinuous_ClearsPhysicsInstance()
    {
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 2f),
            Velocity = new Vector3(0f, 0f, 5f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        NpcVehiclePhysicsController.Apply(
            hard, vehicle, path, nowMs: 0, dt: 1f / 60f, map: null, npcAi: vehicle.NpcAi);
        Assert.IsNotNull(vehicle.PhysicsInstance);

        float jump = Vehicle.PhysicsDiscontinuityDistance * 3f;
        vehicle.SetPosition(new Vector3(jump, 1f, jump));

        Assert.IsNull(vehicle.PhysicsInstance);
        Assert.AreEqual(jump, vehicle.Position.X, 1e-4f);
        Assert.AreEqual(jump, vehicle.Position.Z, 1e-4f);
    }

    /// <summary>
    /// D3: death drops physics instance (player and NPC vehicle paths both call Clear).
    /// </summary>
    [TestMethod]
    public void OnDeath_ClearsPhysicsInstance()
    {
        var vehicle = CreateNpcVehicle();
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 1f, 2f),
            Velocity = new Vector3(0f, 0f, 5f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            NewDirection = 1,
        };
        NpcVehiclePhysicsController.Apply(
            hard, vehicle, StraightPath(), nowMs: 0, dt: 1f / 60f, map: null, npcAi: vehicle.NpcAi);
        Assert.IsNotNull(vehicle.PhysicsInstance);

        // Player-vehicle death path (NpcAi null): still clears.
        vehicle.NpcAi = null;
        vehicle.OnDeath(DeathType.Silent);

        Assert.IsNull(vehicle.PhysicsInstance, "OnDeath must ClearPhysicsInstance");
        Assert.IsTrue(vehicle.IsCorpse);
    }
}
