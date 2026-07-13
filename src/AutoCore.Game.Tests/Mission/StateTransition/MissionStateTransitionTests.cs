using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.StateTransition;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Table-style state transitions for grant / progress / complete / reload.
/// Observable contracts only — executable documentation of the mission lifecycle.
/// </summary>
[TestClass]
public class MissionStateTransitionTests
{
    // Synthetic ids outside retail tutorial chains.
    private const int MissionId = 96001;
    private const int ObjectiveA = 96101;
    private const int ObjectiveB = 96102;
    private const int TargetCbid = 96201;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GrantMission_FromNotTracked_BecomesActive_AndEnqueuesUpsert()
    {
        var obj = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, isRepeatable: 0, obj);
        var player = _fx.CreatePlayer();

        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, expectedSequence: 0);
        Assert.AreEqual(1, _fx.PersistWrites.Count(w => w.Kind == QuestPersistKind.Upsert && w.MissionId == MissionId));
        Assert.IsTrue(_fx.CountPackets<ObjectiveStatePacket>() >= 1);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GrantMission_WhenAlreadyActive_DoesNotDuplicateQuest()
    {
        var obj = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, obj);
        var player = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);
        _fx.PersistWrites.Clear();
        _fx.Sent.Clear();

        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);

        Assert.AreEqual(1, player.Character.CurrentQuests.Count(q => q.MissionId == MissionId));
        // Duplicate grant resyncs UI but must not add a second quest row intent beyond existing.
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveMissionReaction_WhenCompletedNonRepeatable_DoesNotRegrant()
    {
        var obj = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, isRepeatable: 0, obj);
        var player = _fx.CreatePlayer();
        player.Character.CompletedMissionIds.Add(MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, MissionId);
        var ok = reaction.TriggerIfPossible(player.Character);

        Assert.IsFalse(ok, "GiveMission must decline so 0x206C is not re-sent");
        MissionInvariantAssertions.AssertNotActive(player.Character, MissionId);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveMissionReaction_WhenAlreadyActive_DoesNotDuplicate_AndDeclinesBroadcast()
    {
        var obj = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, obj);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, MissionId);
        var ok = reaction.TriggerIfPossible(player.Character);

        Assert.IsFalse(ok);
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveMissionReaction_FromNotTracked_TracksQuest()
    {
        var obj = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, obj);
        var player = _fx.CreatePlayer();

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, MissionId);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Upsert && w.MissionId == MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void AdvanceOrComplete_Intermediate_AdvancesSequence_DoesNotComplete()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        var o1 = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, o0, o1);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o0, source: "Test");
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, expectedSequence: 1);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_fx.CountPackets<CompleteDynamicObjectivePacket>() >= 1);
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Upsert));
        Assert.IsFalse(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void AdvanceOrComplete_Final_RemovesQuest_MarksCompleted_EnqueuesComplete()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o0, source: "Test");
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete && w.MissionId == MissionId));
        Assert.IsTrue(_fx.CountPackets<CompleteDynamicObjectivePacket>() >= 1);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void AdvanceOrComplete_AfterComplete_DoesNotThrow_AndDoesNotReAdd()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o0, source: "Test");
        _fx.PersistWrites.Clear();

        // Stale quest reference after removal — production callers should not re-enter, but
        // must not re-insert the mission into CurrentQuests or completed set twice incorrectly.
        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o0, source: "Test-Replay");

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, player.Character.CompletedMissionIds.Count(id => id == MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ForceComplete_Active_Completes_AndPersists()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        NpcInteractHandler.ForceCompleteMission(player.Connection, player.Character, MissionId);
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ForceComplete_WhenNotActive_NoOp()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();

        NpcInteractHandler.ForceCompleteMission(player.Connection, player.Character, MissionId);

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _fx.PersistWrites.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void KillProgress_Partial_DoesNotComplete()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 2);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
        Assert.AreEqual(1, player.Character.CurrentQuests[0].ObjectiveProgress[0]);
        MissionInvariantAssertions.AssertProgressAtMost(player.Character.CurrentQuests[0], 0, 2);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void KillProgress_AtThreshold_Completes()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 1);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void KillProgress_AfterComplete_DoesNotReopen()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 1);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var prop1 = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop1.SetMurderer(player.Vehicle);
        prop1.OnDeath(DeathType.Silent);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(MissionId));

        var prop2 = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop2.SetMurderer(player.Vehicle);
        prop2.OnDeath(DeathType.Silent);

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, player.Character.CompletedMissionIds.Count(id => id == MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PlayerIsolation_KillByA_DoesNotMutateB()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 1);
        _fx.SeedMission(MissionId, 0, o0);
        var a = _fx.CreatePlayer(characterCoid: 710001, vehicleCoid: 710002);
        var b = _fx.CreatePlayer(characterCoid: 710003, vehicleCoid: 710004);
        // Place B on same map instance as A for shared world, separate quest lists.
        b.Character.SetMap(a.Map);
        b.Vehicle.SetMap(a.Map);
        _fx.GiveQuest(a.Character, MissionId);
        _fx.GiveQuest(b.Character, MissionId);

        var prop = _fx.PlaceKillTarget(a.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(a.Vehicle);
        prop.OnDeath(DeathType.Silent);

        MissionInvariantAssertions.AssertCompleted(a.Character, MissionId);
        MissionInvariantAssertions.AssertActiveMission(b.Character, MissionId, 0);
        Assert.AreEqual(0, b.Character.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Template_NotMutated_ByGrantAndComplete()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 1);
        _fx.SeedMission(MissionId, 0, o0);
        var mission = AssetManager.Instance.GetMission(MissionId)!;
        var reqCount = mission.Objectives[0].Requirements.Count;
        var completeCount = mission.Objectives[0].CompleteCount;

        var player = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);
        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        MissionInvariantAssertions.AssertTemplateUnchanged(mission, ObjectiveA, reqCount, completeCount);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void LoadMissions_RestoresActiveAndCompleted_WithoutCrossTalk()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var otherMission = 96002;
        var oOther = _fx.CreateSimpleObjective(96103, 0, otherMission);
        _fx.SeedMission(otherMission, 0, oOther);

        var player = _fx.CreatePlayer();
        var progress = MissionPersistence.PackProgress(new[] { 3, 0 });
        _fx.LoadFromRows(
            player.Character,
            new[]
            {
                new CharacterQuestData
                {
                    CharacterCoid = player.Character.ObjectId.Coid,
                    MissionId = MissionId,
                    ActiveObjectiveSequence = 0,
                    State = 0,
                    ObjectiveProgress = progress,
                },
            },
            new[]
            {
                new CharacterCompletedMissionData
                {
                    CharacterCoid = player.Character.ObjectId.Coid,
                    MissionId = otherMission,
                },
            });

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
        Assert.AreEqual(3, player.Character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(otherMission));
        MissionInvariantAssertions.AssertNotActive(player.Character, otherMission);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FailMissionReaction_IsStub_DoesNotClearActiveQuest()
    {
        // Characterization of current IncompleteHandlerLog stub — not a fix request.
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.FailMission, MissionId);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void CompleteObjectiveReaction_WrongSequence_DoesNotAdvance()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        var o1 = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, o0, o1);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId); // seq 0 active

        // Fire complete for objective B while still on seq 0.
        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.CompleteObjective, ObjectiveB);
        Assert.IsFalse(reaction.TriggerIfPossible(player.Character));

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void CompleteObjectiveReaction_ActiveSequence_CompletesFinal()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.CompleteObjective, ObjectiveA);
        // Handler returns false to suppress 0x206C double-complete; still mutates server state.
        Assert.IsFalse(reaction.TriggerIfPossible(player.Character));

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
    }
}
