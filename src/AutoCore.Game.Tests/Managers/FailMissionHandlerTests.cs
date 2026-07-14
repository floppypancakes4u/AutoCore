using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// FailMission (0x20B2): C2S abandon + shared fail lifecycle (remove active, not complete).
/// </summary>
[TestClass]
public class FailMissionHandlerTests
{
    private const int MissionId = 91300;
    private const int ObjectiveId = 92300;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FailMission_Active_RemovesQuest_DoesNotComplete_SendsPacketAndJournal()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer(characterCoid: 18374);
        _fx.GiveQuest(player.Character, MissionId);
        _fx.PersistWrites.Clear();
        _fx.Sent.Clear();

        NpcInteractHandler.FailMission(player.Connection, player.Character, MissionId);
        _fx.FlushPersist();

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_fx.PersistWrites.Any(w =>
            w.Kind == QuestPersistKind.Remove && w.MissionId == MissionId && w.Coid == 18374));

        var fail = _fx.Sent.OfType<FailMissionPacket>().Single();
        Assert.AreEqual(18374L, fail.CharacterCoid);
        Assert.AreEqual(MissionId, fail.MissionId);
        Assert.IsTrue(_fx.Sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FailMission_NotActive_IsNoOp()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.PersistWrites.Clear();
        _fx.Sent.Clear();

        NpcInteractHandler.FailMission(player.Connection, player.Character, MissionId);
        _fx.FlushPersist();

        Assert.AreEqual(0, _fx.CountPackets<FailMissionPacket>());
        Assert.AreEqual(0, _fx.PersistWrites.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FailMission_Idempotent_SecondCallNoPacket()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        NpcInteractHandler.FailMission(player.Connection, player.Character, MissionId);
        _fx.FlushPersist();
        _fx.Sent.Clear();
        _fx.PersistWrites.Clear();

        NpcInteractHandler.FailMission(player.Connection, player.Character, MissionId);
        _fx.FlushPersist();

        Assert.AreEqual(0, _fx.CountPackets<FailMissionPacket>());
        Assert.AreEqual(0, _fx.PersistWrites.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void HandleFailMission_C2S_UsesServerCharacter_IgnoresPacketCoid()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer(characterCoid: 5001);
        _fx.GiveQuest(player.Character, MissionId);
        _fx.Sent.Clear();

        NpcInteractHandler.HandleFailMission(player.Connection, new FailMissionPacket
        {
            CharacterCoid = 99999, // client-supplied; ignore
            MissionId = MissionId,
        });

        var fail = _fx.Sent.OfType<FailMissionPacket>().Single();
        Assert.AreEqual(5001L, fail.CharacterCoid);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleFailMission_InvalidMissionId_NoOp()
    {
        var player = _fx.CreatePlayer();
        _fx.Sent.Clear();

        NpcInteractHandler.HandleFailMission(player.Connection, new FailMissionPacket
        {
            CharacterCoid = player.Character.ObjectId.Coid,
            MissionId = 0,
        });
        NpcInteractHandler.HandleFailMission(player.Connection, new FailMissionPacket
        {
            CharacterCoid = player.Character.ObjectId.Coid,
            MissionId = -1,
        });

        Assert.AreEqual(0, _fx.CountPackets<FailMissionPacket>());
    }

    [TestMethod]
    public void FailMissionPacket_Read_FromC2SBody()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0);
        w.Write(42L);
        w.Write(MissionId);
        w.Write(0);
        w.Flush();

        ms.Position = 0;
        using var r = new BinaryReader(ms);
        var packet = new FailMissionPacket();
        packet.Read(r);

        Assert.AreEqual(42L, packet.CharacterCoid);
        Assert.AreEqual(MissionId, packet.MissionId);
    }
}
