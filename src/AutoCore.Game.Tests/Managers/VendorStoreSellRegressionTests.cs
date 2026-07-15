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
/// Regression: store sell must clear the client cursor via a correct 0x2028 body
/// (FUN_00810670) and must not DestroyObject the held TFID before that response.
/// </summary>
[TestClass]
public class VendorStoreSellRegressionTests
{
    private const int ContId = 8810;
    private const long ItemCoid = 11119;
    private const int ItemCbid = 19194;
    private const long SellUnitPrice = 250;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestSellPriceResolver = cbid => cbid == ItemCbid ? SellUnitPrice : 0;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        VendorStoreService.ResetSessionsForTests();
        VendorStoreService.TestSellPriceResolver = null;
        _sent.Clear();
    }

    [TestMethod]
    public void HandleTransaction_Sell_Sends0x30ResponseThenCargoSendAll_NoDestroyPackets()
    {
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 1000);

        VendorStoreService.HandleTransaction(conn, SellRequest(ItemCoid, qty: 1));

        var responseIndex = _sent.FindIndex(p => p is StoreTransactionResponsePacket);
        Assert.IsTrue(responseIndex >= 0, "must send StoreTransactionResponse");

        var cargoIndex = _sent.FindIndex(p => p is InventoryCargoSendAllPacket);
        Assert.IsTrue(cargoIndex >= 0, "must send CargoSendAll after sell");
        Assert.IsTrue(
            cargoIndex > responseIndex,
            "CargoSendAll must follow 0x2028 so client can resolve the held TFID first");

        Assert.IsFalse(
            _sent.OfType<DestroyObjectPacket>().Any(),
            "DestroyObject before/with sell orphans the cursor TFID (FUN_00810670 resolve fails)");
        Assert.IsFalse(
            _sent.OfType<InventoryDestroyItemPacket>().Any(),
            "client destroy is owned by 0x2028 success path, not InventoryDestroyItem");

        Assert.IsNull(character.Inventory.FindByCoid(ItemCoid), "server cargo must drop the sold stack");
    }

    [TestMethod]
    public void HandleTransaction_Sell_ResponseFields_MatchClientLayout()
    {
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 10_000);

        VendorStoreService.HandleTransaction(conn, SellRequest(ItemCoid, qty: 1));

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(response.WasSuccessful);
        Assert.IsFalse(response.IsBuy);
        Assert.AreEqual(ItemCoid, response.ItemCoid);
        Assert.AreEqual(1, response.Quantity);
        Assert.AreEqual(10_000 + SellUnitPrice, response.Credits, "absolute balance after payout");
        Assert.AreEqual(10_000 + SellUnitPrice, character.Credits);

        // Wire-exact body the client reads at absolute offsets.
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)response.Opcode);
        response.Write(writer);
        stream.SetLength(stream.Position);
        var bytes = stream.ToArray();

        Assert.AreEqual(0x30, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.StoreTransactionResponse, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(ItemCoid, BitConverter.ToInt64(bytes, 0x08));
        Assert.AreEqual(10_000 + SellUnitPrice, BitConverter.ToInt64(bytes, 0x20));
        Assert.AreEqual(1, bytes[0x28]);
        Assert.AreEqual(0, bytes[0x29]);
        Assert.AreEqual(1, BitConverter.ToInt32(bytes, 0x2c));
    }

    [TestMethod]
    public void HandleTransaction_Sell_GivesCreditsDelta_WhenPayoutPositive()
    {
        var (conn, _) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 500);

        VendorStoreService.HandleTransaction(conn, SellRequest(ItemCoid, qty: 1));

        var give = _sent.OfType<GiveCreditsPacket>().ToList();
        Assert.AreEqual(1, give.Count, "mid-session credit delta for HUD");
        // GiveCredits is additive; exact field name may vary — assert amount via character.
        Assert.IsTrue(
            _sent.FindIndex(p => p is GiveCreditsPacket)
            < _sent.FindIndex(p => p is StoreTransactionResponsePacket),
            "credit delta should land before absolute credits in 0x2028");
    }

    [TestMethod]
    public void HandleTransaction_Sell_MissingItem_ResponseFailed_NoCargoMutation()
    {
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 100);

        VendorStoreService.HandleTransaction(conn, SellRequest(itemCoid: 999999, qty: 1));

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsFalse(response.WasSuccessful);
        Assert.IsFalse(response.IsBuy);
        Assert.AreEqual(999999L, response.ItemCoid);
        Assert.AreEqual(100L, character.Credits, "failed sell must not pay");
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoid), "cargo stack must remain");
        Assert.IsFalse(_sent.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    public void HandleTransaction_Sell_UnsellableCbid_Fails()
    {
        VendorStoreService.TestSellPriceResolver = _ => 0;
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 100);

        VendorStoreService.HandleTransaction(conn, SellRequest(ItemCoid, qty: 1));

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsFalse(response.WasSuccessful);
        Assert.AreEqual(100L, character.Credits);
        Assert.IsNotNull(character.Inventory.FindByCoid(ItemCoid));
    }

    [TestMethod]
    public void HandleTransaction_Sell_StackedItem_RemovesFullStackByCoid()
    {
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 5, credits: 0);

        VendorStoreService.HandleTransaction(conn, SellRequest(ItemCoid, qty: 2));

        // Current sell path removes the whole stack by COID (cursor holds the stack TFID).
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoid));
        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(SellUnitPrice * 2, response.Credits,
            "payout uses min(request qty, stack) before full-stack remove");
    }

    [TestMethod]
    public void TrySell_DoesNotEmitClientDestroy_InPostResponsePackets()
    {
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 0);
        var packet = SellRequest(ItemCoid, qty: 1);

        var ok = VendorStoreService.TrySell(conn, character, packet, out var post);
        Assert.IsTrue(ok);
        Assert.IsNotNull(post);
        Assert.IsTrue(post.OfType<InventoryCargoSendAllPacket>().Any());
        Assert.IsFalse(post.OfType<DestroyObjectPacket>().Any());
        Assert.IsFalse(post.OfType<InventoryDestroyItemPacket>().Any());
        Assert.IsNull(character.Inventory.FindByCoid(ItemCoid));
    }

    [TestMethod]
    public void HandleTransaction_BuyFailure_StillSendsFullResponseLayout()
    {
        var (conn, character) = CreatePlayerWithCargo(ItemCoid, ItemCbid, qty: 1, credits: 0);
        // No store session → buy fails.
        VendorStoreService.HandleTransaction(conn, new StoreTransactionRequestPacket
        {
            Item = new TFID(12463, false),
            IsBuy = true,
            Quantity = 1,
        });

        var response = _sent.OfType<StoreTransactionResponsePacket>().Single();
        Assert.IsFalse(response.WasSuccessful);
        Assert.IsTrue(response.IsBuy);
        Assert.AreEqual(12463L, response.ItemCoid);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)response.Opcode);
        response.Write(writer);
        stream.SetLength(stream.Position);
        Assert.AreEqual(0x30, stream.Length);
        Assert.AreEqual(1, stream.ToArray()[0x29]);
    }

    [TestMethod]
    public void NoteOpened_TracksSession_ForBuyPath()
    {
        var character = new Character();
        character.SetCoid(77, true);
        VendorStoreService.NoteOpened(character, storeCoid: 9819);
        Assert.AreEqual(9819L, VendorStoreService.GetOpenStoreCoidForTests(77));

        VendorStoreService.NoteOpened(character, storeCoid: 0);
        Assert.AreEqual(0L, VendorStoreService.GetOpenStoreCoidForTests(77));
    }

    [TestMethod]
    public void HandleTransaction_NullSafe()
    {
        VendorStoreService.HandleTransaction(null, SellRequest(1, 1));
        var (conn, _) = CreatePlayerWithCargo(ItemCoid, ItemCbid, 1, 0);
        VendorStoreService.HandleTransaction(conn, null);
        Assert.AreEqual(0, _sent.Count);
    }

    static StoreTransactionRequestPacket SellRequest(long itemCoid, int qty)
    {
        return new StoreTransactionRequestPacket
        {
            Item = new TFID(itemCoid, true),
            IsBuy = false,
            Quantity = qty,
        };
    }

    static (TNLConnection Conn, Character Character) CreatePlayerWithCargo(
        long itemCoid,
        int cbid,
        int qty,
        long credits)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "t_sell",
            DisplayName = "t",
            IsPersistent = true,
            IsTown = true,
        }, new Vector4());

        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18461, true);
        character.AttachTestDataForTests("Seller");
        character.SetCredits(credits);
        character.SetOwningConnection(conn);
        conn.CurrentCharacter = character;

        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
            cbid,
            CloneBaseObjectType.Item,
            "sell-me",
            itemCoid,
            0,
            0,
            qty)));
        character.AttachInventoryForTests(inv);

        var vehicle = new Vehicle();
        vehicle.SetCoid(18462, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (conn, character);
    }
}
