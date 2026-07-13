using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission;

using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory;
using AutoCore.Game.Tests.Mission.Infrastructure;

[TestClass]
public class MissionCargoServiceTests
{
    [TestMethod]
    public void QuantityNeeded_ZeroOrNegative_DefaultsToOne()
    {
        Assert.AreEqual(1, MissionCargoService.QuantityNeeded(0));
        Assert.AreEqual(1, MissionCargoService.QuantityNeeded(-3));
        Assert.AreEqual(4, MissionCargoService.QuantityNeeded(4));
    }

    [TestMethod]
    public void QuantityMissing_NeverNegative()
    {
        Assert.AreEqual(0, MissionCargoService.QuantityMissing(2, 5));
        Assert.AreEqual(3, MissionCargoService.QuantityMissing(5, 2));
        Assert.AreEqual(0, MissionCargoService.QuantityMissing(1, 1));
    }

    [TestMethod]
    public void GetGiveSpecs_OnlyGiveItemOnStartWithPositiveCbid()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 100, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 50,
            NumToDeliver = 2,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 51,
            NumToDeliver = 1,
            GiveItemOnStart = false,
            TakeItemAtEnd = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = -1,
            GiveItemOnStart = true,
        });

        var give = MissionCargoService.GetGiveSpecs(obj);
        Assert.AreEqual(1, give.Count);
        Assert.AreEqual((50, 2), give[0]);

        var take = MissionCargoService.GetTakeSpecs(obj);
        Assert.AreEqual(2, take.Count);
        CollectionAssert.AreEqual(new[] { (50, 2), (51, 1) }, take.ToArray());
    }

    [TestMethod]
    public void EnsureActiveObjectiveItems_GrantsMissingMissionCargo()
    {
        using var fx = new MissionTestFixture();
        var harness = new InventoryTestHarness();
        var player = fx.CreatePlayer();
        player.Character.AttachInventoryForTests(harness.Inventory);

        var obj = MissionObjective.CreateForTests(10, 0, 9001, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 4242,
            NumToDeliver = 3,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
            NPCTargetCBID = 99,
            NPCTargetCompletes = true,
        });
        fx.SeedMission(9001, 0, obj);

        var quest = new CharacterQuest(9001, 0);
        quest.PopulateFromAssets();
        player.Character.CurrentQuests.Add(quest);

        long next = 50_000;
        var packets = MissionCargoService.EnsureActiveObjectiveItems(
            player.Character,
            quest,
            allocateCoid: () => next++,
            itemCreator: new AlwaysSucceedItemCreator());

        Assert.AreEqual(3, harness.Inventory.CountByCbid(4242));
        Assert.IsTrue(harness.Inventory.Items.All(i => i.IsMissionItem));
        Assert.IsTrue(packets.OfType<CreateSimpleObjectPacket>().Any(p => p.PossibleMissionItem));
        Assert.IsTrue(packets.OfType<InventoryAddItemResponsePacket>().Any(p => p.WasSuccessful && p.Quantity == 3));
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
        Assert.IsTrue(harness.Persistence.Upserted[0].Item.IsMissionItem);
    }

    [TestMethod]
    public void EnsureActiveObjectiveItems_Idempotent_DoesNotDuplicate()
    {
        using var fx = new MissionTestFixture();
        var harness = new InventoryTestHarness();
        var player = fx.CreatePlayer();
        player.Character.AttachInventoryForTests(harness.Inventory);

        var obj = MissionObjective.CreateForTests(11, 0, 9002, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 77,
            NumToDeliver = 2,
            GiveItemOnStart = true,
        });
        fx.SeedMission(9002, 0, obj);

        var quest = new CharacterQuest(9002, 0);
        quest.PopulateFromAssets();
        player.Character.CurrentQuests.Add(quest);

        long next = 60_000;
        Func<long> alloc = () => next++;
        var creator = new AlwaysSucceedItemCreator();

        MissionCargoService.EnsureActiveObjectiveItems(player.Character, quest, alloc, creator);
        var second = MissionCargoService.EnsureActiveObjectiveItems(player.Character, quest, alloc, creator);

        Assert.AreEqual(2, harness.Inventory.CountByCbid(77));
        Assert.AreEqual(0, second.Count);
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
    }

    [TestMethod]
    public void TakeObjectiveItems_RemovesMissionCargo()
    {
        using var fx = new MissionTestFixture();
        var harness = new InventoryTestHarness();
        var player = fx.CreatePlayer();
        player.Character.AttachInventoryForTests(harness.Inventory);

        var obj = MissionObjective.CreateForTests(12, 0, 9003, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 88,
            NumToDeliver = 2,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
        });
        fx.SeedMission(9003, 0, obj);

        var quest = new CharacterQuest(9003, 0);
        quest.PopulateFromAssets();
        player.Character.CurrentQuests.Add(quest);

        long next = 70_000;
        MissionCargoService.EnsureActiveObjectiveItems(
            player.Character,
            quest,
            () => next++,
            new AlwaysSucceedItemCreator());

        Assert.AreEqual(2, harness.Inventory.CountByCbid(88));

        MissionCargoService.TakeObjectiveItems(player.Character, quest, obj);

        Assert.AreEqual(0, harness.Inventory.CountByCbid(88));
        Assert.IsTrue(harness.Persistence.DeletedItemCoids.Count >= 1);
    }

    [TestMethod]
    public void GrantMission_GivesDeliverCargo()
    {
        using var fx = new MissionTestFixture();
        var harness = new InventoryTestHarness();
        var player = fx.CreatePlayer();
        player.Character.AttachInventoryForTests(harness.Inventory);

        var obj = MissionObjective.CreateForTests(20, 0, 9100, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            ItemCBID = 1234,
            NumToDeliver = 1,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
            NPCTargetCBID = 5,
            NPCTargetCompletes = true,
        });
        fx.SeedMission(9100, 0, obj);

        // Grant without item creator still builds packets via CreatePacketFor.
        NpcInteractHandler.GrantMission(player.Connection, player.Character, 9100);

        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, harness.Inventory.CountByCbid(1234));
        Assert.IsTrue(harness.Inventory.Items[0].IsMissionItem);
        Assert.IsTrue(fx.Sent.OfType<CreateSimpleObjectPacket>().Any(p =>
            p.CBID == 1234 && p.PossibleMissionItem && p.IsInInventory));
    }

    [TestMethod]
    public void CreateItemObjectPackets_SetsPossibleMissionItemForMissionCargo()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(
            99,
            CloneBaseObjectType.Item,
            "Mission Widget",
            8001,
            0,
            0,
            1,
            IsMissionItem: true));

        var catalog = new InventoryCatalog(() => new[]
        {
            new InventoryCatalogEntry(99, CloneBaseObjectType.Item, "Mission Widget")
        });
        var packets = harness.Inventory.CreateItemObjectPackets(catalog, new AlwaysSucceedItemCreator());

        Assert.AreEqual(1, packets.Count);
        var create = (CreateSimpleObjectPacket)packets[0];
        Assert.IsTrue(create.PossibleMissionItem);
        Assert.IsTrue(create.IsInInventory);
    }

    private sealed class AlwaysSucceedItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new TFID { Coid = coid, Global = true },
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    Quantity = 1,
                    IsInInventory = true,
                    IsIdentified = true,
                },
                entry.DisplayName);
    }
}
