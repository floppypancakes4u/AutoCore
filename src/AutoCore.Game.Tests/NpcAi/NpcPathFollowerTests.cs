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
}
