using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class GiveXPPacketTests
{
    [TestMethod]
    public void Opcode_IsGiveXP()
    {
        Assert.AreEqual(GameOpcode.GiveXP, new GiveXPPacket().Opcode);
    }

    [TestMethod]
    public void Write_DefaultLevelHintIsMinusOne()
    {
        var packet = new GiveXPPacket { Amount = 500 };
        Assert.AreEqual((sbyte)-1, packet.LevelHint);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        Assert.AreEqual(5, bytes.Length);
        Assert.AreEqual(500, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(unchecked((byte)-1), bytes[4]);
    }

    [TestMethod]
    public void Write_CustomLevelHint()
    {
        var packet = new GiveXPPacket { Amount = 12, LevelHint = 7 };
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        Assert.AreEqual(12, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(7, bytes[4]);
    }
}
