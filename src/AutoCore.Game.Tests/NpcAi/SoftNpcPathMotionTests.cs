using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

[TestClass]
public class SoftNpcPathMotionTests
{
    [TestCleanup]
    public void TearDown()
    {
        SoftNpcPathMotion.Enabled = false;
        SoftNpcPathMotion.MaxYawRateRadiansPerSecond = MathF.PI * 1.25f;
        SoftNpcPathMotion.YBlendPerSecond = 3f;
        SoftNpcPathMotion.MaxAcceleration = 40f;
        SoftNpcPathMotion.MaxBrake = 50f;
        SoftNpcPathMotion.LookAheadDistance = 22f;
        SoftNpcPathMotion.MaxLaneOffset = 3.5f;
    }

    [TestMethod]
    public void Apply_WhenDisabled_ReturnsHardStepUnchanged()
    {
        SoftNpcPathMotion.Enabled = false;
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(10f, 5f, 0f),
            Velocity = new Vector3(1f, 0f, 0f),
            Rotation = new Quaternion(0f, 0.707f, 0f, 0.707f),
            NewIndex = 1,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: Quaternion.Default,
            speed: 12f,
            dt: 0.1f,
            path: MakePath(),
            nowMs: 1000);

        Assert.AreEqual(hard.NewPosition, soft.NewPosition);
        Assert.AreEqual(hard.Velocity, soft.Velocity);
        Assert.IsFalse(soft.HasDriveInputs);
    }

    [TestMethod]
    public void Apply_PreservesPreviousY()
    {
        SoftNpcPathMotion.Enabled = true;
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(10f, 99f, 0f),
            Velocity = new Vector3(12f, 0f, 0f),
            Rotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            NewIndex = 1,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(0f, 42f, 0f),
            previousRotation: new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            speed: 12f,
            dt: 0.1f,
            path: MakePath(),
            nowMs: 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));

        Assert.AreEqual(42f, soft.NewPosition.Y, 0.001f);
    }

    [TestMethod]
    public void Apply_SetsDriveInputs_ForClientWheels()
    {
        SoftNpcPathMotion.Enabled = true;
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(10f, 0f, 0f),
            Velocity = new Vector3(12f, 0f, 0f),
            Rotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            NewIndex = 1,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            speed: 12f,
            dt: 0.1f,
            path: MakePath(),
            nowMs: 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));

        Assert.IsTrue(soft.HasDriveInputs);
        Assert.IsTrue(soft.Throttle > 0.5f, $"path move must pack thr for wheels, got {soft.Throttle}");
    }

    [TestMethod]
    public void Apply_WaitArrival_DoesNotHardSnapToWaypoint()
    {
        SoftNpcPathMotion.Enabled = true;
        var path = MakePath();
        path.Points[0].WaitTime = 2000;
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Velocity = default,
            Rotation = Quaternion.Default,
            NewIndex = 1,
            Arrived = true,
            WaitUntilMs = 3000,
        };

        var approach = new Vector3(2f, 0f, 1f);
        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: approach,
            previousRotation: Quaternion.Default,
            speed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000);

        Assert.AreNotEqual(hard.NewPosition.X, soft.NewPosition.X, 0.01f);
    }

    [TestMethod]
    public void Apply_ArrivalWithNoWait_CarriesVelocity()
    {
        SoftNpcPathMotion.Enabled = true;
        var path = MakePath();
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Velocity = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            Arrived = true,
            WaitUntilMs = 1000,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: path.Points[0].Position,
            previousRotation: Quaternion.Default,
            speed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000);

        var spd = MathF.Sqrt((soft.Velocity.X * soft.Velocity.X) + (soft.Velocity.Z * soft.Velocity.Z));
        Assert.IsTrue(spd > 5f, $"zero-wait carry, got {spd}");
    }

    [TestMethod]
    public void ResolveStaggeredPathIndex_IsAlwaysGeometricNearest()
    {
        // Phase offsets left some Skiddoos aiming at a far index → arrives=0, circling one node.
        var path = new MapPathTemplate();
        for (var i = 0; i < 80; i++)
            path.Points.Add(new MapPathTemplate.MapPathPoint
            {
                Position = new Vector3(i * 10f, 0f, 0f),
                AcceptDistance = 2f,
            });

        for (long seed = 0; seed < 40; seed++)
        {
            var idx = SoftNpcPathMotion.ResolveStaggeredPathIndex(
                new Vector3(0f, 0f, 0f), path, seed);
            Assert.AreEqual(0, idx, $"seed {seed} must latch nearest (0), got {idx}");
        }

        Assert.AreEqual(5, SoftNpcPathMotion.ResolveStaggeredPathIndex(
            new Vector3(50f, 0f, 0f), path, seed: 999));
    }

    [TestMethod]
    public void Apply_FollowsHardPathXz_ForAcceptDistance()
    {
        SoftNpcPathMotion.Enabled = true;
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(10f, 0f, 0f),
            Velocity = new Vector3(12f, 0f, 0f),
            Rotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            NewIndex = 1,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            speed: 12f,
            dt: 0.1f,
            path: MakePath(),
            nowMs: 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));

        Assert.AreEqual(hard.NewPosition.X, soft.NewPosition.X, 0.01f);
        Assert.AreEqual(hard.NewPosition.Z, soft.NewPosition.Z, 0.01f);
        Assert.IsTrue(soft.HasDriveInputs && soft.Throttle > 0.5f);
    }

    [TestMethod]
    public void SoftFollow_DenseAcceptPath_AdvancesManyNodes()
    {
        // Regression: pure-pursuit / phase latch left some vehicles with arrives=0 (circling one node).
        SoftNpcPathMotion.Enabled = true;
        var path = new MapPathTemplate();
        for (var i = 0; i < 40; i++)
        {
            path.Points.Add(new MapPathTemplate.MapPathPoint
            {
                Position = new Vector3(i * 25f, 0f, 0f),
                AcceptDistance = 15f,
                WaitTime = 0,
            });
        }

        // Start slightly off node 5 (like a spawn near the path).
        var pos = new Vector3(5 * 25f + 8f, 0f, 3f);
        var idx = SoftNpcPathMotion.ResolveStaggeredPathIndex(pos, path, seed: 12345);
        Assert.AreEqual(5, idx);

        var rot = Quaternion.Default;
        var vel = default(Vector3);
        long wait = 0, now = 0;
        var arrives = 0;
        for (var t = 0; t < 200; t++)
        {
            now += 50;
            var hard = NpcPathFollower.Step(pos, path, idx, 1, wait, now, 27f, 0.05f);
            var soft = SoftNpcPathMotion.Apply(
                hard, pos, rot, 27f, 0.05f, path, now, vel, laneOffset: 2f);
            pos = soft.NewPosition;
            rot = soft.Rotation;
            vel = soft.Velocity;
            idx = soft.NewIndex;
            wait = soft.WaitUntilMs;
            if (hard.Arrived)
                arrives++;
        }

        Assert.IsTrue(arrives >= 8, $"expected multi-node progress, arrives={arrives} endIdx={idx}");
        Assert.IsTrue(idx > 5, $"index must advance past start, endIdx={idx}");
    }

    private static MapPathTemplate MakePath()
    {
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(0f, 0f, 0f),
            AcceptDistance = 1f,
            WaitTime = 0,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 0f),
            AcceptDistance = 1f,
            WaitTime = 0,
        });
        return path;
    }
}
