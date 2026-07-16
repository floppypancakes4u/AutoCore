using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// calcWheelTorque handbrake rear ×0.5 (0x598040) and vestigial service-brake formula.
/// </summary>
[TestClass]
public class HkVehicleBrakeTests
{
    private const float Epsilon = 1e-6f;

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_RearAndHandbrake_HalvesTorque()
    {
        const float torque = 400f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: true, handbrakeActive: true);
        Assert.AreEqual(200f, scaled, Epsilon);
        Assert.AreEqual(torque * HkPhysicsConstants.HandbrakeRearTorqueScale, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_FrontAndHandbrake_Unchanged()
    {
        const float torque = 400f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: false, handbrakeActive: true);
        Assert.AreEqual(torque, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_RearWithoutHandbrake_Unchanged()
    {
        const float torque = 400f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: true, handbrakeActive: false);
        Assert.AreEqual(torque, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_FrontWithoutHandbrake_Unchanged()
    {
        const float torque = 123.5f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: false, handbrakeActive: false);
        Assert.AreEqual(torque, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_ZeroTorque_StaysZero()
    {
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(0f, isRear: true, handbrakeActive: true);
        Assert.AreEqual(0f, scaled, Epsilon);
    }

    [TestMethod]
    public void ApplyHandbrakeDriveTorqueScale_UsesConstantHalf()
    {
        Assert.AreEqual(0.5f, HkPhysicsConstants.HandbrakeRearTorqueScale, Epsilon);
        const float torque = 1000f;
        var scaled = HkVehicleBrake.ApplyHandbrakeDriveTorqueScale(torque, isRear: true, handbrakeActive: true);
        Assert.AreEqual(500f, scaled, Epsilon);
    }

    [TestMethod]
    public void ComputeServiceBrakeTorque_FullPedal_EqualsMaxTorque()
    {
        Assert.AreEqual(100f, HkVehicleBrake.ComputeServiceBrakeTorque(100f, 1f), Epsilon);
    }

    [TestMethod]
    public void ComputeServiceBrakeTorque_HalfPedal_HalfTorque()
    {
        Assert.AreEqual(40f, HkVehicleBrake.ComputeServiceBrakeTorque(80f, 0.5f), Epsilon);
    }

    [TestMethod]
    public void ComputeServiceBrakeTorque_ZeroPedal_ZeroTorque()
    {
        Assert.AreEqual(0f, HkVehicleBrake.ComputeServiceBrakeTorque(200f, 0f), Epsilon);
    }
}
