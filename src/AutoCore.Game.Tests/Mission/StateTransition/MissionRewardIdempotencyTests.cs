using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.StateTransition;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Tests.Experience.Fakes;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Reward safety through production complete paths — exactly-once logical grants on replay.
/// </summary>
[TestClass]
public class MissionRewardIdempotencyTests
{
    private const int MissionId = 96301;
    private const int ObjectiveId = 96401;

    private MissionTestFixture _fx = null!;
    private RecordingProgressPersistence _progress = null!;

    [TestInitialize]
    public void SetUp()
    {
        _fx = new MissionTestFixture();
        _progress = new RecordingProgressPersistence();
        var svc = ExperienceService.Instance;
        svc.Persistence = _progress;
        svc.PersistOnGrant = true;
        svc.SendPacketsOnGrant = false;
        svc.ResolveThreshold = ExperienceService.DefaultRetailThreshold;
        svc.ResolveQuestFrac = ExperienceService.DefaultQuestFrac;
        svc.ResolveQuestCreditsFrac = ExperienceService.DefaultQuestCreditsFrac;
        svc.ResolveQuestBaseCredits = ExperienceService.DefaultQuestBaseCredits;
        svc.ResolveLevelRow = level => new ExperienceLevel
        {
            Level = level,
            Experience = ExperienceService.DefaultRetailThreshold(level),
            SkillPoints = 1,
            AttributePoints = 2,
            ResearchPoints = 0,
        };
    }

    [TestCleanup]
    public void TearDown()
    {
        ExperienceService.Instance.ResetForTests();
        _fx.Dispose();
    }

    private void SeedMissionWithXpIndex(short xpIndex = 1, short targetLevel = 5)
    {
        var objective = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        // XPIndex/scalers are private-set; use reflection like ApplyMissionCompleteRewardsTests.
        typeof(MissionObjective).GetProperty(nameof(MissionObjective.XPIndex))!
            .SetValue(objective, xpIndex);
        typeof(MissionObjective).GetProperty(nameof(MissionObjective.XPScaler))!
            .SetValue(objective, 1f);
        typeof(MissionObjective).GetProperty(nameof(MissionObjective.XPBalanceScaler))!
            .SetValue(objective, 1f);

        var mission = Mission.CreateForTests(MissionId, objective);
        mission.IsRepeatable = 0;
        mission.TargetLevel = targetLevel;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Complete_ThenReplayStaleAdvance_DoesNotGrantXpTwice()
    {
        SeedMissionWithXpIndex();
        var player = _fx.CreatePlayer();
        // GiveXp requires character progress attachment used by ExperienceService.
        player.Character.AttachTestDataForTests($"RewardIdem{player.Character.ObjectId.Coid}");
        player.Character.SetExperience(0);
        player.Character.SetLevel(1);

        _fx.GiveQuest(player.Character, MissionId);
        var quest = player.Character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(MissionId)!;
        var objective = mission.Objectives[0];

        var xpBefore = player.Character.Experience;
        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, objective, source: "RewardOnce");
        var xpAfterFirst = player.Character.Experience;
        Assert.IsTrue(xpAfterFirst > xpBefore, "First complete should grant mission XP");

        // Replay with stale quest reference after removal.
        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, objective, source: "RewardReplay");

        Assert.AreEqual(xpAfterFirst, player.Character.Experience,
            "Replay must not grant additional XP after mission already completed");
        Assert.AreEqual(1, player.Character.CompletedMissionIds.Count(id => id == MissionId));
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ForceComplete_WhenNotActive_DoesNotGrantXp()
    {
        SeedMissionWithXpIndex();
        var player = _fx.CreatePlayer();
        player.Character.AttachTestDataForTests($"RewardNone{player.Character.ObjectId.Coid}");
        player.Character.SetExperience(100);
        player.Character.SetLevel(2);

        NpcInteractHandler.ForceCompleteMission(player.Connection, player.Character, MissionId);

        Assert.AreEqual(100, player.Character.Experience);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
    }
}
