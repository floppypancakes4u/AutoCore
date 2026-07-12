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
            researchPoints: -3);

        Assert.AreEqual((byte)1, snap.Level);
        Assert.AreEqual(0, snap.Experience);
        Assert.AreEqual((short)0, snap.SkillPoints);
        Assert.AreEqual((short)0, snap.AttributePoints);
        Assert.AreEqual((short)0, snap.ResearchPoints);
    }

    [TestMethod]
    public void Constructor_PreservesValidValues()
    {
        var snap = new CharacterProgressSnapshot(12, 45000, 3, 4, 5);
        Assert.AreEqual((byte)12, snap.Level);
        Assert.AreEqual(45000, snap.Experience);
        Assert.AreEqual((short)3, snap.SkillPoints);
        Assert.AreEqual((short)4, snap.AttributePoints);
        Assert.AreEqual((short)5, snap.ResearchPoints);
    }

    [TestMethod]
    public void Constructor_DefaultsOptionalPoolsToZero()
    {
        var snap = new CharacterProgressSnapshot(2, 100);
        Assert.AreEqual(0, snap.SkillPoints);
        Assert.AreEqual(0, snap.AttributePoints);
        Assert.AreEqual(0, snap.ResearchPoints);
    }
}
