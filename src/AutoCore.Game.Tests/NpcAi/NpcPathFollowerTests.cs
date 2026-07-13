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
}
