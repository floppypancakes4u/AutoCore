using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;

/// <summary>
/// Full wire-layout regression for C2S QuickBarUpdate (0x2062).
/// Layout from client FUN_00826720 / FUN_00897170 (Ghidra).
/// </summary>
[TestClass]
public class QuickBarUpdatePacketTests
{
    /// <summary>
    /// Live capture bodyLength=12 body=0000D6343708000000000000
    /// (slot=0, skill, pad garbage, skillId=0x837=2103).
    /// </summary>
    [TestMethod]
    public void Read_SkillPlacement_FromLiveCapture()
    {
        var body = Convert.FromHexString("0000D6343708000000000000");
        var packet = ReadBody(body);

        Assert.AreEqual(GameOpcode.QuickBarUpdate, packet.Opcode);
        Assert.IsTrue(packet.IsValid);
        Assert.AreEqual((byte)0, packet.Slot);
        Assert.IsFalse(packet.IsItem);
        Assert.AreEqual(0x837L, packet.Value);
        Assert.AreEqual(2103, packet.SkillId);
        Assert.AreEqual(-1L, packet.ItemCoid);
        Assert.AreEqual(QuickBarUpdatePacket.BodyLength, packet.RawBody.Length);
        CollectionAssert.AreEqual(body, packet.RawBody);
    }

    [TestMethod]
    public void Read_ItemPlacement_MapsItemCoidAndClearsSkill()
    {
        var packet = ReadBody(BuildBody(slot: 7, isItem: 1, pad: 0, value: 0x0123456789ABCDEFL));

        Assert.IsTrue(packet.IsValid);
        Assert.AreEqual((byte)7, packet.Slot);
        Assert.IsTrue(packet.IsItem);
        Assert.AreEqual(0x0123456789ABCDEFL, packet.Value);
        Assert.AreEqual(0, packet.SkillId);
        Assert.AreEqual(0x0123456789ABCDEFL, packet.ItemCoid);
    }

    [TestMethod]
    public void Read_ClearSlot_IsItemWithNegOne()
    {
        var packet = ReadBody(BuildBody(slot: 3, isItem: 1, pad: 0xBEEF, value: -1L));

        Assert.IsTrue(packet.IsValid);
        Assert.AreEqual((byte)3, packet.Slot);
        Assert.IsTrue(packet.IsItem);
        Assert.AreEqual(-1L, packet.Value);
        Assert.AreEqual(0, packet.SkillId);
        Assert.AreEqual(-1L, packet.ItemCoid);
    }

    [TestMethod]
    public void Read_IsItemAnyNonZero_IsItemTrue()
    {
        var packet = ReadBody(BuildBody(slot: 9, isItem: 0xFF, pad: 0, value: 42L));
        Assert.IsTrue(packet.IsItem);
        Assert.AreEqual(42L, packet.ItemCoid);
        Assert.AreEqual(0, packet.SkillId);
    }

    [TestMethod]
    public void Read_SkillNegativeValue_NormalizesSkillIdToZero()
    {
        var packet = ReadBody(BuildBody(slot: 1, isItem: 0, pad: 0, value: -1L));
        Assert.IsFalse(packet.IsItem);
        Assert.AreEqual(0, packet.SkillId);
        Assert.AreEqual(-1L, packet.ItemCoid);
    }

    [TestMethod]
    public void Read_SkillMaxInt32_MapsSkillId()
    {
        var packet = ReadBody(BuildBody(slot: 99, isItem: 0, pad: 0x1234, value: int.MaxValue));
        Assert.AreEqual((byte)99, packet.Slot);
        Assert.AreEqual(int.MaxValue, packet.SkillId);
        Assert.AreEqual(-1L, packet.ItemCoid);
    }

    [TestMethod]
    public void Read_EmptyStream_IsInvalid_EmptyRawBody()
    {
        var packet = ReadBody(Array.Empty<byte>());
        Assert.IsFalse(packet.IsValid);
        Assert.AreEqual(0, packet.RawBody.Length);
        Assert.AreEqual(GameOpcode.QuickBarUpdate, packet.Opcode);
    }

    [TestMethod]
    public void Read_ShortBody_IsInvalid_PreservesPartialRaw()
    {
        var partial = new byte[] { 0x05, 0x00, 0x00 };
        var packet = ReadBody(partial);
        Assert.IsFalse(packet.IsValid);
        CollectionAssert.AreEqual(partial, packet.RawBody);
        // Defaults when invalid: mapping helpers still safe
        Assert.AreEqual(0, packet.SkillId);
        Assert.AreEqual(-1L, packet.ItemCoid);
    }

    [TestMethod]
    public void Read_ElevenBytes_IsInvalid()
    {
        var body = new byte[11];
        body[0] = 4;
        var packet = ReadBody(body);
        Assert.IsFalse(packet.IsValid);
        Assert.AreEqual(11, packet.RawBody.Length);
    }

    [TestMethod]
    public void Read_TrailingBytes_OnlyConsumesBodyLength()
    {
        var body = BuildBody(slot: 2, isItem: 0, pad: 0, value: 2103L);
        var withTrailing = body.Concat(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }).ToArray();

        using var stream = new MemoryStream(withTrailing);
        using var reader = new BinaryReader(stream);
        var packet = new QuickBarUpdatePacket();
        packet.Read(reader);

        Assert.IsTrue(packet.IsValid);
        Assert.AreEqual(2103, packet.SkillId);
        Assert.AreEqual(QuickBarUpdatePacket.BodyLength, (int)stream.Position);
        Assert.AreEqual(4, stream.Length - stream.Position);
    }

    [TestMethod]
    public void Read_FromNonZeroStreamOffset_ParsesCorrectly()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0xDEADBEEF); // prefix not part of body
            writer.Write(BuildBody(slot: 5, isItem: 0, pad: 0, value: 9001L));
        }

        stream.Position = 4;
        using var reader = new BinaryReader(stream);
        var packet = new QuickBarUpdatePacket();
        packet.Read(reader);

        Assert.IsTrue(packet.IsValid);
        Assert.AreEqual((byte)5, packet.Slot);
        Assert.AreEqual(9001, packet.SkillId);
    }

    [TestMethod]
    public void BodyLength_IsTwelve()
    {
        Assert.AreEqual(12, QuickBarUpdatePacket.BodyLength);
    }

    private static byte[] BuildBody(byte slot, byte isItem, ushort pad, long value)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(slot);
            writer.Write(isItem);
            writer.Write(pad);
            writer.Write(value);
        }

        return stream.ToArray();
    }

    private static QuickBarUpdatePacket ReadBody(byte[] body)
    {
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        var packet = new QuickBarUpdatePacket();
        packet.Read(reader);
        return packet;
    }
}
