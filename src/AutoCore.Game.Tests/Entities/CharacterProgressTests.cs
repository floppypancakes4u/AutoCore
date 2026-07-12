using AutoCore.Game.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

[TestClass]
public class CharacterProgressTests
{
    [TestMethod]
    public void Experience_DefaultZero_WithoutDbData()
    {
        var character = new Character();
        Assert.AreEqual(0, character.Experience);
        Assert.AreEqual(1, character.Level);
        Assert.AreEqual(0, character.SkillPoints);
    }

    [TestMethod]
    public void SetExperience_WithoutDbData_IsNoOp()
    {
        var character = new Character();
        character.SetExperience(500);
        character.SetLevel(5);
        Assert.AreEqual(0, character.Experience);
        Assert.AreEqual(1, character.Level);
    }

    [TestMethod]
    public void SetExperience_AndLevel_UpdateDbData()
    {
        var character = new Character();
        character.SetCoid(11, true);
        character.AttachTestDataForTests("XpChar");

        character.SetExperience(12345);
        character.SetLevel(7);
        character.SetSkillPoints(3);
        character.SetAttributePoints(4);
        character.SetResearchPoints(2);

        Assert.AreEqual(12345, character.Experience);
        Assert.AreEqual(7, character.Level);
        Assert.AreEqual(3, character.SkillPoints);
        Assert.AreEqual(4, character.AttributePoints);
        Assert.AreEqual(2, character.ResearchPoints);
    }

    [TestMethod]
    public void SetExperience_ClampsNegativeToZero()
    {
        var character = new Character();
        character.SetCoid(12, true);
        character.AttachTestDataForTests();
        character.SetExperience(-50);
        Assert.AreEqual(0, character.Experience);
    }
}
