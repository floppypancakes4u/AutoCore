using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Chat;

using AutoCore.Game.Chat;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Diagnostic chat commands that mutate mission state — isolation and persistence side effects.
/// </summary>
[TestClass]
public class MissionChatCommandContractTests
{
    private const int MissionId = 98801;
    private const int ObjectiveId = 98901;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveMission_GrantsActive_AndShowMissionsReportsIt()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();

        var result = ChatCommandService.Instance.Execute(player.Character, $"/giveMission {MissionId}");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Granted");
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);

        var show = ChatCommandService.Instance.Execute(player.Character, "/showMissions");
        StringAssert.Contains(show.Message, MissionId.ToString());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveMission_UnknownId_DoesNotGrant()
    {
        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, "/giveMission 99999999");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Unknown");
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void CompleteMission_Active_CompletesAndPersists()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var result = ChatCommandService.Instance.Execute(player.Character, $"/completeMission {MissionId}");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Completed");
        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        _fx.FlushPersist();
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void CompleteMission_NotActive_NoOp()
    {
        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/completeMission {MissionId}");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "not active");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ClearAllMissions_WipesActiveAndCompleted_InvokesDelete()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        player.Character.CompletedMissionIds.Add(MissionId + 1);

        long? deleted = null;
        MissionPersistence.Instance.DeleteAllRows = coid => deleted = coid;

        var result = ChatCommandService.Instance.Execute(player.Character, "/clearAllMissions");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.AreEqual(0, player.Character.CompletedMissionIds.Count);
        Assert.AreEqual(player.Character.ObjectId.Coid, deleted);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void RemoveCurrentMission_ClearsActive_PreservesCompleted()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        player.Character.CompletedMissionIds.Add(777);

        long? deletedActive = null;
        MissionPersistence.Instance.DeleteActiveRows = coid => deletedActive = coid;

        var result = ChatCommandService.Instance.Execute(player.Character, "/removeCurrentMission");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(777));
        Assert.AreEqual(player.Character.ObjectId.Coid, deletedActive);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MissionCommands_NullCharacter_Safe()
    {
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/showMissions").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/clearAllMissions").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/giveMission 1").Handled);
        Assert.IsTrue(ChatCommandService.Instance.Execute(null, "/completeMission 1").Handled);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PlayerIsolation_ClearAll_OnlyAffectsCaller()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var a = _fx.CreatePlayer(characterCoid: 730001, vehicleCoid: 730002);
        var b = _fx.CreatePlayer(characterCoid: 730003, vehicleCoid: 730004);
        _fx.GiveQuest(a.Character, MissionId);
        _fx.GiveQuest(b.Character, MissionId);

        ChatCommandService.Instance.Execute(a.Character, "/clearAllMissions");

        Assert.AreEqual(0, a.Character.CurrentQuests.Count);
        MissionInvariantAssertions.AssertActiveMission(b.Character, MissionId, 0);
    }
}
