using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.StateTransition;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Retail mission-journal deltas are Sector mission lifecycle packets. ConvoyMissionsResponse
/// is reserved for a Global convoy-member mission-list request and must never be a journal sync.
/// </summary>
[TestClass]
public class MissionJournalSemanticTests
{
    private const int MissionId = 98001;
    private const int ObjectiveA = 98101;
    private const int ObjectiveB = 98102;
    private const int TargetCbid = 98201;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_SoloMissionChange_DoesNotSendConvoyMissionsResponse()
    {
        var objective = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        NpcInteractHandler.ResyncActiveMissionToClient(
            player.Connection,
            player.Character,
            player.Character.CurrentQuests.Single());

        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
        Assert.AreEqual(1, _fx.CountPackets<ObjectiveStatePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_Accept_UsesRetailUpdateMechanism()
    {
        var objective = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();

        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);

        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, _fx.CountPackets<ObjectiveStatePacket>());
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_ObjectiveProgress_UsesRetailUpdateMechanism()
    {
        var objective = _fx.CreateKillObjective(
            ObjectiveA,
            sequence: 0,
            MissionId,
            TargetCbid,
            numToKill: 2);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var target = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        target.SetMurderer(player.Vehicle);
        target.OnDeath(DeathType.Silent);

        var state = _fx.Sent.OfType<ObjectiveStatePacket>().Single();
        Assert.AreEqual(ObjectiveA, state.ObjectiveId);
        Assert.AreEqual(1f, state.SlotProgress[0], 0.001f);
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_ObjectiveComplete_UsesRetailUpdateMechanism()
    {
        var (player, mission, quest, objective) = CreateTwoSequenceMission();

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection,
            player.Character,
            quest,
            mission,
            objective,
            source: "JournalSemantic");

        var complete = _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Single();
        Assert.AreEqual(ObjectiveA, complete.ObjectiveId);
        Assert.IsTrue(_fx.Sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveB));
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_SequenceAdvance_UsesRetailUpdateMechanism()
    {
        var (player, mission, quest, objective) = CreateTwoSequenceMission();

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection,
            player.Character,
            quest,
            mission,
            objective,
            source: "JournalSemantic");

        Assert.AreEqual(1, quest.ActiveObjectiveSequence);
        Assert.AreEqual(1, _fx.CountPackets<CompleteDynamicObjectivePacket>());
        Assert.AreEqual(ObjectiveB, _fx.Sent.OfType<ObjectiveStatePacket>().Single().ObjectiveId);
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_MissionComplete_UsesRetailUpdateMechanism()
    {
        var objective = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests.Single();
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection,
            player.Character,
            quest,
            mission,
            objective,
            source: "JournalSemantic");

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(ObjectiveA, _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Single().ObjectiveId);
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_MissionFail_UsesRetailUpdateMechanism()
    {
        var objective = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        NpcInteractHandler.FailMission(player.Connection, player.Character, MissionId);

        var fail = _fx.Sent.OfType<FailMissionPacket>().Single();
        Assert.AreEqual(MissionId, fail.MissionId);
        Assert.AreEqual(player.Character.ObjectId.Coid, fail.CharacterCoid);
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_SeedActiveCompletion_UsesRetailUpdateMechanism()
    {
        var objective = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var seeded = NpcInteractHandler.MarkMissionsCompletedForSeed(
            player.Connection,
            player.Character,
            new[] { MissionId });

        CollectionAssert.AreEqual(new[] { MissionId }, seeded);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(ObjectiveA, _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Single().ObjectiveId);
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_DuplicateUpdate_IsIdempotent()
    {
        var objective = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, objective);
        var player = _fx.CreatePlayer();

        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);
        _fx.Sent.Clear();
        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);

        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, _fx.CountPackets<ObjectiveStatePacket>());
        Assert.AreEqual(0, _fx.CountPackets<ConvoyMissionsResponsePacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionJournal_LiveStateMatchesRelogSnapshot()
    {
        var (player, mission, quest, objective) = CreateTwoSequenceMission();
        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection,
            player.Character,
            quest,
            mission,
            objective,
            source: "JournalSemantic");
        quest.ObjectiveProgress[1] = 3;

        var reloaded = new Character();
        _fx.LoadFromRows(
            reloaded,
            new[]
            {
                new CharacterQuestData
                {
                    CharacterCoid = player.Character.ObjectId.Coid,
                    MissionId = MissionId,
                    ActiveObjectiveSequence = 1,
                    State = quest.State,
                    ObjectiveProgress = MissionPersistence.PackProgress(quest.ObjectiveProgress),
                },
            },
            Array.Empty<CharacterCompletedMissionData>());

        CollectionAssert.AreEqual(
            WriteQuest(player.Character.CurrentQuests.Single()),
            WriteQuest(reloaded.CurrentQuests.Single()));
    }

    private (PlayerMissionContext Player, Mission Mission, CharacterQuest Quest, MissionObjective Objective)
        CreateTwoSequenceMission()
    {
        var objectiveA = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        var objectiveB = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, objectiveA, objectiveB);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        return (
            player,
            AssetManager.Instance.GetMission(MissionId)!,
            player.Character.CurrentQuests.Single(),
            objectiveA);
    }

    private static byte[] WriteQuest(CharacterQuest quest)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        quest.Write(writer);
        return stream.ToArray();
    }
}
