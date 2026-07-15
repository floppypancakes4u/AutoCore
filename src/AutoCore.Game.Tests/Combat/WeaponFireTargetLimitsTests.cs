using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class WeaponFireTargetLimitsTests
{
    [TestMethod]
    public void Default_NoSprayBits_IsOne()
    {
        Assert.AreEqual(1, WeaponFireTargetLimits.GetMaxTargets(flags: 0, sprayTargets: 5));
    }

    [TestMethod]
    public void SprayBit0_UsesSprayTargets_MinOne()
    {
        Assert.AreEqual(3, WeaponFireTargetLimits.GetMaxTargets(flags: 0x01, sprayTargets: 3));
        Assert.AreEqual(1, WeaponFireTargetLimits.GetMaxTargets(flags: 0x01, sprayTargets: 0));
    }

    [TestMethod]
    public void FlagBit6_WithoutSpray_IsOneHundred()
    {
        Assert.AreEqual(100, WeaponFireTargetLimits.GetMaxTargets(flags: 0x40, sprayTargets: 0));
    }

    [TestMethod]
    public void SprayBitTakesPrecedenceOverBit6()
    {
        Assert.AreEqual(4, WeaponFireTargetLimits.GetMaxTargets(flags: 0x41, sprayTargets: 4));
    }

    [TestMethod]
    public void CapAtOneHundred()
    {
        Assert.AreEqual(100, WeaponFireTargetLimits.GetMaxTargets(flags: 0x01, sprayTargets: 200));
    }
}
