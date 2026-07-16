using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Bit-level port tests for <c>hkAngularVelocityDamper_update @ 0x64d810</c>.
/// </summary>
[TestClass]
public class HkVehicleVelocityDamperTests
{
    private const float Eps = 1e-6f;

    [TestMethod]
    public void DampenAngular_BelowThreshold_UsesNormalDamping()
    {
        // |w| = 1, thr = 4 → normal
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            wx: 1f, wy: 0f, wz: 0f, ww: 0.5f,
            dt: 0.1f,
            normalDamp: 2f,
            collisionDamp: 10f,
            threshold: 4f);

        // f = max(0, 1 - 2*0.1) = 0.8
        Assert.AreEqual(0.8f, wx, Eps);
        Assert.AreEqual(0f, wy, Eps);
        Assert.AreEqual(0f, wz, Eps);
        Assert.AreEqual(0.4f, ww, Eps);
    }

    [TestMethod]
    public void DampenAngular_AboveThreshold_UsesCollisionDamping()
    {
        // |w| = 5, thr = 4 → collision
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            wx: 3f, wy: 4f, wz: 0f, ww: 1f,
            dt: 0.05f,
            normalDamp: 1f,
            collisionDamp: 8f,
            threshold: 4f);

        // f = max(0, 1 - 8*0.05) = 0.6
        Assert.AreEqual(1.8f, wx, Eps);
        Assert.AreEqual(2.4f, wy, Eps);
        Assert.AreEqual(0f, wz, Eps);
        Assert.AreEqual(0.6f, ww, Eps);
    }

    [TestMethod]
    public void DampenAngular_ExactlyAtThreshold_UsesNormalDamping()
    {
        // w·w == thr² → branch is <= so normal
        var (wx, _, _, _) = HkVehicleVelocityDamper.DampenAngular(
            wx: 4f, wy: 0f, wz: 0f, ww: 0f,
            dt: 0.1f,
            normalDamp: 1f,
            collisionDamp: 100f,
            threshold: 4f);

        // f = 1 - 1*0.1 = 0.9 (would be 0 if collision were used)
        Assert.AreEqual(3.6f, wx, Eps);
    }

    [TestMethod]
    public void DampenAngular_RateDtExceedsOne_ClampsScaleToZero()
    {
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            wx: 1f, wy: 2f, wz: 3f, ww: 4f,
            dt: 1f,
            normalDamp: 2f,
            collisionDamp: 2f,
            threshold: 100f);

        // f = max(0, 1 - 2*1) = 0
        Assert.AreEqual(0f, wx, Eps);
        Assert.AreEqual(0f, wy, Eps);
        Assert.AreEqual(0f, wz, Eps);
        Assert.AreEqual(0f, ww, Eps);
    }

    [TestMethod]
    public void DampenAngular_ScalesAllFourComponents()
    {
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            wx: 2f, wy: -4f, wz: 6f, ww: -8f,
            dt: 0.25f,
            normalDamp: 2f,
            collisionDamp: 9f,
            threshold: 100f);

        // f = 1 - 2*0.25 = 0.5
        Assert.AreEqual(1f, wx, Eps);
        Assert.AreEqual(-2f, wy, Eps);
        Assert.AreEqual(3f, wz, Eps);
        Assert.AreEqual(-4f, ww, Eps);
    }

    [TestMethod]
    public void DampenAngular_ZeroAngular_RemainsZero()
    {
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            0f, 0f, 0f, 0f,
            dt: 0.016f,
            normalDamp: 1.5f,
            collisionDamp: 8f,
            threshold: 4f);

        Assert.AreEqual(0f, wx, Eps);
        Assert.AreEqual(0f, wy, Eps);
        Assert.AreEqual(0f, wz, Eps);
        Assert.AreEqual(0f, ww, Eps);
    }

    [TestMethod]
    public void DampenAngular_FourthSlotNotInMagnitudeGate()
    {
        // xyz zero → always normal branch even if ww is huge
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            wx: 0f, wy: 0f, wz: 0f, ww: 1000f,
            dt: 0.1f,
            normalDamp: 1f,
            collisionDamp: 50f,
            threshold: 0.001f);

        // f = 1 - 1*0.1 = 0.9 (normal, not collision)
        Assert.AreEqual(0f, wx, Eps);
        Assert.AreEqual(0f, wy, Eps);
        Assert.AreEqual(0f, wz, Eps);
        Assert.AreEqual(900f, ww, Eps);
    }

    [TestMethod]
    public void DampenAngular_ZeroDt_LeavesUnchanged()
    {
        var (wx, wy, wz, ww) = HkVehicleVelocityDamper.DampenAngular(
            1.25f, -2.5f, 3.75f, 0.125f,
            dt: 0f,
            normalDamp: 9f,
            collisionDamp: 9f,
            threshold: 0f);

        Assert.AreEqual(1.25f, wx, Eps);
        Assert.AreEqual(-2.5f, wy, Eps);
        Assert.AreEqual(3.75f, wz, Eps);
        Assert.AreEqual(0.125f, ww, Eps);
    }
}
