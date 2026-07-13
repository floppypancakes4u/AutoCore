using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Objectives;

using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Boundary contracts for objective progress matching (kill paths and multi-req characterization).
/// </summary>
[TestClass]
public class ObjectiveProgressContractTests
{
    private const int MissionId = 98501;
    private const int ObjectiveId = 98601;
    private const int TargetCbid = 98701;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionContract")]
    public void Kill_Boundary_NumToKill_NMinus1_N()
    {
        foreach (var n in new[] { 1, 2, 5 })
        {
            AssetManager.Instance.ClearTestMissions();
            var o0 = _fx.CreateKillObjective(ObjectiveId, 0, MissionId, TargetCbid, n);
            _fx.SeedMission(MissionId, 0, o0);
            var player = _fx.CreatePlayer(characterCoid: 850000 + n, vehicleCoid: 860000 + n);
            _fx.GiveQuest(player.Character, MissionId);

            for (var i = 0; i < n - 1; i++)
            {
                var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
                prop.SetMurderer(player.Vehicle);
                prop.OnDeath(DeathType.Silent);
                Assert.AreEqual(1, player.Character.CurrentQuests.Count, $"n={n} kill {i + 1}");
                Assert.AreEqual(i + 1, player.Character.CurrentQuests[0].ObjectiveProgress[0]);
            }

            var last = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
            last.SetMurderer(player.Vehicle);
            last.OnDeath(DeathType.Silent);
            MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        }
    }

    [TestMethod]
    [TestCategory("MissionContract")]
    public void Kill_WrongCbid_ZeroProgress()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveId, 0, MissionId, TargetCbid, 1);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), cbid: TargetCbid + 99);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(0, player.Character.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    [TestCategory("MissionContract")]
    public void MultiRequirement_SingleAdvance_CharacterizesIncompleteEvaluation()
    {
        // Documented incomplete: Advance treats multi-req as satisfied without evaluating each.
        var o0 = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        o0.Requirements.Add(new ObjectiveRequirementKill(o0) { TargetCBID = TargetCbid, NumToKill = 5 });
        o0.Requirements.Add(new ObjectiveRequirementDeliver(o0)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
            RequireItemToComplete = false,
        });
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o0, source: "MultiReqChar");

        // Characterization of current behavior (IncompleteHandlerLog path).
        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
    }
}
