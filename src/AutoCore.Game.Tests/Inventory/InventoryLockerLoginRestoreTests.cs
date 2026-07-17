using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// Login restore for locker uses CreateCharacterExtended.InventoryCoids (312 origin slots).
/// Client applies them in CVOGCharacter_ApplyCreateFromPacket after objects exist.
/// </summary>
[TestClass]
public class InventoryLockerLoginRestoreTests
{
    [TestMethod]
    public void ConfigureCharacterLocker_PlacesOriginsInLinearSlots()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1001, 0, 0, 1));
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(11, CloneBaseObjectType.Item, "B", 1002, 2, 1, 1));

        var packet = new CreateCharacterExtendedPacket();
        InventoryPacketFactory.ConfigureCharacterLocker(packet, harness.Inventory);

        Assert.AreEqual(1001, packet.InventoryCoids[0]);
        // slot = y * 6 + x = 1 * 6 + 2 = 8
        Assert.AreEqual(1002, packet.InventoryCoids[8]);
        Assert.AreEqual(-1, packet.InventoryCoids[1]);
    }

    [TestMethod]
    public void ConfigureCharacterLocker_IgnoresNullsAndEmptyLocker()
    {
        var packet = new CreateCharacterExtendedPacket();
        InventoryPacketFactory.ConfigureCharacterLocker(packet, null);
        Assert.IsTrue(packet.InventoryCoids.All(c => c == -1));

        var harness = new InventoryTestHarness();
        InventoryPacketFactory.ConfigureCharacterLocker(packet, harness.Inventory);
        Assert.IsTrue(packet.InventoryCoids.All(c => c == -1));
    }

    [TestMethod]
    public void ConfigureCharacterLocker_DoesNotWriteCargoItems()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Cargo", 2001, 0, 0, 1));
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(11, CloneBaseObjectType.Item, "Locker", 2002, 1, 0, 1));

        var packet = new CreateCharacterExtendedPacket();
        InventoryPacketFactory.ConfigureCharacterLocker(packet, harness.Inventory);

        Assert.AreEqual(-1, packet.InventoryCoids[0]);
        Assert.AreEqual(2002, packet.InventoryCoids[1]);
    }

    [TestMethod]
    public void CreateItemObjectPackets_IncludesLockerItems()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "C", 4001, 0, 0, 1));
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(11, CloneBaseObjectType.Item, "L", 4002, 0, 0, 1));

        var packets = harness.Inventory.CreateItemObjectPackets(
            catalog: null,
            itemCreator: null);

        var coids = packets
            .OfType<CreateSimpleObjectPacket>()
            .Select(p => p.ObjectId.Coid)
            .ToHashSet();
        Assert.IsTrue(coids.Contains(4001));
        Assert.IsTrue(coids.Contains(4002));
    }
}
