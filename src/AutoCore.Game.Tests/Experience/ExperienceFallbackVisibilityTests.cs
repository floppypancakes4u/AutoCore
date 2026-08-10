using System.Reflection;
using AutoCore.Game.Experience;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameMission = AutoCore.Game.Mission.Mission;
using GameMissionObjective = AutoCore.Game.Mission.MissionObjective;

namespace AutoCore.Game.Tests.Experience;

/// <summary>
/// SS-22: every experience/credit lookup used to swallow asset failures with a bare
/// <c>catch</c> commented "Asset manager not initialized in unit tests", then quietly return a
/// built-in retail approximation. In production that converts an asset problem into silently
/// wrong XP, credits and quest rewards — invisible until someone audits the numbers, by which
/// point the character database is already wrong.
/// <para>
/// The fallback itself is correct behaviour (the server stays playable). Being silent was not.
/// </para>
/// </summary>
[TestClass]
public class ExperienceFallbackVisibilityTests
{
    private ExperienceService _svc = null!;

    [TestInitialize]
    public void Init()
    {
        _svc = ExperienceService.Instance;
        _svc.ResetForTests();
        ExperienceService.ResetFallbackTrackingForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _svc.ResetForTests();
        ExperienceService.ResetFallbackTrackingForTests();
        ClearQuestLookupTables();
    }

    /// <summary>
    /// With no injected resolver and no loaded assets, the threshold lookup degrades to the
    /// built-in table — and that degradation must be counted, not silent.
    /// </summary>
    [TestMethod]
    public void GetThreshold_WhenAssetsUnavailable_ReturnsFallback_AndRecordsTheDegradation()
    {
        var value = _svc.GetThreshold(5);

        Assert.AreEqual(
            ExperienceService.DefaultRetailThreshold(5),
            value,
            "The fallback must still return a usable threshold so the server stays playable.");

        Assert.IsTrue(
            ExperienceService.FallbackCount > 0,
            "SS-22: falling back to the built-in retail table must be recorded so an operator " +
            "can tell that awarded XP is being computed from approximations.");
    }

    [TestMethod]
    public void GetCreatureXp_WhenAssetsUnavailable_RecordsTheDegradation()
    {
        _svc.GetCreatureXp(10);

        Assert.IsTrue(ExperienceService.FallbackCount > 0);
    }

    [TestMethod]
    public void GetQuestBaseCredits_WhenAssetsUnavailable_RecordsTheDegradation()
    {
        _svc.GetQuestBaseCredits(10);

        Assert.IsTrue(ExperienceService.FallbackCount > 0);
    }

    /// <summary>
    /// An injected resolver is the healthy path: it must short-circuit before the asset lookup
    /// and therefore must NOT be reported as a fallback, or the signal becomes noise.
    /// </summary>
    [TestMethod]
    public void GetThreshold_WithInjectedResolver_IsNotCountedAsFallback()
    {
        _svc.ResolveThreshold = level => 12345u;

        var value = _svc.GetThreshold(5);

        Assert.AreEqual(12345u, value);
        Assert.AreEqual(
            0,
            ExperienceService.FallbackCount,
            "A configured resolver is the healthy path and must not be reported as degradation.");
    }

    /// <summary>
    /// The counter must keep accumulating across calls so an operator can see how much of the
    /// session's progression was computed from fallback data.
    /// </summary>
    [TestMethod]
    public void FallbackCount_AccumulatesAcrossLookups()
    {
        _svc.GetThreshold(5);
        var afterFirst = ExperienceService.FallbackCount;

        _svc.GetCreatureXp(10);
        _svc.GetQuestFrac(0);

        Assert.IsTrue(
            ExperienceService.FallbackCount > afterFirst,
            "Each degraded lookup must increment the counter.");
    }

    [TestMethod]
    public void ResetFallbackTrackingForTests_ClearsTheCounter()
    {
        _svc.GetThreshold(5);
        Assert.IsTrue(ExperienceService.FallbackCount > 0);

        ExperienceService.ResetFallbackTrackingForTests();

        Assert.AreEqual(0, ExperienceService.FallbackCount);
    }

    // ------------------------------------------------------------------
    // Loaded-table key misses are NOT degradation. Retail's quest lookup is
    // an exact-key map where a miss (e.g. XPIndex -1 on Track This 3979)
    // legitimately means "no reward". Warning "returned no asset data" for
    // those conflates authored data with a broken asset pipeline.
    // ------------------------------------------------------------------

    [TestMethod]
    public void GetQuestFrac_LoadedTableKeyMissNegativeIndex_ReturnsZeroWithoutFallback()
    {
        SeedQuestXpLookup(new Dictionary<int, float> { [0] = 0f, [1] = 0.02f });

        var value = _svc.GetQuestFrac(-1);

        Assert.AreEqual(0f, value);
        Assert.AreEqual(
            0,
            ExperienceService.FallbackCount,
            "A loaded table with a data-intentional negative index miss is retail behaviour, " +
            "not asset degradation, and must not be counted or warned as a fallback.");
    }

    [TestMethod]
    public void GetQuestFrac_LoadedTableKeyMissPositiveIndex_ReturnsZeroWithoutFallback()
    {
        SeedQuestXpLookup(new Dictionary<int, float> { [0] = 0f, [1] = 0.02f });

        var value = _svc.GetQuestFrac(18);

        Assert.AreEqual(0f, value);
        Assert.AreEqual(0, ExperienceService.FallbackCount);
    }

    [TestMethod]
    public void GetQuestFrac_TableAbsent_StillWarnsAndFallsBack()
    {
        var value = _svc.GetQuestFrac(5);

        Assert.AreEqual(ExperienceService.DefaultQuestFrac(5), value);
        Assert.IsTrue(
            ExperienceService.FallbackCount > 0,
            "With no loaded table the built-in retail approximation is a real degradation " +
            "and must keep being recorded (SS-22).");
    }

    [TestMethod]
    public void GetQuestCreditsFrac_LoadedTableKeyMissNegativeIndex_ReturnsZeroWithoutFallback()
    {
        SeedQuestCreditsLookup(new Dictionary<int, float> { [0] = 0f, [1] = 0.2f });

        var value = _svc.GetQuestCreditsFrac(-1);

        Assert.AreEqual(0f, value);
        Assert.AreEqual(0, ExperienceService.FallbackCount);
    }

    [TestMethod]
    public void GetQuestCreditsFrac_LoadedTableKeyMissPositiveIndex_ReturnsZeroWithoutFallback()
    {
        SeedQuestCreditsLookup(new Dictionary<int, float> { [0] = 0f, [1] = 0.2f });

        var value = _svc.GetQuestCreditsFrac(18);

        Assert.AreEqual(0f, value);
        Assert.AreEqual(0, ExperienceService.FallbackCount);
    }

    [TestMethod]
    public void GetQuestCreditsFrac_TableAbsent_StillWarnsAndFallsBack()
    {
        var value = _svc.GetQuestCreditsFrac(5);

        Assert.AreEqual(ExperienceService.DefaultQuestCreditsFrac(5), value);
        Assert.IsTrue(ExperienceService.FallbackCount > 0);
    }

    /// <summary>
    /// Pins retail semantics for the Track This (3979) shape: final objective XPIndex -1,
    /// static XP 0 → zero XP by authored data, with no fallback recorded.
    /// </summary>
    [TestMethod]
    public void ComputeMissionXp_XpIndexMinusOne_ReturnsZero_RetailDataIntentional()
    {
        SeedQuestXpLookup(new Dictionary<int, float> { [0] = 0f, [1] = 0.02f, [5] = 0.10f });

        var mission = GameMission.CreateForTests(3979);
        mission.TargetLevel = 3;
        var objective = GameMissionObjective.CreateForTests(7659, 2, 3979);
        var t = typeof(GameMissionObjective);
        t.GetProperty(nameof(GameMissionObjective.XPIndex))!.SetValue(objective, (short)-1);
        t.GetProperty(nameof(GameMissionObjective.XPScaler))!.SetValue(objective, 1f);
        t.GetProperty(nameof(GameMissionObjective.XPBalanceScaler))!.SetValue(objective, 1f);

        Assert.AreEqual(0, _svc.ComputeMissionXp(mission, objective));
        Assert.AreEqual(0, ExperienceService.FallbackCount);
    }

    private static void SeedQuestXpLookup(IDictionary<int, float> table) =>
        GetWorldDbLoader().QuestXpLookup = table;

    private static void SeedQuestCreditsLookup(IDictionary<int, float> table) =>
        GetWorldDbLoader().QuestCreditsLookup = table;

    private static void ClearQuestLookupTables()
    {
        var loader = GetWorldDbLoader();
        loader.QuestXpLookup = null;
        loader.QuestCreditsLookup = null;
    }

    private static WorldDBLoader GetWorldDbLoader()
    {
        var prop = typeof(AssetManager).GetProperty(
            "WorldDBLoader",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(prop);
        return (WorldDBLoader)prop!.GetValue(AssetManager.Instance)!;
    }
}
