using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class SkillStatusEffectPacketTests
{
    [TestMethod]
    public void Opcode_IsSkillStatusEffect()
    {
        Assert.AreEqual(GameOpcode.SkillStatusEffect, new SkillStatusEffectPacket().Opcode);
    }

    [TestMethod]
    public void Write_Size_MatchesCastSkillOnTargetFormula()
    {
        // CVOGReaction_CastSkillOnTarget: size = count * 0x18 + 0x58
        var packet = new SkillStatusEffectPacket
        {
            SkillId = 1799,
            SkillLevel = 1,
            ApplyPower = 5000,
            Status = 0,
            Flag = 1,
            Caster = new TFID(0x1122334455667788L, false),
            DiceSeed = 42
        };
        packet.AddTarget(new TFID(0x0AABBCCDDEEFF001L, true), powerDelta: 0, aux: 0);

        var bytes = WriteBody(packet);
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        var size = reader.ReadInt16();
        Assert.AreEqual((short)(1 * 0x18 + 0x58), size); // 0x70
        Assert.AreEqual((short)0, reader.ReadInt16());

        Assert.AreEqual(1799, reader.ReadInt32());
        Assert.AreEqual((short)1, reader.ReadInt16());
        Assert.AreEqual((short)0, reader.ReadInt16());

        Assert.AreEqual(5000, reader.ReadInt32());
        Assert.AreEqual((byte)0, reader.ReadByte());
        reader.ReadBytes(3);

        // pos + pad
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadInt32();

        // caster TFID
        Assert.AreEqual(0x1122334455667788L, reader.ReadInt64());
        Assert.IsFalse(reader.ReadBoolean());
        reader.ReadBytes(7);

        Assert.AreEqual((byte)1, reader.ReadByte());
        reader.ReadBytes(3);
        Assert.AreEqual(42, reader.ReadInt32());

        // target (0x18)
        Assert.AreEqual(0x0AABBCCDDEEFF001L, reader.ReadInt64());
        Assert.IsTrue(reader.ReadBoolean());
        reader.ReadBytes(7);
        Assert.AreEqual((short)0, reader.ReadInt16());
        Assert.AreEqual((short)0, reader.ReadInt16());
        Assert.AreEqual(0, reader.ReadInt32());

        // terminator 4 dwords
        Assert.AreEqual(-1, reader.ReadInt32());
        Assert.AreEqual(-1, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
    }

    [TestMethod]
    public void Write_ZeroTargets_SizeIs0x58()
    {
        var packet = new SkillStatusEffectPacket { SkillId = 1, Caster = new TFID(1, false) };
        var bytes = WriteBody(packet);
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        Assert.AreEqual((short)0x58, reader.ReadInt16());
    }

    [TestMethod]
    public void Write_ApplyPowerNegative_ClampedToZero()
    {
        var packet = new SkillStatusEffectPacket
        {
            SkillId = 1799,
            ApplyPower = -5,
            Caster = new TFID(1, false)
        };
        packet.AddTarget(new TFID(1, false));
        var bytes = WriteBody(packet);
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        reader.ReadInt16();
        reader.ReadInt16();
        reader.ReadInt32();
        reader.ReadInt16();
        reader.ReadInt16();
        Assert.AreEqual(0, reader.ReadInt32());
    }

    private static byte[] WriteBody(SkillStatusEffectPacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        writer.Flush();
        return ms.ToArray();
    }
}
