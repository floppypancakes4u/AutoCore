using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Tests.Experience.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameMission = AutoCore.Game.Mission.Mission;
using GameMissionObjective = AutoCore.Game.Mission.MissionObjective;

namespace AutoCore.Game.Tests.Experience;

[TestClass]
public class ExperienceServiceTests
{
    private ExperienceService _svc = null!;
    private RecordingProgressPersistence _persist = null!;

    [TestInitialize]
    public void Init()
    {
        _svc = ExperienceService.Instance;
        _svc.ResetForTests();
        _persist = new RecordingProgressPersistence();
        _svc.Persistence = _persist;
        _svc.PersistOnGrant = true;
        _svc.SendPacketsOnGrant = false;
        _svc.ResolveThreshold = ExperienceService.DefaultRetailThreshold;
        _svc.ResolveLevelRow = level => new ExperienceLevel
        {
            Level = level,
            Experience = ExperienceService.DefaultRetailThreshold(level),
            SkillPoints = 1,
            AttributePoints = 2,
            ResearchPoints = level == 5 ? (byte)3 : (byte)0
        };
        _svc.ResolveCreatureXp = ExperienceService.DefaultCreatureXp;
        _svc.ResolveQuestFrac = ExperienceService.DefaultQuestFrac;
        _svc.ResolveQuestCreditsFrac = ExperienceService.DefaultQuestCreditsFrac;
        _svc.ResolveQuestBaseCredits = ExperienceService.DefaultQuestBaseCredits;
    }

    [TestCleanup]
    public void Cleanup() => _svc.ResetForTests();

    private static Character MakeCharacter(long coid, int xp = 0, byte level = 1)
    {
        var c = new Character();
        c.SetCoid(coid, true);
        c.AttachTestDataForTests($"Char{coid}");
        c.SetExperience(xp);
        c.SetLevel(level);
        return c;
    }

    [TestMethod]
    public void ComputeKillXp_SameLevel1_Is39()
    {
        Assert.AreEqual(39, _svc.ComputeKillXp(1, 1, xpPercent: 1f));
    }

    [TestMethod]
    public void ComputeKillXp_GreyDiff_IsZero()
    {
        Assert.AreEqual(0, _svc.ComputeKillXp(12, 1, xpPercent: 1f));
    }

    [TestMethod]
    public void ComputeMissionXp_TargetLevel5_Index5_Is320()
    {
        var mission = GameMission.CreateForTests(100);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 100);
        SetObjectiveXpFields(objective, xpIndex: 5, xpScaler: 1f, balance: 1f);

        Assert.AreEqual(320, _svc.ComputeMissionXp(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_TargetLevel2_Index4_Is8()
    {
        // base=10, frac=0.8, scaler=1 → ceil(8)=8
        var mission = GameMission.CreateForTests(101);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 101);
        SetObjectiveCreditFields(objective, creditsIndex: 4, creditScaler: 1f);

        Assert.AreEqual(8, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_NullArgs_ReturnsZero()
    {
        Assert.AreEqual(0, _svc.ComputeMissionCredits(null, null));
        var mission = GameMission.CreateForTests(102);
        Assert.AreEqual(0, _svc.ComputeMissionCredits(mission, null));
    }

    [TestMethod]
    public void ComputeMissionCredits_StaticFallback_WhenIndexZero()
    {
        var mission = GameMission.CreateForTests(103);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 103);
        SetObjectiveCreditFields(objective, creditsIndex: 0, creditScaler: 1f, staticCredits: 42);

        Assert.AreEqual(42, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_ZeroFracNoStatic_ReturnsZero()
    {
        var mission = GameMission.CreateForTests(104);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 104);
        SetObjectiveCreditFields(objective, creditsIndex: 0, creditScaler: 1f, staticCredits: 0);

        Assert.AreEqual(0, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_CreditScalerMultiplies()
    {
        // base 10 * frac 0.8 * scaler 1.5 = 12
        var mission = GameMission.CreateForTests(105);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 105);
        SetObjectiveCreditFields(objective, creditsIndex: 4, creditScaler: 1.5f);

        Assert.AreEqual(12, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_CeilFractionalProduct()
    {
        // base 10 * frac 0.2 * scaler 1.1 = 2.2 → ceil 3 (client FUN_0059DF20)
        var mission = GameMission.CreateForTests(106);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 106);
        SetObjectiveCreditFields(objective, creditsIndex: 1, creditScaler: 1.1f);

        Assert.AreEqual(3, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_TargetLevelBelowOne_UsesLevelOneBase()
    {
        // TargetLevel 0 clamps to 1 → base 3 * frac 1.0 (index 5) = 3
        var mission = GameMission.CreateForTests(107);
        mission.TargetLevel = 0;
        var objective = GameMissionObjective.CreateForTests(1, 0, 107);
        SetObjectiveCreditFields(objective, creditsIndex: 5, creditScaler: 1f);

        Assert.AreEqual(3, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_ZeroBase_ReturnsZero()
    {
        _svc.ResolveQuestBaseCredits = _ => 0;
        var mission = GameMission.CreateForTests(108);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 108);
        SetObjectiveCreditFields(objective, creditsIndex: 5, creditScaler: 1f);

        Assert.AreEqual(0, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionCredits_NegativeScaler_ReturnsZero()
    {
        var mission = GameMission.CreateForTests(109);
        mission.TargetLevel = 2;
        var objective = GameMissionObjective.CreateForTests(1, 0, 109);
        SetObjectiveCreditFields(objective, creditsIndex: 4, creditScaler: -1f);

        Assert.AreEqual(0, _svc.ComputeMissionCredits(mission, objective));
    }

    [TestMethod]
    public void ComputeAreaXp_Level1_Is39()
    {
        _svc.ResolveAreaXpLevel = (_, _) => 1;
        Assert.AreEqual(39, _svc.ComputeAreaXp(691, 1));
    }

    [TestMethod]
    public void GiveXp_CrossesLevel1Threshold_LevelsTo2()
    {
        var character = MakeCharacter(1001, xp: 990, level: 1);
        var result = _svc.GiveXp(character, 20, XpSource.Admin);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(20, result.AppliedAmount);
        Assert.AreEqual(1010, result.TotalExperience);
        Assert.AreEqual(2, result.Level);
        Assert.IsTrue(result.Leveled);
        Assert.AreEqual(1, character.SkillPoints); // L2 row grants 1 skill
        Assert.AreEqual(2, character.AttributePoints);
        Assert.AreEqual(1, _persist.Saves.Count);
        Assert.AreEqual(1010, _persist.Saves[0].Progress.Experience);
        Assert.AreEqual(2, _persist.Saves[0].Progress.Level);
        // Always absolute CharacterLevel after grant (login/client bar sync).
        Assert.IsNotNull(result.CharacterLevelPacket);
        Assert.AreEqual(1010, result.CharacterLevelPacket.Experience);
        Assert.AreEqual(2, result.CharacterLevelPacket.Level);
    }

    [TestMethod]
    public void GiveXp_NonLevelUp_StillBuildsCharacterLevelPacket()
    {
        var character = MakeCharacter(1005, xp: 100, level: 1);
        var result = _svc.GiveXp(character, 50, XpSource.Kill);
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.CharacterLevelPacket);
        Assert.AreEqual(150, result.CharacterLevelPacket.Experience);
        Assert.AreEqual(1, result.CharacterLevelPacket.Level);
    }

    [TestMethod]
    public void GiveXp_NotifyClientFalse_WhenLevels_StillBuildsCharacterLevel_NoGiveXpPacket()
    {
        // L1 needs 1000 XP to reach L2; start at 990, grant 20 → level 2.
        var character = MakeCharacter(1006, xp: 990, level: 1);
        var result = _svc.GiveXp(
            character,
            20,
            XpSource.Mission,
            levelHint: -1,
            notifyClient: false);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Leveled);
        Assert.AreEqual(2, result.Level);
        Assert.IsNull(result.GiveXpPacket, "dialog-style path must not re-send XP delta");
        Assert.IsNotNull(result.CharacterLevelPacket, "must still push absolute level on level-up");
        Assert.AreEqual(2, result.CharacterLevelPacket.Level);
        Assert.AreEqual(1010, result.CharacterLevelPacket.Experience);
    }

    [TestMethod]
    public void GiveXp_NotifyClientFalse_NoLevelUp_NoCharacterLevelPacket()
    {
        var character = MakeCharacter(1007, xp: 100, level: 1);
        var result = _svc.GiveXp(character, 50, XpSource.Mission, notifyClient: false);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Leveled);
        Assert.IsNull(result.GiveXpPacket);
        Assert.IsNull(result.CharacterLevelPacket);
        Assert.AreEqual(150, character.Experience);
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_LoadsFromPersistence()
    {
        _persist.Store[2001] = new CharacterProgressSnapshot(5, 9000, 3, 4, 1, 10, 11, 12, 13);
        var character = MakeCharacter(2001, xp: 0, level: 1);
        var packet = _svc.TryCreateLoginRestorePacket(character, _persist);
        Assert.IsNotNull(packet);
        Assert.AreEqual(5, character.Level);
        Assert.AreEqual(9000, character.Experience);
        Assert.AreEqual(9000, packet.Experience);
        Assert.AreEqual(5, packet.Level);
        Assert.AreEqual((short)10, character.AttributeTech);
        Assert.AreEqual((short)11, character.AttributeCombat);
        Assert.AreEqual((short)12, character.AttributeTheory);
        Assert.AreEqual((short)13, character.AttributePerception);
        Assert.AreEqual((short)10, packet.AttributeTech);
        Assert.AreEqual((short)11, packet.AttributeCombat);
        Assert.AreEqual((short)12, packet.AttributeTheory);
        Assert.AreEqual((short)13, packet.AttributePerception);
    }

    [TestMethod]
    public void GiveXp_PersistFailure_DoesNotReportSuccess()
    {
        _persist.ThrowOnSave = true;
        var character = MakeCharacter(1002, xp: 0, level: 1);
        var result = _svc.GiveXp(character, 50, XpSource.Kill);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("Persist"));
    }

    [TestMethod]
    public void GiveXp_ZeroAmount_SucceedsNoSave()
    {
        var character = MakeCharacter(1003);
        var result = _svc.GiveXp(character, 0, XpSource.Other);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, _persist.Saves.Count);
    }

    [TestMethod]
    public void LevelSpan_Level5_Is3200()
    {
        Assert.AreEqual(3200, _svc.LevelSpan(5));
    }

    [TestMethod]
    public void LevelDiffBase_EasyKill_ReducesXp()
    {
        var same = _svc.LevelDiffBase(1, 1);
        var easier = _svc.LevelDiffBase(3, 1);
        Assert.IsTrue(easier < same);
        Assert.IsTrue(easier > 0);
    }

    private static void SetObjectiveXpFields(GameMissionObjective objective, short xpIndex, float xpScaler, float balance)
    {
        var t = typeof(GameMissionObjective);
        t.GetProperty(nameof(GameMissionObjective.XPIndex))!.SetValue(objective, xpIndex);
        t.GetProperty(nameof(GameMissionObjective.XPScaler))!.SetValue(objective, xpScaler);
        t.GetProperty(nameof(GameMissionObjective.XPBalanceScaler))!.SetValue(objective, balance);
    }

    private static void SetObjectiveCreditFields(
        GameMissionObjective objective,
        short creditsIndex,
        float creditScaler,
        int staticCredits = 0)
    {
        var t = typeof(GameMissionObjective);
        t.GetProperty(nameof(GameMissionObjective.CreditsIndex))!.SetValue(objective, creditsIndex);
        t.GetProperty(nameof(GameMissionObjective.CreditScaler))!.SetValue(objective, creditScaler);
        t.GetProperty(nameof(GameMissionObjective.Credits))!.SetValue(objective, staticCredits);
    }
}
