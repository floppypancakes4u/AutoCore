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

    /// <summary>
    /// S2C layout from SMSG_Sector_InventoryDestroyItem (size 0x18):
    /// pad4 + coidItem@+0x08 + lItemQuantity@+0x10 + bDelete@+0x14.
    /// Client needs bDelete=true to drop the mission-inventory object without relog.
    /// </summary>
    [TestMethod]
    public void Write_MatchesDocumentedS2CLayout_WithDeleteTrue()
    {
        var packet = new InventoryDestroyItemPacket(
            itemCoid: 0x0102030405060708,
            quantity: 3,
            delete: true);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x18, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryDestroyItem, BitConverter.ToUInt32(bytes, 0));
        CollectionAssert.AreEqual(new byte[4], bytes[4..8]);
        Assert.AreEqual(0x0102030405060708, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(3, BitConverter.ToInt32(bytes, 0x10));
        Assert.AreEqual(1, bytes[0x14], "bDelete must be set so client destroys the item object");
        CollectionAssert.AreEqual(new byte[3], bytes[0x15..0x18]);
    }
}
