using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryDropPacketTests
{
    [TestMethod]
    public void Read_CapturesClientMoveFields()
    {
        var bytes = new byte[0x20];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDrop).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x0102030405060708L).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x18] = 7;
        bytes[0x19] = 8;
        bytes[0x1a] = 1;

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryDropPacket();
        packet.Read(reader);

        CollectionAssert.AreEqual(bytes, packet.RawBytes);
        Assert.AreEqual(0x0102030405060708L, packet.ItemCoid);
        Assert.IsTrue(packet.ItemGlobal);
        Assert.AreEqual(7, packet.InventoryPositionX);
        Assert.AreEqual(8, packet.InventoryPositionY);
        Assert.AreEqual(1, packet.InventoryType);
    }
}
