using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryUnequipPacketTests
{
    [TestMethod]
    public void Write_MatchesDocumented0x203ELayout()
    {
        var packet = new InventoryUnequipPacket
        {
            ItemId = new TFID(0x1112131415161718, true),
            VehicleId = new TFID(0x2122232425262728, false),
            InventoryPositionX = 0,
            InventoryPositionY = 0,
            InventoryType = 2,
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x30, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryUnequip, BitConverter.ToUInt32(bytes, 0));
        CollectionAssert.AreEqual(new byte[4], bytes[4..8]);
        Assert.AreEqual(0x1112131415161718, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(1, bytes[0x10]);
        CollectionAssert.AreEqual(new byte[7], bytes[0x11..0x18]);
        Assert.AreEqual(0x2122232425262728, BitConverter.ToInt64(bytes, 0x18));
        Assert.AreEqual(0, bytes[0x20]);
        CollectionAssert.AreEqual(new byte[7], bytes[0x21..0x28]);
        Assert.AreEqual(0, bytes[0x28]);
        Assert.AreEqual(0, bytes[0x29]);
        Assert.AreEqual(2, bytes[0x2a]);
        CollectionAssert.AreEqual(new byte[5], bytes[0x2b..0x30]);
    }
}
