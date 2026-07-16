namespace AutoCore.Game.Npc;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Phase 5 physics mover for path-following NPC vehicles.
/// <para>
/// Planar navigation is <b>facing-aligned path motion</b> (yaw toward look-ahead, integrate
/// along the nose at cruise/hard speed). Vertical motion is center-heightfield stick vs
/// ballistic free-fall: plant on continuous terrain; when the center sample drops off a
/// lip/cliff, integrate full gravity from contact climb velocity only (no invented hops).
/// </para>
/// <para>
/// Havok still runs for thr/steer/sharp (client wire) and optional suspension bookkeeping.
/// Opt-in only via <see cref="ServerConfig"/> (enabled + controllerTier=physics).
/// </para>
/// </summary>
public static class NpcVehiclePhysicsController
{
    /// <summary>Planar drift (u) between entity pose and sim before force-resync.</summary>
    public static float ResyncDriftThreshold { get; set; } = 8f;

    /// <summary>
    /// Advance one vehicle with path-facing navigation + ballistic / grounded Y.
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

        MaybeResyncSim(vehicle, inst);

        var lane = npcAi?.PathLaneOffset ?? 0f;
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(
            vehicle.Position, hard, path, lane);

        // --- Planar path-facing navigation ---
        var prevYaw = VehicleDriveInputs.YawFromQuaternion(vehicle.Rotation);
        var desiredYaw = NpcVehicleDriveController.ResolveDesiredYaw(
            vehicle.Position, aim, hard, prevYaw);
        var newYaw = NpcVehicleDriveController.LimitYaw(prevYaw, desiredYaw, dt);

        var prevSpeed = NpcVehicleDriveController.XzSpeed(vehicle.Velocity);
        var hardSpeed = NpcVehicleDriveController.XzSpeed(hard.Velocity);
        var cornerScale = ResolveCruiseScale(hard, path);
        float cruise = hardSpeed > 0.05f
            ? hardSpeed
            : ResolveFallbackCruise(vehicle, inst.Data);
        var desiredSpeed = cruise * cornerScale;
        var rollingThrough = hard.Arrived && hard.WaitUntilMs <= nowMs;
        if (rollingThrough && prevSpeed < desiredSpeed * 0.85f)
            prevSpeed = desiredSpeed;
        var newSpeed = NpcVehicleDriveController.ApproachSpeed(prevSpeed, desiredSpeed, dt);

        // XZ integrate along facing (planar); Y handled separately.
        var pos = NpcVehicleDriveController.IntegrateFacingPosition(
            vehicle.Position, hard.NewPosition, newYaw, newSpeed, dt);

        // Server ghost Y is center heightfield only (same as SnapToTerrain / soft path).
        // Do NOT add wheel-radius ride clearance — that floats the chassis ~0.5–1u in-world.
        // Do NOT force-airborne from front/rear probes — turn samples create false "lips"
        // that re-injected upward velocity and made vehicles float.
        const float ride = 0f;
        var field = map?.MapData?.Heightfield;
        float supportY = ResolveTerrainSupportY(field, pos.X, pos.Z, hard.NewPosition.Y);
        float prevSupportY = ResolveTerrainSupportY(
            field, vehicle.Position.X, vehicle.Position.Z, vehicle.Position.Y);

        float gravityY = inst.Data?.GravityY ?? HkPhysicsConstants.DefaultGravityY;
        // Cap contact climb rate so a single bad terrain sample cannot launch us.
        float maxContactVy = Math.Max(3f, newSpeed * MathF.Tan(0.45f)); // ~24° climb cap
        float maxStickDrop = Math.Max(
            HkPhysicsConstants.PathMaxStickSurfaceDrop,
            maxContactVy * dt + HkPhysicsConstants.PathAirborneClearance);

        // prevVy is contact climb / free-fall only — never invent hop energy.
        IntegrateVertical(
            prevY: vehicle.Position.Y,
            prevVy: vehicle.Velocity.Y,
            supportY: supportY,
            prevSupportY: prevSupportY,
            landSupportY: supportY,
            rideHeight: ride,
            dt: dt,
            gravityY: gravityY,
            maxContactVy: maxContactVy,
            maxStickDrop: maxStickDrop,
            out var y,
            out var vy,
            out var grounded);

        pos = new Vector3(pos.X, y, pos.Z);

        // Pitch: short front/rear baseline when grounded on continuous grade only.
        // Airborne → velocity pitch. Never bridge a lip with a long multi-sample plane.
        var rotation = TerrainContactPlane.YawOnly(newYaw);
        const float probe = HkPhysicsConstants.PathProbeHalfLength;
        bool haveFoot = TrySampleFrontRear(
            field, pos, newYaw, probe, wheelHardPoints: null,
            out var yFront, out var yRear, out _);
        float frontDrop = haveFoot ? (yRear - yFront) : 0f;

        if (grounded && haveFoot
            && MathF.Abs(frontDrop) < HkPhysicsConstants.PathRampLipFrontDrop)
        {
            float span = Math.Max(probe * 2f, 0.5f);
            float pitch = MathF.Atan2(yRear - yFront, span);
            pitch = Math.Clamp(pitch, -0.55f, 0.55f);
            rotation = TerrainContactPlane.FromYawPitchRoll(newYaw, pitch, roll: 0f);
        }
        else if (!grounded)
        {
            float flightPitch = MathF.Atan2(vy, Math.Max(newSpeed, 0.5f));
            flightPitch = Math.Clamp(flightPitch, -0.65f, 0.65f);
            rotation = TerrainContactPlane.FromYawPitchRoll(newYaw, flightPitch, roll: 0f);
        }

        // Velocity: planar path speed on XZ; Y from vertical integrator only.
        Vector3 velocity;
        if (newSpeed > 1e-4f)
        {
            var fx = MathF.Sin(newYaw);
            var fz = MathF.Cos(newYaw);
            velocity = new Vector3(fx * newSpeed, vy, fz * newSpeed);
        }
        else
        {
            velocity = new Vector3(0f, vy, 0f);
        }

        // --- Drive axes (retail thr base −1) ---
        ExtractBasis(rotation, out var right, out var forward);
        var acceptDist = ResolveAcceptDistance(hard, path);
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            pos,
            right,
            forward,
            velocity,
            aim,
            acceptDist,
            cruiseScale: cornerScale > 0f ? cornerScale : 1f,
            allowReverse: true,
            alwaysDrive: false);

        if (newSpeed > 0.5f && thr > -0.5f && thr <= 0f)
            thr = -1f;
        // Airborne: no handbrake assist from sharp (keeps rear torque alive for client spin).
        if (!grounded)
            sharp = 0;

        // Havok bookkeeping: pose + thr only; do not let sim rewrite authored Y/XZ.
        var body = inst.Body;
        body.PosX = pos.X;
        body.PosY = pos.Y;
        body.PosZ = pos.Z;
        body.QuatX = rotation.X;
        body.QuatY = rotation.Y;
        body.QuatZ = rotation.Z;
        body.QuatW = rotation.W;
        body.LinVelX = velocity.X;
        body.LinVelY = velocity.Y;
        body.LinVelZ = velocity.Z;
        body.AngVelX = body.AngVelY = body.AngVelZ = 0f;

        IVehicleCollisionQuery query = BuildCollisionQuery(map, supportY, excludeSelf: vehicle);
        inst.Step(thr, steer, handbrake: sharp != 0, dt, query);

        // Force body back to authored pose (physics must not hop us off the path).
        body.PosX = pos.X;
        body.PosY = pos.Y;
        body.PosZ = pos.Z;
        body.QuatX = rotation.X;
        body.QuatY = rotation.Y;
        body.QuatZ = rotation.Z;
        body.QuatW = rotation.W;
        body.LinVelX = velocity.X;
        body.LinVelY = velocity.Y;
        body.LinVelZ = velocity.Z;
        body.AngVelX = body.AngVelY = body.AngVelZ = 0f;

        float dYaw = NpcVehicleDriveController.NormalizeRadians(newYaw - prevYaw);
        float yawRate = dYaw / Math.Max(dt, 1e-4f);

        return new PathStepResult
        {
            NewPosition = pos,
            Velocity = velocity,
            Rotation = rotation,
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
            AngularVelocity = new Vector3(0f, yawRate, 0f),
        };
    }

    /// <summary>
    /// Vertical contact vs ballistic free-flight (center support only).
    /// Continuous ground: plant at terrain. Surface dropped past stick threshold, or body
    /// already above clearance: free-fall under full gravity using caller <paramref name="prevVy"/>
    /// (contact climb carried off a crest — never invent hop energy here).
    /// </summary>
    internal static void IntegrateVertical(
        float prevY,
        float prevVy,
        float supportY,
        float prevSupportY,
        float landSupportY,
        float rideHeight,
        float dt,
        float gravityY,
        float maxContactVy,
        float maxStickDrop,
        out float y,
        out float vy,
        out bool grounded)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            y = prevY;
            vy = prevVy;
            grounded = true;
            return;
        }

        float ride = Math.Max(0f, rideHeight);
        float groundY = supportY + ride;
        float prevGroundY = prevSupportY + ride;
        float landY = landSupportY + ride;
        float stickDrop = maxStickDrop > 0f
            ? maxStickDrop
            : HkPhysicsConstants.PathMaxStickSurfaceDrop;

        bool wasAirborne = prevY > prevGroundY + HkPhysicsConstants.PathAirborneClearance;
        // Center heightfield only — front/rear probes must not decide contact (false lips on turns).
        bool surfaceFellAway = groundY < prevGroundY - stickDrop;

        if (wasAirborne || surfaceFellAway)
        {
            float freeY = prevY + prevVy * dt + 0.5f * gravityY * dt * dt;
            float freeVy = prevVy + gravityY * dt;

            if (freeY <= landY && freeVy <= 0f)
            {
                y = landY;
                vy = 0f;
                grounded = true;
            }
            else
            {
                y = freeY;
                vy = freeVy;
                grounded = false;
            }
            return;
        }

        // Continuous contact: plant on center terrain. Geometric contact vy only (capped).
        y = groundY;
        float contactVy = dt > 1e-6f ? (groundY - prevY) / dt : 0f;
        float cap = maxContactVy > 0f ? maxContactVy : 15f;
        if (contactVy > cap)
            contactVy = cap;
        else if (contactVy < -cap)
            contactVy = -cap;
        vy = contactVy;
        grounded = true;
    }

    /// <summary>
    /// Sample terrain under front / rear of the chassis (hardpoints or half-length footprint).
    /// </summary>
    internal static bool TrySampleFrontRear(
        MapTerrainHeightfield field,
        Vector3 position,
        float yawRadians,
        float halfLength,
        Vector3[] wheelHardPoints,
        out float yFront,
        out float yRear,
        out float yCenter)
    {
        yFront = yRear = yCenter = 0f;
        if (field == null)
            return false;

        if (!field.TrySample(position.X, position.Z, out yCenter))
            return false;

        float fwdX = MathF.Sin(yawRadians);
        float fwdZ = MathF.Cos(yawRadians);
        float rightX = MathF.Cos(yawRadians);
        float rightZ = -MathF.Sin(yawRadians);

        if (TerrainContactPlane.TryCollectWheelSamples(
                position, fwdX, fwdZ, rightX, rightZ, wheelHardPoints,
                (float x, float z, out float y) => field.TrySample(x, z, out y),
                out _, out yFront, out yRear, out _, out _, out var hl, out _))
        {
            halfLength = hl;
            return true;
        }

        // Short probe — never use the 4u align box (that samples past the ramp lip too early).
        float hl2 = halfLength > 0.25f ? halfLength : HkPhysicsConstants.PathProbeHalfLength;
        if (hl2 > 2.5f)
            hl2 = 2.5f;
        if (!field.TrySample(position.X + fwdX * hl2, position.Z + fwdZ * hl2, out yFront))
            yFront = yCenter;
        if (!field.TrySample(position.X - fwdX * hl2, position.Z - fwdZ * hl2, out yRear))
            yRear = yCenter;
        return true;
    }

    /// <summary>
    /// Per-template ride height + footprint. Default ride is 0 (chassis origin ≈ terrain);
    /// never invent a large constant that floats every NPC.
    /// </summary>
    internal static void ResolveFootprint(
        Vehicle vehicle,
        out float rideHeight,
        out float halfLength,
        out float halfWidth,
        out Vector3[] wheelHardPoints)
    {
        rideHeight = HkPhysicsConstants.PathFallbackRideHeight;
        halfLength = HkPhysicsConstants.PathProbeHalfLength;
        halfWidth = 1.2f;
        wheelHardPoints = null;

        if (vehicle?.CloneBaseObject is CloneBaseVehicle cbv)
        {
            wheelHardPoints = cbv.VehicleSpecific.WheelHardPoints;
            var cbid = vehicle.CBID > 0 ? vehicle.CBID : cbv.CloneBaseSpecific.CloneBaseId;
            if (cbid > 0 && VehicleGroundMetricsCache.TryGet(cbid, out var cached))
            {
                rideHeight = cached.ChassisHeightAboveTerrain;
                if (cached.HalfLength > 0.25f)
                    halfLength = Math.Min(cached.HalfLength, 2.5f);
                if (cached.HalfWidth > 0.25f)
                    halfWidth = cached.HalfWidth;
                return;
            }

            var metrics = VehicleGroundMetricsCache.Compute(cbv.VehicleSpecific);
            rideHeight = metrics.ChassisHeightAboveTerrain;
            if (metrics.HalfLength > 0.25f)
                halfLength = Math.Min(metrics.HalfLength, 2.5f);
            if (metrics.HalfWidth > 0.25f)
                halfWidth = metrics.HalfWidth;
        }
    }

    /// <summary>Chassis height above terrain sample (per-template when cached).</summary>
    internal static float ResolveRideHeight(Vehicle vehicle, HkVehicleData data)
    {
        ResolveFootprint(vehicle, out var ride, out _, out _, out _);
        return ride;
    }

    /// <summary>Approx pitch from body quaternion (for free-flight pitch continuity).</summary>
    internal static float PitchFromQuaternion(Quaternion q)
    {
        // forward.Y = sin(pitch) for yaw-pitch-roll without roll dominance
        var fwd = TerrainContactPlane.ForwardFromQuaternion(q);
        return MathF.Asin(Math.Clamp(fwd.Y, -1f, 1f));
    }

    internal static float ResolveTerrainSupportY(
        MapTerrainHeightfield field, float x, float z, float fallbackY)
    {
        if (field != null && field.TrySample(x, z, out var hy))
            return hy;
        return fallbackY;
    }

    private static float ResolveFallbackCruise(Vehicle vehicle, HkVehicleData data)
    {
        if (data != null)
        {
            if (data.SpeedLimiter > 1f)
                return data.SpeedLimiter;
            if (data.AbsoluteTopSpeed > 1f)
                return data.AbsoluteTopSpeed * 0.5f;
        }

        return 12f;
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
            // No map heightfield: flat plane at chassis Y (ride is ~0 by default).
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

    internal static void SoftPullPlanarToward(float targetX, float targetZ, ref float posX, ref float posZ)
    {
        float dx = posX - targetX;
        float dz = posZ - targetZ;
        float d2 = dx * dx + dz * dz;
        float max = HkPhysicsConstants.PathSoftPullMaxDrift;
        if (d2 <= max * max || max <= 0f)
            return;
        float d = MathF.Sqrt(d2);
        float s = max / d;
        posX = targetX + dx * s;
        posZ = targetZ + dz * s;
    }

    internal static void SoftPullVerticalToward(float supportY, ref float posY, ref float velY)
    {
        float max = HkPhysicsConstants.PathSoftPullMaxVerticalDrift;
        if (max <= 0f)
            return;

        float hi = supportY + max;
        float lo = supportY - max;
        if (posY > hi)
        {
            posY = hi;
            if (velY > 0f)
                velY = 0f;
        }
        else if (posY < lo)
        {
            posY = lo;
            if (velY < 0f)
                velY = 0f;
        }
    }

    internal static void SoftPullPlanarVelocityToward(
        float targetVx, float targetVz, ref float vx, ref float vz)
    {
        float dx = vx - targetVx;
        float dz = vz - targetVz;
        float d2 = dx * dx + dz * dz;
        float max = HkPhysicsConstants.PathSoftPullMaxPlanarVelDrift;
        if (d2 <= max * max || max <= 0f)
            return;
        float d = MathF.Sqrt(d2);
        float s = max / d;
        vx = targetVx + dx * s;
        vz = targetVz + dz * s;
    }

    private static void MaybeResyncSim(Vehicle vehicle, VehiclePhysicsInstance inst)
    {
        var body = inst.Body;
        var dx = body.PosX - vehicle.Position.X;
        var dy = body.PosY - vehicle.Position.Y;
        var dz = body.PosZ - vehicle.Position.Z;
        var distSq = (dx * dx) + (dy * dy) + (dz * dz);
        var thr = ResyncDriftThreshold;
        if (distSq > thr * thr)
            vehicle.SyncPhysicsInstanceFromEntity();
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
