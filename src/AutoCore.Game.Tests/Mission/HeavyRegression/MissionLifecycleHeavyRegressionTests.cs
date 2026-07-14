using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Chat;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy regression: grant / fail / complete / remove / show mission lifecycle and chat commands.
/// </summary>
[TestClass]
public class MissionLifecycleHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;
    private const int Mid = 94100;
    private const int O0 = 95100;
    private const long P0 = 96100;

    [TestInitialize]
    public void SetUp() => _fx = new MissionHeavyRegressionFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    // --- GrantMission (5+) ---

    [TestMethod]
    public void Grant_AddsActiveQuest()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
    }

    [TestMethod]
    public void Grant_UnknownId_StillAddsQuestShell()
    {
        // GrantMission is optimistic for chat/diag; asset may be missing.
        var (conn, ch, _, _) = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(conn, ch, 99999991);
        Assert.AreEqual(1, ch.CurrentQuests.Count);
        Assert.AreEqual(99999991, ch.CurrentQuests[0].MissionId);
    }

    [TestMethod]
    public void Grant_Duplicate_StaysSingleActive()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        Assert.AreEqual(1, ch.CurrentQuests.Count(q => q.MissionId == Mid));
    }

    [TestMethod]
    public void Grant_ChatGiveMission_Grants()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        var r = ChatCommandService.Instance.Execute(ch, $"/giveMission {Mid}");
        Assert.IsTrue(r.Handled);
        StringAssert.Contains(r.Message, "Granted");
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
    }

    [TestMethod]
    public void Grant_ChatUnknown_ReportsUnknown()
    {
        var (_, ch, _, _) = _fx.CreatePlayer();
        var r = ChatCommandService.Instance.Execute(ch, "/giveMission 99999992");
        Assert.IsTrue(r.Handled);
        StringAssert.Contains(r.Message, "Unknown");
    }

    [TestMethod]
    public void Grant_PersistsUpsert()
    {
        SeedSimple();
        var writes = new List<(long Coid, int MissionId, QuestPersistKind Kind)>();
        MissionPersistence.Instance.PersistQuestRow = (c, m, op) => writes.Add((c, m, op.Kind));
        MissionPersistence.Instance.AutoFlushOnEnqueue = true;
        var (conn, ch, _, _) = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        MissionPersistence.Instance.FlushPending();
        Assert.IsTrue(writes.Any(w => w.MissionId == Mid && w.Kind == QuestPersistKind.Upsert));
    }

    // --- FailMission / abandon (5+) ---

    [TestMethod]
    public void Fail_RemovesActive()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        NpcInteractHandler.FailMission(conn, ch, Mid);
        MissionInvariantAssertions.AssertNotActive(ch, Mid);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Fail_SendsFailMissionPacket()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        NpcInteractHandler.FailMission(conn, ch, Mid);
        Assert.IsTrue(_fx.Sent.OfType<FailMissionPacket>().Any(p => p.MissionId == Mid));
    }

    [TestMethod]
    public void Fail_NotActive_NoOp()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        NpcInteractHandler.FailMission(conn, ch, Mid);
        Assert.AreEqual(0, ch.CurrentQuests.Count);
    }

    [TestMethod]
    public void Fail_DoesNotTouchOtherMissions()
    {
        SeedSimple();
        var other = Mid + 1;
        SeedSimple(other, O0 + 1);
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        MissionHeavyRegressionFixture.GiveQuest(ch, other);
        NpcInteractHandler.FailMission(conn, ch, Mid);
        MissionInvariantAssertions.AssertActiveMission(ch, other, 0);
    }

    [TestMethod]
    public void Fail_PersistsRemove()
    {
        SeedSimple();
        var kinds = new List<QuestPersistKind>();
        MissionPersistence.Instance.AutoFlushOnEnqueue = false;
        MissionPersistence.Instance.PersistQuestRow = (_, _, op) => kinds.Add(op.Kind);
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        NpcInteractHandler.FailMission(conn, ch, Mid);
        Assert.IsTrue(MissionPersistence.Instance.FlushPending() >= 1
            || kinds.Contains(QuestPersistKind.Remove));
        Assert.IsTrue(kinds.Contains(QuestPersistKind.Remove),
            "expected Remove op, got: " + string.Join(",", kinds));
    }

    // --- ForceComplete / completeMission (5+) ---

    [TestMethod]
    public void Complete_Active_MovesToCompleted()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        NpcInteractHandler.ForceCompleteMission(conn, ch, Mid);
        MissionInvariantAssertions.AssertCompleted(ch, Mid);
    }

    [TestMethod]
    public void Complete_ChatCommand_Completes()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        var r = ChatCommandService.Instance.Execute(ch, $"/completeMission {Mid}");
        Assert.IsTrue(r.Handled);
        StringAssert.Contains(r.Message, "Completed");
        MissionInvariantAssertions.AssertCompleted(ch, Mid);
    }

    [TestMethod]
    public void Complete_NotActive_ReportsNotActive()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        var r = ChatCommandService.Instance.Execute(ch, $"/completeMission {Mid}");
        StringAssert.Contains(r.Message, "not active");
    }

    [TestMethod]
    public void Complete_AlreadyCompleted_ReportsAlready()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        ch.CompletedMissionIds.Add(Mid);
        var r = ChatCommandService.Instance.Execute(ch, $"/completeMission {Mid}");
        StringAssert.Contains(r.Message, "already completed");
    }

    [TestMethod]
    public void Complete_Sends0x2070()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        NpcInteractHandler.ForceCompleteMission(conn, ch, Mid);
        Assert.IsTrue(_fx.Sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.MissionId == Mid));
    }

    // --- removeMission / clearAll / removeCurrent (5+) ---

    [TestMethod]
    public void RemoveMission_Active_Abandons()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        var r = ChatCommandService.Instance.Execute(ch, $"/removeMission {Mid}");
        Assert.IsTrue(r.Handled);
        MissionInvariantAssertions.AssertNotActive(ch, Mid);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void RemoveMission_Completed_ClearsCompleted()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        ch.CompletedMissionIds.Add(Mid);
        var r = ChatCommandService.Instance.Execute(ch, $"/removeMission {Mid}");
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        StringAssert.Contains(r.Message, "Removed");
    }

    [TestMethod]
    public void RemoveMission_Both_ClearsBoth()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        ch.CompletedMissionIds.Add(Mid);
        ChatCommandService.Instance.Execute(ch, $"/removeMission {Mid}");
        Assert.AreEqual(0, ch.CurrentQuests.Count);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void RemoveMission_NotFound_Message()
    {
        var (_, ch, _, _) = _fx.CreatePlayer();
        var r = ChatCommandService.Instance.Execute(ch, $"/removeMission {Mid}");
        StringAssert.Contains(r.Message, "not found");
    }

    [TestMethod]
    public void ClearAll_WipesActiveAndCompleted()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        ch.CompletedMissionIds.Add(Mid + 5);
        long? deleted = null;
        MissionPersistence.Instance.DeleteAllRows = c => deleted = c;
        ChatCommandService.Instance.Execute(ch, "/clearAllMissions");
        Assert.AreEqual(0, ch.CurrentQuests.Count);
        Assert.AreEqual(0, ch.CompletedMissionIds.Count);
        Assert.AreEqual(ch.ObjectId.Coid, deleted);
    }

    [TestMethod]
    public void RemoveCurrent_ClearsActive_KeepsCompleted()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        ch.CompletedMissionIds.Add(777);
        MissionPersistence.Instance.DeleteActiveRows = _ => { };
        ChatCommandService.Instance.Execute(ch, "/removeCurrentMission");
        Assert.AreEqual(0, ch.CurrentQuests.Count);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(777));
    }

    // --- showMissions / AdvanceOrComplete sequence (5+) ---

    [TestMethod]
    public void ShowMissions_ReportsActiveAndCompleted()
    {
        SeedSimple();
        var (_, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        ch.CompletedMissionIds.Add(100);
        var r = ChatCommandService.Instance.Execute(ch, "/showMissions");
        StringAssert.Contains(r.Message, Mid.ToString());
        StringAssert.Contains(r.Message, "100");
    }

    [TestMethod]
    public void Advance_Intermediate_KeepsQuest()
    {
        SeedTwoSeq();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        var mission = AssetManager.Instance.GetMission(Mid);
        var obj0 = mission.Objectives[0];
        NpcInteractHandler.AdvanceOrCompleteObjective(conn, ch, ch.CurrentQuests[0], mission, obj0, "Test");
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 1);
    }

    [TestMethod]
    public void Advance_Final_CompletesMission()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        var mission = AssetManager.Instance.GetMission(Mid);
        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn, ch, ch.CurrentQuests[0], mission, mission.Objectives[0], "Test");
        MissionInvariantAssertions.AssertCompleted(ch, Mid);
    }

    [TestMethod]
    public void Advance_Sends0x2070ByDefault()
    {
        SeedTwoSeq();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        var mission = AssetManager.Instance.GetMission(Mid);
        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn, ch, ch.CurrentQuests[0], mission, mission.Objectives[0], "Test");
        Assert.IsTrue(_fx.Sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    [TestMethod]
    public void Advance_Skip0x2070_WhenRequested()
    {
        SeedTwoSeq();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        var mission = AssetManager.Instance.GetMission(Mid);
        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn, ch, ch.CurrentQuests[0], mission, mission.Objectives[0], "Test",
            sendCompleteDynamicObjective: false);
        Assert.AreEqual(0, _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Count());
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
    }

    [TestMethod]
    public void Advance_StaleQuest_Ignored()
    {
        SeedSimple();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        var quest = ch.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(Mid);
        ch.CurrentQuests.Remove(quest);
        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn, ch, quest, mission, mission.Objectives[0], "Test");
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void NullCharacter_ChatCommands_Safe()
    {
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/showMissions").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, $"/giveMission {Mid}").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, $"/removeMission {Mid}").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, $"/completeMission {Mid}").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/clearAllMissions").Handled);
    }

    private void SeedSimple(int missionId = Mid, int objId = O0)
    {
        var obj = MissionObjective.CreateForTests(objId, 0, missionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = P0;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, obj));
    }

    private void SeedTwoSeq()
    {
        var o0 = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        o0.Requirements.Add(new ObjectiveRequirementPatrol(o0) { AutoComplete = true, TargetCount = 1 });
        ((ObjectiveRequirementPatrol)o0.Requirements[0]).GenericTargets[0] = P0;
        var o1 = MissionObjective.CreateForTests(O0 + 1, 1, Mid, 1);
        o1.Requirements.Add(new ObjectiveRequirementDeliver(o1)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, o0, o1));
    }
}
