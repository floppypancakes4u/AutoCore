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
        var stepLen = speed * dt;

        // Arrive when already inside the accept radius or this tick would carry us to/past the point.
        if (dist <= accept || stepLen >= dist)
        {
            result.NewPosition = target;
            result.Velocity = new Vector3(0f, 0f, 0f);
            result.Rotation = dist > 0f ? YawQuaternion(dx, dz) : Quaternion.Default;
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

        // Steer in XZ at `speed`; Y comes straight from the target waypoint.
        var inv = 1f / dist;
        result.NewPosition = new Vector3(
            position.X + (dx * inv * stepLen),
            target.Y,
            position.Z + (dz * inv * stepLen));
        result.Velocity = new Vector3(dx * inv * speed, 0f, dz * inv * speed);
        result.Rotation = YawQuaternion(dx, dz);
        result.Arrived = false;
        result.FireReactionCoid = 0;
        result.WaitUntilMs = waitUntilMs;
        result.NowReversing = direction < 0;
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
