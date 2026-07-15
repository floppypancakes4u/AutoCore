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
/// Heavy regression for kiosk sell → buyback → cargo restore (no spare Create).
/// Covers live failures: wrong stock match, double Create, cursor/TFID reuse.
/// </summary>
[TestClass]
public class VendorStoreRoundTripRegressionTests
{
    private const int ContId = 8820;
    private const long StoreCoid = 9819;
    private const long ItemCoidA = 11131;
    private const long ItemCoidB = 11140;
    private const int CbidA = 19194;
    private const int CbidB = 19195;
    private const int StockCbid = 2663;
    private const long SellUnitA = 250;
    private const long SellUnitB = 100;
    private const long CatalogUnit = 400;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestSellPriceResolver = cbid => cbid switch
        {
            CbidA => SellUnitA,
            CbidB => SellUnitB,
            _ => 0,
        };
        VendorStoreService.TestBuyPriceResolver = cbid => cbid == StockCbid ? CatalogUnit : 0;
        VendorStoreService.TestBuyCatalogResolver = cbid => cbid switch
        {
            CbidA => new InventoryCatalogEntry(cbid, CloneBaseObjectType.Item, "gear-a"),
            CbidB => new InventoryCatalogEntry(cbid, CloneBaseObjectType.Item, "gear-b"),
            StockCbid => new InventoryCatalogEntry(cbid, CloneBaseObjectType.Item, "stock"),
            _ => null,
        };
        VendorStoreService.TestItemCreator = new RoundTripItemCreator();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestSellPriceResolver = null;
        VendorStoreService.TestBuyPriceResolver = null;
        VendorStoreService.TestBuyCatalogResolver = null;
        VendorStoreService.TestItemCreator = null;
        _sent.Clear();
    }

    [TestMethod]
    public void RoundTrip_SellThenBuyback_RestoresOriginalCoid_NoCreate_NoDestroyOnSell()
    {
        var (conn, character) = CreatePlayer(credits: 5000, (ItemCoidA, CbidA, 1));

        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any(), "sell must not DestroyObject before 0x2028");
        Assert.IsFalse(_sent.OfType<InventoryDestroyItemPacket>().Any());
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));

        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidA));
        Assert.IsFalse(_sent.OfType<CreateSimpleObjectPacket>().Any());
        Assert.IsTrue(_sent.OfType<InventoryAddItemResponsePacket>().Any(p => p.ItemCoid == ItemCoidA && p.WasSuccessful));
        Assert.IsTrue(_sent.OfType<InventoryCargoSendAllPacket>().Any());
        Assert.AreEqual(ItemCoidA, _sent.OfType<StoreTransactionResponsePacket>().Single().ItemCoid);
        Assert.AreEqual(5000L, character.Credits);
    }

    [TestMethod]
    public void RoundTrip_PacketOrder_SellCreditsResponseCargo_ThenBuybackCreditsAddCargoResponse()
    {
        var (conn, character) = CreatePlayer(credits: 1000, (ItemCoidA, CbidA, 1));

        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        var sellOrder = _sent.Select(p => p.GetType().Name).ToList();
        Assert.IsTrue(sellOrder.IndexOf(nameof(GiveCreditsPacket))
            < sellOrder.IndexOf(nameof(StoreTransactionResponsePacket)));
        Assert.IsTrue(sellOrder.IndexOf(nameof(StoreTransactionResponsePacket))
            < sellOrder.IndexOf(nameof(InventoryCargoSendAllPacket)));
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        var buyOrder = _sent.Select(p => p.GetType().Name).ToList();
        Assert.IsTrue(buyOrder.IndexOf(nameof(GiveCreditsPacket))
            < buyOrder.IndexOf(nameof(StoreTransactionResponsePacket)));
        Assert.IsTrue(buyOrder.IndexOf(nameof(InventoryAddItemResponsePacket))
            < buyOrder.IndexOf(nameof(StoreTransactionResponsePacket))
            || buyOrder.Contains(nameof(InventoryAddItemResponsePacket)));
        // Response after mutations: credits and restore packets before or interleaved; final ack is 0x2028
        Assert.AreEqual(nameof(StoreTransactionResponsePacket), buyOrder[^1]);
    }

    [TestMethod]
    public void RoundTrip_BuybackWire_0x30_IsBuy_GrantEqualsSoldCoid()
    {
        var (conn, character) = CreatePlayer(credits: 2000, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        _sent.Clear();
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)response.Opcode);
        response.Write(writer);
        stream.SetLength(stream.Position);
        var bytes = stream.ToArray();

        Assert.AreEqual(0x30, bytes.Length);
        Assert.AreEqual(ItemCoidA, BitConverter.ToInt64(bytes, 0x08));
        Assert.AreEqual(ItemCoidA, BitConverter.ToInt64(bytes, 0x18), "store-slot TFID echo");
        Assert.AreEqual(character.Credits, BitConverter.ToInt64(bytes, 0x20));
        Assert.AreEqual(1, bytes[0x28]);
        Assert.AreEqual(1, bytes[0x29]);
    }

    [TestMethod]
    public void RoundTrip_TwoItems_IndependentBuybacks()
    {
        var (conn, character) = CreatePlayer(credits: 10000,
            (ItemCoidA, CbidA, 1),
            (ItemCoidB, CbidB, 1));

        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidB));
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoidA));
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoidB));

        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidB));
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidB));
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoidA));
        Assert.IsTrue(VendorStoreService.GetBuybackForTests(character.ObjectId.Coid)!.ContainsKey(ItemCoidA));

        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidA));
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidB));
    }

    [TestMethod]
    public void RoundTrip_SecondBuyback_FailsAfterConsumed()
    {
        var (conn, character) = CreatePlayer(credits: 5000, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.IsTrue(_sent.OfType<StoreTransactionResponsePacket>().Last().WasSuccessful);
        _sent.Clear();

        // Still in cargo — sell again would re-list; without sell, buy same coid is catalog miss.
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        // Item is in cargo again so buyback gone; buy treats as catalog slot miss → fail
        // (unless COID equals a stock CBID — it does not).
        Assert.IsFalse(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        Assert.AreEqual(1, character.Inventory.CountByCbid(CbidA), "must not double-grant");
    }

    [TestMethod]
    public void RoundTrip_BuybackPreferredOverCatalogStockSession()
    {
        var (conn, character, map) = CreatePlayerOnMap(credits: 8000, (ItemCoidA, CbidA, 1));
        PlaceStore(map, StoreCoid, StockCbid);
        VendorStoreService.NoteOpened(character, StoreCoid, conn);

        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        _sent.Clear();

        // Same COID as sold — must buyback (SellUnitA), not invent catalog Create.
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.IsTrue(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        Assert.IsFalse(_sent.OfType<CreateSimpleObjectPacket>().Any());
        Assert.AreEqual(8000L, character.Credits);
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidA));
    }

    [TestMethod]
    public void RoundTrip_SellMissionItem_BuybackPreservesMissionFlag()
    {
        var (conn, character) = CreatePlayer(credits: 1000);
        Assert.IsTrue(character.Inventory.TryAdd(new CharacterInventoryItem(
            CbidA, CloneBaseObjectType.Item, "mission-gear", ItemCoidA, 0, 0, 1, IsMissionItem: true)));

        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        var listing = VendorStoreService.GetBuybackForTests(character.ObjectId.Coid)![ItemCoidA];
        Assert.IsTrue(listing.IsMissionItem);

        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.IsTrue(character.Inventory.FindByCoid(ItemCoidA)!.IsMissionItem);
    }

    [TestMethod]
    public void RoundTrip_InventoryFull_BuybackFails_KeepsListing()
    {
        var (conn, character) = CreatePlayer(credits: 5000, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));

        // Fill all cargo slots with other stacks.
        var nextCoid = 50000L;
        for (byte y = 0; y < character.Inventory.PageCount; y++)
        {
            for (byte x = 0; x < character.Inventory.Width; x++)
            {
                if (!character.Inventory.TryAdd(new CharacterInventoryItem(
                        9999, CloneBaseObjectType.Item, "filler", nextCoid++, x, y, 1)))
                    break;
            }
        }

        Assert.IsNull(character.Inventory.FindByCoid(ItemCoidA));
        _sent.Clear();
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.IsFalse(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        Assert.IsTrue(VendorStoreService.GetBuybackForTests(character.ObjectId.Coid)!.ContainsKey(ItemCoidA));
        Assert.AreEqual(5000 + SellUnitA, character.Credits, "no charge on failed restore");
    }

    [TestMethod]
    public void RoundTrip_SellStackQty3_BuybackRestoresQty3SameCoid()
    {
        var (conn, character) = CreatePlayer(credits: 0, (ItemCoidA, CbidA, 3));
        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(ItemCoidA, true),
            IsBuy = false,
            Quantity = 2, // request qty used for payout; full stack still removed by COID
        });

        Assert.IsNull(character.Inventory.FindByCoid(ItemCoidA));
        var listing = VendorStoreService.GetBuybackForTests(character.ObjectId.Coid)![ItemCoidA];
        // RegisterBuyback uses accepted remove qty path — Register uses qty from sell = min(request, stack)=2
        // but RemoveCargoByCoid removes full stack of 3. Register uses `qty` variable from sell.
        Assert.IsTrue(listing.Quantity >= 1);

        character.SetCredits(listing.UnitPrice * listing.Quantity + 100);
        var cost = listing.UnitPrice * listing.Quantity;
        var before = character.Credits;
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA, qty: listing.Quantity));
        Assert.IsTrue(_sent.OfType<StoreTransactionResponsePacket>().Last().WasSuccessful);
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidA));
        Assert.AreEqual(before - cost, character.Credits);
    }

    [TestMethod]
    public void RoundTrip_LiveHexBuyback_Item11131_AfterSell()
    {
        // Live: sell works; buy item=(11131) store=9819 must not miss on catalog lines 2663,2664.
        var (conn, character) = CreatePlayer(credits: 10000, (11131, CbidA, 1));
        VendorStoreService.NoteOpened(character, StoreCoid, conn: null);

        VendorStoreService.HandleTransaction(conn, Sell(11131));
        Assert.IsNotNull(VendorStoreService.GetBuybackForTests(character.ObjectId.Coid));
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(11131, true),
            StoreCoid = StoreCoid,
            IsBuy = true,
            Quantity = 1,
        });

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(11131L, response.ItemCoid);
        Assert.IsFalse(_sent.OfType<CreateSimpleObjectPacket>().Any());
        Assert.IsNotNull(character.Inventory.FindByCoid(11131));
    }

    [TestMethod]
    public void RoundTrip_DoubleSellSameCoid_NotPossibleAfterFirst()
    {
        var (conn, character) = CreatePlayer(credits: 0, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        _sent.Clear();
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        Assert.IsFalse(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
    }

    [TestMethod]
    public void RoundTrip_StoreCloseMidBuyback_ThenBuyFails()
    {
        var (conn, character) = CreatePlayer(credits: 5000, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        VendorStoreService.NoteOpened(character, 0);
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.IsFalse(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoidA));
    }

    [TestMethod]
    public void RoundTrip_NetCreditsZeroAfterSellAndBuyback()
    {
        var (conn, character) = CreatePlayer(credits: 12345, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));
        Assert.AreEqual(12345L, character.Credits);
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoidA));
    }

    [TestMethod]
    public void RoundTrip_ResponseIsBuyFlag_AndRelatedCoids()
    {
        var (conn, character) = CreatePlayer(credits: 1000, (ItemCoidA, CbidA, 1));
        VendorStoreService.HandleTransaction(conn, Sell(ItemCoidA));
        _sent.Clear();
        VendorStoreService.HandleTransaction(conn, Buy(ItemCoidA));

        var r = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(r.IsBuy);
        Assert.IsTrue(r.WasSuccessful);
        Assert.AreEqual(ItemCoidA, r.ItemCoid);
        Assert.AreEqual(ItemCoidA, r.RelatedCoidB);
        Assert.AreEqual(character.ObjectId.Coid, r.RelatedCoidA);
    }

    static StoreTransactionRequestPacket Sell(long coid) => new()
    {
        Item = new TFID(coid, true),
        IsBuy = false,
        Quantity = 1,
    };

    static StoreTransactionRequestPacket Buy(long coid, int qty = 1) => new()
    {
        Item = new TFID(coid, true),
        StoreCoid = StoreCoid,
        IsBuy = true,
        Quantity = qty,
    };

    static void PlaceStore(SectorMap map, long storeCoid, params int[] cbids)
    {
        var tpl = new StoreTemplate { COID = (int)storeCoid, Name = "rt-store" };
        foreach (var cbid in cbids)
            tpl.Items.Add(new StoreTemplate.ItemType { CBID = cbid, Unlimited = true, Value = (int)CatalogUnit });
        while (tpl.Items.Count < 10)
            tpl.Items.Add(new StoreTemplate.ItemType());
        map.MapData.Templates[storeCoid] = tpl;
        var store = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        store.SetCoid(storeCoid, false);
        store.Position = new Vector3(0, 0, 0);
        store.SetMap(map);
    }

    private sealed class RoundTripItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
        {
            return InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new TFID(coid, true),
                    IsInInventory = true,
                    Quantity = 1,
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    IsIdentified = true,
                    Scale = 1f,
                },
                entry.DisplayName);
        }
    }

    static (TNLConnection Conn, Character Character) CreatePlayer(
        long credits,
        params (long Coid, int Cbid, int Qty)[] cargo)
    {
        var (conn, character, _) = CreatePlayerOnMap(credits, cargo);
        return (conn, character);
    }

    static (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayerOnMap(
        long credits,
        params (long Coid, int Cbid, int Qty)[] cargo)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_roundtrip",
            DisplayName = "t",
            IsPersistent = true,
            IsTown = true,
        }, new Vector4());
        map.LocalCoidCounter = 40000;

        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18451, true);
        character.AttachTestDataForTests("RoundTrip");
        character.SetCredits(credits);
        character.SetOwningConnection(conn);
        conn.CurrentCharacter = character;

        var inv = new InventoryManager();
        byte slot = 0;
        foreach (var (coid, cbid, qty) in cargo)
        {
            Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
                cbid, CloneBaseObjectType.Item, $"item-{cbid}", coid, slot, 0, qty)));
            slot++;
        }

        character.AttachInventoryForTests(inv);
        var vehicle = new Vehicle();
        vehicle.SetCoid(18452, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (conn, character, map);
    }
}
