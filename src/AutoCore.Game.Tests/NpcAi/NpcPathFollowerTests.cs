using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>
/// Stage 8: pure path stepper (<see cref="NpcPathFollower.Step"/>) — client parity 005df950.
/// No entity/map state; all timing passed explicitly (no sleeping).
/// </summary>
[TestClass]
public class NpcPathFollowerTests
{
    private const float Tolerance = 0.001f;

    [TestCleanup]
    public void TearDown() => NpcPathFollower.PublishSteerGoal = false;

    /// <summary>
    /// Default is server-authoritative: the goal is the creature's own position, which parks the
    /// client AI so motion comes only from the server pose stream. Publishing a real goal makes the
    /// client walk the creature itself and it diverges from the server's path — measured at p50 ~44
    /// units of drift for every lookahead tried, against a server stream that was itself smooth.
    /// </summary>
    [TestMethod]
    public void Step_ByDefault_PublishesPositionAsSteerGoal_ParkingTheClientAi()
    {
        var path = Path(false, new Vector3(500f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 5f, dt: 0.05f);

        Assert.AreEqual(result.NewPosition.X, result.SteerGoal.X, 0.001f,
            "default goal must be the creature's own position so the client AI stays parked");
        Assert.AreEqual(result.NewPosition.Z, result.SteerGoal.Z, 0.001f);
    }

    private static MapPathTemplate Path(bool reverse, params Vector3[] points)
    {
        var path = new MapPathTemplate { ReverseDirection = reverse };
        foreach (var p in points)
            path.Points.Add(new MapPathTemplate.MapPathPoint { Position = p, AcceptDistance = 1f });
        return path;
    }

    [TestMethod]
    public void Step_IndexMinusOne_PicksNearestPoint()
    {
        var path = Path(false,
            new Vector3(0f, 0f, 0f),
            new Vector3(50f, 0f, 0f),
            new Vector3(100f, 0f, 0f));

        // Sit next to point index 1, far enough not to "arrive" this tick.
        var result = NpcPathFollower.Step(
            new Vector3(50f, 0f, 3f), path, index: -1, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 1f, dt: 0.1f);

        Assert.AreEqual(1, result.NewIndex, "index -1 must resolve to the nearest waypoint");
        Assert.IsFalse(result.Arrived);
    }

    [TestMethod]
    public void NearestPoint_ReturnsClosestWaypoint()
    {
        var path = Path(false,
            new Vector3(0f, 0f, 0f),
            new Vector3(100f, 0f, 0f),
            new Vector3(100f, 0f, 100f));

        // (90,0,10) is nearest to (100,0,0): distSq 200 vs 8200/8200.
        var nearest = NpcPathFollower.NearestPoint(new Vector3(90f, 0f, 10f), path);

        Assert.AreEqual(100f, nearest.X, Tolerance);
        Assert.AreEqual(0f, nearest.Y, Tolerance);
        Assert.AreEqual(0f, nearest.Z, Tolerance);
    }

    [TestMethod]
    public void Step_MovesTowardPointAtSpeedTimesDt()
    {
        var path = Path(false, new Vector3(100f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 10f, dt: 0.5f);

        // speed * dt = 5 units toward (100,0,0).
        Assert.IsFalse(result.Arrived);
        Assert.AreEqual(5f, result.NewPosition.X, Tolerance);
        Assert.AreEqual(0f, result.NewPosition.Z, Tolerance);
        Assert.AreEqual(0, result.NewIndex);
    }

    /// <summary>
    /// Path points encode ground height; Y must advance along the segment with XZ progress.
    /// Snapping Y to the destination waypoint every tick makes NPCs "fly" at the target height.
    /// </summary>
    [TestMethod]
    public void Step_BetweenUnevenHeights_LerpsYWithXzProgress()
    {
        var path = Path(false, new Vector3(100f, 10f, 0f));

        // Halfway toward the point in one step (speed*dt = 50, dist = 100).
        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 100f, dt: 0.5f);

        Assert.IsFalse(result.Arrived);
        Assert.AreEqual(50f, result.NewPosition.X, Tolerance);
        Assert.AreEqual(5f, result.NewPosition.Y, 0.01f,
            "Y must be halfway from 0 to 10 when XZ is halfway — not snapped to target.Y=10");
        Assert.AreEqual(0f, result.NewPosition.Z, Tolerance);
    }

    [TestMethod]
    public void Step_BetweenUnevenHeights_DoesNotJumpToTargetYOnFirstTick()
    {
        var path = Path(false, new Vector3(100f, 20f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 2f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 10f, dt: 0.1f); // move 1u of 100u

        Assert.IsFalse(result.Arrived);
        Assert.IsTrue(result.NewPosition.Y < 4f,
            $"first step must stay near start Y (2), not jump toward 20; got {result.NewPosition.Y}");
        Assert.IsTrue(result.NewPosition.Y >= 2f - Tolerance);
    }

    [TestMethod]
    public void Step_Arrival_SnapsAndReportsReactionAndWait()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(10f, 2f, 10f),
            AcceptDistance = 1f,
            ReactionCoid = 55501,
            WaitTime = 3000,
        });

        // Already at the point → arrival this tick.
        var result = NpcPathFollower.Step(
            new Vector3(10f, 2f, 10f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 5000, speed: 12f, dt: 0.1f);

        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(10f, result.NewPosition.X, Tolerance);
        Assert.AreEqual(2f, result.NewPosition.Y, Tolerance);
        Assert.AreEqual(10f, result.NewPosition.Z, Tolerance);
        Assert.AreEqual(55501L, result.FireReactionCoid);
        Assert.AreEqual(5000L + 3000L, result.WaitUntilMs, "WaitUntilMs = now + WaitTime(ms)");
    }

    [TestMethod]
    public void Step_LargeAcceptDistance_DoesNotTeleportFullGapInOneTick()
    {
        // Live client capture: |v|=18 predicts ~1u/tick but every few hundred ms
        // dist jumped ~14u while dt stayed ~50ms — path AcceptDistance snap.
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 0f),
            AcceptDistance = 15f, // retail-scale accept ring
            ReactionCoid = 99,
            WaitTime = 0,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(200f, 0f, 0f),
            AcceptDistance = 15f,
        });

        // 14u inside the accept ring, but one step is only 0.9u (18 * 0.05).
        var result = NpcPathFollower.Step(
            new Vector3(86f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        var moved = result.NewPosition.X - 86f;
        Assert.IsTrue(moved > 0f && moved <= 18f * 0.05f + Tolerance,
            $"Must not teleport across accept gap; moved {moved} (stepLen={18f * 0.05f})");
        Assert.IsTrue(result.Arrived,
            "Still counts as arrived when inside AcceptDistance so the path advances.");
        Assert.AreEqual(1, result.NewIndex, "Advance to next waypoint after accept arrival.");
        Assert.AreEqual(99L, result.FireReactionCoid);
    }

    [TestMethod]
    public void Step_InsideAccept_KeepsNonZeroVelocityTowardNextWhenNoWait()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 0f),
            AcceptDistance = 15f,
            WaitTime = 0,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(200f, 0f, 0f),
            AcceptDistance = 1f,
        });

        var result = NpcPathFollower.Step(
            new Vector3(90f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        Assert.IsTrue(result.Arrived);
        // Continuous step, not zeroed arrival snap — client gets non-zero vel between packs.
        Assert.IsTrue(result.Velocity.X > 0f || result.NewPosition.X > 90f);
    }

    [TestMethod]
    public void Step_WaitTime_HoldsUntilDeadline()
    {
        var path = Path(false, new Vector3(100f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 1000, nowMs: 500, speed: 12f, dt: 0.1f);

        Assert.IsFalse(result.Arrived);
        Assert.AreEqual(0f, result.NewPosition.X, Tolerance, "must not move while waiting");
        Assert.AreEqual(0f, result.NewPosition.Z, Tolerance);
        Assert.AreEqual(1000L, result.WaitUntilMs, "wait deadline is unchanged while holding");
        Assert.AreEqual(0, result.NewIndex);
    }

    [TestMethod]
    public void Step_EndOfPath_WrapsWhenNotReverse()
    {
        var path = Path(false,
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 0f));

        // At the last point → arrive and advance.
        var result = NpcPathFollower.Step(
            new Vector3(10f, 0f, 0f), path, index: 1, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 12f, dt: 0.1f);

        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(0, result.NewIndex, "non-reverse path wraps to index 0");
        Assert.AreEqual(1, result.NewDirection);
        Assert.IsFalse(result.NowReversing);
    }

    [TestMethod]
    public void Step_EndOfPath_PingPongsWhenReverse()
    {
        var path = Path(true,
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(10f, 0f, 0f), path, index: 1, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 12f, dt: 0.1f);

        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(0, result.NewIndex, "ping-pong steps back to count-2");
        Assert.AreEqual(-1, result.NewDirection, "ping-pong flips direction at the end");
        Assert.IsTrue(result.NowReversing);
    }

    [TestMethod]
    public void Step_ZeroSpeed_NoMovement()
    {
        var path = Path(false, new Vector3(100f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(1f, 0f, 2f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 0f, dt: 0.1f);

        Assert.IsFalse(result.Arrived);
        Assert.AreEqual(1f, result.NewPosition.X, Tolerance);
        Assert.AreEqual(2f, result.NewPosition.Z, Tolerance);
        Assert.AreEqual(0, result.NewIndex);
    }

    [TestMethod]
    public void Step_AcceptArrivalWithWait_ZerosVelocity()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 0f),
            AcceptDistance = 15f,
            WaitTime = 500,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(200f, 0f, 0f),
            AcceptDistance = 1f,
        });

        var result = NpcPathFollower.Step(
            new Vector3(90f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(0f, result.Velocity.X, Tolerance);
        Assert.AreEqual(0f, result.Velocity.Z, Tolerance);
        Assert.AreEqual(1500L, result.WaitUntilMs);
    }

    [TestMethod]
    public void Step_OutsideAccept_DoesNotArriveAndStepsAtMostStepLen()
    {
        var path = Path(false, new Vector3(100f, 0f, 0f));
        // Accept=1; start at X=50 → remaining 50 >> accept after 0.9 step.
        var result = NpcPathFollower.Step(
            new Vector3(50f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        Assert.IsFalse(result.Arrived);
        Assert.AreEqual(50f + 0.9f, result.NewPosition.X, Tolerance);
        Assert.AreEqual(0, result.NewIndex);
        Assert.AreEqual(0L, result.FireReactionCoid);
    }

    [TestMethod]
    public void Step_EmptyPath_NoOp()
    {
        var path = new MapPathTemplate();
        var start = new Vector3(3f, 1f, 4f);
        var result = NpcPathFollower.Step(
            start, path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        Assert.AreEqual(start, result.NewPosition);
        Assert.IsFalse(result.Arrived);
    }

    [TestMethod]
    public void Step_NullPath_NoOp()
    {
        var start = new Vector3(1f, 2f, 3f);
        var result = NpcPathFollower.Step(
            start, path: null!, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        Assert.AreEqual(start, result.NewPosition);
        Assert.IsFalse(result.Arrived);
    }

    [TestMethod]
    public void Step_SinglePointPath_AcceptAdvancesIndexToSelf()
    {
        var path = Path(false, new Vector3(5f, 0f, 5f));
        path.Points[0].AcceptDistance = 15f;

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        // dist≈7.07 < 15 → accept arrival; single-point Advance stays at 0.
        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(0, result.NewIndex);
        Assert.IsTrue(result.NewPosition.X > 0f && result.NewPosition.X < 5f);
    }

    /// <summary>
    /// Facing pop: the non-steering return paths (exact arrival, waypoint hold, empty/zero-speed
    /// path) all published <see cref="Quaternion.Default"/> — identity, i.e. world-forward — rather
    /// than the facing the NPC already had. That ships as a real rotation on the next
    /// <c>PositionMask</c> pack, and the client applies it verbatim:
    /// <c>CVOGPhysicsBase::DoPositionUpdate</c> @0053eec0 writes the quaternion straight into its
    /// last-server snapshot after an <c>hkQuaternion::isOk</c> check only. A patrolling NPC that
    /// stops therefore snaps to face world-forward.
    /// </summary>
    [TestMethod]
    public void Step_ExactArrival_PreservesCurrentFacing_NotIdentity()
    {
        var path = Path(false, new Vector3(10f, 0f, 0f));
        var facing = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f); // yaw 90°, clearly not identity

        // Standing exactly on the waypoint: dist <= onPointEps, the true-arrival branch.
        var result = NpcPathFollower.Step(
            new Vector3(10f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f,
            currentRotation: facing);

        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(facing.Y, result.Rotation.Y, Tolerance,
            "arriving on a waypoint must hold the NPC's existing facing, not snap to world-forward");
        Assert.AreEqual(facing.W, result.Rotation.W, Tolerance);
    }

    /// <summary>Holding out a waypoint's WaitTime must not re-face the NPC either.</summary>
    [TestMethod]
    public void Step_HoldingForWaitTime_PreservesCurrentFacing()
    {
        var path = Path(false, new Vector3(100f, 0f, 0f));
        var facing = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f);

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 5000, nowMs: 1000, speed: 18f, dt: 0.05f,
            currentRotation: facing);

        Assert.AreEqual(facing.Y, result.Rotation.Y, Tolerance,
            "a waiting NPC keeps its facing; identity here is a visible spin on the client");
        Assert.AreEqual(facing.W, result.Rotation.W, Tolerance);
    }

    /// <summary>
    /// The client walks the creature to the steer goal itself and stops within 1.0 unit of it, so the
    /// goal bounds how far it can run ahead of the server. Publishing the raw waypoint put it 80-160
    /// units out and the client ran that far ahead (measured: maxDrift 166 vs maxGoalDist 155),
    /// tripping the 5.0 snap limit ~19x/second.
    /// </summary>
    [TestMethod]
    public void Step_DistantWaypoint_ClampsSteerGoalToLookahead()
    {
        NpcPathFollower.PublishSteerGoal = true;   // client-driven mode is opt-in
        var path = Path(false, new Vector3(500f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 5f, dt: 0.05f);

        // Measured from where the creature ENDS the tick — that is where the client measures from.
        var goalDist = MathF.Sqrt(
            MathF.Pow(result.SteerGoal.X - result.NewPosition.X, 2)
            + MathF.Pow(result.SteerGoal.Z - result.NewPosition.Z, 2));

        // speed 5 * 1.5s = 7.5, which dominates the 3.0 floor.
        Assert.AreEqual(7.5f, goalDist, 0.01f,
            "a 500-unit waypoint must not be published as the goal; the client would sprint to it");
        Assert.IsTrue(goalDist > 1f,
            "goal must clear the client's 1.0-unit arrival stop or the AI freezes the creature");
        Assert.IsTrue(goalDist >= 5f,
            "goal must lead by at least one second of travel or it throttles the client to "
            + "min(distToGoal, speed) and the server outruns it");
    }

    /// <summary>
    /// The client turns the goal into a VELOCITY, not a step:
    /// <c>vel = dir * min(distToGoal, GetCreatureSpeed)</c>. A goal nearer than one second of travel
    /// therefore throttles the client below the creature's real speed, so the server outruns it and
    /// it is snapped forward — measured as ~8,000 overrides with every goal pinned at exactly 3.00.
    /// The goal must always lead by at least the distance covered in a second.
    /// </summary>
    [TestMethod]
    public void ResolveSteerGoal_LeadsByAtLeastOneSecondOfTravel()
    {
        NpcPathFollower.PublishSteerGoal = true;   // client-driven mode is opt-in
        foreach (var speed in new[] { 2.5f, 5f, 12f, 27f })
        {
            var goal = NpcPathFollower.ResolveSteerGoal(
                new Vector3(0f, 0f, 0f), new Vector3(1000f, 0f, 0f), 1000f, speed);

            var dist = MathF.Sqrt((goal.X * goal.X) + (goal.Z * goal.Z));

            Assert.IsTrue(dist >= speed,
                $"speed {speed}: goal {dist:F2} is nearer than one second of travel, which caps the "
                + "client's velocity at min(dist, speed) and lets the server outrun it");
            Assert.IsTrue(dist > 1f, $"speed {speed}: goal must clear the 1.0 arrival stop");
        }
    }

    /// <summary>
    /// The clamp must apply to every steer goal the server publishes, not just the patrol path.
    /// Combat pursuit briefly shipped the raw target — goals 100-200 units out — and the client
    /// sprinted to them, which measured as 2,093 client-side hard snaps in one session.
    /// </summary>
    [TestMethod]
    public void ResolveSteerGoal_DistantPursuitTarget_ClampsToLookahead()
    {
        NpcPathFollower.PublishSteerGoal = true;   // client-driven mode is opt-in
        var from = new Vector3(0f, 0f, 0f);
        var target = new Vector3(200f, 0f, 0f);

        const float speed = 2f;   // slow: the floor lookahead dominates
        var goal = NpcPathFollower.ResolveSteerGoal(from, target, 200f, speed);

        var dist = MathF.Sqrt(
            ((goal.X - from.X) * (goal.X - from.X)) + ((goal.Z - from.Z) * (goal.Z - from.Z)));
        Assert.AreEqual(NpcPathFollower.ClientSteerLookahead, dist, 0.01f,
            "a 200-unit pursuit target must not be published as the goal");
        Assert.IsTrue(dist > 1f, "must clear the client's arrival stop");
        Assert.IsTrue(dist >= speed, "must not throttle the client below creature speed");
    }

    /// <summary>A waypoint already inside the lookahead is the goal — no need to shorten it.</summary>
    [TestMethod]
    public void Step_NearWaypoint_UsesWaypointAsSteerGoal()
    {
        NpcPathFollower.PublishSteerGoal = true;   // client-driven mode is opt-in
        var path = Path(false, new Vector3(2f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 5f, dt: 0.05f);

        Assert.AreEqual(2f, result.SteerGoal.X, 0.01f);
    }

    /// <summary>The goal must lead along the path, not trail behind the creature.</summary>
    [TestMethod]
    public void Step_SteerGoal_LeadsInTheTravelDirection()
    {
        NpcPathFollower.PublishSteerGoal = true;   // client-driven mode is opt-in
        var path = Path(false, new Vector3(0f, 0f, 300f));

        var result = NpcPathFollower.Step(
            new Vector3(0f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 5f, dt: 0.05f);

        Assert.IsTrue(result.SteerGoal.Z > 0f,
            $"goal must lead toward the waypoint, was Z={result.SteerGoal.Z}");
        Assert.IsTrue(result.SteerGoal.Z > result.NewPosition.Z,
            "goal must stay ahead of the position reached this tick");
    }

    /// <summary>Omitting the facing keeps the historical identity default for the pure-stepper tests.</summary>
    [TestMethod]
    public void Step_ExactArrival_WithoutCurrentRotation_StillDefaultsToIdentity()
    {
        var path = Path(false, new Vector3(10f, 0f, 0f));

        var result = NpcPathFollower.Step(
            new Vector3(10f, 0f, 0f), path, index: 0, direction: 1,
            waitUntilMs: 0, nowMs: 1000, speed: 18f, dt: 0.05f);

        Assert.AreEqual(Quaternion.Default.W, result.Rotation.W, Tolerance);
    }
}
