using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryGrabPacketTests
{
    [TestMethod]
    public void Read_CapturesRawBytesAndProvisionalFields()
    {
        var bytes = new byte[0x3c];
        BitConverter.GetBytes((uint)GameOpcode.InventoryGrab).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x0102030405060708L).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x18] = 1;
        BitConverter.GetBytes(3).CopyTo(bytes, 0x1c);
        BitConverter.GetBytes(7).CopyTo(bytes, 0x28);
        BitConverter.GetBytes(8).CopyTo(bytes, 0x2c);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryGrabPacket();
        packet.Read(reader);

        CollectionAssert.AreEqual(bytes, packet.RawBytes);
        Assert.AreEqual(0x0102030405060708L, packet.ItemCoid);
        Assert.IsTrue(packet.ItemGlobal);
        Assert.AreEqual(1, packet.InventoryType);
        Assert.AreEqual(3, packet.Quantity);
        Assert.IsTrue(packet.HasRequestedInventoryPosition);
        Assert.AreEqual(7, packet.RequestedInventoryPositionX);
        Assert.AreEqual(8, packet.RequestedInventoryPositionY);
    }

    [TestMethod]
    public void Read_EquipmentGrabUsesEquippedItemCoidAndQuantityOne()
    {
        var bytes = Convert.FromHexString("34200000A4F61901380000000000000001F519014307776D02824276A4F61901");

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryGrabPacket();
        packet.Read(reader);

        Assert.AreEqual((byte)2, packet.InventoryType);
        Assert.AreEqual(56, packet.ItemCoid);
        Assert.AreEqual(0x0119F6A4, packet.EquipmentCbid);
        Assert.AreEqual(1, packet.Quantity);
    }

    [TestMethod]
    public void Read_EquipmentGrabWithDifferentEquippedCoidStillUsesTfidAtOffsetEight()
    {
        var bytes = Convert.FromHexString("34200000A4F61901CD0000000000000001F519014307776D02824276A4F61901");

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryGrabPacket();
        packet.Read(reader);

        Assert.AreEqual((byte)2, packet.InventoryType);
        Assert.AreEqual(205, packet.ItemCoid);
        Assert.AreEqual(0x0119F6A4, packet.EquipmentCbid);
        Assert.AreEqual(1, packet.Quantity);
    }

    [TestMethod]
    public void Read_EquipmentGrabWithSourceTokenUsesEquippedItemTfid()
    {
        var bytes = Convert.FromHexString("3420000000000100CD0000000000000001000000040000000200000000000000");

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryGrabPacket();
        packet.Read(reader);

        Assert.AreEqual((byte)2, packet.InventoryType);
        Assert.AreEqual(205, packet.ItemCoid);
        Assert.AreEqual(65536, packet.EquipmentCbid);
        Assert.AreEqual(4, packet.EquipmentSlotHint);
        Assert.AreEqual(1, packet.Quantity);
    }

}
