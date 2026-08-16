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

    /// <summary>
    /// Where the NPC is currently heading — the active waypoint, not the position it reached this
    /// tick. Shipped to the client as <c>TargetPosition</c> and stored in its
    /// <c>m_vMoveToTarget</c>.
    /// <para>
    /// The client's own creature AI (<c>CVOGHBAICreatureBase::DoMovement</c> @005cd3b0) walks toward
    /// this goal and halts the creature outright once it is within 1.0 unit of it. Publishing the
    /// current position here therefore reads as "already arrived" every update, so the client stops
    /// the creature and the only motion left is the discrete pose writes — the teleporting look.
    /// </para>
    /// </summary>
    public Vector3 SteerGoal;

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
    /// <summary>
    /// How far ahead of the creature the client's steer goal is placed, in world units.
    /// <para>
    /// The client walks the creature toward this goal <b>itself</b>, at full creature speed, and
    /// then stops when it gets within 1.0 unit of it
    /// (<c>CVOGHBAICreatureBase::DoMovement</c> @005cd3b0). So the goal bounds how far the client
    /// can run ahead of the server's authoritative position, which puts it in a narrow band:
    /// </para>
    /// <list type="bullet">
    /// <item><description>below <b>1.0</b> the client treats it as already reached and freezes the
    /// creature, leaving only discrete pose writes — the original teleporting bug;</description></item>
    /// <item><description>above <b>5.0</b> the client's accrued drift trips
    /// <c>m_bPacketOverride</c> (<c>CVOGCreature::DoPositionUpdate</c> @004c6360 uses a 5.0 limit for
    /// clone type 0x14, not the 15.0 <c>cfMaxNetworkOffset</c> that governs the physics path) and it
    /// is snapped back to the server position.</description></item>
    /// </list>
    /// <para>
    /// Publishing the raw waypoint put the goal 80-160 units out, and client-side measurement showed
    /// drift tracking it exactly (maxDrift 166 against maxGoalDist 155) with ~19 snaps/second. A
    /// short lookahead keeps the client's own simulation tethered to the server's.
    /// </para>
    /// </summary>
    public static float ClientSteerLookahead { get; set; } = 3.0f;

    /// <summary>
    /// Seconds of travel the steer goal must lead by, so the client is never velocity-throttled.
    /// <para>
    /// The client does not step toward the goal — it derives a <b>velocity</b> from it and lets
    /// Havok integrate:
    /// <c>vel = dir * min(distToGoal, GetCreatureSpeed(creature))</c>
    /// (<c>CVOGHBAICreatureBase::DoMovement</c> @005cd3b0). So a goal closer than the creature's
    /// speed caps how fast the client can walk: with a 3-unit goal a 5 u/s creature moves at 3 u/s
    /// on the client while the server advances at 5, and the client falls behind 2 u/s until it
    /// crosses the 5-unit override limit and is snapped forward — measured as ~8,000 overrides with
    /// every goal pinned at exactly 3.00.
    /// </para>
    /// <para>
    /// Leading by more than one second of travel keeps <c>min()</c> resolving to the full speed, so
    /// both simulations advance at the same rate. Holding at a waypoint still collapses the goal
    /// onto the position, which is what stops the client walking on while the server waits.
    /// </para>
    /// </summary>
    public static float ClientSteerLookaheadSeconds { get; set; } = 1.5f;

    /// <summary>
    /// Places the client's steer goal along the path heading, never past the waypoint itself, far
    /// enough ahead that the client walks at full creature speed rather than being throttled by
    /// <c>min(distToGoal, speed)</c>.
    /// </summary>
    /// <summary>
    /// When false (default), the steer goal published to the client is the creature's own position,
    /// which parks the client's AI and leaves the server as the sole authority on where the creature
    /// is.
    /// <para>
    /// Publishing a real goal makes the client walk the creature <b>itself</b>
    /// (<c>CVOGHBAICreatureBase::DoMovement</c> @005cd3b0) along a straight line to that goal, while
    /// the server walks the actual path with turns, accept-distances and waypoint holds. The two
    /// simulations then diverge continuously, and measurement showed that divergence at every goal
    /// distance tried — p50 drift ~44 units at both a 3.0 and a 17.55 lookahead, rising to 229 as the
    /// goal grew — against a server stream that was itself smooth (maxBack 0.16-1.52 per pose).
    /// Enlarging the goal only bought the client more room to wander.
    /// </para>
    /// <para>
    /// A parked AI means motion comes solely from the server's pose stream, so smoothness is then a
    /// function of pose rate rather than of a client simulation racing the server.
    /// </para>
    /// </summary>
    public static bool PublishSteerGoal { get; set; }

    internal static Vector3 ResolveSteerGoal(Vector3 position, Vector3 target, float dist, float speed)
    {
        // Server-authoritative: goal == position parks the client AI (it reads "arrived").
        if (!PublishSteerGoal)
            return position;

        var lookahead = ClientSteerLookahead;
        var travel = speed * ClientSteerLookaheadSeconds;
        if (travel > lookahead)
            lookahead = travel;

        if (lookahead <= 0f || dist <= lookahead || dist <= 1e-4f)
            return target;   // already inside the lookahead: aim at the waypoint

        var t = lookahead / dist;
        return new Vector3(
            position.X + ((target.X - position.X) * t),
            position.Y + ((target.Y - position.Y) * t),
            position.Z + ((target.Z - position.Z) * t));
    }

    public static PathStepResult Step(
        Vector3 position,
        MapPathTemplate path,
        int index,
        int direction,
        long waitUntilMs,
        long nowMs,
        float speed,
        float dt,
        Quaternion? currentRotation = null)
    {
        // Facing published by every non-steering return path (empty/zero-speed path, waypoint
        // hold, exact arrival). Identity here is world-forward, not "unchanged": the client applies
        // the quaternion verbatim (CVOGPhysicsBase::DoPositionUpdate @0053eec0 stores it after only
        // an isOk check), so a stopping NPC visibly snaps to face north. Callers that know the
        // entity's current facing pass it; the pure-stepper tests keep the historical default.
        var restingRotation = currentRotation ?? Quaternion.Default;

        var result = new PathStepResult
        {
            NewPosition = position,
            Velocity = new Vector3(0f, 0f, 0f),
            Rotation = restingRotation,
            // Overwritten with the active waypoint once one is resolved below. Standing still is
            // correctly expressed as goal == position: the client AI then holds the creature.
            SteerGoal = position,
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
        // SteerGoal is resolved from the position this tick ENDS at, never the one it starts from:
        // the creature advances up to stepLen first, so a goal measured from the start position
        // lands that much closer and can fall back inside the client's 1.0-unit freeze zone.
        result.SteerGoal = ResolveSteerGoal(position, target, dist, speed);
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
            result.Rotation = dist > onPointEps ? YawQuaternion(dx, dz) : restingRotation;
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

        // Re-anchor the goal on where this tick actually left the creature (see ResolveSteerGoal).
        result.SteerGoal = ResolveSteerGoal(result.NewPosition, target, dist - move, speed);

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
