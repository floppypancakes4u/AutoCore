using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Buy path: client sends store-slot TFID (not catalog CBID). Server must map session
/// stock COIDs / CBIDs and grant cargo with a correct 0x2028 body (FUN_00810670).
/// Live fail: item=(11103) stock lines=2663,2664 — no CBID match.
/// </summary>
[TestClass]
public class VendorStoreBuyRegressionTests
{
    private const int ContId = 8811;
    private const long StoreCoid = 9819;
    private const int StockCbidA = 2663;
    private const int StockCbidB = 2664;
    private const long BuyUnitPrice = 400;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestBuyPriceResolver = cbid =>
            cbid is StockCbidA or StockCbidB ? BuyUnitPrice : 0;
        VendorStoreService.TestBuyCatalogResolver = cbid =>
            cbid is StockCbidA or StockCbidB
                ? new InventoryCatalogEntry(cbid, CloneBaseObjectType.Item, $"stock-{cbid}")
                : null;
        VendorStoreService.TestItemCreator = new TestBuyItemCreator();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestBuyPriceResolver = null;
        VendorStoreService.TestBuyCatalogResolver = null;
        VendorStoreService.TestItemCreator = null;
        _sent.Clear();
    }

    [TestMethod]
    public void StoreTransactionRequest_LiveBuyHex_ParsesItemAndStoreCoid()
    {
        // Live capture: isBuy, qty=1, item=11103 Global, store=9819 at +0x28
        var hex = "272000000000000063BF526E48135A1A00000000000000005F2B00000000000001000000000000005B26000000000000000000000000000001817A0001000000";
        var raw = Convert.FromHexString(hex);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        br.ReadUInt32();
        var packet = new StoreTransactionRequestPacket();
        packet.Read(br);

        Assert.IsTrue(packet.IsBuy);
        Assert.AreEqual(1, packet.Quantity);
        Assert.AreEqual(11103L, packet.Item.Coid);
        Assert.IsTrue(packet.Item.Global);
        Assert.AreEqual(9819L, packet.StoreCoid, "store TFID/coid at +0x28");
    }

    [TestMethod]
    public void MatchBuyLine_ByCatalogCbid_StillWorks()
    {
        var stock = new List<StoreTemplate.ItemType>
        {
            new() { CBID = StockCbidA, Unlimited = true, Value = (int)BuyUnitPrice },
            new() { CBID = StockCbidB, Unlimited = true, Value = (int)BuyUnitPrice },
        };

        var line = VendorStoreService.MatchBuyLineForTests(stock, itemCoid: StockCbidA, session: null);
        Assert.IsNotNull(line);
        Assert.AreEqual(StockCbidA, line.CBID);
    }

    [TestMethod]
    public void MatchBuyLine_BySessionSlotCoid_MapsClientStoreObject()
    {
        var stock = new List<StoreTemplate.ItemType>
        {
            new() { CBID = StockCbidA, Unlimited = true },
            new() { CBID = StockCbidB, Unlimited = true },
        };
        var session = new Dictionary<long, StoreTemplate.ItemType>
        {
            [11103] = stock[0],
            [11104] = stock[1],
        };

        var line = VendorStoreService.MatchBuyLineForTests(stock, itemCoid: 11103, session);
        Assert.IsNotNull(line);
        Assert.AreEqual(StockCbidA, line.CBID);
    }

    [TestMethod]
    public void NoteOpened_MaterializesSessionStock_WithCreatePackets()
    {
        var (conn, character, map) = CreatePlayer(credits: 50_000);
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);

        VendorStoreService.NoteOpened(character, StoreCoid, conn);

        var session = VendorStoreService.GetStockSessionForTests(character.ObjectId.Coid);
        Assert.IsNotNull(session);
        Assert.AreEqual(2, session.Count, "one session slot per stock CBID");
        Assert.IsTrue(session.Values.Any(l => l.CBID == StockCbidA));
        Assert.IsTrue(session.Values.Any(l => l.CBID == StockCbidB));

        var creates = _sent.OfType<CreateSimpleObjectPacket>().ToList();
        Assert.AreEqual(2, creates.Count);
        Assert.IsTrue(creates.All(c => c.CoidStore == StoreCoid));
        Assert.IsTrue(creates.All(c => c.IsInInventory));
        Assert.IsTrue(creates.All(c => c.IsInfinite));
        Assert.IsTrue(creates.Select(c => c.CBID).OrderBy(x => x).SequenceEqual(new[] { StockCbidA, StockCbidB }));
    }

    [TestMethod]
    public void HandleTransaction_Buy_BySessionCoid_GrantsItemAndCharges()
    {
        var (conn, character, map) = CreatePlayer(credits: 50_000);
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);
        VendorStoreService.NoteOpened(character, StoreCoid, conn);
        _sent.Clear();

        var session = VendorStoreService.GetStockSessionForTests(character.ObjectId.Coid);
        var slotCoid = session.First(kv => kv.Value.CBID == StockCbidA).Key;

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(slotCoid, true),
            StoreCoid = StoreCoid,
            IsBuy = true,
            Quantity = 1,
        });

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(response.WasSuccessful);
        Assert.IsTrue(response.IsBuy);
        Assert.AreEqual(50_000 - BuyUnitPrice, character.Credits);
        Assert.AreEqual(character.Credits, response.Credits);
        Assert.AreEqual(slotCoid, response.RelatedCoidB, "echo store-slot TFID at +0x18");
        Assert.IsTrue(response.ItemCoid > 0, "new grant COID at +0x08");
        Assert.AreNotEqual(slotCoid, response.ItemCoid);

        Assert.IsTrue(
            character.Inventory.CountByCbid(StockCbidA) >= 1,
            "purchased CBID must land in cargo");
        Assert.IsTrue(_sent.OfType<CreateSimpleObjectPacket>().Any(p => p.ObjectId.Coid == response.ItemCoid)
            || _sent.OfType<InventoryAddItemResponsePacket>().Any(),
            "client cargo wire for grant");
    }

    [TestMethod]
    public void HandleTransaction_Buy_ByCatalogCbid_WithoutSession_StillWorks()
    {
        var (conn, character, map) = CreatePlayer(credits: 10_000);
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);
        // Session open without materialize packets (legacy path)
        VendorStoreService.NoteOpened(character, StoreCoid, conn: null);
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(StockCbidB, false),
            IsBuy = true,
            Quantity = 1,
        });

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(10_000 - BuyUnitPrice, character.Credits);
        Assert.IsTrue(character.Inventory.CountByCbid(StockCbidB) >= 1);
    }

    [TestMethod]
    public void HandleTransaction_Buy_UnknownCoid_Fails()
    {
        var (conn, character, map) = CreatePlayer(credits: 10_000);
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);
        VendorStoreService.NoteOpened(character, StoreCoid, conn);
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(999999, true),
            IsBuy = true,
            Quantity = 1,
        });

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsFalse(response.WasSuccessful);
        Assert.AreEqual(10_000L, character.Credits);
    }

    [TestMethod]
    public void HandleTransaction_Buy_InsufficientCredits_Fails()
    {
        var (conn, character, map) = CreatePlayer(credits: 1);
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);
        VendorStoreService.NoteOpened(character, StoreCoid, conn);
        var slotCoid = VendorStoreService.GetStockSessionForTests(character.ObjectId.Coid).Keys.First();
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(slotCoid, true),
            IsBuy = true,
            Quantity = 1,
        });

        Assert.IsFalse(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        Assert.AreEqual(1L, character.Credits);
    }

    [TestMethod]
    public void HandleTransaction_Buy_ResponseWire_IsBuyAndLayout()
    {
        var (conn, character, map) = CreatePlayer(credits: 50_000);
        PlaceStore(map, StoreCoid, StockCbidA, StockCbidB);
        VendorStoreService.NoteOpened(character, StoreCoid, conn);
        var slotCoid = VendorStoreService.GetStockSessionForTests(character.ObjectId.Coid)
            .First(kv => kv.Value.CBID == StockCbidA).Key;
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(slotCoid, true),
            IsBuy = true,
            Quantity = 1,
        });

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)response.Opcode);
        response.Write(writer);
        stream.SetLength(stream.Position);
        var bytes = stream.ToArray();

        Assert.AreEqual(0x30, bytes.Length);
        Assert.AreEqual(1, bytes[0x28]);
        Assert.AreEqual(1, bytes[0x29], "isBuy");
        Assert.AreEqual(response.ItemCoid, BitConverter.ToInt64(bytes, 0x08));
        Assert.AreEqual(slotCoid, BitConverter.ToInt64(bytes, 0x18));
        Assert.AreEqual(character.Credits, BitConverter.ToInt64(bytes, 0x20));
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
                Value = (int)BuyUnitPrice,
            });
        }

        // Pad to map template shape (reader always fills 10/30 slots)
        while (tpl.Items.Count < 10)
            tpl.Items.Add(new StoreTemplate.ItemType());

        map.MapData.Templates[storeCoid] = tpl;

        var store = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        store.SetCoid(storeCoid, false);
        store.Position = new Vector3(0, 0, 0);
        store.SetMap(map);
    }

    private sealed class TestBuyItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
        {
            var packet = new CreateSimpleObjectPacket
            {
                CBID = entry.Cbid,
                ObjectId = new TFID(coid, true),
                IsInInventory = true,
                Quantity = 1,
                InventoryPositionX = x,
                InventoryPositionY = y,
                IsIdentified = true,
                Scale = 1f,
            };
            return InventoryItemCreateResult.Success(packet, entry.DisplayName);
        }
    }

    static (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer(long credits)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_buy",
            DisplayName = "t",
            IsPersistent = true,
            IsTown = true,
        }, new Vector4());
        map.LocalCoidCounter = 20000;

        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18451, true);
        character.AttachTestDataForTests("Buyer");
        character.SetCredits(credits);
        character.SetOwningConnection(conn);
        conn.CurrentCharacter = character;
        character.AttachInventoryForTests(new InventoryManager());

        var vehicle = new Vehicle();
        vehicle.SetCoid(18452, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (conn, character, map);
    }
}
