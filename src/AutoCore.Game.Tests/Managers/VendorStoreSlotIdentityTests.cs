using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// SS-31: vendor store display slots must not mint Global=true TFIDs from the raw map
/// counter — that space overlaps DB-sequence cargo coids and crashes the client on collision.
/// Slots must come from the reserved <see cref="StoreSlotIdentity"/> offset range instead.
/// </summary>
[TestClass]
public class VendorStoreSlotIdentityTests
{
    private const int ContId = 8812;
    private const long StoreCoid = 9820;
    private const int StockCbidA = 2665;
    private const int StockCbidB = 2666;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        VendorStoreService.ResetSessionsForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        VendorStoreService.ResetSessionsForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void AllocateSlotCoid_ReturnsGlobalCoidAtOrAboveBase_AndAdvancesCounter()
    {
        long counter = 7;
        var id = StoreSlotIdentity.AllocateSlotCoid(ref counter);

        Assert.IsTrue(id.Global, "Store slot COIDs must be Global so they do not match client-local map objects.");
        Assert.IsTrue(id.Coid >= StoreSlotIdentity.CoidBase, "Store slot COIDs must sit above the reserved base.");
        Assert.AreEqual(StoreSlotIdentity.CoidBase + 7, id.Coid);
        Assert.AreEqual(8, counter, "Counter must advance after allocation.");
        Assert.IsTrue(StoreSlotIdentity.IsStoreSlotIdentity(id));
    }

    [TestMethod]
    public void AllocateSlotCoid_RangeDisjointFromMapNpcIdentity()
    {
        // SS-31 why: MapNpcIdentity occupies 0x5000_0000+; a distinct vendor-slot base
        // must sit strictly above it so the two allocators' minted ranges never overlap
        // for realistic counter values.
        long counter = 0;
        var slotId = StoreSlotIdentity.AllocateSlotCoid(ref counter);

        Assert.IsTrue(StoreSlotIdentity.CoidBase > MapNpcIdentity.CoidBase, "Store slot base must sit above the map-NPC base.");
        Assert.IsTrue(slotId.Coid > MapNpcIdentity.CoidBase, "Store slot base must sit above the map-NPC range.");
        Assert.IsFalse(StoreSlotIdentity.IsStoreSlotIdentity(new TFID(MapNpcIdentity.CoidBase, global: true)),
            "A map-NPC base id must not be misread as a store slot id.");
    }

    [TestMethod]
    public void MaterializeStock_SlotCoids_DoNotCollideWithCargoCoids_EvenWhenCounterMatchesCargo()
    {
        // SS-31 why: old code emitted Global=true raw counter values that collided
        // client-side with DB-sequence cargo coids. Simulate that exact overlap: the map's
        // local counter is seeded to the same value as an existing cargo item's coid.
        const long cargoCoid = 500;
        var (conn, character, map) = CreatePlayer(credits: 1_000, mapLocalCoidCounter: cargoCoid);
        character.Inventory.TryAdd(new CharacterInventoryItem(999, CloneBaseObjectType.Item, "cargo-item", cargoCoid, 0, 0, 1));
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);

        VendorStoreService.NoteOpened(character, StoreCoid, conn);

        var session = VendorStoreService.GetStockSessionForTests(character.ObjectId.Coid);
        Assert.IsNotNull(session);
        Assert.AreEqual(2, session.Count, "one session slot per stock CBID");

        var cargoCoids = new HashSet<long> { cargoCoid };
        foreach (var slotCoid in session.Keys)
        {
            Assert.IsTrue(slotCoid >= StoreSlotIdentity.CoidBase,
                $"Slot coid {slotCoid} must be minted from the reserved offset range.");
            Assert.IsFalse(cargoCoids.Contains(slotCoid),
                $"Slot coid {slotCoid} must not collide with an existing cargo coid.");
        }

        var creates = _sent.OfType<CreateSimpleObjectPacket>().ToList();
        Assert.AreEqual(2, creates.Count);
        foreach (var create in creates)
        {
            Assert.IsTrue(create.ObjectId.Global, "Wire slot TFIDs must stay Global=true.");
            Assert.IsTrue(create.ObjectId.Coid >= StoreSlotIdentity.CoidBase,
                "Wire slot TFIDs must be minted from the reserved offset range.");
        }
    }

    static void PlaceStore(SectorMap map, long storeCoid, params int[] cbids)
    {
        var tpl = new StoreTemplate
        {
            COID = (int)storeCoid,
            Name = "test-store",
        };
        foreach (var cbid in cbids)
        {
            tpl.Items.Add(new StoreTemplate.ItemType
            {
                Type = 52,
                CBID = cbid,
                Unlimited = true,
                Value = 100,
            });
        }

        while (tpl.Items.Count < 10)
            tpl.Items.Add(new StoreTemplate.ItemType());

        map.MapData.Templates[storeCoid] = tpl;

        var store = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        store.SetCoid(storeCoid, false);
        store.Position = new Vector3(0, 0, 0);
        store.SetMap(map);
    }

    static (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer(long credits, long mapLocalCoidCounter)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_slotid",
            DisplayName = "t",
            IsPersistent = true,
            IsTown = true,
        }, new Vector4());
        map.LocalCoidCounter = mapLocalCoidCounter;

        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18461, true);
        character.AttachTestDataForTests("SlotIdBuyer");
        character.SetCredits(credits);
        character.SetOwningConnection(conn);
        conn.CurrentCharacter = character;
        character.AttachInventoryForTests(new InventoryManager());

        var vehicle = new Vehicle();
        vehicle.SetCoid(18462, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (conn, character, map);
    }
}
