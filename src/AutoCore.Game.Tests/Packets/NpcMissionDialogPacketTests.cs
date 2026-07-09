using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// S2C NpcMissionDialog (0x206D) layout from Client_RecvNpcMissionDialog.
/// </summary>
[TestClass]
public class NpcMissionDialogPacketTests
{
    [TestMethod]
    public void Write_SingleMission_MatchesClientOffsets()
    {
        const long npcCoid = 94001;
        const int missionId = 91001;

        var packet = new NpcMissionDialogPacket
        {
            NpcTfid = new TFID(npcCoid, false),
        };
        packet.MissionIds.Add(missionId);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(Math.Max(ms.Position, packet.PayloadLength));

        var bytes = ms.ToArray();
        Assert.AreEqual(GameOpcode.MissionDialog, packet.Opcode);
        Assert.AreEqual(0x206D, (int)packet.Opcode);
        Assert.AreEqual(0x206D, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(npcCoid, BitConverter.ToInt64(bytes, 8));
        Assert.AreEqual(0, bytes[16]);
        Assert.AreEqual(1, BitConverter.ToInt32(bytes, 0x18));
        Assert.AreEqual(missionId, BitConverter.ToInt32(bytes, 0x20));

        for (var i = 0; i < 8; i++)
            Assert.AreEqual(-1, BitConverter.ToInt32(bytes, 0x28 + i * 4));
    }

    [TestMethod]
    public void Write_MultipleMissions_UsesEntryStride()
    {
        var packet = new NpcMissionDialogPacket
        {
            NpcTfid = new TFID(1, true),
        };
        packet.MissionIds.Add(91001);
        packet.MissionIds.Add(91002);
        packet.MissionItemCoids.Add(new[] { 11, 12, -1, -1, -1, -1, -1, -1 });

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(Math.Max(ms.Position, packet.PayloadLength));

        var bytes = ms.ToArray();
        Assert.AreEqual(2, BitConverter.ToInt32(bytes, 0x18));
        Assert.AreEqual(91001, BitConverter.ToInt32(bytes, 0x20));
        Assert.AreEqual(11, BitConverter.ToInt32(bytes, 0x28));
        Assert.AreEqual(91002, BitConverter.ToInt32(bytes, 0x20 + 40));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, 0x28 + 40));
        Assert.AreEqual(1, bytes[16]); // global
    }

    [TestMethod]
    public void PayloadLength_EmptyMissions_IsHeaderOnly()
    {
        var packet = new NpcMissionDialogPacket { NpcTfid = new TFID(1, false) };
        Assert.AreEqual(NpcMissionDialogPacket.FirstMissionOffset, packet.PayloadLength);
    }
}
