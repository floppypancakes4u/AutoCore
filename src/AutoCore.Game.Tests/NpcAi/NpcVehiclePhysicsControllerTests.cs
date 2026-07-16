using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Npc;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Controller contracts for the physics-tier NPC vehicle mover.
/// D1 rewrites motion tests for sim-driven tolerances and documents D2 sim-authority
/// contracts as <c>[Ignore("D2")]</c> until the hybrid force-restore rewrite lands.
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

        // ~1s of cruise: expect clear progress along the path, not a crawl freeze.
        Assert.IsTrue(pos.Z > 2f,
            $"expected path progress along +Z, got Z={pos.Z}");
        // Bounded lateral slip (path is X=0).
        Assert.IsTrue(MathF.Abs(pos.X) < 4f,
            $"lateral slip out of bound: X={pos.X}");

        var speed = MathF.Sqrt(vehicle.Velocity.X * vehicle.Velocity.X + vehicle.Velocity.Z * vehicle.Velocity.Z);
        Assert.IsTrue(speed > 1f,
            $"expected non-trivial planar speed under sim-driven motion, got {speed}");
    }

    /// <summary>
    /// Sim-driven reorient: when facing is off-path, yaw toward the aim and keep velocity
    /// mostly along the nose with bounded lateral slip (no pure sideways slide).
    /// </summary>
    [TestMethod]
    public void Apply_VelocityAlignsWithFacing_NotLateralSlide()
    {
        var vehicle = CreateNpcVehicle();
        // Face +X (yaw = +π/2); aim still along path +Z so we must reorient.
        var yawEast = MathF.PI * 0.5f;
        var eastRot = TerrainContactPlane.YawOnly(yawEast);
        vehicle.ApplyServerMove(new Vector3(0f, 1f, 0f), eastRot, default, 0f);

        var path = StraightPath();
        const float dt = 1f / 60f;
        PathStepResult result = default;
        for (var i = 0; i < 90; i++)
        {
            var hard = HardCruiseAlongZ(vehicle.Position.Z, hardSpeed: 10f, dt);
            result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);
            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);
        }

        var yaw = VehicleDriveInputs.YawFromQuaternion(result.Rotation);
        // Should be facing roughly +Z (yaw ~ 0), not stuck facing +X. Looser for sim yaw rate.
        Assert.IsTrue(MathF.Abs(yaw) < 0.85f,
            $"expected reorient toward path +Z, yaw={yaw}");

        var speed = MathF.Sqrt(result.Velocity.X * result.Velocity.X + result.Velocity.Z * result.Velocity.Z);
        if (speed > 0.5f)
        {
            // Forward from yaw: (sin yaw, cos yaw) on XZ.
            var fx = MathF.Sin(yaw);
            var fz = MathF.Cos(yaw);
            var align = (result.Velocity.X * fx + result.Velocity.Z * fz) / speed;
            Assert.IsTrue(align > 0.7f,
                $"velocity should be mostly along facing, align={align} v=({result.Velocity.X},{result.Velocity.Z})");

            // Lateral slip = planar component orthogonal to facing.
            var rx = fz;
            var rz = -fx;
            var lateral = MathF.Abs(result.Velocity.X * rx + result.Velocity.Z * rz) / speed;
            Assert.IsTrue(lateral < 0.55f,
                $"bounded lateral slip expected, lateral={lateral} v=({result.Velocity.X},{result.Velocity.Z})");
        }
    }

    // -------------------------------------------------------------------------
    // D2 sim-authority contracts (Ignore until controller rewrite lands)
    // -------------------------------------------------------------------------

    /// <summary>
    /// D2: published pose/vel/angVel must equal <c>inst.Body</c> after <c>Step</c> with no
    /// kinematic force-restore. Today the hybrid zeros body angVel and rewrites pose from
    /// authored navigation, so published AngularVelocity ≠ body angVel during reorient.
    /// </summary>
    [TestMethod]
    [Ignore("D2: sim-authoritative publish blocked by hybrid force-restore of body pose/angVel")]
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

        // Pose / linear velocity published verbatim from the sim body.
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

        // Angular velocity must come from the free-running body (not kinematic dYaw with body zeroed).
        Assert.IsTrue(result.AngularVelocity.HasValue);
        var ang = result.AngularVelocity.Value;
        Assert.AreEqual(body.AngVelX, ang.X, 1e-4f,
            "published angVel.X must match sim body (no force-zero)");
        Assert.AreEqual(body.AngVelY, ang.Y, 1e-4f,
            "published angVel.Y must match sim body (no kinematic yaw-rate substitute)");
        Assert.AreEqual(body.AngVelZ, ang.Z, 1e-4f,
            "published angVel.Z must match sim body (no force-zero)");
    }

    /// <summary>
    /// D2: first-create of the physics instance must seat the chassis on terrain via ReGround
    /// (not leave the body floating at the entity seed height forever).
    /// </summary>
    [TestMethod]
    [Ignore("D2: first-create ReGround / seat-on-terrain not yet wired (hybrid seeds authored pose only)")]
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
    [Ignore("D2: out-of-world recovery (SetPose(hard)+ReGround) not yet implemented")]
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
    [Ignore("D2: path-divergence teleport+ReGround not yet implemented (hybrid uses entity sync only)")]
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
    }
}
