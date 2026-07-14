using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory;

[TestClass]
public class RemoveMissionCargoCommandTests
{
    [TestMethod]
    public void RemoveMissionCargo_NoCharacter_ReturnsMessage()
    {
        var result = ChatCommandService.Instance.Execute(null, "/removeMissionCargo");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No character");
    }

    [TestMethod]
    public void RemoveMissionCargo_RemovesOnlyMissionFlaggedStacks()
    {
        var harness = new InventoryTestHarness(characterCoid: 6100);
        var character = harness.Character;

        // Mission gear
        harness.Inventory.GrantMissionCargoItem(
            11849,
            CloneBaseObjectType.QuestObject,
            "explosives",
            coid: 1001,
            characterCoid: 6100,
            quantity: 1);

        // Normal cargo (not mission)
        harness.Inventory.TryAdd(new CharacterInventoryItem(
            42,
            CloneBaseObjectType.Item,
            "normal",
            1002,
            1,
            0,
            1,
            IsMissionItem: false));

        var result = ChatCommandService.Instance.Execute(character, "/removeMissionCargo");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, harness.Inventory.CountByCbid(11849));
        Assert.AreEqual(1, harness.Inventory.CountByCbid(42), "non-mission cargo must remain");
        StringAssert.Contains(result.Message.ToLowerInvariant(), "removed");
        Assert.IsTrue(result.Packets != null && result.Packets.Count > 0);

        // Live UI clear: destroy the client object, then resync cargo grid.
        var destroy = result.Packets.OfType<InventoryDestroyItemPacket>().Single();
        Assert.AreEqual(1001L, destroy.ItemCoid);
        Assert.IsTrue(destroy.Delete);
        Assert.AreEqual(1, destroy.Quantity);
        Assert.IsTrue(result.Packets.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    public void RemoveMissionCargo_OptionalCbid_OnlyThatMissionStack()
    {
        var harness = new InventoryTestHarness(characterCoid: 6101);
        var character = harness.Character;

        harness.Inventory.GrantMissionCargoItem(
            11849, CloneBaseObjectType.QuestObject, "a", 2001, 6101, 1);
        harness.Inventory.GrantMissionCargoItem(
            5502, CloneBaseObjectType.QuestObject, "b", 2002, 6101, 1);

        var result = ChatCommandService.Instance.Execute(character, "/removeMissionCargo 11849");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, harness.Inventory.CountByCbid(11849));
        Assert.AreEqual(1, harness.Inventory.CountByCbid(5502));

        var destroy = result.Packets.OfType<InventoryDestroyItemPacket>().Single();
        Assert.AreEqual(2001L, destroy.ItemCoid);
        Assert.IsTrue(destroy.Delete);
    }
}
