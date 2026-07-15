using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class CharacterAttributePoolsTests
{
    [TestMethod]
    public void GetForCombat_FloorsZeroAndNegativeToOne()
    {
        Assert.AreEqual(1, CharacterAttributePools.GetForCombat(0));
        Assert.AreEqual(1, CharacterAttributePools.GetForCombat(-5));
        Assert.AreEqual(1, CharacterAttributePools.GetForCombat(1));
    }

    [TestMethod]
    public void GetForCombat_CapsRawAt200BeforeBonus()
    {
        Assert.AreEqual(200, CharacterAttributePools.GetForCombat(200));
        Assert.AreEqual(200, CharacterAttributePools.GetForCombat(500));
        Assert.AreEqual(250, CharacterAttributePools.GetForCombat(200, 100));
        Assert.AreEqual(250, CharacterAttributePools.GetForCombat(200, 200));
    }

    [TestMethod]
    public void GetForCombat_AddsBonusWithinCap()
    {
        Assert.AreEqual(50, CharacterAttributePools.GetForCombat(40, 10));
        Assert.AreEqual(15, CharacterAttributePools.GetForCombat(10, 5));
    }

    [TestMethod]
    public void NormalizeSpent_FloorsBelowOne()
    {
        Assert.AreEqual((short)1, CharacterAttributePools.NormalizeSpent(0));
        Assert.AreEqual((short)1, CharacterAttributePools.NormalizeSpent(-3));
        Assert.AreEqual((short)7, CharacterAttributePools.NormalizeSpent(7));
        // Exactly MinSpent must pass through (boundary vs <= mutant is equivalent for value 1,
        // but value 2 must not be forced to 1)
        Assert.AreEqual((short)2, CharacterAttributePools.NormalizeSpent(2));
    }

    [TestMethod]
    public void GetForCombat_SumExactlyPoolMax_Returns250()
    {
        // 150 + 100 = 250 → hit PoolMax return path
        Assert.AreEqual(250, CharacterAttributePools.GetForCombat(150, 100));
        Assert.AreEqual(249, CharacterAttributePools.GetForCombat(149, 100));
    }
}
