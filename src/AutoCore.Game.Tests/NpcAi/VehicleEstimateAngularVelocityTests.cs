using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

[TestClass]
public class VehicleEstimateAngularVelocityTests
{
    [TestMethod]
    public void EstimateAngularVelocity_Identity_IsZero()
    {
        var q = Quaternion.Default;
        var w = Vehicle.EstimateAngularVelocity(q, q, 0.1f);
        Assert.AreEqual(0f, w.X, 1e-4f);
        Assert.AreEqual(0f, w.Y, 1e-4f);
        Assert.AreEqual(0f, w.Z, 1e-4f);
    }

    [TestMethod]
    public void EstimateAngularVelocity_YawOnly_HasDominantY()
    {
        var from = Yaw(0f);
        var to = Yaw(MathF.PI * 0.5f); // 90° about Y
        var w = Vehicle.EstimateAngularVelocity(from, to, 0.1f);
        Assert.IsTrue(MathF.Abs(w.Y) > MathF.Abs(w.X) && MathF.Abs(w.Y) > MathF.Abs(w.Z),
            $"expected yaw-dominant, got {w.X},{w.Y},{w.Z}");
        // ~ (π/2) / 0.1 ≈ 15.7 rad/s
        Assert.AreEqual(MathF.PI * 0.5f / 0.1f, MathF.Abs(w.Y), 0.5f);
    }

    [TestMethod]
    public void EstimateAngularVelocity_TinyDt_DoesNotExplode()
    {
        var from = Yaw(0f);
        var to = Yaw(0.01f);
        var w = Vehicle.EstimateAngularVelocity(from, to, 0f); // clamps to 1e-4
        Assert.IsTrue(float.IsFinite(w.X) && float.IsFinite(w.Y) && float.IsFinite(w.Z));
    }

    [TestMethod]
    public void ApplyServerMove_WithDt_SetsFullAngularVelocity()
    {
        var v = new Vehicle();
        v.Rotation = Yaw(0f);
        var next = Yaw(0.2f);
        v.ApplyServerMove(new Vector3(1, 0, 0), next, new Vector3(5, 0, 0), dt: 0.1f,
            driveThrottle: 1f, driveSteering: 0f, sharpTurn: 0);
        Assert.IsTrue(MathF.Abs(v.AngularVelocity.Y) > 0.5f, $"angVel Y={v.AngularVelocity.Y}");
        Assert.AreEqual(1f, v.Acceleration, 0.01f);
        Assert.AreEqual(0f, v.Steering, 0.01f);
    }

    private static Quaternion Yaw(float yaw)
    {
        var h = yaw * 0.5f;
        return new Quaternion(0f, MathF.Sin(h), 0f, MathF.Cos(h));
    }
}
