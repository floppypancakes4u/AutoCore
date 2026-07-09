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

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        var bytes = stream.ToArray();
        Assert.IsTrue(bytes.Length >= ItemDropResponsePacket.MinimumLength);
        Assert.AreEqual((uint)GameOpcode.ItemDropResponse, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(19464384, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(42000L, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(1, bytes[0x30]);
    }
}
