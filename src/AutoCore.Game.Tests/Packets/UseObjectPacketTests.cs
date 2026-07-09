using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// C2S UseObject (0x2072) wire format from Client_SendUseObject:
/// after opcode strip: pad4 + TFID(16) + IDObjective(i32).
/// </summary>
[TestClass]
public class UseObjectPacketTests
{
    [TestMethod]
    public void Read_MatchesGhidraLayout_Pad4TfidIdObjective()
    {
        var target = new TFID(94001, false);
        const int objectiveId = 92001;

        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write(0);
        writer.Write(target.Coid);
        writer.Write(target.Global);
        writer.Write(new byte[7]);
        writer.Write(objectiveId);
        writer.Flush();

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var packet = new UseObjectPacket();
        packet.Read(reader);

        Assert.AreEqual(GameOpcode.UseObject, packet.Opcode);
        Assert.AreEqual(target.Coid, packet.Target.Coid);
        Assert.AreEqual(target.Global, packet.Target.Global);
        Assert.AreEqual(objectiveId, packet.ObjectiveId);
    }

    [TestMethod]
    public void Read_ObjectiveIdMinusOne_WhenNoMatchingObjective()
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write(0);
        writer.Write(42L);
        writer.Write(true);
        writer.Write(new byte[7]);
        writer.Write(-1);
        writer.Flush();

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var packet = new UseObjectPacket();
        packet.Read(reader);

        Assert.AreEqual(42L, packet.Target.Coid);
        Assert.IsTrue(packet.Target.Global);
        Assert.AreEqual(-1, packet.ObjectiveId);
    }
}
