using AutoCore.Game.Experience;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Experience;

[TestClass]
public class CharacterProgressSnapshotTests
{
    [TestMethod]
    public void Constructor_ClampsInvalidValues()
    {
        var snap = new CharacterProgressSnapshot(
            level: 0,
            experience: -5,
            skillPoints: -1,
            attributePoints: -2,
            researchPoints: -3,
            attributeTech: -1,
            attributeCombat: -2,
            attributeTheory: -3,
            attributePerception: -4);

        Assert.AreEqual((byte)1, snap.Level);
        Assert.AreEqual(0, snap.Experience);
        Assert.AreEqual((short)0, snap.SkillPoints);
        Assert.AreEqual((short)0, snap.AttributePoints);
        Assert.AreEqual((short)0, snap.ResearchPoints);
        Assert.AreEqual((short)1, snap.AttributeTech);
        Assert.AreEqual((short)1, snap.AttributeCombat);
        Assert.AreEqual((short)1, snap.AttributeTheory);
        Assert.AreEqual((short)1, snap.AttributePerception);
    }

    [TestMethod]
    public void Constructor_PreservesValidValues()
    {
        var snap = new CharacterProgressSnapshot(12, 45000, 3, 4, 5, 10, 11, 12, 13);
        Assert.AreEqual((byte)12, snap.Level);
        Assert.AreEqual(45000, snap.Experience);
        Assert.AreEqual((short)3, snap.SkillPoints);
        Assert.AreEqual((short)4, snap.AttributePoints);
        Assert.AreEqual((short)5, snap.ResearchPoints);
        Assert.AreEqual((short)10, snap.AttributeTech);
        Assert.AreEqual((short)11, snap.AttributeCombat);
        Assert.AreEqual((short)12, snap.AttributeTheory);
        Assert.AreEqual((short)13, snap.AttributePerception);
    }

    [TestMethod]
    public void Constructor_DefaultsSpentAttributesToOne()
    {
        var snap = new CharacterProgressSnapshot(2, 100);
        Assert.AreEqual(0, snap.SkillPoints);
        Assert.AreEqual(0, snap.AttributePoints);
        Assert.AreEqual(0, snap.ResearchPoints);
        Assert.AreEqual((short)1, snap.AttributeTech);
        Assert.AreEqual((short)1, snap.AttributeCombat);
        Assert.AreEqual((short)1, snap.AttributeTheory);
        Assert.AreEqual((short)1, snap.AttributePerception);
    }

    [TestMethod]
    public void Constructor_FloorsZeroSpentAttributesToOne()
    {
        var snap = new CharacterProgressSnapshot(5, 10, 0, 0, 0, 0, 0, 0, 0);
        Assert.AreEqual((short)1, snap.AttributeTech);
        Assert.AreEqual((short)1, snap.AttributeCombat);
        Assert.AreEqual((short)1, snap.AttributeTheory);
        Assert.AreEqual((short)1, snap.AttributePerception);
    }
}
