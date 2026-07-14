using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class InitCreateObjectPacketTests
{
    [TestMethod]
    public void Opcode_IsInitCreateObject()
    {
        Assert.AreEqual(GameOpcode.InitCreateObject, new InitCreateObjectPacket().Opcode);
    }

    [TestMethod]
    public void Write_DoDeath_LayoutMatchesRetail0x10Body()
    {
        // Absolute: opcode@0, bCreate@4, bDoDeath@5, pad@6, coid@8. Body after opcode = 0x0C.
        var packet = new InitCreateObjectPacket(0x1122334455667788L, create: false, doDeath: true);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var body = ms.ToArray();

        Assert.AreEqual(12, body.Length);
        Assert.AreEqual(0, body[0]); // create
        Assert.AreEqual(1, body[1]); // doDeath
        Assert.AreEqual(0, body[2]);
        Assert.AreEqual(0, body[3]);
        Assert.AreEqual(0x1122334455667788L, BitConverter.ToInt64(body, 4));
    }

    [TestMethod]
    public void Write_CreateTrue_ClearsDoDeathSemantics()
    {
        var packet = new InitCreateObjectPacket(99, create: true, doDeath: false);
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var body = ms.ToArray();
        Assert.AreEqual(1, body[0]);
        Assert.AreEqual(0, body[1]);
    }
}
