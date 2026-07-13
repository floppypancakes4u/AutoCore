using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class AttributeIncrementPacketTests
{
    [TestMethod]
    public void Opcode_IsAttributeIncrement()
    {
        Assert.AreEqual(GameOpcode.AttributeIncrement, new AttributeIncrementPacket().Opcode);
    }

    [TestMethod]
    public void Read_TechMask()
    {
        var packet = ReadMask(0x00010000u);
        Assert.AreEqual(0x00010000u, packet.AttributeMask);
        Assert.AreEqual(CharacterAttributeKind.Tech, packet.Attribute);
    }

    [TestMethod]
    public void Read_AllKnownMasks()
    {
        Assert.AreEqual(CharacterAttributeKind.Combat, ReadMask(0x00000001u).Attribute);
        Assert.AreEqual(CharacterAttributeKind.Theory, ReadMask(0x00000100u).Attribute);
        Assert.AreEqual(CharacterAttributeKind.Tech, ReadMask(0x00010000u).Attribute);
        Assert.AreEqual(CharacterAttributeKind.Perception, ReadMask(0x01000000u).Attribute);
    }

    [TestMethod]
    public void Read_UnknownMask_IsNone()
    {
        var packet = ReadMask(0xDEADBEEFu);
        Assert.AreEqual(CharacterAttributeKind.None, packet.Attribute);
    }

    private static AttributeIncrementPacket ReadMask(uint mask)
    {
        var bytes = BitConverter.GetBytes(mask);
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        var packet = new AttributeIncrementPacket();
        packet.Read(r);
        return packet;
    }
}
