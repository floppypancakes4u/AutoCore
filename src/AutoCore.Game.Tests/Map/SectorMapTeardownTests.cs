using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Regression coverage for the disconnect crash "This object is not on the map!"
/// (SectorMap.LeaveMap). EnterMap/LeaveMap must be idempotent so a Map/Objects desync
/// cannot throw out of the MainLoop tick during teardown, and ResetLocalWorldToAuthored
/// must never orphan a still-present character.
/// </summary>
[TestClass]
public class SectorMapTeardownTests
{
    private static SectorMap CreateTestMap(int continentId = 9901) =>
        SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = continentId,
                MapFileName = $"tm_sectormap_teardown_{continentId}",
                DisplayName = "test",
                IsTown = false,
                IsPersistent = true,
            },
            new Vector4(0, 0, 0, 0));

    private static Character CreateCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        return character;
    }

    [TestMethod]
    public void SetMapNull_OnOrphanedCharacter_DoesNotThrow()
    {
        var map = CreateTestMap();
        var character = CreateCharacter(1001);
        character.SetMap(map);

        // Reproduce the crash state: a reset's Objects.Clear() drops the character but leaves
        // its Map reference pointing here (SetMap(null) is deliberately skipped for characters).
        map.Objects.Remove(character.ObjectId);
        Assert.AreEqual(map, character.Map, "Precondition: character still references the map.");

        // Before the fix this threw InvalidOperationException out of SetMap -> MainLoop.
        character.SetMap(null);

        Assert.IsNull(character.Map, "Teardown must complete and clear the character's map.");
    }

    [TestMethod]
    public void ResetLocalWorldToAuthored_WithCharacterPresent_DoesNotOrphan()
    {
        var map = CreateTestMap();
        var character = CreateCharacter(1002);
        character.SetMap(map);

        // Simulate PlayerCount drift: reset fires while a character is still on the map.
        map.ResetLocalWorldToAuthored();

        Assert.IsTrue(map.Objects.ContainsKey(character.ObjectId),
            "Reset must skip while a character is present; it must not drop the character from Objects.");
        Assert.AreEqual(map, character.Map, "Character's map reference must stay consistent with Objects.");
    }

    [TestMethod]
    public void EnterMap_SameObjectTwice_DoesNotThrowOrDoubleCount()
    {
        var map = CreateTestMap();
        var character = CreateCharacter(1003);
        character.SetMap(map);

        Assert.AreEqual(1, map.PlayerCount);

        // Re-enter (reconnect/desync). Before the fix this threw "already on the map".
        map.EnterMap(character);

        Assert.AreEqual(1, map.PlayerCount, "Re-entry must not double-count the player.");
        Assert.AreEqual(1, map.Players.Count, "Re-entry must not duplicate the player entry.");
    }

    [TestMethod]
    public void EnterThenLeave_NormalLifecycle_RemovesCharacter()
    {
        var map = CreateTestMap();
        var character = CreateCharacter(1004);

        character.SetMap(map);
        Assert.IsTrue(map.Objects.ContainsKey(character.ObjectId));
        Assert.AreEqual(1, map.PlayerCount);

        character.SetMap(null);

        Assert.IsFalse(map.Objects.ContainsKey(character.ObjectId));
        Assert.AreEqual(0, map.PlayerCount);
        Assert.IsNull(character.Map);
    }
}
