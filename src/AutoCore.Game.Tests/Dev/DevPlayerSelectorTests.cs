using AutoCore.Sector.Dev;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Dev;

[TestClass]
public class DevPlayerSelectorTests
{
    [TestMethod]
    public void Select_UsesSingleConnectedCharacterByDefault()
    {
        var character = Character("Floppy");

        var selected = DevPlayerSelector.Select(new[] { character }, null);

        Assert.AreSame(character, selected);
    }

    [TestMethod]
    public void Select_RequiresCharacterNameWhenMultipleCharactersAreConnected()
    {
        var characters = new[] { Character("Floppy"), Character("Other") };

        var selected = DevPlayerSelector.Select(characters, "other");

        Assert.AreEqual("Other", selected.CharacterName);
        Assert.ThrowsException<InvalidOperationException>(() => DevPlayerSelector.Select(characters, null));
    }

    [TestMethod]
    public void Select_ReportsMissingCharacter()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => DevPlayerSelector.Select(new[] { Character("Floppy") }, "Missing"));

        Assert.AreEqual("No connected character named 'Missing' was found.", ex.Message);
    }

    private static DevConnectedCharacter Character(string name)
    {
        return new DevConnectedCharacter(1, "admin", name, 52, null);
    }
}
