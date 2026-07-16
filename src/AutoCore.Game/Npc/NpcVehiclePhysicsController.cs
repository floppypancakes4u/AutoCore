namespace AutoCore.Game.Npc;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Physics-tier mover for path-following NPC vehicles (sim-authoritative).
/// <para>
/// Steers toward the hard navigator's look-ahead aim via the retail
/// <see cref="VehicleDriveController"/> axes, steps the Havok vehicle sim, and
/// publishes <c>inst.Body</c> pose/velocity/angular velocity verbatim. No
/// kinematic force-restore of pose; recovery teleports + <see cref="VehiclePhysicsInstance.ReGround"/>
/// only on first-create, non-finite state, out-of-world fall, or path divergence.
/// </para>
/// <para>
/// Opt-in only via <see cref="ServerConfig"/> (enabled + controllerTier=physics).
/// Reverse throttle is a service-brake pedal only (C8) — this controller must not
/// also apply kinematic reverse-throttle deceleration.
/// </para>
/// </summary>
public static class NpcVehiclePhysicsController
{
    /// <summary>
    /// Max |body.Pos − hard.NewPosition| before path-divergence recovery (world units).
    /// </summary>
    public static float ResyncDriftThreshold { get; set; } = 8f;

    /// <summary>Y drop below terrain support that triggers out-of-world recovery.</summary>
    public const float OutOfWorldSupportMargin = 50f;

    /// <summary>
    /// When body-up · world-up falls below this, treat as flipped and recover
    /// (teleport to hard pose + ReGround). Keeps NPCs from thrashing indefinitely.
    /// </summary>
    public const float MinUprightDot = 0.35f;

    /// <summary>
    /// Advance one vehicle via sim-authoritative path-following.
    /// On failure (no data / bad dt) returns <paramref name="hard"/> unchanged (fail closed).
    /// </summary>
    public static PathStepResult Apply(
        PathStepResult hard,
        Vehicle vehicle,
        MapPathTemplate path,
        long nowMs,
        float dt,
        SectorMap map,
        NpcAiState npcAi)
    {
        if (vehicle == null || path == null || path.Points.Count == 0 || dt <= 0f || !float.IsFinite(dt))
            return hard;

        bool firstCreate = vehicle.PhysicsInstance == null;
        var inst = vehicle.GetOrCreatePhysicsInstance();
        if (inst == null)
        {
            if (ServerConfig.DebugLogging)
            {
                Logger.WriteLog(LogType.Error,
                    $"NpcVehiclePhysics: no HkVehicleData for cbid={vehicle.CBID}; fail closed to hard path");
            }
            return hard;
        }

        if (hard.Arrived && hard.WaitUntilMs > nowMs)
        {
            return new PathStepResult
            {
                NewPosition = vehicle.Position,
                Velocity = default,
                Rotation = vehicle.Rotation,
                NewIndex = hard.NewIndex,
                NewDirection = hard.NewDirection,
                Arrived = true,
                FireReactionCoid = hard.FireReactionCoid,
                WaitUntilMs = hard.WaitUntilMs,
                NowReversing = hard.NowReversing,
                Throttle = 0f,
                Steering = 0f,
                SharpTurn = 1,
                HasDriveInputs = true,
                AngularVelocity = default,
            };
        }

        var body = inst.Body;
        float supportY = ResolveSupportY(map, body.PosX, body.PosZ, hard.NewPosition.Y);
        IVehicleCollisionQuery query = BuildCollisionQuery(map, supportY, excludeSelf: vehicle);

        // Recovery: first-create seats; non-finite / freefall / path-divergence teleport + ReGround.
        // ReGround snaps the chassis origin onto the terrain hit; raise so wheel hardpoints sit
        // near suspension rest (origin-on-terrain fully compresses and launches / starves drive).
        if (firstCreate)
        {
            // GetOrCreate already seeded pose from the entity; seat wheels on terrain.
            inst.ReGround(query);
            RaiseChassisToWheelRest(inst);
        }
        else if (NeedsRecovery(body, hard, supportY))
        {
            SetPoseFromHard(inst, hard);
            // Re-resolve support / query under the hard XZ after teleport.
            supportY = ResolveSupportY(map, body.PosX, body.PosZ, hard.NewPosition.Y);
            query = BuildCollisionQuery(map, supportY, excludeSelf: vehicle);
            inst.ReGround(query);
            RaiseChassisToWheelRest(inst);
        }

        var lane = npcAi?.PathLaneOffset ?? 0f;
        var bodyPos = new Vector3(body.PosX, body.PosY, body.PosZ);
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(bodyPos, hard, path, lane);

        // Drive axes from current sim body basis/velocity (retail 0x4fc650).
        var bodyRot = new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW);
        ExtractBasis(bodyRot, out var right, out var forward);
        var velocity = new Vector3(body.LinVelX, body.LinVelY, body.LinVelZ);
        var acceptDist = ResolveAcceptDistance(hard, path);
        var cornerScale = ResolveCruiseScale(hard, path);
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            bodyPos,
            right,
            forward,
            velocity,
            aim,
            acceptDist,
            cruiseScale: cornerScale > 0f ? cornerScale : 1f,
            allowReverse: true,
            alwaysDrive: false);

        // Step free-running; do not force-restore pose after. C8 brake owns reverse thr as pedal —
        // no kinematic reverse-throttle deceleration here.
        inst.Step(thr, steer, handbrake: sharp != 0, dt, query);

        // Publish inst.Body pose/rot/vel/angVel verbatim.
        return new PathStepResult
        {
            NewPosition = new Vector3(body.PosX, body.PosY, body.PosZ),
            Velocity = new Vector3(body.LinVelX, body.LinVelY, body.LinVelZ),
            Rotation = new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            NewIndex = hard.NewIndex,
            NewDirection = hard.NewDirection,
            Arrived = hard.Arrived,
            FireReactionCoid = hard.FireReactionCoid,
            WaitUntilMs = hard.WaitUntilMs,
            NowReversing = hard.NowReversing,
            Throttle = thr,
            Steering = steer,
            SharpTurn = sharp,
            HasDriveInputs = true,
            AngularVelocity = new Vector3(body.AngVelX, body.AngVelY, body.AngVelZ),
        };
    }

    /// <summary>
    /// True when the free-running body is non-finite, flipped, fell out of world, or drifted
    /// past the path-divergence threshold from the hard navigator.
    /// </summary>
    internal static bool NeedsRecovery(HkRigidBody body, PathStepResult hard, float supportY)
    {
        if (body == null)
            return true;

        if (!IsBodyFinite(body))
            return true;

        if (body.PosY < supportY - OutOfWorldSupportMargin)
            return true;

        // Flipped / on roof — sim alone rarely self-rights under path thrash; re-seat on path.
        if (BodyUpDotWorldUp(body) < MinUprightDot)
            return true;

        float thr = ResyncDriftThreshold;
        if (thr > 0f)
        {
            float dx = body.PosX - hard.NewPosition.X;
            float dy = body.PosY - hard.NewPosition.Y;
            float dz = body.PosZ - hard.NewPosition.Z;
            if ((dx * dx) + (dy * dy) + (dz * dz) > thr * thr)
                return true;
        }

        return false;
    }

    /// <summary>Body +Y axis · world +Y (1 = upright, −1 = inverted).</summary>
    internal static float BodyUpDotWorldUp(HkRigidBody body)
    {
        // Rotate local (0,1,0) by body quat → world up.
        float x = body.QuatX, y = body.QuatY, z = body.QuatZ, w = body.QuatW;
        // up = q * (0,1,0) * q^-1 ; Y component of rotated unit Y:
        // 1 - 2(x² + z²) for the world-Y of body-up, or expanded:
        float upY = 1f - 2f * (x * x + z * z);
        return upY;
    }

    internal static bool IsBodyFinite(HkRigidBody body)
    {
        return float.IsFinite(body.PosX) && float.IsFinite(body.PosY) && float.IsFinite(body.PosZ)
            && float.IsFinite(body.QuatX) && float.IsFinite(body.QuatY)
            && float.IsFinite(body.QuatZ) && float.IsFinite(body.QuatW)
            && float.IsFinite(body.LinVelX) && float.IsFinite(body.LinVelY) && float.IsFinite(body.LinVelZ)
            && float.IsFinite(body.AngVelX) && float.IsFinite(body.AngVelY) && float.IsFinite(body.AngVelZ);
    }

    internal static void SetPoseFromHard(VehiclePhysicsInstance inst, PathStepResult hard)
    {
        var r = hard.Rotation;
        // Quaternion default is all-zero on some paths; keep a valid identity if unset.
        float qw = r.W;
        float qx = r.X;
        float qy = r.Y;
        float qz = r.Z;
        if (!float.IsFinite(qw) || !float.IsFinite(qx) || !float.IsFinite(qy) || !float.IsFinite(qz)
            || (qx * qx + qy * qy + qz * qz + qw * qw) < 1e-8f)
        {
            qx = qy = qz = 0f;
            qw = 1f;
        }

        inst.SetPose(
            hard.NewPosition.X, hard.NewPosition.Y, hard.NewPosition.Z,
            qx, qy, qz, qw);
    }

    /// <summary>
    /// After <see cref="VehiclePhysicsInstance.ReGround"/> (origin on terrain), lift the chassis
    /// so each wheel hardpoint is approximately <c>radius + restLength</c> above the ground plane.
    /// Without this the suspension is fully compressed on the recovery frame (launch / no grip).
    /// </summary>
    internal static void RaiseChassisToWheelRest(VehiclePhysicsInstance inst)
    {
        if (inst?.Data?.Wheels == null || inst.Data.Wheels.Count == 0)
            return;

        float raise = 0f;
        foreach (var w in inst.Data.Wheels)
        {
            // bodyY = ground − hardpointY + radius + rest  (ground already applied by ReGround)
            float r = -w.HardpointY + w.Radius + w.SuspensionRestLength;
            if (r > raise)
                raise = r;
        }

        if (raise > 0f && float.IsFinite(raise))
            inst.Body.PosY += raise;
    }

    /// <summary>
    /// Terrain heightfield query, optionally wrapped in <see cref="CompositeVehicleCollisionQuery"/>
    /// when <see cref="ServerConfig.CompositeWheelCollisionEnabled"/> is on (CW; default off).
    /// </summary>
    /// <param name="excludeSelf">Casting vehicle — skipped by the object pass so wheels miss own skirt.</param>
    internal static IVehicleCollisionQuery BuildCollisionQuery(
        SectorMap map,
        float fallbackGroundY,
        ClonedObjectBase excludeSelf = null)
    {
        var field = map?.MapData?.Heightfield;
        IVehicleCollisionQuery terrain;
        if (field != null)
        {
            terrain = new TerrainHeightfieldCollisionQuery(field);
        }
        else
        {
            // No map heightfield: flat plane at support / hard Y.
            float groundY = fallbackGroundY;
            terrain = new TerrainHeightfieldCollisionQuery(
                (float x, float z, out float y) =>
                {
                    y = groundY;
                    return true;
                });
        }

        if (ServerConfig.CompositeWheelCollisionEnabled && map != null)
            return new CompositeVehicleCollisionQuery(terrain, map, excludeSelf);

        return terrain;
    }

    /// <summary>
    /// Terrain sample under (x,z) when available; else hard-path Y as support reference.
    /// </summary>
    internal static float ResolveSupportY(SectorMap map, float x, float z, float hardY)
    {
        var field = map?.MapData?.Heightfield;
        if (field != null && field.TrySample(x, z, out var hy))
            return hy;
        return hardY;
    }

    internal static float ResolveTerrainSupportY(
        MapTerrainHeightfield field, float x, float z, float fallbackY)
    {
        if (field != null && field.TrySample(x, z, out var hy))
            return hy;
        return fallbackY;
    }

    private static float ResolveAcceptDistance(PathStepResult hard, MapPathTemplate path)
    {
        var n = path.Points.Count;
        if (n == 0)
            return 3f;
        var idx = Math.Clamp(hard.NewIndex, 0, n - 1);
        var ad = path.Points[idx].AcceptDistance;
        return ad > 0f ? ad : 3f;
    }

    private static float ResolveCruiseScale(PathStepResult hard, MapPathTemplate path)
    {
        var n = path.Points.Count;
        if (n < 3)
            return 1f;
        var idx = Math.Clamp(hard.NewIndex, 0, n - 1);
        var prev = path.Points[(idx - 1 + n) % n].Position;
        var cur = path.Points[idx].Position;
        var next = path.Points[(idx + 1) % n].Position;
        return PathCurvature.SpeedScale(PathCurvature.Radius(prev, cur, next));
    }

    internal static void ExtractBasis(Quaternion q, out Vector3 right, out Vector3 forward)
    {
        right = Rotate(q, 1f, 0f, 0f);
        forward = Rotate(q, 0f, 0f, 1f);
    }

    private static Vector3 Rotate(Quaternion q, float vx, float vy, float vz)
    {
        float qx = q.X, qy = q.Y, qz = q.Z, qw = q.W;
        float tx = 2f * (qy * vz - qz * vy);
        float ty = 2f * (qz * vx - qx * vz);
        float tz = 2f * (qx * vy - qy * vx);
        return new Vector3(
            vx + qw * tx + (qy * tz - qz * ty),
            vy + qw * ty + (qz * tx - qx * tz),
            vz + qw * tz + (qx * ty - qy * tx));
    }
}
