using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryDestroyItemPacketTests
{
    [TestMethod]
    public void Read_CapturesRawBytesAndItemIdentity()
    {
        var bytes = new byte[0x14];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDestroyItem).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x99L).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x11] = 0xCC;

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryDestroyItemPacket();
        packet.Read(reader);

        CollectionAssert.AreEqual(bytes, packet.RawBytes);
        Assert.AreEqual(0x99L, packet.ItemCoid);
        Assert.IsTrue(packet.ItemGlobal);
        Assert.IsTrue(packet.TailBytes.Length >= 1);
        Assert.AreEqual(0xCC, packet.TailBytes[0]);
    }
}
