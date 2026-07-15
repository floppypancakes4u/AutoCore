using System.Reflection;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

[TestClass]
public class CharacterAttributeFloorTests
{
    [TestMethod]
    public void NewCharacter_WithoutDbData_AttributesDefaultToOne()
    {
        var character = new Character();
        Assert.AreEqual((short)1, character.AttributeTech);
        Assert.AreEqual((short)1, character.AttributeCombat);
        Assert.AreEqual((short)1, character.AttributeTheory);
        Assert.AreEqual((short)1, character.AttributePerception);
    }

    [TestMethod]
    public void SetAttribute_ZeroOrNegative_ClampsToOne()
    {
        var character = MakeCharacter(1);
        character.SetAttributeTech(0);
        character.SetAttributeCombat(-5);
        character.SetAttributeTheory(0);
        character.SetAttributePerception(-1);
        Assert.AreEqual((short)1, character.AttributeTech);
        Assert.AreEqual((short)1, character.AttributeCombat);
        Assert.AreEqual((short)1, character.AttributeTheory);
        Assert.AreEqual((short)1, character.AttributePerception);
    }

    [TestMethod]
    public void Getters_FloorStoredZeroOnDbRow()
    {
        var character = MakeCharacter(2);
        // Bypass setter to simulate legacy DB zeros.
        var db = (CharacterData)typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(character)!;
        db.AttributeTech = 0;
        db.AttributeCombat = 0;
        db.AttributeTheory = 0;
        db.AttributePerception = 0;
        Assert.AreEqual((short)1, character.AttributeTech);
        Assert.AreEqual((short)1, character.AttributeCombat);
        Assert.AreEqual((short)1, character.AttributeTheory);
        Assert.AreEqual((short)1, character.AttributePerception);
    }

    private static Character MakeCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        var dbData = new CharacterData { Coid = coid, Name = "Floor", Level = 1 };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }
}
