using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// UseObject dispatch for vendor OpenStore (open-only via 0x206C).
/// </summary>
[TestClass]
public class ObjectUseManagerVendorTests
{
    private const int ContId = 8801;
    private const long StoreCoid = 9819;
    private const long ReactionCoid = 50001;
    private const long KioskCoid = 50002;
    private const int KioskCbid = 12700;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        SectorMap.SendGroupReactionCall = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
    }

    [TestMethod]
    public void UseObject_OpenStoreNearKiosk_SendsGroupReactionCall()
    {
        var (conn, character, map) = CreatePlayer();
        PlaceStoreAndOpenReaction(map, storePos: new Vector3(2, 0, 0), reactionCoid: ReactionCoid, storeCoid: StoreCoid);
        PlaceKiosk(map, KioskCoid, KioskCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        ObjectUseManager.Handle(conn, new UseObjectPacket
        {
            Target = new TFID(KioskCoid, false),
            ObjectiveId = -1,
        });

        var grc = _sent.OfType<GroupReactionCallPacket>().ToList();
        Assert.AreEqual(1, grc.Count, "OpenStore should emit GroupReactionCall 0x206C");
        Assert.AreEqual(1, grc[0].Count);
    }

    [TestMethod]
    public void UseObject_OpenStore_ClickStoreCoid_Opens()
    {
        var (conn, character, map) = CreatePlayer();
        PlaceStoreAndOpenReaction(map, storePos: new Vector3(1, 0, 0), reactionCoid: ReactionCoid, storeCoid: StoreCoid);
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        ObjectUseManager.Handle(conn, new UseObjectPacket
        {
            Target = new TFID(StoreCoid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(1, _sent.OfType<GroupReactionCallPacket>().Count());
    }

    [TestMethod]
    public void UseObject_OpenStore_OutOfRange_NoPacket()
    {
        var (conn, character, map) = CreatePlayer();
        PlaceStoreAndOpenReaction(map, storePos: new Vector3(500, 0, 0), reactionCoid: ReactionCoid, storeCoid: StoreCoid);
        PlaceKiosk(map, KioskCoid, KioskCbid, new Vector3(500, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        ObjectUseManager.Handle(conn, new UseObjectPacket
        {
            Target = new TFID(KioskCoid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count());
    }

    [TestMethod]
    public void UseObject_OpenStore_KioskFartherThanOldCap_StillOpens()
    {
        // Kiosk can sit ~40–60u from stock object while player is at the kiosk.
        var (conn, character, map) = CreatePlayer();
        PlaceStoreAndOpenReaction(map, storePos: new Vector3(50, 0, 0), reactionCoid: ReactionCoid, storeCoid: StoreCoid);
        PlaceKiosk(map, KioskCoid, KioskCbid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        ObjectUseManager.Handle(conn, new UseObjectPacket
        {
            Target = new TFID(KioskCoid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(1, _sent.OfType<GroupReactionCallPacket>().Count());
    }

    [TestMethod]
    public void UseObject_KioskSpawnTriggerEvents_FiresOpenStoreChain()
    {
        // Retail: spawn TriggerEvents[2] = trigger → OpenStore reaction (0x206C).
        var (conn, character, map) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        const long spawnCoid = 9837;
        const long triggerCoid = 9838;
        const long openStoreReactionCoid = 9839;

        // Store stock (far away is OK — client opens by COID).
        var store = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        store.SetCoid(StoreCoid, false);
        store.Position = new Vector3(5000, 0, 5000);
        store.SetMap(map);

        var openStoreTpl = new ReactionTemplate
        {
            COID = (int)openStoreReactionCoid,
            ReactionType = ReactionType.OpenStore,
            GenericVar1 = (int)StoreCoid,
            Name = "l1_openstore_dispensary",
            ObjectiveIDCheck = -1,
            ActOnActivator = true,
        };
        var openStore = new Reaction(openStoreTpl);
        openStore.SetCoid(openStoreReactionCoid, false);
        openStore.Position = new Vector3(0, 0, 0);
        openStore.SetMap(map);

        var triggerTpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            Name = "l1_rem_generalstore_1",
            Location = new Vector4(0, 0, 0, 1),
            ActivationCount = -1,
            DoOnActivate = true,
        };
        triggerTpl.Reactions.Add(openStoreReactionCoid);
        var trigger = (Trigger)triggerTpl.Create();
        trigger.SetCoid(triggerCoid, false);
        trigger.SetMap(map);

        var spawnTpl = new SpawnPointTemplate
        {
            COID = (int)spawnCoid,
            Location = new Vector4(0, 0, 0, 1),
            TriggerEvents = new long[] { -1, -1, triggerCoid },
        };
        // Minimal spawn list so SpawnPoint exists as owner (child already placed).
        var spawn = (SpawnPoint)spawnTpl.Create();
        spawn.SetCoid(spawnCoid, false);
        spawn.SetMap(map);

        var kiosk = new Creature();
        kiosk.SetCoid(KioskCoid, true); // MapNpcIdentity-style global id
        kiosk.SetCbidForTests(KioskCbid);
        kiosk.SpawnOwner = spawnCoid;
        kiosk.Position = new Vector3(0, 0, 0);
        kiosk.SetMap(map);

        ObjectUseManager.Handle(conn, new UseObjectPacket
        {
            Target = new TFID(KioskCoid, true),
            ObjectiveId = -1,
        });

        Assert.AreEqual(1, _sent.OfType<GroupReactionCallPacket>().Count(),
            "spawn TriggerEvents should fire OpenStore via 0x206C");
    }

    [TestMethod]
    public void EnsureOpenStore_MaterializesFromMapTemplates()
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId + 1,
            MapFileName = "t_store_mat",
            DisplayName = "t",
            IsPersistent = true,
        }, new Vector4());

        var storeTpl = new StoreTemplate
        {
            COID = (int)StoreCoid,
            CBID = 1784,
            Location = new Vector4(5, 0, 0, 0),
            Name = "test dispensary",
        };
        map.MapData.Templates[StoreCoid] = storeTpl;

        var rxTpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            CBID = 86,
            ReactionType = ReactionType.OpenStore,
            GenericVar1 = (int)StoreCoid,
            Name = "l1_openstore_test",
            ObjectiveIDCheck = -1,
        };
        map.MapData.Templates[ReactionCoid] = rxTpl;

        Assert.IsNull(map.GetObjectByCoid(ReactionCoid));
        VendorStoreService.EnsureOpenStoreReactionsMaterialized(map);
        Assert.IsInstanceOfType(map.GetObjectByCoid(ReactionCoid), typeof(Reaction));

        var best = VendorStoreService.FindBestOpenStoreReaction(
            map, new Vector3(0, 0, 0), targetCoid: KioskCoid);
        Assert.IsNotNull(best);
        Assert.AreEqual((int)StoreCoid, best.Template.GenericVar1);
    }

    [TestMethod]
    public void FindBestOpenStore_PrefersMatchingStoreCoid()
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_store",
            DisplayName = "t",
            IsPersistent = true,
        }, new Vector4());

        PlaceStoreAndOpenReaction(map, storePos: new Vector3(0, 0, 0), reactionCoid: 1, storeCoid: 100);
        PlaceStoreAndOpenReaction(map, storePos: new Vector3(1, 0, 0), reactionCoid: 2, storeCoid: 200);

        var best = VendorStoreService.FindBestOpenStoreReaction(map, new Vector3(0, 0, 0), targetCoid: 200);
        Assert.IsNotNull(best);
        Assert.AreEqual(200, best.Template.GenericVar1);
    }

    [TestMethod]
    public void UseObject_FacilityBodyShop_SendsGroupReactionCall()
    {
        var (conn, character, map) = CreatePlayer();
        var reaction = PlaceFacilityReaction(map, ReactionType.OpenBodyShop, reactionCoid: 60001, pos: new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        ObjectUseManager.Handle(conn, new UseObjectPacket
        {
            Target = new TFID(reaction.ObjectId.Coid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(1, _sent.OfType<GroupReactionCallPacket>().Count());
    }

    private static void PlaceStoreAndOpenReaction(
        SectorMap map,
        Vector3 storePos,
        long reactionCoid,
        long storeCoid)
    {
        var store = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        store.SetCoid(storeCoid, false);
        store.Position = storePos;
        store.SetMap(map);

        var template = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.OpenStore,
            ActOnActivator = true,
            ObjectiveIDCheck = -1,
            GenericVar1 = (int)storeCoid,
            Name = "test_openstore",
        };

        var reaction = new Reaction(template);
        reaction.SetCoid(reactionCoid, false);
        reaction.Position = storePos;
        reaction.SetMap(map);
    }

    private static Reaction PlaceFacilityReaction(
        SectorMap map,
        ReactionType type,
        long reactionCoid,
        Vector3 pos)
    {
        var template = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = type,
            ActOnActivator = true,
            ObjectiveIDCheck = -1,
            GenericVar1 = (int)reactionCoid,
            Name = "test_facility",
        };

        var reaction = new Reaction(template);
        reaction.SetCoid(reactionCoid, false);
        reaction.Position = pos;
        reaction.SetMap(map);
        return reaction;
    }

    private static void PlaceKiosk(SectorMap map, long coid, int cbid, Vector3 pos)
    {
        var npc = new Creature();
        npc.SetCoid(coid, false);
        npc.SetCbidForTests(cbid);
        npc.Position = pos;
        npc.SetMap(map);
    }

    private static (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_vendor",
            DisplayName = "t",
            IsPersistent = true,
            IsTown = true,
        }, new Vector4());

        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18451, true);
        character.SetOwningConnection(conn);
        conn.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(18452, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (conn, character, map);
    }
}
