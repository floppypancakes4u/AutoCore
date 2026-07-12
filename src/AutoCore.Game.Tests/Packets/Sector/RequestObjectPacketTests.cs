using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// C2S RequestObject (0x2011): client batches missing TFIDs.
/// Ghidra FUN_0091da70: after opcode = u8 count + 3 pad + count * TFID16.
/// Misreading as a bare TFID yields garbage high COIDs like 0x500047A000000001.
/// </summary>
[TestClass]
public class RequestObjectPacketTests
{
    [TestMethod]
    public void Read_SingleTfid_CountPadAndCoid()
    {
        // Live log residue: count=1 residual pad, map NPC coid = CoidBase+18336
        const long realCoid = 0x5000_0000L + 18336;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)1);           // count
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((byte)0);           // pad3
        w.Write(realCoid);          // TFID coid
        w.Write(true);              // global
        w.Write(new byte[7]);
        w.Flush();
        ms.Position = 0;

        using var r = new BinaryReader(ms);
        var packet = new RequestObjectPacket();
        packet.Read(r);

        Assert.AreEqual(GameOpcode.RequestObject, packet.Opcode);
        Assert.AreEqual(1, packet.Objects.Count);
        Assert.AreEqual(realCoid, packet.Objects[0].Coid);
        Assert.IsTrue(packet.Objects[0].Global);
    }

    [TestMethod]
    public void Read_WithoutCountPad_WouldLookLikeGarbageCoid_Documented()
    {
        // Shows why the old bare-ReadTFID path produced 5764686275554574337.
        const long realCoid = 0x5000_0000L + 18336;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)1);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write(realCoid);
        w.Write(false);
        w.Write(new byte[7]);
        w.Flush();
        ms.Position = 0;

        using var r = new BinaryReader(ms);
        var misread = r.ReadInt64();
        Assert.AreEqual(5764686275554574337L, misread,
            "count=1 + low dword of map COID must match the live 'not on map' log value");
    }

    [TestMethod]
    public void Read_MultipleTfids()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)2);
        w.Write(new byte[3]);
        foreach (var coid in new long[] { 100, 200 })
        {
            w.Write(coid);
            w.Write(false);
            w.Write(new byte[7]);
        }
        w.Flush();
        ms.Position = 0;

        using var r = new BinaryReader(ms);
        var packet = new RequestObjectPacket();
        packet.Read(r);

        Assert.AreEqual(2, packet.Objects.Count);
        Assert.AreEqual(100, packet.Objects[0].Coid);
        Assert.AreEqual(200, packet.Objects[1].Coid);
    }

    [TestMethod]
    public void Read_ZeroCount_EmptyList()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)0);
        w.Write(new byte[3]);
        w.Flush();
        ms.Position = 0;

        using var r = new BinaryReader(ms);
        var packet = new RequestObjectPacket();
        packet.Read(r);

        Assert.AreEqual(0, packet.Objects.Count);
    }
}
