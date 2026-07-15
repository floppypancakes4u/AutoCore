using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Live: sell item COID then buy the same TFID back from the store UI.
/// Client keeps sold object COID (e.g. 11131); server must list buyback by that COID.
/// </summary>
[TestClass]
public class VendorStoreBuybackTests
{
    private const int ContId = 8812;
    private const long ItemCoid = 11131;
    private const int ItemCbid = 19194;
    private const long SellUnitPrice = 250;
    private const long StoreCoid = 9819;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestSellPriceResolver = cbid => cbid == ItemCbid ? SellUnitPrice : 0;
        VendorStoreService.TestBuyCatalogResolver = cbid =>
            cbid == ItemCbid
                ? new InventoryCatalogEntry(cbid, CloneBaseObjectType.Item, "sold-gear")
                : null;
        VendorStoreService.TestItemCreator = new BuybackItemCreator();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestSellPriceResolver = null;
        VendorStoreService.TestBuyCatalogResolver = null;
        VendorStoreService.TestItemCreator = null;
        _sent.Clear();
    }

    [TestMethod]
    public void Sell_RegistersBuybackByItemCoid()
    {
        var (conn, character) = CreatePlayerWithCargo(credits: 1000);

        VendorStoreService.HandleTransaction(conn, SellRequest());

        var buybacks = VendorStoreService.GetBuybackForTests(character.ObjectId.Coid);
        Assert.IsNotNull(buybacks);
        Assert.IsTrue(buybacks.ContainsKey(ItemCoid));
        Assert.AreEqual(ItemCbid, buybacks[ItemCoid].Cbid);
        Assert.AreEqual(1, buybacks[ItemCoid].Quantity);
        Assert.AreEqual(SellUnitPrice, buybacks[ItemCoid].UnitPrice);
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoid));
    }

    [TestMethod]
    public void SellThenBuy_SameCoid_RestoresItemAndChargesSellPrice()
    {
        var (conn, character) = CreatePlayerWithCargo(credits: 1000);

        VendorStoreService.HandleTransaction(conn, SellRequest());
        Assert.AreEqual(1000 + SellUnitPrice, character.Credits);
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, BuyRequest(ItemCoid));

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(response.WasSuccessful, "buyback must succeed for sold TFID");
        Assert.IsTrue(response.IsBuy);
        Assert.AreEqual(1000L, character.Credits, "buyback charges the sell payout");
        Assert.IsTrue(character.Inventory.CountByCbid(ItemCbid) >= 1);

        var restored = character.Inventory.FindByCoid(ItemCoid);
        Assert.IsNotNull(restored, "buyback must restore the original sold COID, not allocate a new one");
        Assert.AreEqual(ItemCbid, restored.Cbid);
        Assert.AreEqual(ItemCoid, response.ItemCoid, "0x2028 grant COID is the live client TFID");

        Assert.IsFalse(
            _sent.OfType<CreateSimpleObjectPacket>().Any(p => p.ObjectId.Coid != ItemCoid),
            "buyback must not Create a spare object for a different COID");
        // No create at all for buyback — client still holds the store-slot TFID.
        Assert.IsFalse(
            _sent.OfType<CreateSimpleObjectPacket>().Any(),
            "buyback must not emit CreateSimpleObject (would spawn an invalid spare copy)");
        Assert.IsTrue(_sent.OfType<InventoryAddItemResponsePacket>().Any(p => p.ItemCoid == ItemCoid));

        var buybacks = VendorStoreService.GetBuybackForTests(character.ObjectId.Coid);
        Assert.IsTrue(buybacks == null || !buybacks.ContainsKey(ItemCoid), "listing consumed");
    }

    [TestMethod]
    public void Buyback_LiveStyle_Item11131_WithoutCatalogStockMatch()
    {
        // Mirrors log: item=(11131) store=9819 sessionSlots=2 lines=2663,2664 — no CBID match.
        var (conn, character) = CreatePlayerWithCargo(credits: 5000);
        VendorStoreService.NoteOpened(character, StoreCoid, conn: null);

        VendorStoreService.HandleTransaction(conn, SellRequest());
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(ItemCoid, true),
            StoreCoid = StoreCoid,
            IsBuy = true,
            Quantity = 1,
        });

        Assert.IsTrue(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        Assert.AreEqual(5000L, character.Credits);
        Assert.IsTrue(character.Inventory.CountByCbid(ItemCbid) >= 1);
    }

    [TestMethod]
    public void Buyback_InsufficientCredits_FailsAndKeepsListing()
    {
        var (conn, character) = CreatePlayerWithCargo(credits: 0);

        VendorStoreService.HandleTransaction(conn, SellRequest());
        // Spent all payout somehow — set credits below buyback cost.
        character.SetCredits(0);
        _sent.Clear();

        VendorStoreService.HandleTransaction(conn, BuyRequest(ItemCoid));

        Assert.IsFalse(_sent.OfType<StoreTransactionResponsePacket>().Single().WasSuccessful);
        var buybacks = VendorStoreService.GetBuybackForTests(character.ObjectId.Coid);
        Assert.IsNotNull(buybacks);
        Assert.IsTrue(buybacks.ContainsKey(ItemCoid));
    }

    [TestMethod]
    public void StoreClose_ClearsBuyback()
    {
        var (conn, character) = CreatePlayerWithCargo(credits: 100);
        VendorStoreService.HandleTransaction(conn, SellRequest());
        Assert.IsNotNull(VendorStoreService.GetBuybackForTests(character.ObjectId.Coid));

        VendorStoreService.NoteOpened(character, 0);
        Assert.IsNull(VendorStoreService.GetBuybackForTests(character.ObjectId.Coid));
    }

    static StoreTransactionRequestPacket SellRequest() => new()
    {
        Item = new TFID(ItemCoid, true),
        IsBuy = false,
        Quantity = 1,
    };

    static StoreTransactionRequestPacket BuyRequest(long itemCoid) => new()
    {
        Item = new TFID(itemCoid, true),
        StoreCoid = StoreCoid,
        IsBuy = true,
        Quantity = 1,
    };

    private sealed class BuybackItemCreator : IInventoryItemCreator
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

    static (TNLConnection Conn, Character Character) CreatePlayerWithCargo(long credits)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_buyback",
            DisplayName = "t",
            IsPersistent = true,
            IsTown = true,
        }, new Vector4());
        map.LocalCoidCounter = 30000;

        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18451, true);
        character.AttachTestDataForTests("Buyback");
        character.SetCredits(credits);
        character.SetOwningConnection(conn);
        conn.CurrentCharacter = character;

        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
            ItemCbid,
            CloneBaseObjectType.Item,
            "sell-me",
            ItemCoid,
            0,
            0,
            1)));
        character.AttachInventoryForTests(inv);

        var vehicle = new Vehicle();
        vehicle.SetCoid(18452, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (conn, character);
    }
}
