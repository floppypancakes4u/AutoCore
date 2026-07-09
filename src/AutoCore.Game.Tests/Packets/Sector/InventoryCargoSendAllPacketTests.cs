using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryCargoSendAllPacketTests
{
    [TestMethod]
    public void Write_MatchesDocumentedPacketLayoutAndUsesMinusOneForEmptySlots()
    {
        var packet = new InventoryCargoSendAllPacket();
        packet.Items[1] = new InventoryPacketItem
        {
            ItemCoid = 0x0102030405060708,
            PositionX = 1,
            PositionY = 0
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x1388, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryCargoSendAll, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(InventoryCargoSendAllPacket.DefaultCargoPageCount, bytes[4]);
        CollectionAssert.AreEqual(new byte[3], bytes[5..8]);

        Assert.AreEqual(-1, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(0, bytes[16]);
        Assert.AreEqual(0, bytes[17]);
        CollectionAssert.AreEqual(new byte[6], bytes[18..24]);

        Assert.AreEqual(0x0102030405060708, BitConverter.ToInt64(bytes, 24));
        Assert.AreEqual(1, bytes[32]);
        Assert.AreEqual(0, bytes[33]);
        CollectionAssert.AreEqual(new byte[6], bytes[34..40]);
    }
}
