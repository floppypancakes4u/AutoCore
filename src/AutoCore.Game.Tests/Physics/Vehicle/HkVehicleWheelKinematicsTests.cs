using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Wheel spin from preUpdate 0x64cf20 Loop 3:
/// <c>ω = (longContactVel + chassisLongVel) / radius</c>,
/// <c>angle += dt · ω</c>.
/// Evidence: docs/reconstruction/physics/verified/fn_0064cf20_preUpdate.md
/// </summary>
[TestClass]
public class HkVehicleWheelKinematicsTests
{
    private const float Radius = 0.4f;
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void ComputeSpinSpeed_LongVelOverRadius()
    {
        // Only long contact vel: ω = (2 + 0) / 0.4 = 5
        float ω = HkVehicleWheelKinematics.ComputeSpinSpeed(
            longContactVel: 2f,
            chassisLongVel: 0f,
            radius: Radius);

        Assert.AreEqual(5f, ω, 1e-5f);
    }

    [TestMethod]
    public void ComputeSpinSpeed_AddsChassisLongVel()
    {
        // ω = (1.2 + 0.8) / 0.4 = 5
        float ω = HkVehicleWheelKinematics.ComputeSpinSpeed(
            longContactVel: 1.2f,
            chassisLongVel: 0.8f,
            radius: Radius);

        Assert.AreEqual(5f, ω, 1e-5f);
    }

    [TestMethod]
    public void ComputeSpinSpeed_NegativeLongVel_NegativeSpin()
    {
        // Reverse: ω = (−4 + 0) / 0.4 = −10
        float ω = HkVehicleWheelKinematics.ComputeSpinSpeed(
            longContactVel: -4f,
            chassisLongVel: 0f,
            radius: Radius);

        Assert.AreEqual(-10f, ω, 1e-5f);
    }

    [TestMethod]
    public void ComputeSpinSpeed_NonPositiveRadius_ReturnsZero()
    {
        Assert.AreEqual(0f, HkVehicleWheelKinematics.ComputeSpinSpeed(2f, 1f, 0f));
        Assert.AreEqual(0f, HkVehicleWheelKinematics.ComputeSpinSpeed(2f, 1f, -0.1f));
    }

    [TestMethod]
    public void IntegrateSpinAngle_AddsDtTimesSpin()
    {
        // angle' = 1.0 + (1/60)*10 = 1 + 1/6
        float next = HkVehicleWheelKinematics.IntegrateSpinAngle(
            spinAngle: 1f,
            spinSpeed: 10f,
            dt: Dt);

        Assert.AreEqual(1f + Dt * 10f, next, 1e-6f);
    }

    [TestMethod]
    public void IntegrateSpinAngle_ZeroDt_Unchanged()
    {
        Assert.AreEqual(3.5f, HkVehicleWheelKinematics.IntegrateSpinAngle(3.5f, 99f, 0f), 1e-6f);
    }

    [TestMethod]
    public void IntegrateSpin_WhenEnabled_WritesSpinAndAdvancesAngle()
    {
        float spin = 0f;
        float angle = 0.5f;

        HkVehicleWheelKinematics.IntegrateSpin(
            ref spin,
            ref angle,
            longContactVel: 2f,
            chassisLongVel: 0f,
            radius: Radius,
            dt: Dt,
            integrate: true);

        Assert.AreEqual(5f, spin, 1e-5f);                 // 2/0.4
        Assert.AreEqual(0.5f + Dt * 5f, angle, 1e-6f);
    }

    [TestMethod]
    public void IntegrateSpin_WhenDisabled_ZerosSpin_LeavesAngle()
    {
        float spin = 99f;
        float angle = 1.25f;

        HkVehicleWheelKinematics.IntegrateSpin(
            ref spin,
            ref angle,
            longContactVel: 10f,
            chassisLongVel: 5f,
            radius: Radius,
            dt: Dt,
            integrate: false);

        Assert.AreEqual(0f, spin, 1e-6f);
        Assert.AreEqual(1.25f, angle, 1e-6f);
    }

    [TestMethod]
    public void IntegrateSpin_MultipleSteps_AccumulatesAngle()
    {
        float spin = 0f;
        float angle = 0f;
        const float longVel = 0.4f; // ω = 1 rad/s at r=0.4

        for (var i = 0; i < 60; i++)
        {
            HkVehicleWheelKinematics.IntegrateSpin(
                ref spin, ref angle,
                longContactVel: longVel,
                chassisLongVel: 0f,
                radius: Radius,
                dt: Dt,
                integrate: true);
        }

        Assert.AreEqual(1f, spin, 1e-5f);
        Assert.AreEqual(1f, angle, 1e-3f); // 60 * (1/60) * 1 ≈ 1 rad
    }
}
