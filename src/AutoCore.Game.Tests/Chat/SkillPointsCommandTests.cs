using System.Reflection;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

[TestClass]
public class SkillPointsCommandTests
{
    private int _persistCalls;

    [TestInitialize]
    public void Init()
    {
        _persistCalls = 0;
        CharacterSkillService.PersistForTests = _ => _persistCalls++;
        CharacterLevelManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        CharacterSkillService.PersistForTests = null;
        CharacterLevelManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void SkillPoints_Query_ReportsCurrent()
    {
        var character = MakeCharacter(1);
        character.SetSkillPoints(3);

        var result = ChatCommandService.Instance.Execute(character, "/skillPoints");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "3");
    }

    [TestMethod]
    public void SkillPoints_SetAbsolute_PersistsAndPackets()
    {
        var character = MakeCharacter(2);
        character.SetSkillPoints(0);

        var result = ChatCommandService.Instance.Execute(character, "/skillPoints 25");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(25, character.SkillPoints);
        Assert.AreEqual(1, _persistCalls);
        Assert.IsTrue(result.Packets.Count >= 1);
        StringAssert.Contains(result.Message, "25");
    }

    [TestMethod]
    public void SkillPoints_Add_Increments()
    {
        var character = MakeCharacter(3);
        character.SetSkillPoints(5);

        var result = ChatCommandService.Instance.Execute(character, "/skillPoints add 10");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(15, character.SkillPoints);
    }

    [TestMethod]
    public void Skillpoints_LowercaseAlias_Works()
    {
        var character = MakeCharacter(4);
        var result = ChatCommandService.Instance.Execute(character, "/skillpoints 7");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(7, character.SkillPoints);
    }

    [TestMethod]
    public void SkillPoints_NullCharacter_Handled()
    {
        var result = ChatCommandService.Instance.Execute(null, "/skillPoints 5");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No character");
    }

    [TestMethod]
    public void SkillPoints_InvalidInput_ShowsUsage()
    {
        var character = MakeCharacter(5);
        var result = ChatCommandService.Instance.Execute(character, "/skillPoints no");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage");
    }

    [TestMethod]
    public void SkillPoints_AddInvalid_ShowsUsage()
    {
        var character = MakeCharacter(6);
        var result = ChatCommandService.Instance.Execute(character, "/skillPoints add xyz");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage");
    }

    [TestMethod]
    public void SkillPoints_AddNegative_ClampsAtZero()
    {
        var character = MakeCharacter(7);
        character.SetSkillPoints(3);
        var result = ChatCommandService.Instance.Execute(character, "/skillPoints add -100");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, character.SkillPoints);
    }

    private static Character MakeCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        var dbData = new CharacterData { Coid = coid, Name = "SkillPts", Level = 1 };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }
}
