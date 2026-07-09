using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class ItemDropResponsePacketTests
{
    [TestMethod]
    public void Write_SetsSuccessByteAtOffset0x30()
    {
        var packet = new ItemDropResponsePacket
        {
            SourceObjectId = 19464384,
            ItemCoid = 42000,
            DropPosition = new AutoCore.Game.Structures.Vector3(1f, 2f, 3f),
            TailValue = 5276635759L,
            WasSuccessful = true
        };

        var bytes = WritePacket(packet);

        Assert.IsTrue(bytes.Length >= ItemDropResponsePacket.MinimumLength);
        Assert.AreEqual((uint)GameOpcode.ItemDropResponse, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(19464384, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(42000L, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(1f, BitConverter.ToSingle(bytes, 0x10));
        Assert.AreEqual(2f, BitConverter.ToSingle(bytes, 0x14));
        Assert.AreEqual(3f, BitConverter.ToSingle(bytes, 0x18));
        Assert.AreEqual(5276635759L, BitConverter.ToInt64(bytes, 0x28));
        Assert.AreEqual(1, bytes[0x30]);
    }

    [TestMethod]
    public void Write_FailureWritesZeroSuccessByte()
    {
        var packet = new ItemDropResponsePacket
        {
            SourceObjectId = 1,
            ItemCoid = 2,
            DropPosition = new AutoCore.Game.Structures.Vector3(0f, 0f, 0f),
            TailValue = 0,
            WasSuccessful = false
        };

        var bytes = WritePacket(packet);
        Assert.AreEqual(0, bytes[0x30]);
        Assert.AreEqual(GameOpcode.ItemDropResponse, packet.Opcode);
    }

    private static byte[] WritePacket(ItemDropResponsePacket packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);
        return stream.ToArray();
    }
}
