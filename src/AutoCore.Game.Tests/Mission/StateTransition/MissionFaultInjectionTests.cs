using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.StateTransition;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Controlled faults at persistence / template boundaries — memory state must stay coherent.
/// </summary>
[TestClass]
public class MissionFaultInjectionTests
{
    private const int MissionId = 98301;
    private const int ObjectiveId = 98401;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PersistFailure_RetainsPending_MemoryQuestIntact()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();

        MissionPersistence.Instance.PersistQuestRow = (_, _, _) =>
            throw new InvalidOperationException("database unavailable");

        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);

        var flushed = MissionPersistence.Instance.FlushPending();
        Assert.AreEqual(0, flushed);
        Assert.IsTrue(MissionPersistence.Instance.PendingPersistCount >= 1);

        // Restore working persist and retry.
        var writes = 0;
        MissionPersistence.Instance.PersistQuestRow = (_, _, _) => writes++;
        Assert.AreEqual(1, MissionPersistence.Instance.FlushPending());
        Assert.AreEqual(1, writes);
        Assert.AreEqual(0, MissionPersistence.Instance.PendingPersistCount);
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Complete_MissingTemplate_StillRemovesQuest_NoCrash()
    {
        // Quest active but template removed from AssetManager mid-flight.
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        AssetManager.Instance.ClearTestMissions();

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o0, source: "MissingTemplate");

        // Mission object still held in hand; complete path uses passed mission reference.
        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Grant_MissingTemplate_StillTracksQuest()
    {
        // Grant path does not require template for CharacterQuest creation; max re-derived later.
        var player = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(player.Connection, player.Character, missionId: 999991);
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
        Assert.AreEqual(999991, player.Character.CurrentQuests[0].MissionId);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Load_MissingTemplate_OverlaysProgressSafely()
    {
        var player = _fx.CreatePlayer();
        var progress = MissionPersistence.PackProgress(new[] { 2, 3, 4 });
        _fx.LoadFromRows(
            player.Character,
            new[]
            {
                new AutoCore.Database.Char.Models.CharacterQuestData
                {
                    CharacterCoid = player.Character.ObjectId.Coid,
                    MissionId = 888001,
                    ActiveObjectiveSequence = 1,
                    State = 0,
                    ObjectiveProgress = progress,
                },
            },
            Array.Empty<AutoCore.Database.Char.Models.CharacterCompletedMissionData>());

        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
        Assert.AreEqual(888001, player.Character.CurrentQuests[0].MissionId);
        Assert.AreEqual(1, player.Character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(player.Character.CurrentQuests[0].ObjectiveProgress[0] >= 0);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Complete_WithNullConnection_StillMutatesServerState()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn: null, player.Character, quest, mission, o0, source: "NoConn");

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        _fx.FlushPersist();
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PersistComplete_AfterFailedUpsert_LatestWinsComplete()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(1, MissionId, QuestPersistOp.Upsert(0, 0, Array.Empty<byte>()));
        Assert.AreEqual(0, queue.Flush((_, _, _) => throw new TimeoutException("db")));
        Assert.AreEqual(1, queue.PendingCount);

        queue.Enqueue(1, MissionId, QuestPersistOp.Complete());
        var kinds = new List<QuestPersistKind>();
        Assert.AreEqual(1, queue.Flush((_, _, op) => kinds.Add(op.Kind)));
        CollectionAssert.AreEqual(new[] { QuestPersistKind.Complete }, kinds);
    }
}
