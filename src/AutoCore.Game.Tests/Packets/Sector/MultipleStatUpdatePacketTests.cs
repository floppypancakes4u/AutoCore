using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

[TestClass]
public class MultipleStatUpdatePacketTests
{
    [TestMethod]
    public void Opcode_IsMultipleStatUpdate_0x2010()
    {
        Assert.AreEqual(GameOpcode.MultipleStatUpdate, new MultipleStatUpdatePacket().Opcode);
        Assert.AreEqual(0x2010, (int)GameOpcode.MultipleStatUpdate);
    }

    [TestMethod]
    public void Write_SingleShieldStat_MatchesClientFun0080bc40Layout()
    {
        // Client FUN_0080BC40: uint16 count, TFID(16), uint8 nStats, n * 12-byte entries
        // Entry: type@0, pad 7, float@8 — type 1 = Vehicle_SetCurrentShield
        var packet = MultipleStatUpdatePacket.ForVehicleShield(new TFID(18452, true), currentShield: 5);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
            packet.Write(w);

        var bytes = ms.ToArray();
        Assert.AreEqual(2 + 16 + 1 + 12, bytes.Length);

        using var br = new BinaryReader(new MemoryStream(bytes));
        Assert.AreEqual(1, br.ReadUInt16(), "object count");
        Assert.AreEqual(18452L, br.ReadInt64(), "coid");
        Assert.AreEqual(1, br.ReadByte(), "global");
        br.ReadBytes(7); // TFID pad
        Assert.AreEqual(1, br.ReadByte(), "numStats");
        Assert.AreEqual((byte)MultipleStatUpdatePacket.StatType.Shield, br.ReadByte(), "type=shield");
        br.ReadBytes(7); // entry pad
        Assert.AreEqual(5f, br.ReadSingle(), "shield value as float");
    }

    [TestMethod]
    public void Write_EmptyObjects_WritesZeroCount()
    {
        var packet = new MultipleStatUpdatePacket();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(w);
        Assert.AreEqual(2, ms.Length);
        ms.Position = 0;
        using var br = new BinaryReader(ms);
        Assert.AreEqual(0, br.ReadUInt16());
    }
}
