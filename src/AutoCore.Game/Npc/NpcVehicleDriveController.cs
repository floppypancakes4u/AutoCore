namespace AutoCore.Game.Npc;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Vehicle-only path movement controller (opt-in via <see cref="Enabled"/>).
/// Converts <see cref="NpcPathFollower"/> navigation into facing-aligned velocity,
/// look-ahead thr/steer, curvature-limited speed, and optional terrain pitch/roll.
/// When disabled, <see cref="NpcTicker"/> uses legacy hard path / <see cref="SoftNpcPathMotion"/>.
/// </summary>
/// <remarks>
/// Retail local AI aims thr/steer into Havok (<c>MoveToTarget3DPoint</c> @ 0x004fc650).
/// Server-authoritative foreign ghosts hard-write pose, so this controller authors a
/// vehicle-like pose instead of chord XZ + lagged yaw (legacy soft slide).
/// </remarks>
public static class NpcVehicleDriveController
{
    /// <summary>Master switch — default false preserves legacy path motion.</summary>
    public static bool Enabled { get; set; }

    /// <summary>~1.5π rad/s. Literal avoids untestable default-expression mutants.</summary>
    public static float MaxYawRateRadiansPerSecond { get; set; } = 4.712389f;

    public static float MaxAcceleration { get; set; } = 50f;

    public static float MaxBrake { get; set; } = 60f;

    /// <summary>Look-ahead distance for thr/steer aim and desired heading.</summary>
    public static float LookAheadDistance { get; set; } = 28f;

    /// <summary>
    /// Max XZ distance the facing-integrated pose may lag/lead the hard navigator target
    /// before being blended back (AcceptDistance safety).
    /// </summary>
    public static float MaxPathDrift { get; set; } = 6f;

    public static PathStepResult Apply(
        PathStepResult hard,
        Vector3 previousPosition,
        Quaternion previousRotation,
        float cruiseSpeed,
        float dt,
        MapPathTemplate path,
        long nowMs,
        Vector3 previousVelocity = default,
        float laneOffset = 0f,
        MapTerrainHeightfield heightfield = null,
        Vehicle vehicle = null)
    {
        if (!Enabled || dt <= 0f || path == null || path.Points.Count == 0)
            return hard;

        // Wait dwell: park at approach pose (do not stack on shared waypoint XYZ).
        if (hard.Arrived && hard.WaitUntilMs > nowMs)
        {
            return new PathStepResult
            {
                NewPosition = previousPosition,
                Velocity = default,
                Rotation = previousRotation,
                NewIndex = hard.NewIndex,
                NewDirection = hard.NewDirection,
                Arrived = true,
                FireReactionCoid = hard.FireReactionCoid,
                WaitUntilMs = hard.WaitUntilMs,
                NowReversing = hard.NowReversing,
                Throttle = 0f,
                Steering = 0f,
                SharpTurn = 0,
                HasDriveInputs = true,
            };
        }

        var cruise = Math.Max(0f, cruiseSpeed);
        var prevSpeed = XzSpeed(previousVelocity);
        var hardSpeed = XzSpeed(hard.Velocity);
        // Stryker disable equality
        var rollingThrough = hard.Arrived && hard.WaitUntilMs <= nowMs;

        var cornerScale = ResolveCornerSpeedScale(hard, path);
        var desiredSpeed = (hardSpeed > 0.05f || rollingThrough) ? cruise * cornerScale : 0f;
        if (rollingThrough && prevSpeed < desiredSpeed * 0.85f)
            prevSpeed = desiredSpeed;
        // Stryker restore equality
        var newSpeed = ApproachSpeed(prevSpeed, desiredSpeed, dt);

        var aim = ResolveLookAheadAim(previousPosition, hard, path, laneOffset);
        var prevYaw = VehicleDriveInputs.YawFromQuaternion(previousRotation);
        var desiredYaw = ResolveDesiredYaw(previousPosition, aim, hard, prevYaw);
        var newYaw = LimitYaw(prevYaw, desiredYaw, dt);

        // Facing-aligned position: integrate along nose, then pull toward hard target if we drift.
        var pos = IntegrateFacingPosition(previousPosition, hard.NewPosition, newYaw, newSpeed, dt);

        ResolveFootprint(vehicle, out var halfLen, out var halfWid, out var clearance, out var wheelHps);

        var rotation = TerrainContactPlane.YawOnly(newYaw);
        if (heightfield != null &&
            TerrainContactPlane.TryAlign(
                pos, newYaw, heightfield, out var grounded, out var aligned,
                halfLen, halfWid, clearance, wheelHps))
        {
            // Per-vehicle: Y from that chassis' wheel tracks (or center fallback).
            pos = grounded;
            rotation = aligned;
        }
        else
        {
            // No heightfield (or align refused): keep previous Y; MapTerrainHeightfield always samples.
            pos = new Vector3(pos.X, previousPosition.Y, pos.Z);
        }

        // Velocity along chassis forward (unit from yaw/pitch/roll — includes Y on slopes).
        Vector3 velocity;
        // Stryker disable equality
        if (newSpeed > 1e-4f)
        {
            var fwd = TerrainContactPlane.ForwardFromQuaternion(rotation);
            velocity = new Vector3(fwd.X * newSpeed, fwd.Y * newSpeed, fwd.Z * newSpeed);
        }
        else
        {
            velocity = default;
        }

        var (thr, steer, sharp) = VehicleDriveInputs.Compute(
            previousPosition, newYaw, aim, newSpeed, cruiseThrottle: 1f);
        // Keep thr high while rolling so client Havok does not idle-out.
        if (newSpeed > 0.5f && thr >= 0f)
            thr = Math.Max(thr, 0.95f);
        // Stryker restore equality

        var result = hard;
        result.NewPosition = pos;
        result.Rotation = rotation;
        result.Velocity = velocity;
        result.Throttle = thr;
        result.Steering = steer;
        result.SharpTurn = sharp;
        result.HasDriveInputs = true;
        return result;
    }

    private static void ResolveFootprint(
        Vehicle vehicle,
        out float halfLength,
        out float halfWidth,
        out float clearance,
        out Vector3[] wheelHardPoints)
    {
        // 0 / NaN sentinels → TerrainContactPlane uses DefaultHalf* / DefaultGroundClearance
        halfLength = 0f;
        halfWidth = 0f;
        clearance = float.NaN;
        wheelHardPoints = null;
        if (vehicle?.CloneBaseObject is not CloneBaseVehicle cbv)
            return;
        var vs = cbv.VehicleSpecific;
        wheelHardPoints = vs.WheelHardPoints;

        var cbid = cbv.CloneBaseSpecific.CloneBaseId;
        if (cbid != 0 && VehicleGroundMetricsCache.TryGet(cbid, out var cached))
        {
            halfLength = cached.HalfLength;
            halfWidth = cached.HalfWidth;
            clearance = cached.ChassisHeightAboveTerrain;
            return;
        }

        // Cache miss (tests / partial load): compute from this template.
        var metrics = VehicleGroundMetricsCache.Compute(vs);
        halfLength = metrics.HalfLength;
        halfWidth = metrics.HalfWidth;
        clearance = metrics.ChassisHeightAboveTerrain;
    }

    private static float ResolveCornerSpeedScale(PathStepResult hard, MapPathTemplate path)
    {
        var n = path.Points.Count;
        if (n < 3)
            return 1f;

        var idx = Math.Clamp(hard.NewIndex, 0, n - 1);
        // Neighbors around the active target index
        var prev = path.Points[(idx - 1 + n) % n].Position;
        var cur = path.Points[idx].Position;
        var next = path.Points[(idx + 1) % n].Position;
        return PathCurvature.SpeedScale(PathCurvature.Radius(prev, cur, next));
    }

    internal static float ResolveDesiredYaw(
        Vector3 position,
        Vector3 aim,
        PathStepResult hard,
        float fallbackYaw)
    {
        var adx = aim.X - position.X;
        var adz = aim.Z - position.Z;
        // Stryker disable equality
        if ((adx * adx) + (adz * adz) > 1e-4f)
            return MathF.Atan2(adx, adz);

        var hs = XzSpeed(hard.Velocity);
        if (hs > 0.05f)
            return MathF.Atan2(hard.Velocity.X, hard.Velocity.Z);
        // Stryker restore equality

        return fallbackYaw;
    }

    internal static Vector3 IntegrateFacingPosition(
        Vector3 previous,
        Vector3 hardTarget,
        float yaw,
        float speed,
        float dt)
    {
        var step = Math.Max(0f, speed) * Math.Max(dt, 0f);
        var fx = MathF.Sin(yaw);
        var fz = MathF.Cos(yaw);
        var integrated = new Vector3(
            previous.X + (fx * step),
            previous.Y,
            previous.Z + (fz * step));

        // Pull toward hard navigator target so AcceptDistance progress cannot orbit forever.
        var dx = hardTarget.X - integrated.X;
        var dz = hardTarget.Z - integrated.Z;
        var drift = MathF.Sqrt((dx * dx) + (dz * dz));
        // Stryker disable equality
        // Stryker disable logical
        if (drift <= MaxPathDrift || drift < 1e-4f)
            return integrated;
        // Stryker restore all

        // Blend excess drift out while keeping most of the facing step.
        var pull = 1f - (MaxPathDrift / drift);
        pull = Math.Clamp(pull, 0f, 1f);
        return new Vector3(
            integrated.X + (dx * pull),
            integrated.Y,
            integrated.Z + (dz * pull));
    }

    internal static Vector3 ResolveLookAheadAim(
        Vector3 position,
        PathStepResult hard,
        MapPathTemplate path,
        float laneOffset)
    {
        var n = path.Points.Count;
        var idx = Math.Clamp(hard.NewIndex, 0, n - 1);
        var look = Math.Max(LookAheadDistance, 16f);
        var remaining = look;
        var cursor = position;
        var i = idx;

        for (var guard = 0; guard < n + 2 && remaining > 0f; guard++)
        {
            var pt = path.Points[i].Position;
            var dx = pt.X - cursor.X;
            var dz = pt.Z - cursor.Z;
            var seg = MathF.Sqrt((dx * dx) + (dz * dz));
            if (seg < 1e-3f)
            {
                i = (i + 1) % n;
                continue;
            }

            if (seg >= remaining)
            {
                var t = remaining / seg;
                var aim = new Vector3(cursor.X + (dx * t), pt.Y, cursor.Z + (dz * t));
                return ApplyLaneOffset(aim, i, path, laneOffset);
            }

            remaining -= seg;
            cursor = pt;
            i = (i + 1) % n;
        }

        return ApplyLaneOffset(path.Points[idx].Position, idx, path, laneOffset);
    }

    internal static Vector3 ApplyLaneOffset(Vector3 point, int index, MapPathTemplate path, float laneOffset)
    {
        // Stryker disable equality
        // Stryker disable logical
        if (MathF.Abs(laneOffset) < 1e-4f || path.Points.Count < 2)
            return point;
        // Stryker restore all

        var idx = Math.Clamp(index, 0, path.Points.Count - 1);
        var next = path.Points[(idx + 1) % path.Points.Count].Position;
        var prev = idx > 0 ? path.Points[idx - 1].Position : point;
        var sx = next.X - prev.X;
        var sz = next.Z - prev.Z;
        var len = MathF.Sqrt((sx * sx) + (sz * sz));
        // Stryker disable once equality
        if (len < 1e-3f)
            return point;

        var nx = -sz / len;
        var nz = sx / len;
        return new Vector3(point.X + (nx * laneOffset), point.Y, point.Z + (nz * laneOffset));
    }

    internal static float ApproachSpeed(float current, float desired, float dt)
    {
        // Stryker disable once equality
        if (desired >= current)
            return current + Math.Min(desired - current, Math.Max(0f, MaxAcceleration) * dt);
        return current - Math.Min(current - desired, Math.Max(0f, MaxBrake) * dt);
    }

    internal static float LimitYaw(float fromYaw, float toYaw, float dt)
    {
        var delta = NormalizeRadians(toYaw - fromYaw);
        var maxStep = Math.Max(0f, MaxYawRateRadiansPerSecond) * Math.Max(dt, 1e-4f);
        // Stryker disable once equality
        if (MathF.Abs(delta) <= maxStep)
            return toYaw;
        return fromYaw + Math.Clamp(delta, -maxStep, maxStep);
    }

    internal static float XzSpeed(Vector3 v)
        => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));

    internal static float NormalizeRadians(float a)
    {
        // Stryker disable equality
        while (a > MathF.PI)
            a -= MathF.PI * 2f;
        while (a < -MathF.PI)
            a += MathF.PI * 2f;
        // Stryker restore equality
        return a;
    }
}
