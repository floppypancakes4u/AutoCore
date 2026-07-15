using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;

[TestClass]
public class StoreTransactionPacketTests
{
    [TestMethod]
    public void StoreTransactionRequest_ReadsBuyLayoutFromFullFrame()
    {
        var raw = new byte[0x40];
        BitConverter.GetBytes((int)GameOpcode.StoreTransactionRequest).CopyTo(raw, 0);
        BitConverter.GetBytes(12463L).CopyTo(raw, 0x18);
        raw[0x20] = 0;
        raw[0x38] = 1;
        BitConverter.GetBytes(2).CopyTo(raw, 0x3c);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        br.ReadUInt32();
        var packet = new StoreTransactionRequestPacket();
        packet.Read(br);

        Assert.IsTrue(packet.IsBuy);
        Assert.AreEqual(2, packet.Quantity);
        Assert.AreEqual(12463L, packet.Item.Coid);
        Assert.IsFalse(packet.Item.Global);
    }

    [TestMethod]
    public void StoreTransactionRequest_ReadsSellFlag()
    {
        var raw = new byte[0x40];
        BitConverter.GetBytes((int)GameOpcode.StoreTransactionRequest).CopyTo(raw, 0);
        BitConverter.GetBytes(999L).CopyTo(raw, 0x18);
        raw[0x20] = 1; // global
        raw[0x38] = 0;
        BitConverter.GetBytes(1).CopyTo(raw, 0x3c);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        br.ReadUInt32();
        var packet = new StoreTransactionRequestPacket();
        packet.Read(br);

        Assert.IsFalse(packet.IsBuy);
        Assert.AreEqual(999L, packet.Item.Coid);
        Assert.IsTrue(packet.Item.Global);
        Assert.AreEqual(1, packet.Quantity);
    }

    [TestMethod]
    public void StoreTransactionRequest_LiveSellCapture_Coid11119()
    {
        // Live sell hex (coid=11119 global, isBuy=0, qty=1) — regression on parse offsets.
        var raw = new byte[0x40];
        BitConverter.GetBytes((int)GameOpcode.StoreTransactionRequest).CopyTo(raw, 0);
        BitConverter.GetBytes(11119L).CopyTo(raw, 0x18);
        raw[0x20] = 1;
        raw[0x38] = 0;
        BitConverter.GetBytes(1).CopyTo(raw, 0x3c);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        br.ReadUInt32();
        var packet = new StoreTransactionRequestPacket();
        packet.Read(br);

        Assert.IsFalse(packet.IsBuy);
        Assert.AreEqual(11119L, packet.Item.Coid);
        Assert.IsTrue(packet.Item.Global);
    }

    [TestMethod]
    public void StoreTransactionRequest_QuantityClampedToAtLeastOne()
    {
        var raw = new byte[0x40];
        BitConverter.GetBytes((int)GameOpcode.StoreTransactionRequest).CopyTo(raw, 0);
        BitConverter.GetBytes(1L).CopyTo(raw, 0x18);
        raw[0x38] = 1;
        BitConverter.GetBytes(0).CopyTo(raw, 0x3c);

        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        br.ReadUInt32();
        var packet = new StoreTransactionRequestPacket();
        packet.Read(br);

        Assert.AreEqual(1, packet.Quantity);
    }

    /// <summary>
    /// Client FUN_00810670 (StoreTransaction_Response 0x2028) absolute layout:
    /// +0x08 item coid i64, +0x20 credits i64, +0x28 success, +0x29 isBuy, +0x2c quantity.
    /// Size 0x30 including opcode.
    /// </summary>
    [TestMethod]
    public void StoreTransactionResponse_WritesClientSellLayout_0x30()
    {
        var packet = new StoreTransactionResponsePacket
        {
            ItemCoid = 11119,
            Credits = 50_000,
            WasSuccessful = true,
            IsBuy = false,
            Quantity = 1,
        };

        var bytes = Serialize(packet);

        Assert.AreEqual(0x30, bytes.Length, "Response body must be 0x30 including opcode");
        Assert.AreEqual((uint)GameOpcode.StoreTransactionResponse, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(11119L, BitConverter.ToInt64(bytes, 0x08));
        Assert.AreEqual(0L, BitConverter.ToInt64(bytes, 0x10), "related A pad");
        Assert.AreEqual(0L, BitConverter.ToInt64(bytes, 0x18), "related B pad");
        Assert.AreEqual(50_000L, BitConverter.ToInt64(bytes, 0x20));
        Assert.AreEqual(1, bytes[0x28]);
        Assert.AreEqual(0, bytes[0x29], "sell: IsBuy=0");
        Assert.AreEqual(1, BitConverter.ToInt32(bytes, 0x2c));
    }

    [TestMethod]
    public void StoreTransactionResponse_WritesBuyFlag()
    {
        var packet = new StoreTransactionResponsePacket
        {
            ItemCoid = 42,
            RelatedCoidA = 100,
            RelatedCoidB = 200,
            Credits = 100,
            WasSuccessful = true,
            IsBuy = true,
            Quantity = 3,
        };

        var bytes = Serialize(packet);

        Assert.AreEqual(1, bytes[0x29], "buy: IsBuy=1");
        Assert.AreEqual(3, BitConverter.ToInt32(bytes, 0x2c));
        Assert.AreEqual(100L, BitConverter.ToInt64(bytes, 0x10));
        Assert.AreEqual(200L, BitConverter.ToInt64(bytes, 0x18));
    }

    [TestMethod]
    public void StoreTransactionResponse_FailedSell_WritesSuccessFalseAt0x28()
    {
        var packet = new StoreTransactionResponsePacket
        {
            ItemCoid = 11119,
            Credits = 0,
            WasSuccessful = false,
            IsBuy = false,
            Quantity = 1,
        };

        var bytes = Serialize(packet);
        Assert.AreEqual(0, bytes[0x28], "client shows Unable to sell item when +0x28 is 0");
        Assert.AreEqual(0, bytes[0x29]);
    }

    [TestMethod]
    public void StoreTransactionResponse_FailedBuy_WritesIsBuyAndSuccess()
    {
        var packet = new StoreTransactionResponsePacket
        {
            ItemCoid = 1,
            WasSuccessful = false,
            IsBuy = true,
            Quantity = 1,
        };

        var bytes = Serialize(packet);
        Assert.AreEqual(0, bytes[0x28]);
        Assert.AreEqual(1, bytes[0x29], "client shows item unavailable when buy fails");
    }

    [TestMethod]
    public void StoreTransactionResponse_DoesNotPlaceSuccessAtBodyStart()
    {
        // Regression: old provisional layout wrote success at +0x04; client reads +0x28.
        var packet = new StoreTransactionResponsePacket
        {
            ItemCoid = 5,
            Credits = 9,
            WasSuccessful = true,
            IsBuy = false,
            Quantity = 1,
        };

        var bytes = Serialize(packet);
        // +0x04 is pad (must not be the only success flag the client uses).
        Assert.AreEqual(0, bytes[0x04]);
        Assert.AreEqual(1, bytes[0x28]);
    }

    static byte[] Serialize(StoreTransactionResponsePacket packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);
        return stream.ToArray();
    }
}
