using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryDropResponsePacketTests
{
    [TestMethod]
    public void Write_MatchesGhidraSmsgInventoryDropResponseLayout()
    {
        // SMSG_Sector_InventoryDrop_Response (size 0x40):
        // +0x08 fidItem, +0x18 x/y/typeTo, +0x1c lQuantity, +0x20 uiUsesLeft,
        // +0x22 bWasSuccessful, +0x23 bSwapped, +0x28 fidSwapItem, +0x38 bConcatinate.
        var packet = new InventoryDropResponsePacket
        {
            ItemCoid = 0x0102030405060708,
            ItemGlobal = true,
            InventoryPositionX = 7,
            InventoryPositionY = 8,
            InventoryType = 1,
            Quantity = 4,
            UsesLeft = 0,
            WasSuccessful = true,
            HasSwappedOrConcatenatedItem = false,
            SwapItemCoid = -1,
            Concatenate = false
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x40, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryDropResponse, BitConverter.ToUInt32(bytes, 0));
        CollectionAssert.AreEqual(new byte[4], bytes[4..8]);
        Assert.AreEqual(0x0102030405060708, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(1, bytes[0x10]);
        CollectionAssert.AreEqual(new byte[7], bytes[0x11..0x18]);
        Assert.AreEqual(7, bytes[0x18]);
        Assert.AreEqual(8, bytes[0x19]);
        Assert.AreEqual(1, bytes[0x1a]);
        Assert.AreEqual(0, bytes[0x1b]);
        Assert.AreEqual(4, BitConverter.ToInt32(bytes, 0x1c));
        Assert.AreEqual(0, BitConverter.ToUInt16(bytes, 0x20));
        Assert.AreEqual(1, bytes[0x22]);
        Assert.AreEqual(0, bytes[0x23]);
        CollectionAssert.AreEqual(new byte[4], bytes[0x24..0x28]);
        Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, 0x28));
        Assert.AreEqual(0, bytes[0x38]);
        CollectionAssert.AreEqual(new byte[7], bytes[0x39..0x40]);
    }
}
