using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Loot attribution: only combat a player actually took part in may drop loot, and the credit
/// follows the player who initiated it (the "tagger") even when an NPC lands the killing blow.
///
/// Regression: NPC-vs-NPC kills dropped full loot piles because <c>ProcessDeathLoot</c> never
/// checked whether a player was involved — <c>Killer</c> was simply null and every ground drop
/// still ran. Combined with the map-wide loot broadcast that made every player on the map watch
/// loot rain from NPC skirmishes they never fought.
/// </summary>
[TestClass]
public class LootAttributionTests
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

    // ---------------------------------------------------------------- tagging

    [TestMethod]
    public void TakeDamage_FromPlayerVehicle_TagsVictimWithTheCharacter()
    {
        var map = CreateMap(9600);
        var player = CreateCharacterOnMap(map, characterCoid: 5600);
        var victim = CreateCreature(map, cbid: 8600, coid: 9601);

        victim.TakeDamage(5, player.CurrentVehicle);

        Assert.IsNotNull(victim.LootTaggedBy, "player damage must tag the victim for loot");
        Assert.AreEqual(player.ObjectId.Coid, victim.LootTaggedBy.Coid);
        Assert.IsTrue(victim.LootTaggedBy.Global);
    }

    [TestMethod]
    public void TakeDamage_FromNpc_DoesNotTagVictim()
    {
        var map = CreateMap(9610);
        var attacker = CreateCreature(map, cbid: 8610, coid: 9611);
        var victim = CreateCreature(map, cbid: 8611, coid: 9612);

        victim.TakeDamage(5, attacker);

        Assert.IsNull(victim.LootTaggedBy, "NPC-vs-NPC damage must never tag a victim for loot");
    }

    [TestMethod]
    public void TakeDamage_FirstPlayerTaggerWins()
    {
        var map = CreateMap(9620);
        var first = CreateCharacterOnMap(map, characterCoid: 5620);
        var second = CreateCharacterOnMap(map, characterCoid: 5630);
        var victim = CreateCreature(map, cbid: 8620, coid: 9621);

        victim.TakeDamage(5, first.CurrentVehicle);
        victim.TakeDamage(5, second.CurrentVehicle);

        Assert.AreEqual(
            first.ObjectId.Coid,
            victim.LootTaggedBy.Coid,
            "the player who initiated combat keeps the credit");
    }

    [TestMethod]
    public void TakeDamage_ThatDealsNoDamage_DoesNotTag()
    {
        var map = CreateMap(9630);
        var player = CreateCharacterOnMap(map, characterCoid: 5640);
        var victim = CreateCreature(map, cbid: 8630, coid: 9631);

        victim.TakeDamage(0, player.CurrentVehicle);

        Assert.IsNull(victim.LootTaggedBy, "a hit that lands no damage does not initiate combat");
    }

    // ------------------------------------------------------------ loot gating

    [TestMethod]
    public void ProcessDeathLoot_WithoutKiller_DropsNothing()
    {
        const int lootCbid = 8701;
        const int lootTableId = 87;
        SeedAlwaysDroppingTable(lootTableId, lootCbid);

        var map = CreateMap(9700);
        CreateCharacterOnMap(map, characterCoid: 5700);

        LootManager.Instance.ProcessDeathLoot(new LootManager.DeathLootRequest
        {
            Map = map,
            Position = new Vector3(1, 0, 1),
            Rotation = Quaternion.Default,
            Killer = null,
            VictimCbid = 8700,
            Level = 1,
            LootTableId = lootTableId,
            UseCreatureDropFormula = true,
            CreatureBaseLootChance = 255,
        });

        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == lootCbid),
            "loot must not drop when no player was involved in the kill");
    }

    [TestMethod]
    public void CreatureKilledByNpc_WithNoPlayerInvolvement_DropsNothing()
    {
        const int creatureCbid = 8800;
        const int lootCbid = 8801;
        const int lootTableId = 88;
        SeedAlwaysDroppingCreature(creatureCbid, lootTableId, lootCbid);

        var map = CreateMap(9800);
        CreateCharacterOnMap(map, characterCoid: 5800);
        var npcKiller = CreateCreature(map, cbid: 8802, coid: 9802);
        var victim = CreateCreature(map, cbid: creatureCbid, coid: 9801);

        // An NPC damages and kills it; no player ever touched this fight.
        victim.TakeDamage(5, npcKiller);
        victim.SetMurderer(npcKiller);
        victim.OnDeath(DeathType.Violent);

        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == lootCbid),
            "an NPC killing an NPC must not litter the map with loot");
    }

    [TestMethod]
    public void CreatureTaggedByPlayerButKilledByNpc_DropsLootCreditedToTagger()
    {
        const int creatureCbid = 8900;
        const int lootCbid = 8901;
        const int lootTableId = 89;
        SeedAlwaysDroppingCreature(creatureCbid, lootTableId, lootCbid);

        var map = CreateMap(9900);
        var player = CreateCharacterOnMap(map, characterCoid: 5900);
        var npcKiller = CreateCreature(map, cbid: 8902, coid: 9902);
        var victim = CreateCreature(map, cbid: creatureCbid, coid: 9901);
        victim.Position = player.Position;

        // Player starts the fight, an NPC finishes it.
        victim.TakeDamage(5, player.CurrentVehicle);
        victim.TakeDamage(5, npcKiller);
        victim.SetMurderer(npcKiller);
        victim.OnDeath(DeathType.Violent);

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == lootCbid),
            "a fight the player initiated still drops loot when an NPC lands the killing blow");
    }

    [TestMethod]
    public void CreatureKilledByPlayer_StillDropsLoot()
    {
        const int creatureCbid = 9000;
        const int lootCbid = 9001;
        const int lootTableId = 90;
        SeedAlwaysDroppingCreature(creatureCbid, lootTableId, lootCbid);

        var map = CreateMap(10000);
        var player = CreateCharacterOnMap(map, characterCoid: 6000);
        var victim = CreateCreature(map, cbid: creatureCbid, coid: 10001);
        victim.Position = player.Position;

        victim.TakeDamage(5, player.CurrentVehicle);
        victim.SetMurderer(player.CurrentVehicle);
        victim.OnDeath(DeathType.Violent);

        Assert.IsTrue(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == lootCbid),
            "normal player kills must be unaffected by the attribution gate");
    }

    // ------------------------------------------------------------------ setup

    private static void SeedAlwaysDroppingTable(int lootTableId, int lootCbid)
    {
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
    }

    private static void SeedAlwaysDroppingCreature(int creatureCbid, int lootTableId, int lootCbid)
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(creatureCbid, baseLevel: 1);
        var creatureBase = (AutoCore.Game.CloneBases.CloneBaseCreature)
            AssetManager.Instance.GetCloneBase(creatureCbid)!;
        var cs = creatureBase.CreatureSpecific;
        cs.LootTableId = lootTableId;
        cs.BaseLootChance = 255;
        creatureBase.CreatureSpecific = cs;

        SeedAlwaysDroppingTable(lootTableId, lootCbid);
    }

    private static Creature CreateCreature(SectorMap map, int cbid, long coid)
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, baseLevel: 1);
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.LoadCloneBase(cbid);
        creature.Level = 1;
        creature.ScaleHealthForLevel(1);
        creature.Position = new Vector3(10, 0, 10);
        creature.SetMap(map);
        return creature;
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(localCoid % 10000),
            MapFileName = $"tm_loot_attribution_{localCoid}",
            DisplayName = "lootattrib",
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
