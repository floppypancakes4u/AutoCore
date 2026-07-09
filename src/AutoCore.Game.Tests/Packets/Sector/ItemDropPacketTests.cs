using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class ItemDropPacketTests
{
    [TestMethod]
    public void Read_LiveWorldTossCapture_ParsesKnownFields()
    {
        var bytes = Convert.FromHexString(
            "57200000BCF21901FE4600000000000042CF6B439A880B418A3A05430000000000000000000000006F12833A01000000");

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new ItemDropPacket();
        packet.Read(reader);

        CollectionAssert.AreEqual(bytes, packet.RawBytes);
        Assert.AreEqual(18477756, packet.SourceObjectId);
        Assert.AreEqual(18174, packet.ItemCoid);
        Assert.AreEqual(235.8096f, packet.DropPosition.X, 0.001f);
        Assert.AreEqual(8.7208f, packet.DropPosition.Y, 0.001f);
        Assert.AreEqual(133.2287f, packet.DropPosition.Z, 0.001f);
        Assert.AreEqual(5276635759L, packet.TailValue);
    }

    [TestMethod]
    public void Read_CapturesDropPositionFloats()
    {
        var bytes = new byte[ItemDropPacket.MinimumLength];
        BitConverter.GetBytes((uint)GameOpcode.ItemDrop).CopyTo(bytes, 0);
        BitConverter.GetBytes(1).CopyTo(bytes, 4);
        BitConverter.GetBytes(99L).CopyTo(bytes, 8);
        BitConverter.GetBytes(10f).CopyTo(bytes, 0x10);
        BitConverter.GetBytes(20f).CopyTo(bytes, 0x14);
        BitConverter.GetBytes(30f).CopyTo(bytes, 0x18);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new ItemDropPacket();
        packet.Read(reader);

        Assert.AreEqual(10f, packet.DropPosition.X);
        Assert.AreEqual(20f, packet.DropPosition.Y);
        Assert.AreEqual(30f, packet.DropPosition.Z);
    }
}
