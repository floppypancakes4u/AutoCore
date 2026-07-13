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
        packet.AddTarget(new TFID(0x0AABBCCDDEEFF001L, true), mana: 55, maxMana: 100);

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
        Assert.AreEqual((short)55, reader.ReadInt16());
        Assert.AreEqual((short)100, reader.ReadInt16());
        Assert.AreEqual(0, reader.ReadInt32());
        // terminator 4 dwords
        Assert.AreEqual(-1, reader.ReadInt32());
        Assert.AreEqual(-1, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        reader.ReadBytes(8);
        Assert.AreEqual(size - sizeof(uint), bytes.Length,
            "body length must match uiSize minus the opcode that WriteBody omits");
    }

    [TestMethod]
    public void Write_ZeroTargets_SizeIs0x58()
    {
        var packet = new SkillStatusEffectPacket { SkillId = 1, Caster = new TFID(1, false) };
        var bytes = WriteBody(packet);
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);
        Assert.AreEqual((short)0x58, reader.ReadInt16());
        Assert.AreEqual(0x58 - sizeof(uint), bytes.Length);
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

    [TestMethod]
    public void Write_LearnedCast_PreservesCharacterSourceAndSelectedTargetAtRetailOffsets()
    {
        var character = new TFID(18325, true);
        var selectedTarget = new TFID(1342195395, true);
        var packet = new SkillStatusEffectPacket
        {
            SkillId = 2103,
            SkillLevel = 2,
            ApplyPower = 0,
            Status = 0,
            PosX = 247.5f,
            PosY = 12.5f,
            PosZ = 365.5f,
            Caster = character,
            Flag = 0,
        };
        packet.AddTarget(selectedTarget, 0, 0);

        var body = WriteBody(packet);

        // WriteBody starts at absolute packet +0x04 because the opcode is written externally.
        Assert.AreEqual(character.Coid, BitConverter.ToInt64(body, 0x28 - 4));
        Assert.AreEqual((byte)1, body[0x30 - 4]);
        Assert.AreEqual((byte)0, body[0x38 - 4], "learned cast must not use item-skill routing");
        Assert.AreEqual(selectedTarget.Coid, BitConverter.ToInt64(body, 0x40 - 4));
        Assert.AreEqual((byte)1, body[0x48 - 4]);
        Assert.AreNotEqual(character.Coid, BitConverter.ToInt64(body, 0x40 - 4),
            "source-owner TFID must never leak into the first target slot");
    }

    [TestMethod]
    public void Write_MultipleTargets_UsesRetailStrideAndTerminatorAfterLastTarget()
    {
        var packet = new SkillStatusEffectPacket
        {
            SkillId = 2103,
            Caster = new TFID(100, true),
            Flag = 0,
        };
        packet.AddTarget(new TFID(200, true), 10, 20);
        packet.AddTarget(new TFID(300, false), 30, 40);

        var body = WriteBody(packet);
        Assert.AreEqual((short)(0x58 + 2 * 0x18), BitConverter.ToInt16(body, 0));
        Assert.AreEqual(200L, BitConverter.ToInt64(body, 0x40 - 4));
        Assert.AreEqual((short)10, BitConverter.ToInt16(body, 0x50 - 4));
        Assert.AreEqual((short)20, BitConverter.ToInt16(body, 0x52 - 4));
        Assert.AreEqual(300L, BitConverter.ToInt64(body, 0x58 - 4));
        Assert.AreEqual((short)30, BitConverter.ToInt16(body, 0x68 - 4));
        Assert.AreEqual((short)40, BitConverter.ToInt16(body, 0x6A - 4));
        Assert.AreEqual(-1, BitConverter.ToInt32(body, 0x70 - 4));
        Assert.AreEqual(-1, BitConverter.ToInt32(body, 0x74 - 4));
    }

    [TestMethod]
    public void Write_TargetCountAboveCapacity_IsCappedWithoutMovingTerminator()
    {
        var packet = new SkillStatusEffectPacket { SkillId = 2103, Caster = new TFID(100, true) };
        for (var i = 0; i < 40; i++)
            packet.AddTarget(new TFID(1000 + i, true));

        var body = WriteBody(packet);
        const int cappedCount = 32;
        var expectedSize = 0x58 + cappedCount * 0x18;
        Assert.AreEqual((short)expectedSize, BitConverter.ToInt16(body, 0));
        Assert.AreEqual(expectedSize - sizeof(uint), body.Length);
        Assert.AreEqual(1031L, BitConverter.ToInt64(body, (0x40 - 4) + 31 * 0x18));
        Assert.AreEqual(-1, BitConverter.ToInt32(body, (0x40 - 4) + 32 * 0x18));
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
