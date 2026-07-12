using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Experience;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Experience.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameMission = AutoCore.Game.Mission.Mission;
using GameMissionObjective = AutoCore.Game.Mission.MissionObjective;

namespace AutoCore.Game.Tests.Experience;

/// <summary>
/// Heavy regression coverage for ExperienceService formulas, grants, caps, login, and edge paths (docs/XP.md).
/// </summary>
[TestClass]
public class ExperienceServiceRegressionTests
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

    private static void SetObjectiveXpFields(
        GameMissionObjective objective,
        short xpIndex,
        float xpScaler,
        float balance,
        int staticXp = 0,
        int skillPoints = 0,
        int attribPoints = 0)
    {
        var t = typeof(GameMissionObjective);
        t.GetProperty(nameof(GameMissionObjective.XPIndex))!.SetValue(objective, xpIndex);
        t.GetProperty(nameof(GameMissionObjective.XPScaler))!.SetValue(objective, xpScaler);
        t.GetProperty(nameof(GameMissionObjective.XPBalanceScaler))!.SetValue(objective, balance);
        t.GetProperty(nameof(GameMissionObjective.XP))!.SetValue(objective, staticXp);
        t.GetProperty(nameof(GameMissionObjective.SkillPoints))!.SetValue(objective, skillPoints);
        t.GetProperty(nameof(GameMissionObjective.AttribPoints))!.SetValue(objective, attribPoints);
    }

    // --- Kill formula regression (docs/XP.md) ---

    [TestMethod]
    public void ComputeKillXp_ParticipationZero_ReturnsZero()
    {
        Assert.AreEqual(0, _svc.ComputeKillXp(5, 5, xpPercent: 1f, participation: 0f));
    }

    [TestMethod]
    public void ComputeKillXp_XpPercentZero_ReturnsZero()
    {
        Assert.AreEqual(0, _svc.ComputeKillXp(5, 5, xpPercent: 0f));
    }

    [TestMethod]
    public void ComputeKillXp_HardKill_BoostsAboveSameLevel()
    {
        var same = _svc.ComputeKillXp(5, 5);
        var hard = _svc.ComputeKillXp(5, 8);
        Assert.IsTrue(hard > same, $"hard={hard} same={same}");
    }

    [TestMethod]
    public void ComputeKillXp_VictimMoreThan3Above_IsClamped()
    {
        // Prep clamp: v = p+3 when v-p > 3
        var atCap = _svc.LevelDiffBase(5, 8);
        var beyond = _svc.LevelDiffBase(5, 20);
        Assert.AreEqual(atCap, beyond);
    }

    [TestMethod]
    public void ComputeKillXp_HardDiffBeyond9_IsClamped()
    {
        // hardDiff min -9 for bonus calc; table still uses victim level after prep clamp
        var a = _svc.LevelDiffBase(10, 13); // prep clamp to 13
        Assert.IsTrue(a > 0);
    }

    [TestMethod]
    public void ComputeKillXp_SpreeStacks_AddBonus()
    {
        var baseXp = _svc.ComputeKillXp(1, 1, spree: 0);
        var spreeXp = _svc.ComputeKillXp(1, 1, spree: 5);
        Assert.AreEqual(baseXp, _svc.ComputeKillXp(1, 1, spree: 1), "spree<=1 is no bonus");
        Assert.IsTrue(spreeXp > baseXp, $"spree={spreeXp} base={baseXp}");
    }

    [TestMethod]
    public void ComputeKillXp_ConvoySplit_ReducesPerPlayerShare()
    {
        var solo = _svc.ComputeKillXp(1, 1, convoyCount: 0);
        var convoy = _svc.ComputeKillXp(1, 1, convoyCount: 4);
        Assert.IsTrue(convoy > 0);
        Assert.IsTrue(convoy <= solo, $"convoy={convoy} solo={solo}");
    }

    [TestMethod]
    public void LevelDiffBase_EasyKill_GreyPath_ReducesFromBase()
    {
        var baseXp = _svc.LevelDiffBase(5, 5);
        var easy1 = _svc.LevelDiffBase(6, 5);
        var easy3 = _svc.LevelDiffBase(8, 5); // mild easy still awards
        var grey = _svc.LevelDiffBase(15, 5); // diff 10 = grey
        Assert.IsTrue(easy1 < baseXp && easy1 > 0);
        Assert.IsTrue(easy3 < easy1 && easy3 > 0);
        Assert.AreEqual(0, grey);
        // Large easy reduction can floor to 0 before grey cutoff
        Assert.AreEqual(0, _svc.LevelDiffBase(14, 5));
    }

    [TestMethod]
    public void LevelDiffBase_CreatureXpZero_ReturnsZero()
    {
        _svc.ResolveCreatureXp = _ => 0;
        Assert.AreEqual(0, _svc.LevelDiffBase(5, 5));
        Assert.AreEqual(0, _svc.LevelDiffBase(5, 8));
    }

    [TestMethod]
    public void DefaultCreatureXp_TableSamples()
    {
        Assert.AreEqual(38, ExperienceService.DefaultCreatureXp(0));
        Assert.AreEqual(39, ExperienceService.DefaultCreatureXp(1));
        Assert.AreEqual(112, ExperienceService.DefaultCreatureXp(20));
        Assert.AreEqual(263, ExperienceService.DefaultCreatureXp(50));
        Assert.AreEqual(38 + 99, ExperienceService.DefaultCreatureXp(99));
    }

    // --- Mission formula ---

    [TestMethod]
    public void ComputeMissionXp_NullArgs_ReturnsZero()
    {
        Assert.AreEqual(0, _svc.ComputeMissionXp(null, null));
        var mission = GameMission.CreateForTests(1);
        Assert.AreEqual(0, _svc.ComputeMissionXp(mission, null));
    }

    [TestMethod]
    public void ComputeMissionXp_ZeroFrac_UsesStaticXpFallbackWhenIndexZero()
    {
        var mission = GameMission.CreateForTests(10);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 10);
        SetObjectiveXpFields(objective, xpIndex: 0, xpScaler: 1f, balance: 1f, staticXp: 77);
        Assert.AreEqual(77, _svc.ComputeMissionXp(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionXp_ZeroFracNoStatic_ReturnsZero()
    {
        var mission = GameMission.CreateForTests(11);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 11);
        SetObjectiveXpFields(objective, xpIndex: 0, xpScaler: 1f, balance: 1f, staticXp: 0);
        Assert.AreEqual(0, _svc.ComputeMissionXp(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionXp_ZeroSpanMult_ReturnsZero()
    {
        var mission = GameMission.CreateForTests(12);
        mission.TargetLevel = 5;
        var objective = GameMissionObjective.CreateForTests(1, 0, 12);
        SetObjectiveXpFields(objective, xpIndex: 5, xpScaler: 0f, balance: 1f);
        Assert.AreEqual(0, _svc.ComputeMissionXp(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionXp_TargetLevelBelowOne_ClampsToOne()
    {
        var mission = GameMission.CreateForTests(13);
        mission.TargetLevel = 0;
        var objective = GameMissionObjective.CreateForTests(1, 0, 13);
        // span(1)=1000, index 5 = 0.10 → 100
        SetObjectiveXpFields(objective, xpIndex: 5, xpScaler: 1f, balance: 1f);
        Assert.AreEqual(100, _svc.ComputeMissionXp(mission, objective));
    }

    [TestMethod]
    public void ComputeMissionXp_AllQuestFracIndexes()
    {
        var mission = GameMission.CreateForTests(14);
        mission.TargetLevel = 1; // span 1000
        for (short i = 1; i <= 9; i++)
        {
            var objective = GameMissionObjective.CreateForTests(i, 0, 14);
            SetObjectiveXpFields(objective, xpIndex: i, xpScaler: 1f, balance: 1f);
            var expected = (int)Math.Floor(1000 * ExperienceService.DefaultQuestFrac(i) + ExperienceService.MissionRoundBias);
            Assert.AreEqual(expected, _svc.ComputeMissionXp(mission, objective), $"index {i}");
        }
        Assert.AreEqual(0f, ExperienceService.DefaultQuestFrac(99));
    }

    [TestMethod]
    public void LevelSpan_Edges()
    {
        Assert.AreEqual(1000, _svc.LevelSpan(1));
        // level 0 is treated like level 1 (span uses threshold(1))
        Assert.AreEqual(1000, _svc.LevelSpan(0));
        Assert.AreEqual(2300, _svc.LevelSpan(2)); // 3300-1000
        Assert.AreEqual(3200, _svc.LevelSpan(5));
    }

    [TestMethod]
    public void DefaultRetailThreshold_SamplesAndFallback()
    {
        Assert.AreEqual(0u, ExperienceService.DefaultRetailThreshold(0));
        Assert.AreEqual(1000u, ExperienceService.DefaultRetailThreshold(1));
        Assert.AreEqual(39000u, ExperienceService.DefaultRetailThreshold(10));
        Assert.AreEqual((uint)(1000 + (11 - 1) * 2500), ExperienceService.DefaultRetailThreshold(11));
    }

    // --- Area XP ---

    [TestMethod]
    public void ComputeAreaXp_ZeroLevel_ReturnsZero()
    {
        _svc.ResolveAreaXpLevel = (_, _) => 0;
        Assert.AreEqual(0, _svc.ComputeAreaXp(1, 1));
    }

    [TestMethod]
    public void GetAreaXpLevel_DefaultWhenUnresolved_IsZero()
    {
        _svc.ResolveAreaXpLevel = null;
        // AssetManager not loaded → catch → 0
        Assert.AreEqual(0, _svc.GetAreaXpLevel(999, 1));
    }

    // --- GiveXp paths ---

    [TestMethod]
    public void GiveXp_NullCharacter_Fails()
    {
        var result = _svc.GiveXp(null, 10, XpSource.Admin);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("character", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GiveXp_InvalidCoid_Fails()
    {
        var c = new Character();
        c.AttachTestDataForTests();
        // coid 0
        var result = _svc.GiveXp(c, 10, XpSource.Admin);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("coid", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GiveXp_PersonalXpGain_ScalesAmount()
    {
        _svc.PersonalXpGain = 2f;
        var character = MakeCharacter(3001, xp: 0, level: 1);
        var result = _svc.GiveXp(character, 50, XpSource.Kill);
        Assert.AreEqual(100, result.AppliedAmount);
        Assert.AreEqual(100, character.Experience);
    }

    [TestMethod]
    public void GiveXp_PersonalXpGainTiny_StillAppliesAtLeastOne()
    {
        _svc.PersonalXpGain = 0.001f;
        var character = MakeCharacter(3002, xp: 0, level: 1);
        var result = _svc.GiveXp(character, 1, XpSource.Kill);
        Assert.AreEqual(1, result.AppliedAmount);
    }

    [TestMethod]
    public void GiveXp_NegativeAmount_ClampsTotalToZero()
    {
        var character = MakeCharacter(3003, xp: 20, level: 1);
        var result = _svc.GiveXp(character, -100, XpSource.Admin);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, character.Experience);
        Assert.AreEqual(-100, result.AppliedAmount);
    }

    [TestMethod]
    public void GiveXp_MultiLevelJump_GrantsPointsPerLevel()
    {
        // 0 → 6000 crosses L1 (1000), L2 (3300), L3 (5600) → ends at level 4
        var character = MakeCharacter(3004, xp: 0, level: 1);
        var result = _svc.GiveXp(character, 6000, XpSource.Admin);
        Assert.IsTrue(result.Leveled);
        Assert.AreEqual(4, result.Level);
        Assert.AreEqual(6000, character.Experience);
        // levels 2,3,4 each grant 1 skill + 2 attrib = 3 each
        Assert.AreEqual(3, character.SkillPoints);
        Assert.AreEqual(6, character.AttributePoints);
    }

    [TestMethod]
    public void GiveXp_AtMaxLevel_NearCap_ClampsApplied()
    {
        _svc.MaxLevel = 5;
        // threshold for level 5 = 12000; at max level, total must stay < 12000 (cap-1)
        var character = MakeCharacter(3005, xp: 11990, level: 5);
        var result = _svc.GiveXp(character, 100, XpSource.Admin);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(11999, character.Experience);
        Assert.AreEqual(9, result.AppliedAmount);
        Assert.AreEqual(5, character.Level);
    }

    [TestMethod]
    public void GiveXp_AtMaxLevel_AlreadyAtCap_AppliesZero()
    {
        _svc.MaxLevel = 5;
        var character = MakeCharacter(3006, xp: 11999, level: 5);
        var result = _svc.GiveXp(character, 50, XpSource.Admin);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.AppliedAmount);
        Assert.AreEqual(11999, character.Experience);
        Assert.IsTrue(result.Message.Contains("max level", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GiveXp_Delevel_WhenNegativeCrossesThreshold()
    {
        // At level 2 with 1010 XP; drop below 1000 → back to level 1
        var character = MakeCharacter(3007, xp: 1010, level: 2);
        var result = _svc.GiveXp(character, -50, XpSource.Admin);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(960, character.Experience);
        Assert.AreEqual(1, character.Level);
        Assert.IsTrue(result.Leveled);
    }

    [TestMethod]
    public void GiveXp_NotifyTrue_BuildsGiveXpWithLevelHintOnLevelUp()
    {
        var character = MakeCharacter(3008, xp: 990, level: 1);
        var result = _svc.GiveXp(character, 20, XpSource.Kill);
        Assert.IsNotNull(result.GiveXpPacket);
        Assert.AreEqual(20, result.GiveXpPacket.Amount);
        Assert.AreEqual((sbyte)2, result.GiveXpPacket.LevelHint);
        Assert.IsNotNull(result.CharacterLevelPacket);
    }

    [TestMethod]
    public void GiveXp_ExplicitLevelHint_Preserved()
    {
        var character = MakeCharacter(3009, xp: 0, level: 1);
        var result = _svc.GiveXp(character, 10, XpSource.Admin, levelHint: 9);
        Assert.IsNotNull(result.GiveXpPacket);
        Assert.AreEqual((sbyte)9, result.GiveXpPacket.LevelHint);
    }

    [TestMethod]
    public void GiveXp_PersistOff_DoesNotSave()
    {
        _svc.PersistOnGrant = false;
        var character = MakeCharacter(3010, xp: 0, level: 1);
        var result = _svc.GiveXp(character, 50, XpSource.Kill);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, _persist.Saves.Count);
        Assert.AreEqual(50, character.Experience);
    }

    // --- SetExperienceAbsolute ---

    [TestMethod]
    public void SetExperienceAbsolute_Null_Fails()
    {
        Assert.IsFalse(_svc.SetExperienceAbsolute(null, 100).Success);
    }

    [TestMethod]
    public void SetExperienceAbsolute_InvalidCoid_Fails()
    {
        var c = new Character();
        c.AttachTestDataForTests();
        Assert.IsFalse(_svc.SetExperienceAbsolute(c, 100).Success);
    }

    [TestMethod]
    public void SetExperienceAbsolute_SetsLevelFromThresholds()
    {
        var character = MakeCharacter(3011, xp: 0, level: 1);
        var result = _svc.SetExperienceAbsolute(character, 9000);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(9000, character.Experience);
        // 8800 reaches L5; next threshold 12000 not met → level 5
        Assert.AreEqual(5, character.Level);
        Assert.IsTrue(result.Leveled);
        Assert.IsNotNull(result.GiveXpPacket);
        Assert.AreEqual(9000, result.GiveXpPacket.Amount);
        Assert.IsNotNull(result.CharacterLevelPacket);
        Assert.AreEqual(1, _persist.Saves.Count);
    }

    [TestMethod]
    public void SetExperienceAbsolute_NegativeClampedToZero()
    {
        var character = MakeCharacter(3012, xp: 500, level: 1);
        var result = _svc.SetExperienceAbsolute(character, -10);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, character.Experience);
        Assert.IsNull(result.GiveXpPacket); // delta not > 0
        Assert.IsNotNull(result.CharacterLevelPacket);
    }

    [TestMethod]
    public void SetExperienceAbsolute_PersistFailure_Fails()
    {
        _persist.ThrowOnSave = true;
        var character = MakeCharacter(3013, xp: 0, level: 1);
        var result = _svc.SetExperienceAbsolute(character, 100);
        Assert.IsFalse(result.Success);
    }

    // --- Login restore / send ---

    [TestMethod]
    public void TryCreateLoginRestorePacket_NullCharacter_ReturnsNull()
    {
        Assert.IsNull(_svc.TryCreateLoginRestorePacket(null));
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_InvalidCoid_ReturnsNull()
    {
        var c = new Character();
        Assert.IsNull(_svc.TryCreateLoginRestorePacket(c));
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_LoadThrows_UsesInMemory()
    {
        _persist.ThrowOnLoad = true;
        var character = MakeCharacter(3014, xp: 42, level: 3);
        var packet = _svc.TryCreateLoginRestorePacket(character, _persist);
        Assert.IsNotNull(packet);
        Assert.AreEqual(42, packet.Experience);
        Assert.AreEqual(3, packet.Level);
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_MissingStore_DefaultsLevel1()
    {
        var character = MakeCharacter(3015, xp: 99, level: 7);
        var packet = _svc.TryCreateLoginRestorePacket(character, _persist);
        Assert.IsNotNull(packet);
        Assert.AreEqual(1, character.Level);
        Assert.AreEqual(0, character.Experience);
    }

    [TestMethod]
    public void SendLoginProgressToClient_NullOrNoConnection_NoThrow()
    {
        _svc.SendLoginProgressToClient(null);
        var character = MakeCharacter(3016, xp: 100, level: 1);
        _svc.SendLoginProgressToClient(character); // no OwningConnection
    }

    [TestMethod]
    public void BuildCharacterLevelPacket_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => _svc.BuildCharacterLevelPacket(null));
    }

    [TestMethod]
    public void BuildCharacterLevelPacket_MapsFields()
    {
        var character = MakeCharacter(3017, xp: 1234, level: 4);
        character.SetSkillPoints(3);
        character.SetAttributePoints(5);
        character.SetResearchPoints(1);
        character.SetCredits(999);
        var packet = _svc.BuildCharacterLevelPacket(character);
        Assert.AreEqual(4, packet.Level);
        Assert.AreEqual(1234, packet.Experience);
        Assert.AreEqual(999, packet.Currency);
        Assert.AreEqual(3, packet.SkillPoints);
        Assert.AreEqual(5, packet.AttributePoints);
        Assert.AreEqual(1, packet.ResearchPoints);
        Assert.AreEqual(character.ObjectId.Coid, packet.CharacterId.Coid);
    }

    // --- Threshold / creature / quest fallbacks when injectables null ---

    [TestMethod]
    public void GetThreshold_WithoutInject_FallsBackToDefaultRetail()
    {
        _svc.ResolveThreshold = null;
        Assert.AreEqual(1000u, _svc.GetThreshold(1));
    }

    [TestMethod]
    public void GetCreatureXp_WithoutInject_FallsBackToDefault()
    {
        _svc.ResolveCreatureXp = null;
        Assert.AreEqual(39, _svc.GetCreatureXp(1));
    }

    [TestMethod]
    public void GetQuestFrac_WithoutInject_FallsBackToDefault()
    {
        _svc.ResolveQuestFrac = null;
        Assert.AreEqual(0.10f, _svc.GetQuestFrac(5), 0.0001f);
    }

    [TestMethod]
    public void GetLevelRow_WithoutInject_ReturnsNullSafely()
    {
        _svc.ResolveLevelRow = null;
        // Multi-level still works; points not granted when row null
        var character = MakeCharacter(3018, xp: 990, level: 1);
        var result = _svc.GiveXp(character, 20, XpSource.Admin);
        Assert.IsTrue(result.Leveled);
        Assert.AreEqual(2, character.Level);
        Assert.AreEqual(0, character.SkillPoints);
    }

    [TestMethod]
    public void GiveXpResult_Fail_NullMessage_UsesDefault()
    {
        var fail = GiveXpResult.Fail(null);
        Assert.IsFalse(fail.Success);
        Assert.AreEqual("failed", fail.Message);
    }
}
