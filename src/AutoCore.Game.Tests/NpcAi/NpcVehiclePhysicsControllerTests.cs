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
// HkPhysicsConstants used by airborne clearance assertions.

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
    }

    [TestCleanup]
    public void TearDown()
    {
        ServerConfig.ResetToDefaults();
        HkVehicleDataCache.Clear();
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
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

    [TestMethod]
    public void Apply_PathCruise_AdvancesAlongPathAtNearHardSpeed()
    {
        // Live bug: pure reduced-physics crawl (~walking-NPC speed) while path hard target runs away.
        var vehicle = CreateNpcVehicle();
        var path = StraightPath();
        const float hardSpeed = 12f;
        const float dt = 1f / 60f;
        var pos = vehicle.Position;
        var rot = vehicle.Rotation;

        for (var i = 0; i < 60; i++)
        {
            // Hard navigator advances along +Z at cruise speed.
            var hard = new PathStepResult
            {
                NewPosition = new Vector3(0f, 1f, pos.Z + hardSpeed * dt),
                Velocity = new Vector3(0f, 0f, hardSpeed),
                Rotation = Quaternion.Default,
                NewIndex = 1,
                NewDirection = 1,
                Arrived = false,
            };

            var result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: 1000 + i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);

            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);

            pos = result.NewPosition;
            rot = result.Rotation;
        }

        // ~1s at 12 u/s → expect substantial progress (allow accel ramp).
        Assert.IsTrue(pos.Z > 6f,
            $"expected path-paced progress along +Z, got Z={pos.Z}");
        var speed = MathF.Sqrt(vehicle.Velocity.X * vehicle.Velocity.X + vehicle.Velocity.Z * vehicle.Velocity.Z);
        Assert.IsTrue(speed > 6f,
            $"expected near-cruise planar speed, got {speed}");
    }

    [TestMethod]
    public void Apply_VelocityAlignsWithFacing_NotLateralSlide()
    {
        // Live bug: body yaw drifts while XZ is pulled along path → "sideways" slide.
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
            var hard = new PathStepResult
            {
                NewPosition = new Vector3(0f, 1f, vehicle.Position.Z + 10f * dt),
                Velocity = new Vector3(0f, 0f, 10f),
                NewIndex = 1,
                NewDirection = 1,
            };
            result = NpcVehiclePhysicsController.Apply(
                hard, vehicle, path, nowMs: i * 16, dt: dt, map: null, npcAi: vehicle.NpcAi);
            vehicle.ApplyServerMove(
                result.NewPosition, result.Rotation, result.Velocity, dt,
                result.Throttle, result.Steering, result.SharpTurn, result.AngularVelocity);
        }

        var yaw = VehicleDriveInputs.YawFromQuaternion(result.Rotation);
        // Should be facing roughly +Z (yaw ~ 0), not stuck facing +X.
        Assert.IsTrue(MathF.Abs(yaw) < 0.6f,
            $"expected reorient toward path +Z, yaw={yaw}");

        var speed = MathF.Sqrt(result.Velocity.X * result.Velocity.X + result.Velocity.Z * result.Velocity.Z);
        if (speed > 0.5f)
        {
            // Forward from yaw: (sin yaw, cos yaw) on XZ.
            var fx = MathF.Sin(yaw);
            var fz = MathF.Cos(yaw);
            var align = (result.Velocity.X * fx + result.Velocity.Z * fz) / speed;
            Assert.IsTrue(align > 0.85f,
                $"velocity should be along facing, align={align} v=({result.Velocity.X},{result.Velocity.Z})");
        }
    }

    [TestMethod]
    public void IntegrateVertical_OnFlat_StaysGroundedAtRideHeight()
    {
        const float ride = 0.5f;
        const float support = 10f;
        const float dt = 1f / 60f;
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: support + ride,
            prevVy: 0f,
            supportY: support,
            prevSupportY: support,
            landSupportY: support,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsTrue(grounded);
        Assert.AreEqual(support + ride, y, 1e-3f);
        Assert.AreEqual(0f, vy, 1e-2f);
    }

    [TestMethod]
    public void IntegrateVertical_ContinuousSlopeClimb_StaysPlanted_EvenWithHighPrevVy()
    {
        // Continuous grade never drops support → must plant, not free-fly.
        const float ride = 0.5f;
        const float dt = 1f / 60f;
        float prevSupport = 10f;
        float newSupport = 10.2f;
        float prevY = prevSupport + ride;

        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: prevY,
            prevVy: 40f,
            supportY: newSupport,
            prevSupportY: prevSupport,
            landSupportY: newSupport,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsTrue(grounded, "continuous slope must stay planted");
        Assert.AreEqual(newSupport + ride, y, 1e-3f);
        Assert.IsTrue(vy <= 15f + 1e-3f, $"contact vy capped, got {vy}");
        Assert.IsTrue(vy >= 0f, "climbing grade keeps non-negative contact vy");
    }

    [TestMethod]
    public void IntegrateVertical_CenterContinuous_StaysPlanted_DoesNotFloatUp()
    {
        // Live bug: false ramp-lip on turns injected upward velocity while center support was fine.
        // Center continuous + body on ground → plant at terrain, zero hop.
        const float support = 20f;
        const float dt = 1f / 60f;
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: support,
            prevVy: 0f,
            supportY: support,
            prevSupportY: support,
            landSupportY: support,
            rideHeight: 0f,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsTrue(grounded);
        Assert.AreEqual(support, y, 1e-4f);
        Assert.AreEqual(0f, vy, 1e-3f);
    }

    [TestMethod]
    public void IntegrateVertical_RampLip_BallisticImmediateWithGravity()
    {
        // Center surface drops hard under the chassis → airborne same tick with gravity on vy.
        const float ride = 0.5f;
        const float dt = 1f / 60f;
        float prevSupport = 20f;
        float newSupport = 10f;
        float prevY = prevSupport + ride;
        float prevVy = 8f;

        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: prevY,
            prevVy: prevVy,
            supportY: newSupport,
            prevSupportY: prevSupport,
            landSupportY: newSupport,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsFalse(grounded, "ramp lip must free-fly immediately");
        Assert.IsTrue(y > newSupport + ride + 0.5f, $"y={y}");
        Assert.IsTrue(vy < prevVy, "gravity applies on the first airborne frame");
        Assert.IsTrue(vy > 0f, "still climbing for a moment after lip");
        // Must use caller's prevVy only — no artificial hop boost.
        float expectedVy = prevVy + (-9.81f) * dt;
        Assert.AreEqual(expectedVy, vy, 1e-4f);
    }

    [TestMethod]
    public void IntegrateVertical_CenterDropsAtLip_BallisticFromContactVy_NotFrontPlant()
    {
        // When center crosses the lip, free-fly from deck Y + contact climb rate.
        // Do not plant onto the low post-lip sample while still above it.
        const float dt = 1f / 60f;
        float prevSupport = 20f;
        float centerAfterLip = 10f;
        float prevY = prevSupport;
        float prevVy = 6f; // contact climb carried off the crest

        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: prevY,
            prevVy: prevVy,
            supportY: centerAfterLip,
            prevSupportY: prevSupport,
            landSupportY: centerAfterLip,
            rideHeight: 0f,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsFalse(grounded);
        Assert.IsTrue(y > centerAfterLip + 1f, $"must not plant on post-lip ground; y={y}");
        Assert.AreEqual(prevVy + (-9.81f) * dt, vy, 1e-4f);
    }

    [TestMethod]
    public void IntegrateVertical_MultiFrameFreeFall_MatchesFullGravity()
    {
        // Live bug: ramps felt like ~10% gravity because upward re-injection fought free-fall.
        // Full g from rest over 0.5s drops ~1.23u; 10% g would drop ~0.12u.
        const float g = -9.81f;
        const float dt = 1f / 60f;
        const int frames = 30; // 0.5s
        float y = 30f;
        float vy = 0f;
        float support = 0f;

        for (int i = 0; i < frames; i++)
        {
            NpcVehiclePhysicsController.IntegrateVertical(
                prevY: y,
                prevVy: vy,
                supportY: support,
                prevSupportY: support,
                landSupportY: support,
                rideHeight: 0f,
                dt: dt,
                gravityY: g,
                maxContactVy: 15f,
                maxStickDrop: 0.45f,
                out y, out vy, out var grounded);
            Assert.IsFalse(grounded, $"still airborne at frame {i}, y={y}");
        }

        float t = frames * dt;
        // Per-frame: y += vy*dt + 0.5*g*dt^2; vy += g*dt  → kinematic free-fall for constant g.
        float expectedY = 30f + 0.5f * g * t * t;
        float expectedVy = g * t;
        Assert.AreEqual(expectedY, y, 0.05f, $"y={y} expected~{expectedY} (full g free-fall)");
        Assert.AreEqual(expectedVy, vy, 0.05f, $"vy={vy}");
        float dropped = 30f - y;
        Assert.IsTrue(dropped > 1.0f, $"dropped only {dropped:F2}u — expected ~1.23u at full g");
        // 10% gravity would only drop ~0.12u in 0.5s.
        Assert.IsTrue(dropped > 0.5f * MathF.Abs(0.1f * g) * t * t * 2f,
            $"drop {dropped:F2} looks like weakened gravity");
    }

    [TestMethod]
    public void IntegrateVertical_GroundedPlant_IsTerrainY_NotRideOffset()
    {
        // Ghost pose must sit on the heightfield sample (soft path / SnapToTerrain), not +0.6 ride.
        const float support = 42f;
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: support,
            prevVy: 0f,
            supportY: support,
            prevSupportY: support,
            landSupportY: support,
            rideHeight: 0f,
            dt: 1f / 60f,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out _, out var grounded);

        Assert.IsTrue(grounded);
        Assert.AreEqual(support, y, 1e-4f);
    }

    [TestMethod]
    public void IntegrateVertical_Airborne_DoesNotSnapToGround()
    {
        const float ride = 0.5f;
        const float support = 0f;
        const float dt = 1f / 60f;
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: 15f,
            prevVy: 2f,
            supportY: support,
            prevSupportY: support,
            landSupportY: support,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsFalse(grounded);
        Assert.IsTrue(y > 14f, $"must not snap; y={y}");
        Assert.IsTrue(vy < 2f);
    }

    [TestMethod]
    public void IntegrateVertical_FallingIntoGround_Lands()
    {
        const float ride = 0.5f;
        const float support = 5f;
        const float dt = 1f / 60f;
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: support + ride + 0.05f,
            prevVy: -8f,
            supportY: support,
            prevSupportY: support,
            landSupportY: support,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsTrue(grounded);
        Assert.AreEqual(support + ride, y, 1e-3f);
        Assert.IsTrue(vy <= 0f);
    }

    [TestMethod]
    public void IntegrateVertical_CliffEdge_GoesAirborneSameTick()
    {
        const float ride = 0.5f;
        const float dt = 1f / 60f;
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: 50.5f,
            prevVy: 0f,
            supportY: 0f,
            prevSupportY: 50f,
            landSupportY: 0f,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsFalse(grounded);
        Assert.IsTrue(y > 49f, $"y={y}");
        Assert.IsTrue(vy < 0f, "gravity on first airborne frame");
    }

    [TestMethod]
    public void IntegrateVertical_GentleDownhill_StaysGrounded()
    {
        const float ride = 0.5f;
        const float dt = 1f / 60f;
        float prevSupport = 10f;
        float newSupport = 9.85f;

        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: prevSupport + ride,
            prevVy: -2f,
            supportY: newSupport,
            prevSupportY: prevSupport,
            landSupportY: newSupport,
            rideHeight: ride,
            dt: dt,
            gravityY: -9.81f,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out var y, out var vy, out var grounded);

        Assert.IsTrue(grounded);
        Assert.AreEqual(newSupport + ride, y, 1e-3f);
    }

    [TestMethod]
    public void IntegrateVertical_FloatingAboveTerrain_FallsWithGravity_DoesNotStayHovering()
    {
        // After a false hop, body sits above continuous ground — must free-fall, not re-boost.
        const float g = -9.81f;
        const float dt = 1f / 60f;
        const float support = 10f;
        float y = support + 3f;
        float vy = 0.5f;

        // First frame: gravity reduces vy (may still climb a hair if residual +vy).
        NpcVehiclePhysicsController.IntegrateVertical(
            prevY: y,
            prevVy: vy,
            supportY: support,
            prevSupportY: support,
            landSupportY: support,
            rideHeight: 0f,
            dt: dt,
            gravityY: g,
            maxContactVy: 15f,
            maxStickDrop: 0.45f,
            out y, out vy, out var grounded);

        Assert.IsFalse(grounded);
        Assert.AreEqual(0.5f + g * dt, vy, 1e-4f);
        Assert.IsTrue(vy < 0.5f, "must not re-boost upward velocity");

        // ~0.5s later: clearly below the hover height, still falling under full g.
        for (int i = 0; i < 30; i++)
        {
            NpcVehiclePhysicsController.IntegrateVertical(
                prevY: y,
                prevVy: vy,
                supportY: support,
                prevSupportY: support,
                landSupportY: support,
                rideHeight: 0f,
                dt: dt,
                gravityY: g,
                maxContactVy: 15f,
                maxStickDrop: 0.45f,
                out y, out vy, out grounded);
        }

        Assert.IsFalse(grounded);
        Assert.IsTrue(y < support + 2.0f, $"must fall from hover; y={y}");
        Assert.IsTrue(vy < 0f, "must be falling downward");
    }
}
