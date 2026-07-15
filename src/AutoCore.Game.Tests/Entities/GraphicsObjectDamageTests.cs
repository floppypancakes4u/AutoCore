using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Combat;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Inventory.Fakes;

/// <summary>
/// Map props are GraphicsObject (not SimpleObject/Creature). Combat must reduce HP and
/// destroy the object so destroy-objective missions can complete.
/// </summary>
[TestClass]
public class GraphicsObjectDamageTests
{
    private const int ContId = 909;
    private const long PropCoid = 9301;
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
        LootTuning.ResetToDefaults();
        MapPropCorpseDespawn.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
        LootTuning.ResetToDefaults();
        MapPropCorpseDespawn.ResetForTests();
    }

    [TestMethod]
    public void GraphicsObject_TakeDamage_ReducesHpAndReturnsActual()
    {
        var prop = CreateDamagableProp(maxHp: 100);
        prop.SetInvincible(false);

        var dealt = prop.TakeDamage(30);

        Assert.AreEqual(30, dealt);
        Assert.AreEqual(70, prop.GetCurrentHP());
        Assert.AreEqual(100, prop.GetMaximumHP());
        Assert.IsFalse(prop.IsCorpse);
    }

    [TestMethod]
    public void SetInvincible_False_DoesNotCreatePlainGhostObject()
    {
        var fresh = new GraphicsObject(GraphicsObjectType.Graphics);
        fresh.InitializeHealthForTests(50);
        Assert.IsNull(fresh.Ghost, "Map props must not ghost at init (exhausts client ghost slots / hides NPCs)");

        fresh.SetInvincible(false);

        // Plain GhostObject + local TFID → client AV 0x005B0EFF (FUN_005b0ed0 null iface).
        // HP for map props is server-authoritative; client ram FX is local-only.
        Assert.IsNull(fresh.Ghost, "MakeNotInvincible must not create plain GhostObject (client crash)");
        Assert.IsFalse(fresh.IsInvincible);
    }

    [TestMethod]
    public void TakeDamage_DoesNotCreatePlainGhostObject()
    {
        var fresh = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        fresh.InitializeHealthForTests(80);
        Assert.IsNull(fresh.Ghost);

        Assert.AreEqual(25, fresh.TakeDamage(25));
        Assert.IsNull(fresh.Ghost, "TakeDamage must not CreateGhost — ram multi-kill was AV 0x005B0EFF");
        Assert.AreEqual(55, fresh.GetCurrentHP());
    }

    [TestMethod]
    public void GraphicsObject_TakeDamage_Invincible_ReturnsZero()
    {
        var prop = CreateDamagableProp(maxHp: 50);
        prop.SetInvincible(true);

        Assert.AreEqual(0, prop.TakeDamage(999));
        Assert.AreEqual(50, prop.GetCurrentHP());
    }

    [TestMethod]
    public void GraphicsObject_TakeDamage_ToZero_ThenOnDeath_RemovesAndBroadcastsDestroy()
    {
        var (character, map) = CreatePlayerOnMap();
        var prop = CreateDamagableProp(maxHp: 40);
        prop.SetCoid(PropCoid, false);
        prop.SetMap(map);
        prop.SetInvincible(false);

        var dealt = prop.TakeDamage(100);
        Assert.AreEqual(40, dealt);
        Assert.AreEqual(0, prop.GetCurrentHP());

        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.IsTrue(prop.IsCorpse);
        Assert.IsNotNull(map.GetObjectByCoid(PropCoid), "corpse stays on map until delayed leave");
        Assert.AreEqual(1, MapPropCorpseDespawn.PendingCountForTests);
        Assert.IsTrue(
            _sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == PropCoid),
            "DestroyObject must ship on OnDeath (weapons have no client-local break FX); sent=" +
            string.Join(',', _sent.Select(p => p.Opcode)));

        MapPropCorpseDespawn.FlushAllForTests();

        Assert.IsNull(map.GetObjectByCoid(PropCoid), "after delay, prop leaves the sector map");
    }

    [TestMethod]
    public void GraphicsObject_CombatStyle_KillLoop_WorksLikeVehicleFire()
    {
        // Mirrors Vehicle.ProcessCombatInternal death check without full weapon stack.
        var (character, map) = CreatePlayerOnMap();
        var prop = CreateDamagableProp(maxHp: 25);
        prop.SetCoid(PropCoid, false);
        prop.SetMap(map);
        prop.SetInvincible(false);

        var remaining = prop.GetCurrentHP();
        while (remaining > 0)
        {
            var dmg = 10;
            var actual = prop.TakeDamage(dmg);
            Assert.IsTrue(actual > 0);
            remaining = prop.GetCurrentHP();
            if (remaining <= 0)
            {
                prop.SetMurderer(character.CurrentVehicle);
                prop.OnDeath(DeathType.Silent);
            }
        }

        Assert.IsTrue(prop.IsCorpse);
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(PropCoid));
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any());
    }

    [TestMethod]
    public void GraphicsObject_OnDeath_NoCommodityWithoutLootWeights()
    {
        // Retail: rubble/fence/street-light have no tLootWeights → no prop drops.
        // Commodities (Nuts and Bolts, etc.) come from combatant death, not random scenery.
        const int propCbid = 8801;
        const int salvageCbid = 5468; // Salvaged Nuts and Bolts
        AssetManagerTestHelper.RegisterCloneBase(propCbid, CloneBaseObjectType.Object);
        AssetManagerTestHelper.RegisterCloneBase(salvageCbid, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(salvageCbid, minLevel: 1, maxLevel: 100, dropChance: 1f);

        var (character, map) = CreatePlayerOnMap(dropCommodities: true);
        var prop = CreateDamagableProp(maxHp: 10, cbid: propCbid);
        prop.SetCoid(PropCoid, false);
        prop.Position = new Vector3(5, 0, 5);
        prop.SetMap(map);
        prop.SetInvincible(false);
        prop.SetMurderer(character.CurrentVehicle);

        prop.TakeDamage(10);
        prop.OnDeath(DeathType.Silent);

        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == salvageCbid),
            "map props without tLootWeights must not roll commodity pool");
        MapPropCorpseDespawn.FlushAllForTests();
    }

    [TestMethod]
    public void GraphicsObject_OnDeath_LootSpawnsNearOverridePosition()
    {
        const int propCbid = 8804;
        const int junkCbid = 2580;
        AssetManagerTestHelper.RegisterCloneBase(propCbid, CloneBaseObjectType.Object);
        AssetManagerTestHelper.RegisterCloneBase(junkCbid, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, junkCbid, 1);
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = propCbid, LootCbid = junkCbid, Weight = 500 },
        });

        var (character, map) = CreatePlayerOnMap(dropCommodities: true);
        var prop = CreateDamagableProp(maxHp: 10, cbid: propCbid);
        prop.SetCoid(PropCoid, false);
        // Wrong/stale prop pose (origin) — loot must follow ram override at vehicle.
        prop.Position = new Vector3(0, 0, 0);
        prop.DeathLootOverridePosition = new Vector3(100, 5, 200);
        prop.SetMap(map);
        prop.SetInvincible(false);
        prop.SetMurderer(character.CurrentVehicle);

        prop.TakeDamage(10);
        prop.OnDeath(DeathType.Silent);

        var loot = map.Objects.Values.OfType<SimpleObject>().FirstOrDefault(o => o.CBID == junkCbid);
        Assert.IsNotNull(loot, "weighted map prop must drop fixed junk");
        Assert.IsTrue(loot.Position.Dist(new Vector3(100, 5, 200)) < 2f,
            $"loot at {loot.Position} should be near override (100,5,200)");
        MapPropCorpseDespawn.FlushAllForTests();
    }

    [TestMethod]
    public void GraphicsObject_OnDeath_DropsFixedJunkFromLootWeights()
    {
        const int propCbid = 8802;
        const int junkCbid = 2580; // Scrap Tire style junk for destroyed CBID
        AssetManagerTestHelper.RegisterCloneBase(propCbid, CloneBaseObjectType.Object);
        AssetManagerTestHelper.RegisterCloneBase(junkCbid, CloneBaseObjectType.Item);
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = propCbid, LootCbid = junkCbid, Weight = 500 },
        });
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, 1, 1);

        var (character, map) = CreatePlayerOnMap(dropCommodities: false);
        var prop = CreateDamagableProp(maxHp: 10, cbid: propCbid);
        prop.SetCoid(PropCoid, false);
        prop.Position = new Vector3(8, 0, 8);
        prop.SetMap(map);
        prop.SetInvincible(false);
        prop.SetMurderer(character.CurrentVehicle);

        prop.TakeDamage(10);
        prop.OnDeath(DeathType.Silent);

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == junkCbid),
            "tLootWeights must apply to destroyed map props by CBID");
    }

    [TestMethod]
    public void GraphicsObject_OnDeath_NoCommodityEvenWhenContinentAllows()
    {
        const int propCbid = 8803;
        const int salvageCbid = 5477; // Salvaged Radioactive Material
        AssetManagerTestHelper.RegisterCloneBase(propCbid, CloneBaseObjectType.Object);
        AssetManagerTestHelper.RegisterCloneBase(salvageCbid, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(salvageCbid, minLevel: 1, maxLevel: 100, dropChance: 1f);

        var (character, map) = CreatePlayerOnMap(dropCommodities: true);
        var prop = CreateDamagableProp(maxHp: 10, cbid: propCbid);
        prop.SetCoid(PropCoid, false);
        prop.SetMap(map);
        prop.SetInvincible(false);
        prop.SetMurderer(character.CurrentVehicle);

        prop.TakeDamage(10);
        prop.OnDeath(DeathType.Silent);

        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == salvageCbid),
            "radioactive/nuts commodities are combatant tracks, not random prop rams");
        MapPropCorpseDespawn.FlushAllForTests();
    }

    private static GraphicsObject CreateDamagableProp(int maxHp, int cbid = 0)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        // Unit tests may not have clonebase.wad; seed HP directly after a fake clonebase-style init.
        prop.InitializeHealthForTests(maxHp);
        if (cbid > 0)
            prop.LoadCloneBase(cbid);
        return prop;
    }

    private (Character Character, SectorMap Map) CreatePlayerOnMap(bool dropCommodities = true)
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_prop_dmg_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
            DropCommodities = dropCommodities,
            MinLevel = 1,
            MaxLevel = 50,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(450, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(451, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, map);
    }
}
