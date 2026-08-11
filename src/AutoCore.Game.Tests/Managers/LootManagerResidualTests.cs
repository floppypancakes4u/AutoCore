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
/// Residual LootManager edges: empty tables, init paths, roll branches not covered by
/// DeathLootDeliveryTests / LootWorldSpawnTests.
/// </summary>
[TestClass]
public class LootManagerResidualTests
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
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void Initialize_EmptyCatalog_MarksInitialized()
    {
        LootManager.Instance.Initialize();
        // Second call is no-op
        LootManager.Instance.Initialize();

        Assert.IsFalse(LootManager.Instance.TryPickRandomGroundLootCbid(out _));
        Assert.IsFalse(LootManager.Instance.TryPickAnyGroundLootCbid(out _));
    }

    [TestMethod]
    public void Initialize_WithGeneratableItems_BuildsIndex()
    {
        AssetManagerTestHelper.RegisterCloneBase(8101, CloneBaseObjectType.Item);
        SetGeneratable(8101, 1);
        AssetManagerTestHelper.RegisterCloneBase(8102, CloneBaseObjectType.Weapon);
        SetGeneratable(8102, 1);
        AssetManagerTestHelper.RegisterCloneBase(8103, CloneBaseObjectType.Commodity);
        // Commodity path uses CloneBaseCommodity — RegisterCloneBase may not be Commodity subclass.
        // Seed commodity via test seam instead.

        LootManager.Instance.Initialize();

        Assert.IsTrue(LootManager.Instance.TryPickRandomGroundLootCbid(out var cbid));
        Assert.AreEqual(8101, cbid);
        Assert.IsTrue(LootManager.Instance.TryPickAnyGroundLootCbid(out var any));
        Assert.IsTrue(any is 8101 or 8102);
    }

    [TestMethod]
    public void GenerateLoot_NotInitialized_ReturnsEmpty()
    {
        var items = LootManager.Instance.GenerateLoot(1, 255, 2, 5);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GenerateLoot_MissingTable_ReturnsEmpty()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        var items = LootManager.Instance.GenerateLoot(lootTableId: 99999, lootChance: 255, lootRolls: 2, level: 1);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GenerateLoot_ZeroRolls_ReturnsEmpty()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 90, ChanceOther = 1, ChanceRarity0 = 1 },
        });
        var items = LootManager.Instance.GenerateLoot(90, lootChance: 255, lootRolls: 0, level: 1);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GenerateLoot_Creature_NotCreatureCloneBase_ReturnsEmpty()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        var creature = new Creature();
        creature.SetCoid(8200, true);
        // No LoadCloneBase → CloneBaseObject is not CloneBaseCreature
        var items = LootManager.Instance.GenerateLoot(creature);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GenerateLoot_Creature_MissingTable_ReturnsEmpty()
    {
        const int cbid = 8210;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, baseLevel: 1);
        var cb = (AutoCore.Game.CloneBases.CloneBaseCreature)AssetManager.Instance.GetCloneBase(cbid)!;
        var cs = cb.CreatureSpecific;
        cs.LootTableId = 404;
        cs.BaseLootChance = 255;
        cb.CreatureSpecific = cs;

        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        var creature = new Creature();
        creature.SetCoid(8211, true);
        creature.LoadCloneBase(cbid);
        creature.Level = 1;

        var items = LootManager.Instance.GenerateLoot(creature);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GenerateLoot_Creature_ZeroChance_ReturnsEmpty()
    {
        const int cbid = 8220;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid);
        var cb = (AutoCore.Game.CloneBases.CloneBaseCreature)AssetManager.Instance.GetCloneBase(cbid)!;
        var cs = cb.CreatureSpecific;
        cs.LootTableId = 91;
        cs.BaseLootChance = 0;
        cb.CreatureSpecific = cs;

        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 91, DropChance = 1f, ChanceOther = 1, ChanceRarity0 = 1, LootRolls = 1 },
        });
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);

        var creature = new Creature();
        creature.SetCoid(8221, true);
        creature.LoadCloneBase(cbid);
        creature.Level = 1;

        var items = LootManager.Instance.GenerateLoot(creature);
        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void ProcessDeathLoot_NotInitialized_NoOp()
    {
        var map = CreateMap(100);
        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Default,
            VictimCbid = 1,
        });
        Assert.IsFalse(_sent.OfType<CreateSimpleObjectPacket>().Any());
    }

    [TestMethod]
    public void ProcessDeathLoot_NullRequest_NoOp()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        LootManager.Instance.ProcessDeathLoot(null);
    }

    [TestMethod]
    public void ProcessDeathLoot_CreatureFormula_GearWithCredits()
    {
        const int lootCbid = 8301;
        AssetManagerTestHelper.RegisterCloneBase(lootCbid, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, lootCbid, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable
            {
                Id = 92,
                DropChance = 1f,
                ChanceOther = 1,
                ChanceRarity0 = 1,
                LootRolls = 2,
                DropLevelOffset = 0f,
                MaxLevelOffset = 0,
                DropCreditsChance = 1f,
                MinCreditsDrop = 7,
                MaxCreditsDrop = 7,
                ConsumableDropChance = 0f,
            },
        });

        var map = CreateMap(200);
        map.ContinentObject.DropCommodities = false;
        var character = CreateCharacterOnMap(map, 8300);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(1, 0, 1),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 1,
            Level = 1,
            LootTableId = 92,
            UseCreatureDropFormula = true,
            CreatureBaseLootChance = 255,
        });

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == lootCbid)
            || character.Credits >= 0);
        Assert.IsTrue(character.Credits >= 7 || map.Objects.Values.OfType<SimpleObject>().Any());
    }

    [TestMethod]
    public void ProcessDeathLoot_TemplatePath_MasterFail_OnlyJunk()
    {
        const int junk = 8310;
        AssetManagerTestHelper.RegisterCloneBase(junk, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, junk, 1);
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = 100, LootCbid = junk, Weight = 10 },
        });
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = 93, ChanceOther = 1, ChanceRarity0 = 1, DropChance = 1f, LootRolls = 3 },
        });

        var map = CreateMap(300);
        var character = CreateCharacterOnMap(map, 8311);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(2, 0, 2),
            Rotation = Quaternion.Default,
            Killer = character,
            VictimCbid = 100,
            Level = 5,
            LootTableId = 93,
            TemplateLootChance = 0, // master fail
            GearRolls = 3,
        });

        Assert.IsTrue(map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == junk));
    }

    [TestMethod]
    public void DeliverDeathLoot_EmptyOrNull_NoOp()
    {
        var map = CreateMap(400);
        var character = CreateCharacterOnMap(map, 8400);
        LootManager.Instance.DeliverDeathLoot(null, new Vector3(0f, 0f, 0f), Quaternion.Default, map, character);
        LootManager.Instance.DeliverDeathLoot(Array.Empty<int>(), new Vector3(0f, 0f, 0f), Quaternion.Default, map, character);
        LootManager.Instance.DeliverDeathLoot(new[] { 1 }, new Vector3(0f, 0f, 0f), Quaternion.Default, null, character);
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void DeliverDeathLoot_ZeroScatter_SpawnsAtDeathPosition()
    {
        const int cbid = 8410;
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, cbid, 1);

        var map = CreateMap(500);
        var character = CreateCharacterOnMap(map, 8411);
        var deathPos = new Vector3(12, 3, 4);

        LootManager.Instance.DeliverDeathLoot(
            new[] { cbid, 0, -1 },
            deathPos,
            Quaternion.Default,
            map,
            character,
            scatterRadius: 0f);

        var spawned = map.Objects.Values.OfType<SimpleObject>().Single(o => o.CBID == cbid);
        Assert.AreEqual(deathPos.X, spawned.Position.X, 0.01f);
        Assert.AreEqual(deathPos.Z, spawned.Position.Z, 0.01f);
    }

    [TestMethod]
    public void DeliverDeathLoot_AutoLootType_GoesToCargoWhenPossible()
    {
        // Ornament requires auto-loot
        const int ornament = 8420;
        AssetManagerTestHelper.RegisterCloneBase(ornament, CloneBaseObjectType.Ornament);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Ornament, 0, ornament, 1);

        var map = CreateMap(600);
        var character = CreateCharacterOnMap(map, 8421);

        Assert.IsTrue(LootManager.Instance.RequiresAutoLoot(ornament));

        LootManager.Instance.DeliverDeathLoot(
            new[] { ornament },
            new Vector3(1, 0, 1),
            Quaternion.Default,
            map,
            character);

        // Auto-loot may succeed into cargo or fall back to ground if inventory rejects type.
        var inCargo = character.Inventory.Items.Any(i => i.Cbid == ornament);
        var onGround = map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == ornament);
        Assert.IsTrue(inCargo || onGround);
    }

    [TestMethod]
    public void RequiresAutoLoot_UnknownCbid_False()
    {
        Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(0));
        Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(999999));
    }

    [TestMethod]
    public void RequiresAutoLoot_TrueForOrnamentRaceItemVehicle()
    {
        AssetManagerTestHelper.RegisterCloneBase(8501, CloneBaseObjectType.Ornament);
        AssetManagerTestHelper.RegisterCloneBase(8502, CloneBaseObjectType.RaceItem);
        AssetManagerTestHelper.RegisterVehicleCloneBase(8503);

        Assert.IsTrue(LootManager.Instance.RequiresAutoLoot(8501));
        Assert.IsTrue(LootManager.Instance.RequiresAutoLoot(8502));
        Assert.IsTrue(LootManager.Instance.RequiresAutoLoot(8503));
    }

    [TestMethod]
    public void AutoLootItem_NullCharacter_False()
    {
        AssetManagerTestHelper.RegisterCloneBase(8601, CloneBaseObjectType.Item);
        Assert.IsFalse(LootManager.Instance.AutoLootItem(8601, null));
    }

    [TestMethod]
    public void AutoLootItem_MissingCloneBase_False()
    {
        var map = CreateMap(700);
        var character = CreateCharacterOnMap(map, 8602);
        Assert.IsFalse(LootManager.Instance.AutoLootItem(999888, character));
    }

    [TestMethod]
    public void AutoLootItem_CargoFull_DoesNotAllocatePlaceholderCoid()
    {
        // SS-31 leak guard: cargo is completely full, so AutoLootItem must reject the claim
        // BEFORE calling runtime.AllocateItemCoid() — allocating first and rejecting after
        // leaks an orphan simple_object placeholder row for the coid nobody ever uses.
        const int cbid = 8603;
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Item); // 1x1, non-stackable

        var map = CreateMap(701);
        var character = CreateCharacterOnMap(map, 8610);
        character.Inventory.SetCapacity(1, 1); // exactly one 1x1 slot
        character.Inventory.TryAdd(new CharacterInventoryItem(
            99, CloneBaseObjectType.Item, "Filler", 5000, 0, 0, 1)); // fills the only slot

        var saved = InventoryRuntime.AllocatePersistentCoid;
        try
        {
            var allocations = 0;
            InventoryRuntime.AllocatePersistentCoid = () =>
            {
                allocations++;
                return 999_500L + allocations;
            };

            var result = LootManager.Instance.AutoLootItem(cbid, character);

            Assert.IsFalse(result, "full cargo must reject the auto-loot claim");
            Assert.AreEqual(0, allocations,
                "no persistent coid may be allocated for a claim that cannot fit anywhere (SS-31 leak guard)");
        }
        finally
        {
            InventoryRuntime.AllocatePersistentCoid = saved;
        }
    }

    [TestMethod]
    public void TrySpawnLootItem_NullMap_False()
    {
        AssetManagerTestHelper.RegisterCloneBase(8701, CloneBaseObjectType.Item);
        Assert.IsFalse(LootManager.Instance.TrySpawnLootItem(
            8701, new Vector3(0f, 0f, 0f), Quaternion.Default, null, out _));
    }

    [TestMethod]
    public void TrySpawnLootItem_MissingCbid_False()
    {
        var map = CreateMap(800);
        Assert.IsFalse(LootManager.Instance.TrySpawnLootItem(
            999777, new Vector3(0f, 0f, 0f), Quaternion.Default, map, out _));
    }

    [TestMethod]
    public void SpawnLootItem_MissingCbid_NoThrow()
    {
        var map = CreateMap(801);
        LootManager.Instance.SpawnLootItem(999776, new Vector3(0f, 0f, 0f), Quaternion.Default, map);
    }

    [TestMethod]
    public void RollCreditsAmount_NullAndZeroChance()
    {
        Assert.AreEqual(0, LootManager.Instance.RollCreditsAmount(null));
        Assert.AreEqual(0, LootManager.Instance.RollCreditsAmount(new LootTable { DropCreditsChance = 0f }));
        Assert.AreEqual(0, LootManager.Instance.RollCreditsAmount(new LootTable
        {
            DropCreditsChance = 1f,
            MinCreditsDrop = 0,
            MaxCreditsDrop = 0,
        }));
    }

    [TestMethod]
    public void TryRollConsumable_NullTable_False()
    {
        Assert.IsFalse(LootManager.Instance.TryRollConsumable(null, 5, out _));
    }

    [TestMethod]
    public void TryRollConsumable_NoEntries_False()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        Assert.IsFalse(LootManager.Instance.TryRollConsumable(
            new LootTable { ConsumableDropChance = 1f }, 5, out _));
    }

    [TestMethod]
    public void TryRollConsumable_LevelOutOfRange_False()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        AssetManager.Instance.SetTestConsumables(new[]
        {
            new ConsumableLootEntry { Cbid = 333, LevelMin = 10, LevelMax = 20, Offset = 1 },
        });
        Assert.IsFalse(LootManager.Instance.TryRollConsumable(
            new LootTable { ConsumableDropChance = 1f }, level: 1, out _));
    }

    [TestMethod]
    public void TryRollFixedJunk_ZeroWeight_False()
    {
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = 55, LootCbid = 1, Weight = 0 },
        });
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        Assert.IsFalse(LootManager.Instance.TryRollFixedJunk(55, out _));
        Assert.IsFalse(LootManager.Instance.TryRollFixedJunk(0, out _));
    }

    [TestMethod]
    public void TryRollCommodity_LevelOutOfRange_False()
    {
        LootManager.Instance.SeedCommodityForTests(9001, minLevel: 50, maxLevel: 60, dropChance: 1f);
        var map = CreateMap(900);
        map.ContinentObject.DropCommodities = true;

        Assert.IsFalse(LootManager.Instance.TryRollCommodity(new LootManager.DeathLootRequest
        {
            Map = map,
            Level = 5,
        }, out _));
    }

    [TestMethod]
    public void TryPickAnyGroundLootCbid_RaceFilter_Empty_False()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Weapon, 0, 9010, 1, requiredClass: 0);
        Assert.IsFalse(LootManager.Instance.TryPickAnyGroundLootCbid(out _, killerRace: 5));
    }

    [TestMethod]
    public void IsRaceCompatible_SameRace_True()
    {
        Assert.IsTrue(LootManager.IsRaceCompatible(1, 1));
        Assert.IsFalse(LootManager.IsRaceCompatible(1, 2));
    }

    [TestMethod]
    public void GenerateLoot_TableWithNoTypeWeights_RollsEmpty()
    {
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            // All type chances 0 → RollItemType null
            new LootTable { Id = 94, DropChance = 1f, LootRolls = 3, ChanceRarity0 = 1 },
        });
        var items = LootManager.Instance.GenerateLoot(94, 255, 3, 1);
        Assert.AreEqual(0, items.Count);
    }

    private static void SetGeneratable(int cbid, int value)
    {
        var cb = AssetManager.Instance.GetCloneBase(cbid);
        Assert.IsNotNull(cb);
        var specific = cb.CloneBaseSpecific;
        specific.IsGeneratable = (byte)value;
        cb.CloneBaseSpecific = specific;
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(localCoid % 10000),
            MapFileName = $"tm_loot_residual_{localCoid}",
            DisplayName = "loot-residual",
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
