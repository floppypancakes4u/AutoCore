using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryAddItemResponsePacketTests
{
    [TestMethod]
    public void Write_MatchesDocumentedPacketLayout()
    {
        var packet = new InventoryAddItemResponsePacket
        {
            ItemCoid = 0x0102030405060708,
            InventoryPositionX = 7,
            InventoryPositionY = 8,
            AddToExistingItem = false,
            Quantity = 1,
            WasSuccessful = true
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x20, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryAddItemResponse, BitConverter.ToUInt32(bytes, 0));
        CollectionAssert.AreEqual(new byte[4], bytes[4..8]);
        Assert.AreEqual(0x0102030405060708, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(7, bytes[16]);
        Assert.AreEqual(8, bytes[17]);
        Assert.AreEqual(0, bytes[18]);
        Assert.AreEqual(0, bytes[19]);
        Assert.AreEqual(1, BitConverter.ToInt32(bytes, 20));
        Assert.AreEqual(1, bytes[24]);
        CollectionAssert.AreEqual(new byte[7], bytes[25..32]);
    }
}
