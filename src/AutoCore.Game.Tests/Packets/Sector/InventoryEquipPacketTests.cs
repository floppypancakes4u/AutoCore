using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryEquipPacketTests
{
    [TestMethod]
    public void Write_MatchesDocumented0x203CLayout()
    {
        var packet = new InventoryEquipPacket
        {
            ItemId = new TFID(0x1112131415161718, true),
            VehicleId = new TFID(0x2122232425262728, false),
            OldItemId = new TFID(-1, false),
            PutInHand = false,
            InventoryPositionX = 1,
            InventoryPositionY = 0,
            InventoryTypeFrom = 1,
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x40, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryEquip, BitConverter.ToUInt32(bytes, 0));
        CollectionAssert.AreEqual(new byte[4], bytes[4..8]);
        Assert.AreEqual(0x1112131415161718, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(1, bytes[0x10]);
        Assert.AreEqual(0x2122232425262728, BitConverter.ToInt64(bytes, 0x18));
        Assert.AreEqual(0, bytes[0x20]);
        Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, 0x28));
        Assert.AreEqual(0, bytes[0x30]);
        Assert.AreEqual(0, bytes[0x38]);
        Assert.AreEqual(1, bytes[0x39]);
        Assert.AreEqual(0, bytes[0x3a]);
        Assert.AreEqual(1, bytes[0x3b]);
    }
}
