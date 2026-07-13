using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// High regression coverage for player remote pose streaming.
/// Server re-broadcast of frozen pose + client hard-snap (FUN_0053EEC0) caused choppy remotes;
/// AdvanceNetworkPose dead-reckons between C2S VehicleMoved so keep-dirty streams advance.
/// </summary>
[TestClass]
public class PlayerVehicleNetworkPoseTests
{
    private const float Tol = 1e-4f;

    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
    }

    #region Core advance behavior

    [TestMethod]
    public void AdvanceNetworkPose_WithLinearVelocity_MovesPositionByVTimesDt()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_001, true);
        vehicle.ApplyServerMove(
            new Vector3(10f, 0f, 20f),
            Quaternion.Default,
            new Vector3(12f, 0f, 0f));

        var advanced = vehicle.AdvanceNetworkPose(0.05f);

        Assert.IsTrue(advanced, "Moving vehicle must advance.");
        Assert.AreEqual(10f + 12f * 0.05f, vehicle.Position.X, Tol);
        Assert.AreEqual(0f, vehicle.Position.Y, Tol);
        Assert.AreEqual(20f, vehicle.Position.Z, Tol);
        Assert.AreEqual(12f, vehicle.Velocity.X, Tol);
        Assert.AreEqual(0f, vehicle.Velocity.Y, Tol);
        Assert.AreEqual(0f, vehicle.Velocity.Z, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_With3DVelocity_MovesAllAxes()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_011, true);
        vehicle.ApplyServerMove(
            new Vector3(1f, 2f, 3f),
            Quaternion.Default,
            new Vector3(4f, -5f, 6f));

        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.1f));

        Assert.AreEqual(1f + 0.4f, vehicle.Position.X, Tol);
        Assert.AreEqual(2f - 0.5f, vehicle.Position.Y, Tol);
        Assert.AreEqual(3f + 0.6f, vehicle.Position.Z, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_WithAngularVelocityY_RotatesYaw()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_002, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            velocity: default);
        var facing = new Quaternion(0f, MathF.Sin(0.1f), 0f, MathF.Cos(0.1f));
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), facing, new Vector3(0.1f, 0f, 0f), dt: 0.1f);
        var yawBefore = Yaw(vehicle.Rotation);

        vehicle.AdvanceNetworkPose(0.05f);

        var dYaw = NormalizeRadians(Yaw(vehicle.Rotation) - yawBefore);
        Assert.IsTrue(MathF.Abs(dYaw) > 0.001f,
            $"Expected yaw to change under angular velocity; dYaw={dYaw}");
    }

    [TestMethod]
    public void AdvanceNetworkPose_PureYawRate_ZeroLinear_RotatesWithoutTranslating()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_012, true);
        vehicle.ApplyServerMove(
            new Vector3(5f, 1f, 7f),
            Quaternion.Default,
            velocity: default);
        vehicle.SetAngularVelocityForTests(new Vector3(0f, 2f, 0f)); // rad/s about Y

        var yawBefore = Yaw(vehicle.Rotation);
        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.05f));

        Assert.AreEqual(5f, vehicle.Position.X, Tol, "Pure spin must not translate X.");
        Assert.AreEqual(1f, vehicle.Position.Y, Tol);
        Assert.AreEqual(7f, vehicle.Position.Z, Tol);
        Assert.AreEqual(2f * 0.05f, NormalizeRadians(Yaw(vehicle.Rotation) - yawBefore), 1e-3f);
    }

    [TestMethod]
    public void AdvanceNetworkPose_NonIdentityRotation_ComposesYawWithoutDroppingPitchHint()
    {
        // Start with a non-pure-yaw quaternion (X component) and spin about Y; composition path
        // (MultiplyQuaternion) must run and keep a non-zero X after a small yaw delta.
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_013, true);
        var tilted = new Quaternion(0.1f, 0f, 0f, MathF.Sqrt(1f - 0.1f * 0.1f));
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), tilted, velocity: default);
        vehicle.SetAngularVelocityForTests(new Vector3(0f, 1f, 0f));

        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.05f));

        Assert.IsTrue(MathF.Abs(vehicle.Rotation.X) > 0.01f,
            "Yaw composition must preserve non-yaw orientation components (MultiplyQuaternion path).");
        var lenSq = (vehicle.Rotation.X * vehicle.Rotation.X)
            + (vehicle.Rotation.Y * vehicle.Rotation.Y)
            + (vehicle.Rotation.Z * vehicle.Rotation.Z)
            + (vehicle.Rotation.W * vehicle.Rotation.W);
        Assert.AreEqual(1f, lenSq, 1e-3f, "Result quaternion should remain unit length.");
    }

    [TestMethod]
    public void AdvanceNetworkPose_LinearOnly_DoesNotSpinIdentityRotation()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_014, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(10f, 0f, 0f));
        // ApplyServerMove without dt clears angVel.

        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.05f));

        Assert.AreEqual(0f, vehicle.Rotation.X, Tol);
        Assert.AreEqual(0f, vehicle.Rotation.Y, Tol);
        Assert.AreEqual(0f, vehicle.Rotation.Z, Tol);
        Assert.AreEqual(1f, vehicle.Rotation.W, Tol);
        Assert.AreEqual(0.5f, vehicle.Position.X, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_NonYawAngularOnly_StillAdvancesAndDirties()
    {
        // angSq above eps via w.X but |w.Y| below yaw threshold → translate skipped, yaw skipped,
        // still return true + dirty (keep stream alive for spinning debris-style cases).
        var connection = ScopeGhost(out var vehicle, 91_015);
        _ = connection;
        var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
        ghostInfo.UpdateMask = 0;

        vehicle.ApplyServerMove(new Vector3(1f, 0f, 1f), Quaternion.Default, velocity: default);
        NetObject.CollapseDirtyList();
        ghostInfo.UpdateMask = 0;
        vehicle.SetAngularVelocityForTests(new Vector3(1f, 0f, 0f));

        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.05f));
        NetObject.CollapseDirtyList();

        Assert.AreEqual(1f, vehicle.Position.X, Tol);
        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask);
    }

    #endregion

    #region Ghost / stream integration

    [TestMethod]
    public void AdvanceNetworkPose_DirtiesPositionMaskOnGhost()
    {
        ScopeGhost(out var vehicle, 91_003);
        var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
        ghostInfo.UpdateMask = 0;

        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(5f, 0f, 0f));
        NetObject.CollapseDirtyList();
        ghostInfo.UpdateMask = 0;

        vehicle.AdvanceNetworkPose(0.05f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask);
    }

    [TestMethod]
    public void AdvanceNetworkPose_ThenPackUpdate_KeepsPositionMaskDirtyWhileMoving()
    {
        ScopeGhost(out var vehicle, 91_016);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(12f, 0f, 0f));
        vehicle.AdvanceNetworkPose(0.05f);

        NetObject.PIsInitialUpdate = false;
        try
        {
            var stream = new BitStream(new byte[8192], 8192);
            var ret = vehicle.Ghost.PackUpdate(null, GhostObject.PositionMask, stream);
            Assert.AreNotEqual(0UL, ret & GhostObject.PositionMask,
                "After advance, moving vehicle must still keep-dirty PositionMask for continuous stream.");
        }
        finally
        {
            NetObject.PIsInitialUpdate = false;
        }

        Assert.IsTrue(GhostVehicle.ShouldStreamPose(vehicle));
        Assert.IsTrue(GhostVehicle.IsMovingForPoseStream(vehicle));
    }

    [TestMethod]
    public void AdvanceNetworkPose_ConsecutivePacks_PositionAdvancesNotIdentical()
    {
        // Regression for the choppy-remote bug: keep-dirty must not rebroadcast the same pose.
        ScopeGhost(out var vehicle, 91_017);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(20f, 0f, 0f));

        var p0 = ReadPackedPositionX(vehicle);
        vehicle.AdvanceNetworkPose(0.05f);
        var p1 = ReadPackedPositionX(vehicle);
        vehicle.AdvanceNetworkPose(0.05f);
        var p2 = ReadPackedPositionX(vehicle);

        Assert.AreEqual(0f, p0, Tol);
        Assert.AreEqual(1f, p1, Tol);
        Assert.AreEqual(2f, p2, Tol);
        Assert.AreNotEqual(p0, p1);
        Assert.AreNotEqual(p1, p2);
    }

    [TestMethod]
    public void AdvanceNetworkPose_NoGhost_StillAdvancesPose()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_009, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(4f, 0f, 0f));

        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.1f));
        Assert.AreEqual(0.4f, vehicle.Position.X, Tol);
    }

    #endregion

    #region Guards and authority

    [TestMethod]
    public void AdvanceNetworkPose_ZeroVelocity_DoesNotMoveOrDirty()
    {
        ScopeGhost(out var vehicle, 91_004);
        var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
        ghostInfo.UpdateMask = 0;

        vehicle.ApplyServerMove(
            new Vector3(3f, 0f, 4f),
            Quaternion.Default,
            velocity: default);
        NetObject.CollapseDirtyList();
        ghostInfo.UpdateMask = 0;

        var advanced = vehicle.AdvanceNetworkPose(0.05f);
        NetObject.CollapseDirtyList();

        Assert.IsFalse(advanced);
        Assert.AreEqual(3f, vehicle.Position.X, Tol);
        Assert.AreEqual(4f, vehicle.Position.Z, Tol);
        Assert.AreEqual(0UL, ghostInfo.UpdateMask & GhostObject.PositionMask);
    }

    [TestMethod]
    public void AdvanceNetworkPose_SubEpsilonMotion_DoesNotAdvance()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_018, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            velocity: default);
        vehicle.SetVelocityForTests(new Vector3(0.01f, 0f, 0f)); // below NetworkPoseMotionEpsilon
        vehicle.SetAngularVelocityForTests(new Vector3(0f, 0.01f, 0f));

        Assert.IsFalse(vehicle.AdvanceNetworkPose(0.05f));
        Assert.AreEqual(0f, vehicle.Position.X, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_ExactlyAtEpsilon_AdvancesLinear()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_019, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            velocity: default);
        // speed == eps → speedSq == eps*eps → condition is <= so does NOT advance.
        vehicle.SetVelocityForTests(new Vector3(Vehicle.NetworkPoseMotionEpsilon, 0f, 0f));
        Assert.IsFalse(vehicle.AdvanceNetworkPose(0.05f));

        // Just above eps.
        vehicle.SetVelocityForTests(new Vector3(Vehicle.NetworkPoseMotionEpsilon + 0.001f, 0f, 0f));
        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.05f));
    }

    [TestMethod]
    public void AdvanceNetworkPose_PreservesThrottleAndSteering()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_005, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(8f, 0f, 0f),
            dt: 0.05f);
        vehicle.Acceleration = 0.75f;
        vehicle.Steering = -0.4f;

        vehicle.AdvanceNetworkPose(0.05f);

        Assert.AreEqual(0.75f, vehicle.Acceleration, Tol);
        Assert.AreEqual(-0.4f, vehicle.Steering, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_ThenAuthorityMove_AuthorityWins()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_006, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(10f, 0f, 0f));

        vehicle.AdvanceNetworkPose(0.05f);
        Assert.AreEqual(0.5f, vehicle.Position.X, Tol);

        var authorityPos = new Vector3(0.4f, 0f, 0f);
        vehicle.ApplyServerMove(authorityPos, Quaternion.Default, new Vector3(10f, 0f, 0f));

        Assert.AreEqual(authorityPos, vehicle.Position);
    }

    [TestMethod]
    public void AdvanceNetworkPose_InvalidOrHugeDt_NoOp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_007, true);
        vehicle.ApplyServerMove(
            new Vector3(1f, 0f, 1f),
            Quaternion.Default,
            new Vector3(10f, 0f, 0f));

        Assert.IsFalse(vehicle.AdvanceNetworkPose(0f));
        Assert.IsFalse(vehicle.AdvanceNetworkPose(-0.01f));
        Assert.IsFalse(vehicle.AdvanceNetworkPose(1f), "dt >= 1 must be rejected");
        Assert.IsFalse(vehicle.AdvanceNetworkPose(2f));
        Assert.AreEqual(1f, vehicle.Position.X, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_NearOneSecondDt_StillAdvances()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_020, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(1f, 0f, 0f));

        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.999f));
        Assert.AreEqual(0.999f, vehicle.Position.X, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_Corpse_NoOp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_008, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(10f, 0f, 0f));
        vehicle.OnDeath(DeathType.Silent);

        Assert.IsTrue(vehicle.IsCorpse);
        Assert.IsFalse(vehicle.AdvanceNetworkPose(0.05f));
        Assert.AreEqual(0f, vehicle.Position.X, Tol);
    }

    [TestMethod]
    public void AdvanceNetworkPose_ConsecutiveTicks_AccumulateWithoutSnapBack()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_010, true);
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(20f, 0f, 0f));

        vehicle.AdvanceNetworkPose(0.05f);
        var p1 = vehicle.Position.X;
        vehicle.AdvanceNetworkPose(0.05f);
        var p2 = vehicle.Position.X;

        Assert.AreEqual(1f, p1, Tol);
        Assert.AreEqual(2f, p2, Tol);
        Assert.IsTrue(p2 > p1);
    }

    [TestMethod]
    public void AdvanceNetworkPose_ManyTicks_MatchesVTimesTotalDt()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_021, true);
        const float speed = 15f;
        vehicle.ApplyServerMove(
            new Vector3(0f, 0f, 0f),
            Quaternion.Default,
            new Vector3(speed, 0f, 0f));

        const float dt = 0.05f;
        const int ticks = 20; // 1 second
        for (var i = 0; i < ticks; i++)
            Assert.IsTrue(vehicle.AdvanceNetworkPose(dt));

        Assert.AreEqual(speed * dt * ticks, vehicle.Position.X, 1e-3f);
    }

    [TestMethod]
    public void AdvanceNetworkPose_NegativeYawRate_RotatesOppositeDirection()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_022, true);
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), Quaternion.Default, velocity: default);
        vehicle.SetAngularVelocityForTests(new Vector3(0f, -3f, 0f));

        var yawBefore = Yaw(vehicle.Rotation);
        Assert.IsTrue(vehicle.AdvanceNetworkPose(0.1f));
        var dYaw = NormalizeRadians(Yaw(vehicle.Rotation) - yawBefore);
        Assert.IsTrue(dYaw < -0.2f, $"Expected negative yaw delta, got {dYaw}");
    }

    #endregion

    #region Helpers

    private static TNLConnection ScopeGhost(out Vehicle vehicle, long coid)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.CreateGhost();
        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(vehicle.Ghost);

        var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        ghostInfo.UpdateMask = 0;
        return connection;
    }

    private static float ReadPackedPositionX(Vehicle vehicle)
    {
        // PackUpdate reads live vehicle.Position for the pose block; use entity field as oracle
        // for consecutive-advance regression (same value that would hit the wire).
        return vehicle.Position.X;
    }

    private static float Yaw(Quaternion q)
    {
        var siny = 2f * ((q.W * q.Y) + (q.X * q.Z));
        var cosy = 1f - (2f * ((q.Y * q.Y) + (q.X * q.X)));
        return MathF.Atan2(siny, cosy);
    }

    private static float NormalizeRadians(float a)
    {
        while (a > MathF.PI)
            a -= MathF.PI * 2f;
        while (a < -MathF.PI)
            a += MathF.PI * 2f;
        return a;
    }

    #endregion
}
