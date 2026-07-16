using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

/// <summary>
/// Golden vectors from <c>docs/reconstruction/physics/drive-controller-spec.md</c>
/// (<c>CVOGVehicle::MoveToTarget3DPoint</c> @ <c>0x004fc650</c>).
/// </summary>
[TestClass]
public class VehicleDriveControllerPhysicsTests
{
    private const float Tol = 1e-3f;

    // Common chassis setup from the spec: origin, right=+X, forward=+Z.
    private static readonly Vector3 Origin = new(0f, 0f, 0f);
    private static readonly Vector3 Right = new(1f, 0f, 0f);
    private static readonly Vector3 Forward = new(0f, 0f, 1f);
    private const float AcceptDist = 3f;
    private const float CruiseScale = 1f;
    private const bool AllowReverse = true;

    [TestMethod]
    public void Golden1_StraightAhead_EaseThrottle_DeadbandSteer()
    {
        // aim (0,0,20), vel (0,0,10) → thr -0.6667, steer 0, sharp 0
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            Origin, Right, Forward,
            velocity: new Vector3(0f, 0f, 10f),
            aim: new Vector3(0f, 0f, 20f),
            AcceptDist, CruiseScale, AllowReverse);

        Assert.AreEqual(-0.6667f, thr, Tol);
        Assert.AreEqual(0f, steer, Tol);
        Assert.AreEqual((byte)0, sharp);
    }

    [TestMethod]
    public void Golden2_HardLeft45_ClampedSteer()
    {
        // aim (-14,0,14), vel (0,0,8) → thr -0.660, steer +1, sharp 0
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            Origin, Right, Forward,
            velocity: new Vector3(0f, 0f, 8f),
            aim: new Vector3(-14f, 0f, 14f),
            AcceptDist, CruiseScale, AllowReverse);

        Assert.AreEqual(-0.660f, thr, Tol);
        Assert.AreEqual(1.000f, steer, Tol);
        Assert.AreEqual((byte)0, sharp);
    }

    [TestMethod]
    public void Golden3_FacingAway_ReverseFullThrottle_DeadbandSpin()
    {
        // aim (0,0,-20), vel 0 → thr +1, steer -1, sharp 0 (base flips, speed≤5)
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            Origin, Right, Forward,
            velocity: new Vector3(0f, 0f, 0f),
            aim: new Vector3(0f, 0f, -20f),
            AcceptDist, CruiseScale, AllowReverse);

        Assert.AreEqual(1.000f, thr, Tol);
        Assert.AreEqual(-1.000f, steer, Tol);
        Assert.AreEqual((byte)0, sharp);
    }

    [TestMethod]
    public void Golden4_HighSpeedSharpRight()
    {
        // aim (20,0,10), vel (0,0,20) → thr -0.745, steer -1, sharp 1
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            Origin, Right, Forward,
            velocity: new Vector3(0f, 0f, 20f),
            aim: new Vector3(20f, 0f, 10f),
            AcceptDist, CruiseScale, AllowReverse);

        Assert.AreEqual(-0.745f, thr, Tol);
        Assert.AreEqual(-1.000f, steer, Tol);
        Assert.AreEqual((byte)1, sharp);
    }
}
