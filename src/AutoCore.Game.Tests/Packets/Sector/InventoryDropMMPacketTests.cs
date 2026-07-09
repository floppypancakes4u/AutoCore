using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryDropMMPacketTests
{
    [TestMethod]
    public void Read_CapturesMoveFields()
    {
        var bytes = new byte[0x20];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDropMM).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x0102030405060708L).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x18] = 7;
        bytes[0x19] = 8;
        bytes[0x1a] = 1;

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryDropMMPacket();
        packet.Read(reader);

        CollectionAssert.AreEqual(bytes, packet.RawBytes);
        Assert.AreEqual(0x0102030405060708L, packet.ItemCoid);
        Assert.IsTrue(packet.ItemGlobal);
        Assert.AreEqual((byte)7, packet.InventoryPositionX);
        Assert.AreEqual((byte)8, packet.InventoryPositionY);
        Assert.AreEqual((byte)1, packet.InventoryType);
    }

    [TestMethod]
    public void Read_ExposesTailBytesAndCandidates()
    {
        var bytes = new byte[0x24];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDropMM).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x42L).CopyTo(bytes, 8);
        bytes[0x10] = 0;
        bytes[0x18] = 3;
        bytes[0x19] = 4;
        bytes[0x1a] = 0;
        bytes[0x1b] = 0xAA;
        bytes[0x1c] = 0xBB;
        BitConverter.GetBytes(99).CopyTo(bytes, 0x20);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryDropMMPacket();
        packet.Read(reader);

        Assert.AreEqual(9, packet.TailBytes.Length);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, packet.TailBytes.Slice(0, 2).ToArray());
        CollectionAssert.Contains(packet.EnumerateInt32Candidates().ToArray(), 99);
        CollectionAssert.Contains(packet.EnumerateInt64Candidates().ToArray(), 0x42L);
    }

    [TestMethod]
    public void Read_ShortPacket_UsesDefaults()
    {
        var bytes = new byte[12];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDropMM).CopyTo(bytes, 0);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryDropMMPacket();
        packet.Read(reader);

        Assert.AreEqual(-1, packet.ItemCoid);
        Assert.IsFalse(packet.ItemGlobal);
        Assert.AreEqual(byte.MaxValue, packet.InventoryPositionX);
        Assert.AreEqual(byte.MaxValue, packet.InventoryPositionY);
        Assert.AreEqual(0, packet.InventoryType);
        Assert.AreEqual(0, packet.TailBytes.Length);
    }
}
