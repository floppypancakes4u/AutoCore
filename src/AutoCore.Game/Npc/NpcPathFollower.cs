namespace AutoCore.Game.Npc;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Structures;

/// <summary>Result of a single <see cref="NpcPathFollower.Step"/> advance (all outputs explicit; no hidden state).</summary>
public struct PathStepResult
{
    /// <summary>Position after this tick (snapped to the waypoint on arrival).</summary>
    public Vector3 NewPosition;

    /// <summary>World velocity applied this tick (zero while holding, waiting, or on arrival).</summary>
    public Vector3 Velocity;

    /// <summary>Facing derived from the XZ travel direction; identity when not moving.</summary>
    public Quaternion Rotation;

    /// <summary>Active waypoint index after this tick.</summary>
    public int NewIndex;

    /// <summary>+1 forward / -1 backward after this tick.</summary>
    public int NewDirection;

    /// <summary>True when the NPC reached the active waypoint this tick.</summary>
    public bool Arrived;

    /// <summary>Reaction COID to fire on arrival (&gt; 0), else 0.</summary>
    public long FireReactionCoid;

    /// <summary>Absolute ms deadline the NPC idles until; unchanged unless a waypoint sets WaitTime.</summary>
    public long WaitUntilMs;

    /// <summary>True when the NPC is now walking the path backward (ping-pong); mirror to PathReversing.</summary>
    public bool NowReversing;

    /// <summary>
    /// Client throttle axis (vehicle+0x614). From <see cref="VehicleDriveInputs"/> when soft path
    /// is on; otherwise filled in <see cref="Entities.Vehicle.ApplyServerMove"/>.
    /// </summary>
    public float Throttle;

    /// <summary>Client steering axis (vehicle+0x618). Same as throttle.</summary>
    public float Steering;

    /// <summary>Client sharp-turn / drift-assist byte (vehicle+0x61c).</summary>
    public byte SharpTurn;

    /// <summary>True when <see cref="Throttle"/>/<see cref="Steering"/> were set by soft path / physics / kinematic.</summary>
    public bool HasDriveInputs;

    /// <summary>
    /// Optional chassis angular velocity (rad/s) from the physics sim.
    /// When set, <see cref="Entities.Vehicle.ApplyServerMove"/> prefers this over quat-delta estimate.
    /// </summary>
    public Vector3? AngularVelocity;
}

/// <summary>
/// Pure, allocation-free path stepper (client parity 005df950). Holds no entity/map state: every
/// input is a parameter and every output is on <see cref="PathStepResult"/>, so it is fully
/// unit-testable with explicit timing (no sleeping).
/// </summary>
public static class NpcPathFollower
{
    public static PathStepResult Step(
        Vector3 position,
        MapPathTemplate path,
        int index,
        int direction,
        long waitUntilMs,
        long nowMs,
        float speed,
        float dt)
    {
        var result = new PathStepResult
        {
            NewPosition = position,
            Velocity = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Default,
            NewIndex = index,
            NewDirection = direction,
            WaitUntilMs = waitUntilMs,
            NowReversing = direction < 0,
        };

        var count = path?.Points.Count ?? 0;
        if (count == 0 || speed <= 0f)
            return result;

        // Hold in place until the waypoint's WaitTime deadline elapses.
        if (nowMs < waitUntilMs)
            return result;

        // No/invalid current waypoint → latch onto the nearest one before steering.
        if (index < 0 || index >= count)
            index = NearestPointIndex(position, path);
        result.NewIndex = index;

        var target = path.Points[index].Position;
        var dx = target.X - position.X;
        var dz = target.Z - position.Z;
        var distSq = (dx * dx) + (dz * dz);
        var dist = (float)System.Math.Sqrt(distSq);
        var accept = path.Points[index].AcceptDistance;
        if (accept < 0f)
            accept = 0f;
        var stepLen = speed * Math.Max(dt, 0f);

        // Already on the point (or step covers the rest): true geometric arrival.
        // IMPORTANT: do NOT snap the full AcceptDistance gap in one tick — live capture showed
        // ~14u teleports every few hundred ms while |v|*dt predicted ~1u (AcceptDistance≈15).
        const float onPointEps = 0.05f;
        if (dist <= onPointEps || (stepLen > 0f && stepLen >= dist))
        {
            result.NewPosition = target;
            result.Velocity = new Vector3(0f, 0f, 0f);
            result.Rotation = dist > onPointEps ? YawQuaternion(dx, dz) : Quaternion.Default;
            result.Arrived = true;

            var reactionCoid = path.Points[index].ReactionCoid;
            result.FireReactionCoid = reactionCoid > 0 ? reactionCoid : 0;
            result.WaitUntilMs = nowMs + path.Points[index].WaitTime;

            Advance(index, direction, count, path.ReverseDirection, out var nextIndex, out var nextDirection);
            result.NewIndex = nextIndex;
            result.NewDirection = nextDirection;
            result.NowReversing = nextDirection < 0;
            return result;
        }

        // Steer in XZ at most one stepLen toward the waypoint (never more).
        // Y advances with XZ progress along the segment — path points already store ground
        // height. Snapping Y to target.Y every tick floats NPCs at the destination altitude.
        var inv = 1f / dist;
        var move = Math.Min(stepLen, dist);
        var t = move * inv; // fraction of remaining segment covered this tick
        result.NewPosition = new Vector3(
            position.X + (dx * inv * move),
            position.Y + ((target.Y - position.Y) * t),
            position.Z + (dz * inv * move));
        result.Velocity = new Vector3(dx * inv * speed, 0f, dz * inv * speed);
        result.Rotation = YawQuaternion(dx, dz);
        result.WaitUntilMs = waitUntilMs;
        result.NowReversing = direction < 0;

        // Inside AcceptDistance: count as arrived (advance path / fire reaction) but do not
        // teleport the remaining gap — position only moved `move` this tick.
        var remaining = dist - move;
        if (remaining <= accept)
        {
            result.Arrived = true;
            var reactionCoid = path.Points[index].ReactionCoid;
            result.FireReactionCoid = reactionCoid > 0 ? reactionCoid : 0;
            result.WaitUntilMs = nowMs + path.Points[index].WaitTime;
            Advance(index, direction, count, path.ReverseDirection, out var nextIndex, out var nextDirection);
            result.NewIndex = nextIndex;
            result.NewDirection = nextDirection;
            result.NowReversing = nextDirection < 0;
            // Zero-wait: SoftNpcPathMotion / next tick aims at the new index; keep velocity.
            if (path.Points[index].WaitTime > 0)
                result.Velocity = new Vector3(0f, 0f, 0f);
            return result;
        }

        result.Arrived = false;
        result.FireReactionCoid = 0;
        return result;
    }

    /// <summary>Advances the waypoint cursor, wrapping (loop) or flipping direction (ping-pong).</summary>
    private static void Advance(int index, int direction, int count, bool reverse, out int nextIndex, out int nextDirection)
    {
        if (count == 1)
        {
            nextIndex = 0;
            nextDirection = direction;
            return;
        }

        var next = index + direction;
        if (next >= count)
        {
            if (reverse)
            {
                nextIndex = count - 2;
                nextDirection = -1;
            }
            else
            {
                nextIndex = 0;
                nextDirection = 1;
            }
            return;
        }

        if (next < 0)
        {
            if (reverse)
            {
                nextIndex = 1;
                nextDirection = 1;
            }
            else
            {
                nextIndex = count - 1;
                nextDirection = direction;
            }
            return;
        }

        nextIndex = next;
        nextDirection = direction;
    }

    /// <summary>
    /// World position of the waypoint on <paramref name="path"/> nearest to <paramref name="position"/>
    /// (XZ distance). Used as the leash/return anchor for a path-following NPC so it returns to its
    /// patrol line rather than its spawn. Caller must ensure <c>path.Points.Count &gt; 0</c>.
    /// </summary>
    public static Vector3 NearestPoint(Vector3 position, MapPathTemplate path)
        => path.Points[NearestPointIndex(position, path)].Position;

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

    /// <summary>Yaw-only quaternion (rotation about +Y) facing the XZ heading (dx, dz).</summary>
    private static Quaternion YawQuaternion(float dx, float dz)
    {
        var yaw = (float)System.Math.Atan2(dx, dz);
        var half = yaw * 0.5f;
        return new Quaternion(0f, (float)System.Math.Sin(half), 0f, (float)System.Math.Cos(half));
    }
}
