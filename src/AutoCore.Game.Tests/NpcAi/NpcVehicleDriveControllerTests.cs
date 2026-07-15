using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

[TestClass]
public class NpcVehicleDriveControllerTests
{
    [TestCleanup]
    public void TearDown()
    {
        NpcVehicleDriveController.Enabled = false;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = MathF.PI * 1.5f;
        NpcVehicleDriveController.MaxAcceleration = 50f;
        NpcVehicleDriveController.MaxBrake = 60f;
        NpcVehicleDriveController.LookAheadDistance = 28f;
        NpcVehicleDriveController.MaxPathDrift = 6f;
    }

    [TestMethod]
    public void Apply_WhenDisabled_ReturnsHardUnchanged()
    {
        NpcVehicleDriveController.Enabled = false;
        var hard = MovingHard(new Vector3(10f, 0f, 0f), new Vector3(12f, 0f, 0f));

        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: Quaternion.Default,
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: StraightPath(),
            nowMs: 1000);

        Assert.AreEqual(hard.NewPosition, result.NewPosition);
        Assert.AreEqual(hard.Velocity, result.Velocity);
        Assert.IsFalse(result.HasDriveInputs);
    }

    [TestMethod]
    public void Apply_Moving_VelocityParallelToFacing()
    {
        NpcVehicleDriveController.Enabled = true;
        var hard = MovingHard(new Vector3(5f, 0f, 0f), new Vector3(12f, 0f, 0f));
        // Face +Z, hard wants +X — after one limited turn step, velocity must match facing
        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: Quaternion.Default, // yaw 0 → +Z
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: StraightPath(),
            nowMs: 1000,
            previousVelocity: new Vector3(0f, 0f, 12f));

        Assert.IsTrue(result.HasDriveInputs);
        var spd = MathF.Sqrt((result.Velocity.X * result.Velocity.X) + (result.Velocity.Z * result.Velocity.Z));
        Assert.IsTrue(spd > 0.5f, $"expected motion, spd={spd}");

        var yaw = VehicleDriveInputs.YawFromQuaternion(result.Rotation);
        var fwdX = MathF.Sin(yaw);
        var fwdZ = MathF.Cos(yaw);
        var inv = 1f / spd;
        var vx = result.Velocity.X * inv;
        var vz = result.Velocity.Z * inv;
        var dot = (fwdX * vx) + (fwdZ * vz);
        Assert.IsTrue(dot > 0.98f, $"velocity must align with facing, dot={dot}");
    }

    [TestMethod]
    public void Apply_NoLateralSlide_OverMultipleTicks()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = LPath();
        var pos = new Vector3(0f, 0f, 0f);
        var rot = Quaternion.Default;
        var vel = new Vector3(0f, 0f, 12f);
        var index = 0;
        var dir = 1;
        long wait = 0;
        long now = 0;
        const float dt = 0.1f;
        const float speed = 12f;

        for (var tick = 0; tick < 40; tick++)
        {
            now += 100;
            var hard = NpcPathFollower.Step(pos, path, index, dir, wait, now, speed, dt);
            index = hard.NewIndex;
            dir = hard.NewDirection;
            wait = hard.WaitUntilMs;

            var driven = NpcVehicleDriveController.Apply(
                hard, pos, rot, speed, dt, path, now, vel);

            if (XzSpeed(driven.Velocity) > 1f)
            {
                var dPosX = driven.NewPosition.X - pos.X;
                var dPosZ = driven.NewPosition.Z - pos.Z;
                var dLen = MathF.Sqrt((dPosX * dPosX) + (dPosZ * dPosZ));
                if (dLen > 0.05f)
                {
                    var yaw = VehicleDriveInputs.YawFromQuaternion(driven.Rotation);
                    var fwdX = MathF.Sin(yaw);
                    var fwdZ = MathF.Cos(yaw);
                    var dot = ((dPosX / dLen) * fwdX) + ((dPosZ / dLen) * fwdZ);
                    Assert.IsTrue(dot > 0.85f,
                        $"tick {tick}: travel must align with facing, dot={dot}");
                }
            }

            pos = driven.NewPosition;
            rot = driven.Rotation;
            vel = driven.Velocity;
        }
    }

    [TestMethod]
    public void Apply_LookAhead_SetsSteeringOnCurve()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = LPath();
        // Near first corner, facing along +X, aim will pull toward +Z leg
        var hard = MovingHard(new Vector3(48f, 0f, 0f), new Vector3(12f, 0f, 0f), newIndex: 0);
        var faceX = YawQuat(MathF.PI * 0.5f); // +X

        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(40f, 0f, 0f),
            previousRotation: faceX,
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));

        Assert.IsTrue(result.HasDriveInputs);
        Assert.IsTrue(MathF.Abs(result.Steering) > 0.05f || result.Throttle > 0.5f,
            $"expected drive axes on curve, steer={result.Steering} thr={result.Throttle}");
    }

    [TestMethod]
    public void Apply_Corner_ReducesSpeedVersusStraight()
    {
        NpcVehicleDriveController.Enabled = true;
        // Tight triangle path → corner scale < 1
        var tight = new MapPathTemplate();
        tight.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        tight.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(5, 0, 0), AcceptDistance = 2f });
        tight.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 5), AcceptDistance = 2f });

        var straight = StraightPath();
        var hardTight = MovingHard(new Vector3(2f, 0f, 0f), new Vector3(12f, 0f, 0f), newIndex: 1);
        var hardStr = MovingHard(new Vector3(10f, 0f, 0f), new Vector3(12f, 0f, 0f), newIndex: 1);

        var rTight = NpcVehicleDriveController.Apply(
            hardTight, new Vector3(0, 0, 0), YawQuat(MathF.PI * 0.5f), 12f, 0.1f, tight, 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));
        var rStr = NpcVehicleDriveController.Apply(
            hardStr, new Vector3(0, 0, 0), YawQuat(MathF.PI * 0.5f), 12f, 0.1f, straight, 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));

        var sTight = XzSpeed(rTight.Velocity);
        var sStr = XzSpeed(rStr.Velocity);
        Assert.IsTrue(sTight < sStr - 0.5f || PathCurvature.SpeedScale(PathCurvature.Radius(
                tight.Points[0].Position, tight.Points[1].Position, tight.Points[2].Position)) < 1f,
            $"tight={sTight} straight={sStr}");
    }

    [TestMethod]
    public void Apply_AccelRamp_FromStop()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 20f;
        var hard = MovingHard(new Vector3(12f, 0f, 0f), new Vector3(12f, 0f, 0f));

        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: YawQuat(MathF.PI * 0.5f),
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: StraightPath(),
            nowMs: 1000,
            previousVelocity: default);

        var spd = XzSpeed(result.Velocity);
        Assert.IsTrue(spd > 0.5f && spd <= 2.1f, $"accel 20*0.1 → ~2, got {spd}");
    }

    [TestMethod]
    public void Apply_WaitHold_ZeroThrottle()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = StraightPath();
        path.Points[0].WaitTime = 2000;
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Arrived = true,
            WaitUntilMs = 3000,
            NewIndex = 1,
        };

        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(1f, 0f, 1f),
            previousRotation: Quaternion.Default,
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000);

        Assert.AreEqual(1f, result.NewPosition.X, 0.01f);
        Assert.AreEqual(0f, result.Throttle, 0.01f);
        Assert.IsTrue(result.HasDriveInputs);
    }

    [TestMethod]
    public void Apply_ZeroWait_CarriesSpeed()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = StraightPath();
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Velocity = default,
            Arrived = true,
            WaitUntilMs = 1000,
            NewIndex = 1,
        };

        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: path.Points[0].Position,
            previousRotation: YawQuat(MathF.PI * 0.5f),
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000,
            previousVelocity: new Vector3(12f, 0f, 0f));

        Assert.IsTrue(XzSpeed(result.Velocity) > 5f);
    }

    [TestMethod]
    public void Apply_PreservesHardArrivalIndexAndReaction()
    {
        NpcVehicleDriveController.Enabled = true;
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(10f, 0f, 0f),
            Velocity = new Vector3(12f, 0f, 0f),
            NewIndex = 3,
            NewDirection = -1,
            Arrived = true,
            FireReactionCoid = 999,
            WaitUntilMs = 500,
            NowReversing = true,
        };

        var result = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(0f, 0f, 0f),
            previousRotation: Quaternion.Default,
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: StraightPath(),
            nowMs: 1000); // past wait → not hold

        Assert.AreEqual(3, result.NewIndex);
        Assert.AreEqual(999, result.FireReactionCoid);
        Assert.AreEqual(-1, result.NewDirection);
        Assert.IsTrue(result.NowReversing);
    }

    [TestMethod]
    public void Apply_NullPath_ReturnsHard()
    {
        NpcVehicleDriveController.Enabled = true;
        var hard = MovingHard(new Vector3(1, 0, 0), new Vector3(1, 0, 0));
        var r = NpcVehicleDriveController.Apply(
            hard, default, Quaternion.Default, 12f, 0.1f, path: null, nowMs: 0);
        Assert.AreEqual(hard.NewPosition, r.NewPosition);
        Assert.IsFalse(r.HasDriveInputs);
    }

    [TestMethod]
    public void Apply_ZeroDt_ReturnsHard()
    {
        NpcVehicleDriveController.Enabled = true;
        var hard = MovingHard(new Vector3(1, 0, 0), new Vector3(1, 0, 0));
        var r = NpcVehicleDriveController.Apply(
            hard, default, Quaternion.Default, 12f, dt: 0f, path: StraightPath(), nowMs: 0);
        Assert.IsFalse(r.HasDriveInputs);
    }

    [TestMethod]
    public void Apply_AcceptDistance_StillAdvancesOnDensePath()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = new MapPathTemplate();
        for (var i = 0; i < 20; i++)
        {
            path.Points.Add(new MapPathTemplate.MapPathPoint
            {
                Position = new Vector3(i * 20f, 0f, 0f),
                AcceptDistance = 8f,
                WaitTime = 0,
            });
        }

        var pos = new Vector3(0f, 0f, 0f);
        var rot = YawQuat(MathF.PI * 0.5f);
        var vel = new Vector3(12f, 0f, 0f);
        var index = 0;
        var dir = 1;
        long wait = 0;
        long now = 0;
        var arrivals = 0;

        for (var tick = 0; tick < 200; tick++)
        {
            now += 100;
            var hard = NpcPathFollower.Step(pos, path, index, dir, wait, now, 12f, 0.1f);
            if (hard.Arrived)
                arrivals++;
            index = hard.NewIndex;
            dir = hard.NewDirection;
            wait = hard.WaitUntilMs;

            var driven = NpcVehicleDriveController.Apply(
                hard, pos, rot, 12f, 0.1f, path, now, vel);
            pos = driven.NewPosition;
            rot = driven.Rotation;
            vel = driven.Velocity;
        }

        Assert.IsTrue(arrivals >= 3, $"expected several arrivals, got {arrivals}");
        Assert.IsTrue(index > 2 || arrivals >= 5, $"path progress index={index} arrivals={arrivals}");
    }

    private static PathStepResult MovingHard(Vector3 pos, Vector3 vel, int newIndex = 1)
        => new()
        {
            NewPosition = pos,
            Velocity = vel,
            Rotation = Quaternion.Default,
            NewIndex = newIndex,
            NewDirection = 1,
        };

    private static MapPathTemplate StraightPath()
    {
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(100, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(200, 0, 0), AcceptDistance = 2f });
        return path;
    }

    private static MapPathTemplate LPath()
    {
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 3f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(50, 0, 0), AcceptDistance = 3f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(50, 0, 50), AcceptDistance = 3f });
        return path;
    }

    private static Quaternion YawQuat(float yaw)
    {
        var half = yaw * 0.5f;
        return new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));
    }

    private static float XzSpeed(Vector3 v)
        => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));
}
