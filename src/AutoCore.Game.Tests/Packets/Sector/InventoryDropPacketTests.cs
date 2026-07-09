using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InventoryDropPacketTests
{
    [TestMethod]
    public void Read_HardpointType2_LiveLogFixture()
    {
        // Reconstructed from live capture 3620000000000000CC0000000000000001405100D8F3E12CFFFF023CC2388600
        // (original log hex had odd length; normalized here to valid 32-byte packet layout).
        var bytes = new byte[32];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDrop).CopyTo(bytes, 0);
        BitConverter.GetBytes(0xCCL).CopyTo(bytes, 8);
        bytes[0x10] = 0;
        bytes[0x18] = 0x14;
        bytes[0x19] = 0x05;
        bytes[0x1a] = 0x02;

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new InventoryDropPacket();
        packet.Read(reader);

        CollectionAssert.AreEqual(bytes, packet.RawBytes);
        Assert.AreEqual(0xCCL, packet.ItemCoid);
        Assert.AreEqual((byte)0x14, packet.InventoryPositionX);
        Assert.AreEqual((byte)0x05, packet.InventoryPositionY);
        Assert.AreEqual((byte)2, packet.InventoryType);
    }

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

    [TestMethod]
    public void Read_ExposesTailBytesAndCandidatesForUnknownInventoryType()
    {
        var bytes = new byte[0x24];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDrop).CopyTo(bytes, 0);
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

        var packet = new InventoryDropPacket();
        packet.Read(reader);

        Assert.AreEqual(9, packet.TailBytes.Length);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, packet.TailBytes.Slice(0, 2).ToArray());
        CollectionAssert.Contains(packet.EnumerateInt32Candidates().ToArray(), 99);
        CollectionAssert.Contains(packet.EnumerateInt64Candidates().ToArray(), 0x42L);
    }
}
