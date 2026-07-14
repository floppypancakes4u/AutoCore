using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

[TestClass]
public class MissionPacketCoverageTests
{
    [TestMethod]
    public void FailMissionPacket_Write_Layout()
    {
        var packet = new FailMissionPacket
        {
            CharacterCoid = 18374,
            MissionId = 554,
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);

        Assert.AreEqual(GameOpcode.FailMission, packet.Opcode);
        // Write seeks +4, then coid i64, mission i32, then +4 pad.
        ms.Position = 4;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(18374L, reader.ReadInt64());
        Assert.AreEqual(554, reader.ReadInt32());
    }

    [TestMethod]
    public void FailMissionPacket_Read_Layout()
    {
        // After opcode is consumed by TNLConnection: pad4 + coid i64 + mission i32 + pad4.
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write(0); // pad
        writer.Write(18374L);
        writer.Write(554);
        writer.Write(0); // pad
        writer.Flush();

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var packet = new FailMissionPacket();
        packet.Read(reader);

        Assert.AreEqual(GameOpcode.FailMission, packet.Opcode);
        Assert.AreEqual(18374L, packet.CharacterCoid);
        Assert.AreEqual(554, packet.MissionId);
        Assert.AreEqual(ms.Length, ms.Position);
    }

    [TestMethod]
    public void FailMissionPacket_WriteThenRead_RoundTrips()
    {
        var original = new FailMissionPacket
        {
            CharacterCoid = 90001,
            MissionId = 3052,
        };

        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        original.Write(writer);
        writer.Flush();

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var restored = new FailMissionPacket();
        restored.Read(reader);

        Assert.AreEqual(original.CharacterCoid, restored.CharacterCoid);
        Assert.AreEqual(original.MissionId, restored.MissionId);
    }

    [TestMethod]
    public void ConvoyMissionsRequest_Read_IsNoOp()
    {
        var packet = new ConvoyMissionsRequestPacket();
        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new BinaryReader(ms);
        packet.Read(reader);
        Assert.AreEqual(GameOpcode.ConvoyMissionsRequest, packet.Opcode);
        Assert.AreEqual(0, ms.Position);
    }

    [TestMethod]
    public void ConvoyMissionsResponse_Write_IncludesQuestBlob()
    {
        var q = new CharacterQuest(91001, 0);
        q.ObjectiveProgress[0] = 1;
        q.ObjectiveMax[0] = 2;

        var packet = new ConvoyMissionsResponsePacket
        {
            CurrentQuests = [q],
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);

        Assert.AreEqual(GameOpcode.ConvoyMissionsResponse, packet.Opcode);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(1, reader.ReadInt32());
        Assert.AreEqual(91001, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadByte());
    }

    [TestMethod]
    public void ConvoyMissionsResponse_Write_EmptyList()
    {
        var packet = new ConvoyMissionsResponsePacket();
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(0, reader.ReadInt32());
    }
}
