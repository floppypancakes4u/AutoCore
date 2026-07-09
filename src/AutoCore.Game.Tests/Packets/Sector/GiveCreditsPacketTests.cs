using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class GiveCreditsPacketTests
{
    [TestMethod]
    public void Opcode_IsGiveCredits()
    {
        Assert.AreEqual(GameOpcode.GiveCredits, new GiveCreditsPacket().Opcode);
    }

    [TestMethod]
    public void Write_PadsThenWritesInt64Amount()
    {
        var packet = new GiveCreditsPacket { Amount = 0x1122334455667788L };
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        Assert.AreEqual(12, bytes.Length);
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, 0)); // pad at +0x04
        Assert.AreEqual(0x1122334455667788L, BitConverter.ToInt64(bytes, 4)); // amount at +0x08
    }

    [TestMethod]
    public void Write_ZeroAmount()
    {
        var packet = new GiveCreditsPacket { Amount = 0 };
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        Assert.AreEqual(0L, BitConverter.ToInt64(ms.ToArray(), 4));
    }
}
