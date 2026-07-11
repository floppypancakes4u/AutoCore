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
        SoftNpcPathMotion.MaxYawRateRadiansPerSecond = MathF.PI;
        SoftNpcPathMotion.YBlendPerSecond = 3f;
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
    }

    [TestMethod]
    public void Apply_BlendsYTowardTarget()
    {
        SoftNpcPathMotion.Enabled = true;
        SoftNpcPathMotion.YBlendPerSecond = 1f; // alpha = 0.1 for dt 0.1
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(0f, 10f, 0f),
            Velocity = new Vector3(0f, 0f, 5f),
            Rotation = Quaternion.Default,
            NewIndex = 0,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: Quaternion.Default,
            speed: 5f,
            dt: 0.1f,
            path: MakePath(),
            nowMs: 1000);

        Assert.IsTrue(soft.NewPosition.Y > 0f && soft.NewPosition.Y < 10f,
            $"Y should blend between 0 and 10, got {soft.NewPosition.Y}");
    }

    [TestMethod]
    public void Apply_LimitsYawRate()
    {
        SoftNpcPathMotion.Enabled = true;
        SoftNpcPathMotion.MaxYawRateRadiansPerSecond = 0.5f; // slow turn
        // Hard step wants ~90° yaw (face +X).
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(1f, 0f, 0f),
            Velocity = new Vector3(5f, 0f, 0f),
            Rotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            NewIndex = 0,
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: Quaternion.Default, // face +Z
            speed: 5f,
            dt: 0.1f,
            path: MakePath(),
            nowMs: 1000);

        var hardYaw = SoftNpcPathMotion.YawFromQuaternion(hard.Rotation);
        var softYaw = SoftNpcPathMotion.YawFromQuaternion(soft.Rotation);
        var prevYaw = SoftNpcPathMotion.YawFromQuaternion(Quaternion.Default);
        var maxStep = SoftNpcPathMotion.MaxYawRateRadiansPerSecond * 0.1f;
        Assert.IsTrue(MathF.Abs(SoftNpcPathMotion.NormalizeRadians(softYaw - prevYaw)) <= maxStep + 1e-3f,
            $"soft yaw step {softYaw - prevYaw} must not exceed max {maxStep}");
        Assert.AreNotEqual(hardYaw, softYaw, 1e-3f);
    }

    [TestMethod]
    public void Apply_ArrivalWithNoWait_CarriesVelocityTowardNextIndex()
    {
        SoftNpcPathMotion.Enabled = true;
        var path = MakePath();
        // Hard arrival zeros velocity (classic steppy look).
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Velocity = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Default,
            NewIndex = 1,
            Arrived = true,
            WaitUntilMs = 1000, // no remaining wait
        };

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: path.Points[0].Position,
            previousRotation: Quaternion.Default,
            speed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000);

        Assert.AreNotEqual(0f, soft.Velocity.X + soft.Velocity.Z,
            "zero-wait arrival should keep non-zero XZ velocity toward the next waypoint");
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
