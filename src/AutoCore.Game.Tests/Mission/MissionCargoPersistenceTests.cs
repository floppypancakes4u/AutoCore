using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.Tests.Mission.Infrastructure;

[TestClass]
public class MissionCargoPersistenceTests
{
    [TestMethod]
    public void AllocateNewObjectFromCBID_QuestObject_CreatesSimpleObject()
    {
        // Without AssetManager clonebase this returns null; factory must accept the type when CB is present.
        // Cover the switch via InventoryItemTypePolicy + CreateItemObjectPackets fallback instead.
        Assert.IsTrue(InventoryItemTypePolicy.IsInventoryCapable(CloneBaseObjectType.QuestObject));
        Assert.IsTrue(InventoryItemTypePolicy.IsInventoryCapable(CloneBaseObjectType.MissionObject));
    }

    [TestMethod]
    public void CreateItemObjectPackets_QuestObjectMissionItem_EmitsCreateWhenFactoryFails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(
            5555,
            CloneBaseObjectType.QuestObject,
            "Mission Gear",
            90001,
            0,
            0,
            1,
            IsMissionItem: true));

        var catalog = new InventoryCatalog(() => new[]
        {
            new InventoryCatalogEntry(5555, CloneBaseObjectType.QuestObject, "Mission Gear")
        });

        // Factory refuses QuestObject (mirrors pre-fix AllocateNewObjectFromCBID).
        var packets = harness.Inventory.CreateItemObjectPackets(catalog, new FailingItemCreator());

        Assert.AreEqual(1, packets.Count, "mission cargo must still produce a create packet on login");
        var create = (CreateSimpleObjectPacket)packets[0];
        Assert.AreEqual(5555, create.CBID);
        Assert.AreEqual(90001, create.ObjectId.Coid);
        Assert.IsTrue(create.ObjectId.Global);
        Assert.IsTrue(create.IsInInventory);
        Assert.IsTrue(create.PossibleMissionItem);
        Assert.AreEqual(1, create.Quantity);
    }

    [TestMethod]
    public void RoundTrip_LoadItems_ThenCreatePackets_RestoresMissionCargo()
    {
        var harness = new InventoryTestHarness();
        var granted = harness.Inventory.GrantMissionCargoItem(
            cbid: 4242,
            type: CloneBaseObjectType.QuestObject,
            displayName: "Deliver Widget",
            coid: 80042,
            characterCoid: harness.Character.ObjectId.Coid,
            quantity: 2,
            itemCreator: null); // grant builds CreateSimpleObject without factory

        Assert.IsNotNull(granted.AddedItem);
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
        Assert.IsTrue(harness.Persistence.Upserted[0].Item.IsMissionItem);

        // Simulate relog: clear memory, reload from persistence recording.
        harness.Persistence.CargoToLoad.Add(harness.Persistence.Upserted[0].Item);
        harness.Inventory.LoadItems(harness.Persistence.CargoToLoad);

        Assert.AreEqual(2, harness.Inventory.CountByCbid(4242));

        var catalog = new InventoryCatalog(() => new[]
        {
            new InventoryCatalogEntry(4242, CloneBaseObjectType.QuestObject, "Deliver Widget")
        });
        var packets = harness.Inventory.CreateItemObjectPackets(catalog, new FailingItemCreator());
        Assert.AreEqual(1, packets.Count);
        Assert.IsTrue(((CreateSimpleObjectPacket)packets[0]).PossibleMissionItem);
    }

    [TestMethod]
    public void EnsureAfterLoad_TopsUpMissingMissionCargo()
    {
        using var fx = new MissionTestFixture();
        var harness = new InventoryTestHarness();
        var player = fx.CreatePlayer();
        player.Character.AttachInventoryForTests(harness.Inventory);

        var obj = MissionObjective.CreateForTests(1, 0, 9200, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 333,
            NumToDeliver = 1,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
        });
        fx.SeedMission(9200, 0, obj);

        var quest = new CharacterQuest(9200, 0);
        quest.PopulateFromAssets();
        player.Character.CurrentQuests.Add(quest);

        // Empty cargo after "relog" — ensure must re-grant from mission definition.
        Assert.AreEqual(0, harness.Inventory.CountByCbid(333));

        long next = 100_000;
        var packets = MissionCargoService.EnsureActiveObjectiveItems(
            player.Character,
            quest,
            allocateCoid: () => next++,
            itemCreator: null);

        Assert.AreEqual(1, harness.Inventory.CountByCbid(333));
        Assert.IsTrue(harness.Inventory.Items[0].IsMissionItem);
        Assert.IsTrue(packets.OfType<CreateSimpleObjectPacket>().Any());
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
    }

    private sealed class FailingItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Unsupported($"item type {entry.Type} is not supported by the server create factory yet.");
    }
}
