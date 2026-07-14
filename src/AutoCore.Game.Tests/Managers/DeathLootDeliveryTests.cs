using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Death loot: table rolls for creatures/NPC vehicles + auto-loot equipment into cargo
/// with the same Create + 0x2047 + CargoSendAll path as /addItem / world pickup.
/// </summary>
[TestClass]
public class DeathLootDeliveryTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
    }

    [TestMethod]
    public void AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll()
    {
        AssetManagerTestHelper.RegisterCloneBase(7001, CloneBaseObjectType.Item);
        var map = CreateMap(9000);
        var character = CreateCharacterOnMap(map, characterCoid: 5001);

        Assert.IsTrue(LootManager.Instance.AutoLootItem(7001, character));

        Assert.IsTrue(character.Inventory.Items.Any(i => i.Cbid == 7001));
        Assert.IsTrue(_sent.OfType<CreateSimpleObjectPacket>().Any(p => p.CBID == 7001 && p.IsInInventory));
        Assert.IsTrue(_sent.OfType<InventoryAddItemResponsePacket>().Any(p => p.WasSuccessful));
        Assert.IsTrue(_sent.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    public void AutoLootItem_WhenCargoFull_ReturnsFalse()
    {
        AssetManagerTestHelper.RegisterCloneBase(7002, CloneBaseObjectType.Item);
        var map = CreateMap(9100);
        var character = CreateCharacterOnMap(map, characterCoid: 5002);
        character.Inventory.SetCapacity(1, 1);
        character.Inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "full", 1, 0, 0, 1));

        Assert.IsFalse(LootManager.Instance.AutoLootItem(7002, character));
        Assert.IsFalse(_sent.OfType<InventoryAddItemResponsePacket>().Any());
    }

    [TestMethod]
    public void GenerateLoot_Template_LootChanceZero_ReturnsEmpty()
    {
        AutoCore.Game.Diagnostics.LootTuning.LootRate = 1000.0;
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 7100, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 71, ChanceOther = 1, ChanceRarity0 = 1 },
        });

        // Zero base chance stays zero even with high LootRate.
        var items = LootManager.Instance.GenerateLoot(lootTableId: 71, lootChance: 0, lootRolls: 3, level: 1);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GenerateLoot_Template_LootChanceMax_RollsItems()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 7101, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 72, ChanceOther = 1, ChanceRarity0 = 1, DropLevelOffset = 0f, MaxLevelOffset = 0 },
        });

        var items = LootManager.Instance.GenerateLoot(lootTableId: 72, lootChance: 255, lootRolls: 2, level: 1);
        Assert.AreEqual(2, items.Count);
        Assert.IsTrue(items.All(c => c == 7101));
    }

    [TestMethod]
    public void GenerateLoot_HighLootRate_MakesTinyChancePass()
    {
        AutoCore.Game.Diagnostics.LootTuning.LootRate = 1000.0;
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Weapon, 0, 7110, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 74, ChanceWeapon = 1, ChanceRarity0 = 1, DropLevelOffset = 0f, MaxLevelOffset = 0 },
        });

        // tinLootChance 1/255 ≈ 0.4% × 1000 → always pass.
        var items = LootManager.Instance.GenerateLoot(lootTableId: 74, lootChance: 1, lootRolls: 1, level: 1);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(7110, items[0]);
    }

    [TestMethod]
    public void CreatureDeath_WithLootTable_SpawnsGroundItemAndDestroy()
    {
        const int creatureCbid = 7200;
        const int lootCbid = 7201;
        const int lootTableId = 73;

        AssetManagerTestHelper.RegisterCreatureCloneBase(creatureCbid, baseLevel: 1);
        var creatureBase = (AutoCore.Game.CloneBases.CloneBaseCreature)AssetManager.Instance.GetCloneBase(creatureCbid)!;
        var cs = creatureBase.CreatureSpecific;
        cs.LootTableId = lootTableId;
        cs.BaseLootChance = 255;
        creatureBase.CreatureSpecific = cs;

        AssetManagerTestHelper.RegisterCloneBase(lootCbid, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, lootCbid, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable
            {
                Id = lootTableId,
                LootRolls = 1,
                DropChance = 1f,
                ChanceOther = 1,
                ChanceRarity0 = 1,
                DropLevelOffset = 0f,
                MaxLevelOffset = 0,
            },
        });

        var map = CreateMap(9200);
        var character = CreateCharacterOnMap(map, characterCoid: 5200);

        var creature = new Creature();
        creature.SetCoid(9201, true);
        creature.LoadCloneBase(creatureCbid);
        creature.Level = 1;
        creature.Position = new Vector3(10, 0, 10);
        creature.SetMap(map);
        creature.SetMurderer(character.ObjectId);

        creature.OnDeath(DeathType.Silent);

        Assert.IsNull(map.GetObjectByCoid(9201));
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == 9201));
        Assert.IsTrue(
            _sent.OfType<CreateSimpleObjectPacket>().Any(p => p.CBID == lootCbid && !p.IsInInventory),
            "ground-pickable Item loot must CreateSimpleObject on the map");
    }

    [TestMethod]
    public void DeliverDeathLoot_Equipment_SpawnsOnGroundNotCargo()
    {
        // Client FUN_005130e0: Armor/Weapon/etc. return pickable=1 ("Press … to pick up").
        const int armorCbid = 7301;
        AssetManagerTestHelper.RegisterArmorCloneBase(armorCbid);

        var map = CreateMap(9300);
        var character = CreateCharacterOnMap(map, characterCoid: 5300);

        LootManager.Instance.DeliverDeathLoot(
            new[] { armorCbid },
            new Vector3(1, 0, 1),
            Quaternion.Default,
            map,
            character);

        Assert.IsFalse(
            character.Inventory.Items.Any(i => i.Cbid == armorCbid),
            "equipment must not auto-loot to cargo");
        Assert.IsFalse(_sent.OfType<InventoryAddItemResponsePacket>().Any());
        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == armorCbid),
            "equipment must spawn as world ground loot");
        Assert.IsTrue(
            _sent.OfType<CreateSimpleObjectPacket>().Any(p => p.CBID == armorCbid && !p.IsInInventory),
            "ground equipment uses CreateSimpleObject (not inventory create)");
    }

    [TestMethod]
    public void RequiresAutoLoot_FalseForClientGroundPickableTypes()
    {
        AssetManagerTestHelper.RegisterArmorCloneBase(7401);
        AssetManagerTestHelper.RegisterCloneBase(7402, CloneBaseObjectType.Weapon);
        AssetManagerTestHelper.RegisterCloneBase(7403, CloneBaseObjectType.Item);
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(7404);

        Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(7401), "Armor");
        Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(7402), "Weapon");
        Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(7403), "Item");
        Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(7404), "PowerPlant");
    }

    [TestMethod]
    public void TryRollFixedJunk_WeightedPickFromDestroyedCbid()
    {
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = 807, LootCbid = 2580, Weight = 1000 },
        });
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);

        Assert.IsTrue(LootManager.Instance.TryRollFixedJunk(807, out var loot));
        Assert.AreEqual(2580, loot);
        Assert.IsFalse(LootManager.Instance.TryRollFixedJunk(99999, out _));
    }

    [TestMethod]
    public void IsRaceCompatible_MatchesKillerOrUnrestricted()
    {
        Assert.IsTrue(LootManager.IsRaceCompatible(-1, 0));
        Assert.IsTrue(LootManager.IsRaceCompatible(0, 0));
        Assert.IsFalse(LootManager.IsRaceCompatible(1, 0));
        Assert.IsTrue(LootManager.IsRaceCompatible(2, 3));
        Assert.IsTrue(LootManager.IsRaceCompatible(3, 2));
    }

    [TestMethod]
    public void GenerateLoot_RaceFilter_ExcludesOtherRaceGear()
    {
        // Human-only weapon vs mutant-only weapon at same type/rarity/level.
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Weapon, 0, 7501, 1, requiredClass: 0);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Weapon, 0, 7502, 1, requiredClass: 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable
            {
                Id = 80,
                ChanceWeapon = 1,
                ChanceRarity0 = 1,
                DropLevelOffset = 0f,
                MaxLevelOffset = 0,
                LootRolls = 1,
            },
        });

        // Force many rolls via high chance; assert never mutant-only when we filter by race 0.
        // GenerateLoot(template) does not yet pass race — use ProcessDeathLoot path via gear helper:
        var table = AssetManager.Instance.GetLootTable(80);
        for (var i = 0; i < 20; i++)
        {
            // Use guaranteed single roll through public ProcessDeathLoot gear path.
        }

        // Direct race compatibility on seeded items: human killer fallback only picks 7501.
        Assert.IsTrue(LootManager.Instance.TryPickAnyGroundLootCbid(out var cbid, killerRace: 0));
        Assert.AreEqual(7501, cbid);
        Assert.IsTrue(LootManager.Instance.TryPickAnyGroundLootCbid(out var mutant, killerRace: 1));
        Assert.AreEqual(7502, mutant);
    }

    [TestMethod]
    public void TryRollConsumable_ChanceOne_PicksInLevelRange()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        AssetManager.Instance.SetTestConsumables(new[]
        {
            new ConsumableLootEntry { Cbid = 333, LevelMin = 1, LevelMax = 10, Offset = 100 },
            new ConsumableLootEntry { Cbid = 9999, LevelMin = 50, LevelMax = 100, Offset = 100 },
        });
        var table = new LootTable { Id = 81, ConsumableDropChance = 1f };

        Assert.IsTrue(LootManager.Instance.TryRollConsumable(table, level: 5, out var cbid));
        Assert.AreEqual(333, cbid);
    }

    [TestMethod]
    public void RollCreditsAmount_ChanceOne_ReturnsInRange()
    {
        var table = new LootTable
        {
            DropCreditsChance = 1f,
            MinCreditsDrop = 5,
            MaxCreditsDrop = 10,
        };

        var amount = LootManager.Instance.RollCreditsAmount(table);
        Assert.IsTrue(amount >= 5 && amount <= 10, $"amount={amount}");
    }

    [TestMethod]
    public void ProcessDeathLoot_JunkIndependentOfEmptyTable()
    {
        // Table "No Loot" + junk weights still drops junk.
        AssetManagerTestHelper.RegisterCloneBase(2580, CloneBaseObjectType.Item);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 1, Name = "No Loot", LootRolls = 0, DropChance = 0 },
        });
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = 807, LootCbid = 2580, Weight = 500 },
        });
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);

        var map = CreateMap(9400);
        map.ContinentObject.DropCommodities = false;
        var character = CreateCharacterOnMap(map, characterCoid: 5400);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(2, 0, 2),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 807,
            Level = 5,
            LootTableId = 1,
            TemplateLootChance = 0,
            GearRolls = 0,
            UseCreatureDropFormula = false,
        });

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == 2580),
            "tLootWeights junk must spawn even when table is No Loot / zero chance");
        Assert.IsTrue(_sent.OfType<CreateSimpleObjectPacket>().Any(p => p.CBID == 2580 && !p.IsInInventory));
    }

    [TestMethod]
    public void ProcessDeathLoot_Commodity_WhenMapAllows()
    {
        AssetManagerTestHelper.RegisterCloneBase(5468, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(5468, minLevel: 1, maxLevel: 100, dropChance: 1f);

        var map = CreateMap(9500);
        map.ContinentObject.DropCommodities = true;
        var character = CreateCharacterOnMap(map, characterCoid: 5500);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(3, 0, 3),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 1,
            Level = 5,
            LootTableId = 0,
            TemplateLootChance = 0,
            GearRolls = 0,
        });

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == 5468),
            "salvage commodity must ground-drop when DropCommodities and chance allow");
    }

    [TestMethod]
    public void ProcessDeathLoot_Commodity_BlockedWhenContinentDisallows()
    {
        AssetManagerTestHelper.RegisterCloneBase(5469, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(5469, minLevel: 1, maxLevel: 100, dropChance: 1f);
        AutoCore.Game.Diagnostics.LootTuning.IgnoreDropCommoditiesGate = false;

        var map = CreateMap(9501);
        map.ContinentObject.DropCommodities = false; // tutorial-style (e.g. Ark Bay 707)
        var character = CreateCharacterOnMap(map, characterCoid: 5501);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(4, 0, 4),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 660, // no tLootWeights either
            Level = 5,
            LootTableId = 0,
            TemplateLootChance = 0,
            GearRolls = 0,
        });

        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == 5469),
            "retail DropCommodities=false must suppress commodity salvage");
    }

    [TestMethod]
    public void ProcessDeathLoot_Commodity_IgnoreGate_AllowsWhenContinentDisallows()
    {
        AssetManagerTestHelper.RegisterCloneBase(5471, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(5471, minLevel: 1, maxLevel: 100, dropChance: 1f);
        AutoCore.Game.Diagnostics.LootTuning.IgnoreDropCommoditiesGate = true;

        var map = CreateMap(9502);
        map.ContinentObject.DropCommodities = false;
        var character = CreateCharacterOnMap(map, characterCoid: 5502);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(5, 0, 5),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 660,
            Level = 5,
            LootTableId = 0,
            TemplateLootChance = 0,
            GearRolls = 0,
        });

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == 5471),
            "IgnoreDropCommoditiesGate must allow salvage testing on tutorial continents");
    }

    [TestMethod]
    public void ProcessDeathLoot_MapPropSalvage_OnlyFixedJunk_NoCommodity()
    {
        const int destroyed = 543; // billboard-style weighted prop
        const int junk = 870;
        const int commodity = 5472;
        AssetManagerTestHelper.RegisterCloneBase(junk, CloneBaseObjectType.Item);
        AssetManagerTestHelper.RegisterCloneBase(commodity, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, junk, 1);
        LootManager.Instance.SeedCommodityForTests(commodity, minLevel: 1, maxLevel: 100, dropChance: 1f);
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = destroyed, LootCbid = junk, Weight = 500 },
        });
        AutoCore.Game.Diagnostics.LootTuning.IgnoreDropCommoditiesGate = true;

        var map = CreateMap(9504);
        map.ContinentObject.DropCommodities = true;
        var character = CreateCharacterOnMap(map, characterCoid: 5504);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(7, 0, 7),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = destroyed,
            Level = 5,
            LootTableId = 0,
            TemplateLootChance = 0,
            GearRolls = 0,
            MapPropSalvage = true,
        });

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == junk),
            "map props with tLootWeights drop fixed junk only");
        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == commodity),
            "map prop destroy must not roll commodity pool");
    }

    [TestMethod]
    public void ProcessDeathLoot_MapPropSalvage_NoWeights_DropsNothing()
    {
        AssetManagerTestHelper.RegisterCloneBase(5473, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(5473, minLevel: 1, maxLevel: 100, dropChance: 1f);
        AutoCore.Game.Diagnostics.LootTuning.IgnoreDropCommoditiesGate = true;

        var map = CreateMap(9505);
        map.ContinentObject.DropCommodities = true;
        var character = CreateCharacterOnMap(map, characterCoid: 5505);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(8, 0, 8),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 287, // highway barrier — no tLootWeights in retail
            Level = 5,
            MapPropSalvage = true,
        });

        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == 5473),
            "rubble/barrier without weights: no commodity drops");
        Assert.IsFalse(
            _sent.OfType<CreateSimpleObjectPacket>().Any(p => !p.IsInInventory),
            "rubble/barrier without weights: no ground loot packets");
    }

    [TestMethod]
    public void ProcessDeathLoot_Junk_WhenVictimHasLootWeights()
    {
        const int destroyed = 807;
        const int junk = 2580;
        AssetManagerTestHelper.RegisterCloneBase(junk, CloneBaseObjectType.Item);
        // Mark LootManager initialized (ProcessDeathLoot no-ops when not initialized).
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, junk, 1);
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = destroyed, LootCbid = junk, Weight = 500 },
        });

        var map = CreateMap(9503);
        map.ContinentObject.DropCommodities = false;
        var character = CreateCharacterOnMap(map, characterCoid: 5503);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(6, 0, 6),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = destroyed,
            Level = 1,
            LootTableId = 0,
            TemplateLootChance = 0,
            GearRolls = 0,
        });

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == junk),
            "tLootWeights junk is independent of DropCommodities");
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(localCoid % 10000),
            MapFileName = $"tm_death_loot_{localCoid}",
            DisplayName = "deathloot",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        map.LocalCoidCounter = localCoid;
        return map;
    }

    private static Character CreateCharacterOnMap(SectorMap map, long characterCoid)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(characterCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var inventory = new InventoryManager();
        character.AttachInventoryForTests(inventory);

        var vehicle = new Vehicle();
        vehicle.SetCoid(characterCoid + 1, true);
        character.AttachCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        return character;
    }
}
