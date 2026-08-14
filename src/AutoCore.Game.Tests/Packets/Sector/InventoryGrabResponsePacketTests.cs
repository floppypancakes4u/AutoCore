using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryGrabResponsePacketTests
{
    [TestMethod]
    public void Write_MatchesGhidraSmsgInventoryGrabResponseLayout()
    {
        // SMSG_Sector_InventoryGrab_Response (size 0x40):
        // +0x08 fidItem, +0x18 ucTypeFrom, +0x1c lQuantity, +0x20 bNewItem,
        // +0x28 fidNewItem (TFID), +0x38 bWasSuccessful.
        var packet = new InventoryGrabResponsePacket
        {
            ItemCoid = 0x0102030405060708,
            ItemGlobal = true,
            InventoryType = 1,
            Quantity = 3,
            AddToExistingItem = false,
            InventoryPositionX = 7,
            InventoryPositionY = 8,
            WasSuccessful = true
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();

        Assert.AreEqual(0x40, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.InventoryGrabResponse, BitConverter.ToUInt32(bytes, 0));
        CollectionAssert.AreEqual(new byte[4], bytes[4..8]);
        Assert.AreEqual(0x0102030405060708, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(1, bytes[0x10]);
        CollectionAssert.AreEqual(new byte[7], bytes[0x11..0x18]);
        Assert.AreEqual(1, bytes[0x18]);
        CollectionAssert.AreEqual(new byte[3], bytes[0x19..0x1c]);
        Assert.AreEqual(3, BitConverter.ToInt32(bytes, 0x1c));
        Assert.AreEqual(0, bytes[0x20], "bNewItem must be clear for ordinary grabs");
        CollectionAssert.AreEqual(new byte[7], bytes[0x21..0x28]);
        // Non-split grabs leave fidNewItem unused; legacy slot echo still occupies the 16-byte region.
        Assert.AreEqual(7, BitConverter.ToInt32(bytes, 0x28));
        Assert.AreEqual(8, BitConverter.ToInt32(bytes, 0x2c));
        CollectionAssert.AreEqual(new byte[8], bytes[0x30..0x38]);
        Assert.AreEqual(1, bytes[0x38]);
        CollectionAssert.AreEqual(new byte[7], bytes[0x39..0x40]);
    }

    [TestMethod]
    public void Write_NewStackSplit_WritesFidNewItemAt0x28()
    {
        var packet = new InventoryGrabResponsePacket
        {
            ItemCoid = 1001,
            ItemGlobal = true,
            InventoryType = 1,
            Quantity = 2,
            AddToExistingItem = true,
            NewItemCoid = 0x0AABBCCDDEEFF001,
            NewItemGlobal = true,
            WasSuccessful = true
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);
        var bytes = stream.ToArray();

        Assert.AreEqual(0x40, bytes.Length);
        Assert.AreEqual(1, bytes[0x20], "bNewItem");
        Assert.AreEqual(0x0AABBCCDDEEFF001, BitConverter.ToInt64(bytes, 0x28));
        Assert.AreEqual(1, bytes[0x30], "fidNewItem.global");
        Assert.AreEqual(1, bytes[0x38]);
    }
}
