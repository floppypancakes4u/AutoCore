using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Goldens for <see cref="HkVehicleSteering"/> from Ghidra decompile of
/// <c>hkDefaultSteering_update</c> @ <c>0x64f840</c> and DAT reads
/// <c>0xa10e78</c>=0.05, <c>0xaf3388</c>=20.0.
/// </summary>
[TestClass]
public class HkVehicleSteeringTests
{
    private const float Epsilon = 1e-5f;

    // --- RampStage1 (applyAction VA+0x24, rate DAT_009d54e0 * dt * {1|2}) ---

    [TestMethod]
    public void RampStage1_FromZero_UsesFactorOne()
    {
        // From 0, open-band is false → factor 1; step = (20/7) * dt
        const float dt = 1f / 60f;
        float expected = HkPhysicsConstants.SteerStage1RateBase * dt;
        Assert.AreEqual(expected, HkVehicleSteering.RampStage1(0f, 1f, dt), Epsilon);
        Assert.AreEqual(-expected, HkVehicleSteering.RampStage1(0f, -1f, dt), Epsilon);
    }

    [TestMethod]
    public void RampStage1_OffZero_UsesOpenBandFactorTwo()
    {
        // Open-band requires target strictly inside (-1,+1): current>0 && target<+1
        const float dt = 1f / 60f;
        float current = 0.1f;
        float target = 0.9f;
        float step = HkPhysicsConstants.SteerStage1RateBase * dt * HkPhysicsConstants.SteerStage1OpenBandFactor;
        Assert.AreEqual(current + step, HkVehicleSteering.RampStage1(current, target, dt), Epsilon);
    }

    [TestMethod]
    public void RampStage1_TargetAtRail_UsesFactorOne()
    {
        // target == +1 → open-band arm (current > 0 && target < +1) is false
        const float dt = 1f / 60f;
        float current = 0.1f;
        float step = HkPhysicsConstants.SteerStage1RateBase * dt; // factor 1
        Assert.AreEqual(current + step, HkVehicleSteering.RampStage1(current, 1f, dt), Epsilon);
    }

    [TestMethod]
    public void RampStage1_SnapsWhenWithinStep()
    {
        const float dt = 1f;
        // rate * 1 * 1 ≈ 2.857 → snaps any remaining delta within ±1
        Assert.AreEqual(0.5f, HkVehicleSteering.RampStage1(0f, 0.5f, dt), Epsilon);
        Assert.AreEqual(1f, HkVehicleSteering.RampStage1(0.9f, 1f, dt), Epsilon);
    }

    [TestMethod]
    public void RampStage1_NonPositiveDt_Unchanged()
    {
        Assert.AreEqual(0.3f, HkVehicleSteering.RampStage1(0.3f, 1f, 0f), Epsilon);
        Assert.AreEqual(0.3f, HkVehicleSteering.RampStage1(0.3f, 1f, -0.1f), Epsilon);
    }

    [TestMethod]
    public void RampStage1_ClampsToSteerInputRange()
    {
        const float dt = 1f;
        Assert.AreEqual(1f, HkVehicleSteering.RampStage1(0.99f, 2f, dt), Epsilon);
        Assert.AreEqual(-1f, HkVehicleSteering.RampStage1(-0.99f, -2f, dt), Epsilon);
    }

    [TestMethod]
    public void SteerStage1RateBase_MatchesCtorDatBits()
    {
        // DAT_009d54e0 = 6e db 36 40
        float fromBits = BitConverter.Int32BitsToSingle(unchecked((int)0x4036db6e));
        Assert.AreEqual(fromBits, HkPhysicsConstants.SteerStage1RateBase, 1e-7f);
        Assert.AreEqual(2f, HkPhysicsConstants.SteerStage1OpenBandFactor, Epsilon);
    }

    // --- RampSteer (applyAction mode 0x02, step DAT_00a10e78) ---

    [TestMethod]
    public void RampSteer_StepsTowardTarget_ByDefaultStep()
    {
        Assert.AreEqual(0.05f, HkVehicleSteering.RampSteer(0f, 1f), Epsilon);
        Assert.AreEqual(-0.05f, HkVehicleSteering.RampSteer(0f, -1f), Epsilon);
    }

    [TestMethod]
    public void RampSteer_SnapsWhenWithinStep()
    {
        Assert.AreEqual(0.03f, HkVehicleSteering.RampSteer(0f, 0.03f), Epsilon);
        Assert.AreEqual(0.42f, HkVehicleSteering.RampSteer(0.40f, 0.42f), Epsilon);
    }

    [TestMethod]
    public void RampSteer_CustomStep()
    {
        Assert.AreEqual(0.2f, HkVehicleSteering.RampSteer(0f, 1f, step: 0.2f), Epsilon);
        Assert.AreEqual(0.5f, HkVehicleSteering.RampSteer(0.5f, 0.5f, step: 0.1f), Epsilon);
    }

    [TestMethod]
    public void RampSteer_ClampsResultToSteerInputRange()
    {
        Assert.AreEqual(1f, HkVehicleSteering.RampSteer(0.98f, 1.5f, step: 0.05f), Epsilon);
        Assert.AreEqual(-1f, HkVehicleSteering.RampSteer(-0.98f, -1.5f, step: 0.05f), Epsilon);
    }

    // --- Mode speed factor (applyAction mode 0x02, divisor 0xaf3388 = 20) ---

    [TestMethod]
    public void ModeSpeedFactor_ZeroAtRest_RampsToOneByTwenty()
    {
        Assert.AreEqual(0f, HkVehicleSteering.ModeSpeedFactor(0f), Epsilon);
        Assert.AreEqual(0.5f, HkVehicleSteering.ModeSpeedFactor(10f), Epsilon);
        Assert.AreEqual(1f, HkVehicleSteering.ModeSpeedFactor(20f), Epsilon);
        Assert.AreEqual(1f, HkVehicleSteering.ModeSpeedFactor(40f), Epsilon);
    }

    [TestMethod]
    public void ModeSpeedFactor_UsesAbsoluteSpeed()
    {
        Assert.AreEqual(0.5f, HkVehicleSteering.ModeSpeedFactor(-10f), Epsilon);
        Assert.AreEqual(1f, HkVehicleSteering.ModeSpeedFactor(-25f), Epsilon);
    }

    // --- ComputeWheelAngles (hkDefaultSteering_update 0x64f840) ---

    [TestMethod]
    public void ComputeWheelAngles_BelowFullSpeedLimit_FullAuthority()
    {
        // maxAngle * steer, no falloff when forwardSpeed < fullSpeedLimit
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 1f,
            maxAngle: 0.6f,
            fullSpeedLimit: 12f,
            forwardSpeed: 6f,
            doesSteer: new[] { true, true, false, false });

        Assert.AreEqual(4, angles.Length);
        Assert.AreEqual(0.6f, angles[0], Epsilon);
        Assert.AreEqual(0.6f, angles[1], Epsilon);
        Assert.AreEqual(0f, angles[2], Epsilon);
        Assert.AreEqual(0f, angles[3], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_AtFullSpeedLimit_IdentityRatio()
    {
        // full == speed → r=1 → r²=1 → no reduction
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 1f,
            maxAngle: 0.6f,
            fullSpeedLimit: 12f,
            forwardSpeed: 12f,
            doesSteer: new[] { true });

        Assert.AreEqual(0.6f, angles[0], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_AboveFullSpeedLimit_QuadraticFalloff()
    {
        // speed=24, full=12 → r=0.5 → r²=0.25 → angle = 0.6 * 1 * 0.25 = 0.15
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 1f,
            maxAngle: 0.6f,
            fullSpeedLimit: 12f,
            forwardSpeed: 24f,
            doesSteer: new[] { true, false });

        Assert.AreEqual(0.15f, angles[0], Epsilon);
        Assert.AreEqual(0f, angles[1], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_QuadraticNotLinear()
    {
        // full=10, speed=20 → r=0.5 → r²=0.25 (linear would be 0.5)
        // max*steer = 1.0 → expected 0.25, not 0.5
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 1f,
            maxAngle: 1f,
            fullSpeedLimit: 10f,
            forwardSpeed: 20f,
            doesSteer: new[] { true });

        Assert.AreEqual(0.25f, angles[0], Epsilon);
        Assert.AreNotEqual(0.5f, angles[0], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_NegativeSteer_PreservesSign()
    {
        // angle = -0.4 * (12/24)² = -0.4 * 0.25 = -0.1
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: -1f,
            maxAngle: 0.4f,
            fullSpeedLimit: 12f,
            forwardSpeed: 24f,
            doesSteer: new[] { true });

        Assert.AreEqual(-0.1f, angles[0], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_ZeroSteer_ZeroAngles()
    {
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 0f,
            maxAngle: 0.6f,
            fullSpeedLimit: 12f,
            forwardSpeed: 30f,
            doesSteer: new[] { true, true, false, false });

        Assert.AreEqual(0f, angles[0], Epsilon);
        Assert.AreEqual(0f, angles[1], Epsilon);
        Assert.AreEqual(0f, angles[2], Epsilon);
        Assert.AreEqual(0f, angles[3], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_ZeroOrNegativeForwardSpeed_NoDivisionFalloff()
    {
        // forwardSpeed <= 0 must not apply r² path (guard); full authority
        var zero = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 0.5f,
            maxAngle: 0.8f,
            fullSpeedLimit: 12f,
            forwardSpeed: 0f,
            doesSteer: new[] { true });
        Assert.AreEqual(0.4f, zero[0], Epsilon);

        var reverse = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 0.5f,
            maxAngle: 0.8f,
            fullSpeedLimit: 12f,
            forwardSpeed: -5f,
            doesSteer: new[] { true });
        Assert.AreEqual(0.4f, reverse[0], Epsilon);
    }

    [TestMethod]
    public void ComputeWheelAngles_AllWheelsSteer_FourWheelSteer()
    {
        var angles = HkVehicleSteering.ComputeWheelAngles(
            steerInput: 1f,
            maxAngle: 0.5f,
            fullSpeedLimit: 10f,
            forwardSpeed: 5f,
            doesSteer: new[] { true, true, true, true });

        foreach (var a in angles)
            Assert.AreEqual(0.5f, a, Epsilon);
    }

    [TestMethod]
    public void DefaultStepConstant_MatchesGhidraDat()
    {
        Assert.AreEqual(0.05f, HkPhysicsConstants.SteerRampPerTick, Epsilon);
        Assert.AreEqual(20f, HkPhysicsConstants.SteerSpeedFactorDivisor, Epsilon);
    }
}
