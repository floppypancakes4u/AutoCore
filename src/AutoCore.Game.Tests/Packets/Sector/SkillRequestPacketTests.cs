using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

[TestClass]
public class SkillRequestPacketTests
{
    [TestMethod]
    public void RequestCastSkill_ReadsTargetSkillAndTargetPosition()
    {
        var target = new TFID(101, false);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0); // packet header/padding at +0x04
            writer.Write(target.Coid);
            writer.Write(target.Global);
            writer.Write(new byte[7]);
            writer.Write(857);
            writer.Write(1.5f);
            writer.Write(2.5f);
            writer.Write(3.5f);
        }

        stream.Position = 0;
        var packet = new RequestCastSkillPacket();
        packet.Read(new BinaryReader(stream));

        Assert.AreEqual(GameOpcode.RequestCastSkill, packet.Opcode);
        Assert.AreEqual(101L, packet.Target.Coid);
        Assert.AreEqual(857, packet.SkillId);
        Assert.AreEqual(1.5f, packet.TargetPosition.X);
        Assert.AreEqual(2.5f, packet.TargetPosition.Y);
        Assert.AreEqual(3.5f, packet.TargetPosition.Z);
    }

    [TestMethod]
    public void CancelSkill_ReadsTargetAndSkill()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0);
            writer.Write(102L);
            writer.Write(true);
            writer.Write(new byte[7]);
            writer.Write(858);
        }

        stream.Position = 0;
        var packet = new CancelSkillPacket();
        packet.Read(new BinaryReader(stream));

        Assert.AreEqual(GameOpcode.CancelSkill, packet.Opcode);
        Assert.AreEqual(102L, packet.Target.Coid);
        Assert.IsTrue(packet.Target.Global);
        Assert.AreEqual(858, packet.SkillId);
    }
}
