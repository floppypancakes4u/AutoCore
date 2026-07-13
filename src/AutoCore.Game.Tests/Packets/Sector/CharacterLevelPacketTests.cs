using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class CharacterLevelPacketTests
{
    [TestMethod]
    public void Opcode_IsCharacterLevel()
    {
        Assert.AreEqual(GameOpcode.CharacterLevel, new CharacterLevelPacket().Opcode);
    }

    [TestMethod]
    public void BuildCurrency_PacksDenominations()
    {
        // globes * 1e9 + bars * 1e6 + scrip * 1e3 + clink
        Assert.AreEqual(0L, CharacterLevelPacket.BuildCurrency(0, 0, 0, 0));
        Assert.AreEqual(4L, CharacterLevelPacket.BuildCurrency(0, 0, 0, 4));
        Assert.AreEqual(3_004L, CharacterLevelPacket.BuildCurrency(0, 0, 3, 4));
        Assert.AreEqual(2_003_004L, CharacterLevelPacket.BuildCurrency(0, 2, 3, 4));
        Assert.AreEqual(1_002_003_004L, CharacterLevelPacket.BuildCurrency(1, 2, 3, 4));
        Assert.AreEqual(123_999_888_777_666L, CharacterLevelPacket.BuildCurrency(123_999, 888, 777, 666));
    }

    [TestMethod]
    public void SplitCurrency_RoundTripsBuildCurrency()
    {
        Assert.AreEqual((0L, 0, 0, 0), CharacterLevelPacket.SplitCurrency(0L));
        Assert.AreEqual((0L, 0, 0, 4), CharacterLevelPacket.SplitCurrency(4L));
        Assert.AreEqual((0L, 0, 3, 4), CharacterLevelPacket.SplitCurrency(3_004L));
        Assert.AreEqual((0L, 2, 3, 4), CharacterLevelPacket.SplitCurrency(2_003_004L));
        Assert.AreEqual((1L, 2, 3, 4), CharacterLevelPacket.SplitCurrency(1_002_003_004L));
        Assert.AreEqual((123_999L, 888, 777, 666), CharacterLevelPacket.SplitCurrency(123_999_888_777_666L));

        var packed = CharacterLevelPacket.BuildCurrency(9, 8, 7, 6);
        var (g, b, s, c) = CharacterLevelPacket.SplitCurrency(packed);
        Assert.AreEqual(packed, CharacterLevelPacket.BuildCurrency(g, b, s, c));
    }

    [TestMethod]
    public void SplitCurrency_Negative_TreatedAsZero()
    {
        Assert.AreEqual((0L, 0, 0, 0), CharacterLevelPacket.SplitCurrency(-1L));
    }

    [TestMethod]
    public void Write_Layout_CurrencyAtOffset0x20()
    {
        var id = new TFID { Coid = 0x1122334455667788L, Global = true };
        var packet = new CharacterLevelPacket
        {
            UnknownHeader = 0x0A0B0C0D,
            CharacterId = id,
            Level = 42,
            Currency = 1_002_003_004L,
            Experience = 12345,
            Health = 123,
            HealthMaximum = 456,
            CurrentMana = 10,
            MaxMana = 20,
            AttributeTech = 1,
            AttributeCombat = 2,
            AttributeTheory = 3,
            AttributePerception = 4,
            AttributePoints = 5,
            SkillPoints = 6,
            Unknown7 = 7,
            ResearchPoints = 8
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        // header(4) + TFID(16) + level(1) + pad(7) + currency(8) + exp(4) + unk(8) + 10 shorts = 4+16+1+7+8+4+8+20 = 68
        Assert.AreEqual(68, bytes.Length);
        Assert.AreEqual(0x0A0B0C0D, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(0x1122334455667788L, BitConverter.ToInt64(bytes, 4));
        Assert.AreEqual(1, bytes[12]); // Global bool as byte
        Assert.AreEqual(42, bytes[0x14]); // Level after header+TFID (4+16=20=0x14)
        Assert.AreEqual(1_002_003_004L, BitConverter.ToInt64(bytes, 0x1C)); // after level+7 pad = 0x14+1+7=0x1C
        // Doc says Currency at 0x20 relative to message with opcode; our Write starts after opcode.
        // Relative to Write stream: Currency at offset 0x1C (28) = header 4 + TFID 16 + level 1 + pad 7.
        Assert.AreEqual(12345, BitConverter.ToInt32(bytes, 0x24));
        Assert.AreEqual(123, BitConverter.ToInt32(bytes, 0x28));
        Assert.AreEqual(456, BitConverter.ToInt32(bytes, 0x2C));
        Assert.AreEqual((short)10, BitConverter.ToInt16(bytes, 0x30));
        Assert.AreEqual((short)20, BitConverter.ToInt16(bytes, 0x32));
        // Pad after level must be zero (WriteZeros)
        for (var i = 0x15; i < 0x1C; i++)
            Assert.AreEqual(0, bytes[i], $"pad byte at {i}");
    }
}
