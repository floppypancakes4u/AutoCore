using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Wire-format tests for mission dialog response (C2S) and objective/finish (S2C) packets.
/// </summary>
[TestClass]
public class MissionDialogAndObjectivePacketTests
{
    [TestMethod]
    public void MissionDialogResponse_Read_MatchesLayout()
    {
        const int missionId = 91001;
        var giver = new TFID(94001, true);

        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write(missionId);
        writer.Write(true); // accepted
        writer.Write(new byte[3]); // pad
        writer.Write(new byte[4]); // pad
        writer.Write(giver.Coid);
        writer.Write(giver.Global);
        writer.Write(new byte[7]);
        writer.Flush();

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var packet = new MissionDialogResponsePacket();
        packet.Read(reader);

        Assert.AreEqual(GameOpcode.MissionDialogResponse, packet.Opcode);
        Assert.AreEqual(0x206E, (int)packet.Opcode);
        Assert.AreEqual(missionId, packet.MissionId);
        Assert.IsTrue(packet.Accepted);
        Assert.AreEqual(giver.Coid, packet.MissionGiver.Coid);
        Assert.IsTrue(packet.MissionGiver.Global);
    }

    [TestMethod]
    public void ObjectiveState_Write_MatchesClientOffsets()
    {
        var packet = new ObjectiveStatePacket
        {
            ObjectiveBitmask = 0u,
            ObjectiveId = 92001,
        };
        packet.SlotProgress[0] = 1.0f;
        packet.SlotProgress[2] = 0.5f;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(Math.Max(ms.Position, ObjectiveStatePacket.PayloadLength));

        var bytes = ms.ToArray();
        Assert.AreEqual(GameOpcode.ObjectiveState, packet.Opcode);
        Assert.AreEqual(0x2071, (int)packet.Opcode);
        Assert.AreEqual(0u, BitConverter.ToUInt32(bytes, 0x10));
        Assert.AreEqual(92001, BitConverter.ToInt32(bytes, 0x14));
        Assert.AreEqual(1.0f, BitConverter.ToSingle(bytes, 0x18));
        Assert.AreEqual(0.0f, BitConverter.ToSingle(bytes, 0x1C));
        Assert.AreEqual(0.5f, BitConverter.ToSingle(bytes, 0x20));
    }

    [TestMethod]
    public void CompleteDynamicObjective_Write_PrefersObjectiveId()
    {
        var packet = new CompleteDynamicObjectivePacket
        {
            MissionId = 91001,
            ObjectiveId = 92001,
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(Math.Max(ms.Position, CompleteDynamicObjectivePacket.LookupIdOffset + 4));

        var bytes = ms.ToArray();
        Assert.AreEqual(GameOpcode.CompleteDynamicObjective, packet.Opcode);
        Assert.AreEqual(0x2070, (int)packet.Opcode);
        Assert.AreEqual(92001, BitConverter.ToInt32(bytes, 0x10));
    }

    [TestMethod]
    public void CompleteDynamicObjective_Write_FallsBackToMissionId()
    {
        var packet = new CompleteDynamicObjectivePacket
        {
            MissionId = 91001,
            ObjectiveId = 0,
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(Math.Max(ms.Position, CompleteDynamicObjectivePacket.LookupIdOffset + 4));

        var bytes = ms.ToArray();
        Assert.AreEqual(91001, BitConverter.ToInt32(bytes, 0x10));
    }
}
