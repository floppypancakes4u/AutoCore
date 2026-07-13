namespace AutoCore.Game.Npc;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Structures;

/// <summary>
/// Softens path steps for client-visible driving without breaking waypoint AcceptDistance.
/// Path progress stays on the hard follower (laps); soft rate-limits yaw and packs
/// <see cref="VehicleDriveInputs"/> thr/steer (client <c>MoveToTarget3DPoint</c> @ 0x004fc650).
/// </summary>
/// <remarks>
/// Pure-pursuit <b>position</b> replacement was rejected twice: vehicles orbited a node or
/// never entered AcceptDistance when start index was not the geometric nearest (Skiddoo path 5092:
/// many spawns had arrives=0 for 15s while one lucky latch lapped fine).
/// </remarks>
public static class SoftNpcPathMotion
{
    public static bool Enabled { get; set; }

    public static float MaxYawRateRadiansPerSecond { get; set; } = MathF.PI * 1.5f;

    public static float YBlendPerSecond { get; set; } = 3f;

    public static float MaxAcceleration { get; set; } = 50f;

    public static float MaxBrake { get; set; } = 60f;

    /// <summary>Look-ahead for thr/steer aim only (not position).</summary>
    public static float LookAheadDistance { get; set; } = 28f;

    public static float BrakeDistance { get; set; } = 8f;

    public static float FullCornerSlowdownRadians { get; set; } = MathF.PI * 0.5f;

    public static float MaxLaneOffset { get; set; } = 2.5f;

    public static PathStepResult Apply(
        PathStepResult hard,
        Vector3 previousPosition,
        Quaternion previousRotation,
        float speed,
        float dt,
        MapPathTemplate path,
        long nowMs,
        Vector3 previousVelocity = default,
        float laneOffset = 0f)
    {
        if (!Enabled || dt <= 0f || path == null || path.Points.Count == 0)
            return hard;

        // Wait dwell: keep approach pose (no shared-path stack on exact XYZ).
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

        var cruise = Math.Max(0f, speed);
        var prevSpeed = MathF.Sqrt(
            (previousVelocity.X * previousVelocity.X) + (previousVelocity.Z * previousVelocity.Z));
        if (!float.IsFinite(prevSpeed) || prevSpeed < 0f)
            prevSpeed = 0f;

        var hardSpeed = MathF.Sqrt(
            (hard.Velocity.X * hard.Velocity.X) + (hard.Velocity.Z * hard.Velocity.Z));
        var rollingThrough = hard.Arrived && hard.WaitUntilMs <= nowMs;

        // Speed: ramp to cruise whenever hard is progressing or rolling through a zero-wait node.
        var desiredSpeed = (hardSpeed > 0.05f || rollingThrough) ? cruise : 0f;
        if (rollingThrough && prevSpeed < desiredSpeed * 0.85f)
            prevSpeed = desiredSpeed;
        var newSpeed = ApproachSpeed(prevSpeed, desiredSpeed, dt);

        // Position: hard path XZ only. Lane offset on pose broke AcceptDistance for some
        // start points (vehicle orbiting beside the node while index never advanced).
        // De-stack with thr/steer aim offset only.
        var pos = new Vector3(hard.NewPosition.X, previousPosition.Y, hard.NewPosition.Z);

        // Face path travel (rate-limited) — lag creates visible turn without blocking progress.
        var face = hard.Rotation;
        if (hardSpeed > 0.05f)
            face = YawQuaternion(hard.Velocity.X, hard.Velocity.Z);
        var newRotation = LimitYaw(previousRotation, face, dt);
        var newYaw = VehicleDriveInputs.YawFromQuaternion(newRotation);

        // Drive axes toward look-ahead on the path (client MoveToTarget3DPoint style).
        var aim = ResolveLookAheadAim(previousPosition, hard, path, laneOffset);
        var (thr, steer, sharp) = VehicleDriveInputs.Compute(
            previousPosition, newYaw, aim, newSpeed, cruiseThrottle: 1f);
        if (newSpeed > 0.5f && thr >= 0f && thr < 0.9f)
            thr = 0.95f;

        Vector3 velocity;
        if (newSpeed > 1e-4f && hardSpeed > 1e-4f)
        {
            var inv = 1f / hardSpeed;
            velocity = new Vector3(hard.Velocity.X * inv * newSpeed, 0f, hard.Velocity.Z * inv * newSpeed);
        }
        else if (rollingThrough && newSpeed > 1e-4f)
        {
            velocity = new Vector3(MathF.Sin(newYaw) * newSpeed, 0f, MathF.Cos(newYaw) * newSpeed);
        }
        else
        {
            velocity = default;
        }

        var result = hard;
        result.NewPosition = pos;
        result.Rotation = newRotation;
        result.Velocity = velocity;
        result.Throttle = thr;
        result.Steering = steer;
        result.SharpTurn = sharp;
        result.HasDriveInputs = true;
        return result;
    }

    public static float ResolveLaneOffset(long coid)
    {
        unchecked
        {
            var h = (uint)coid * 2654435761u;
            var t = (h % 1001) / 1000f;
            return (t * 2f - 1f) * MaxLaneOffset;
        }
    }

    /// <summary>
    /// First path latch: geometric nearest only. Phase offsets made some vehicles aim at a far
    /// index and never hit AcceptDistance (stuck / circling that one node).
    /// </summary>
    public static int ResolveStaggeredPathIndex(Vector3 position, MapPathTemplate path, long seed)
    {
        _ = seed;
        if (path == null || path.Points.Count == 0)
            return -1;
        return NearestPointIndex(position, path);
    }

    private static Vector3 ResolveLookAheadAim(
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
        // If hard already arrived, NewIndex is the next node — start there.
        // If not, aim along from current target index.
        if (!hard.Arrived)
            i = Math.Clamp(hard.NewIndex, 0, n - 1);

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

    private static Vector3 ApplyLaneOffset(Vector3 point, int index, MapPathTemplate path, float laneOffset)
    {
        if (MathF.Abs(laneOffset) < 1e-4f || path.Points.Count < 2)
            return point;

        var idx = Math.Clamp(index, 0, path.Points.Count - 1);
        var next = path.Points[(idx + 1) % path.Points.Count].Position;
        var prev = idx > 0 ? path.Points[idx - 1].Position : point;
        var sx = next.X - prev.X;
        var sz = next.Z - prev.Z;
        var len = MathF.Sqrt((sx * sx) + (sz * sz));
        if (len < 1e-3f)
            return point;

        var nx = -sz / len;
        var nz = sx / len;
        return new Vector3(point.X + (nx * laneOffset), point.Y, point.Z + (nz * laneOffset));
    }

    private static float ApproachSpeed(float current, float desired, float dt)
    {
        if (desired >= current)
            return current + Math.Min(desired - current, Math.Max(0f, MaxAcceleration) * dt);
        return current - Math.Min(current - desired, Math.Max(0f, MaxBrake) * dt);
    }

    private static int NearestPointIndex(Vector3 position, MapPathTemplate path)
    {
        var best = 0;
        var bestSq = float.MaxValue;
        for (var i = 0; i < path.Points.Count; i++)
        {
            var p = path.Points[i].Position;
            var dx = p.X - position.X;
            var dz = p.Z - position.Z;
            var sq = (dx * dx) + (dz * dz);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }

        return best;
    }

    internal static Quaternion LimitYaw(Quaternion from, Quaternion to, float dt)
    {
        var fromYaw = VehicleDriveInputs.YawFromQuaternion(from);
        var toYaw = VehicleDriveInputs.YawFromQuaternion(to);
        var delta = NormalizeRadians(toYaw - fromYaw);
        var maxStep = Math.Max(0f, MaxYawRateRadiansPerSecond) * Math.Max(dt, 1e-4f);
        if (MathF.Abs(delta) <= maxStep)
            return to;
        return YawQuaternionFromYaw(fromYaw + Math.Clamp(delta, -maxStep, maxStep));
    }

    internal static float YawFromQuaternion(Quaternion q) => VehicleDriveInputs.YawFromQuaternion(q);

    internal static float NormalizeRadians(float a)
    {
        while (a > MathF.PI)
            a -= MathF.PI * 2f;
        while (a < -MathF.PI)
            a += MathF.PI * 2f;
        return a;
    }

    private static Quaternion YawQuaternion(float dx, float dz)
        => YawQuaternionFromYaw(MathF.Atan2(dx, dz));

    private static Quaternion YawQuaternionFromYaw(float yaw)
    {
        var half = yaw * 0.5f;
        return new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));
    }
}
