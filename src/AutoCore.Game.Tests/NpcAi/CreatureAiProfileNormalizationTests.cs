using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Structures;

/// <summary>
/// SS-44 tripwires: authored tCreatureAI rows mix ratio and PERCENT conventions in the HP-band
/// columns (AIID 23 "Flee, NoFlee 50" authors val2=50; AIID 48 "25 flee" authors val4=40), and
/// several rows author exactly 1.0 where retail's own "Flee Immediately" row (AIID 18) uses
/// 0.99. Consumed raw as ratios, threshold ≥ 1.0 made whole NPC families flee at FULL HP before
/// ever firing a shot, and a percent val4 re-extended the flee latch forever. The derived
/// accessors normalize: values ≥ 2 are percent (÷100), then clamp to [0, 0.99] so an undamaged
/// NPC always fights until first damage.
/// </summary>
[TestClass]
public class CreatureAiProfileNormalizationTests
{
    private static CreatureAiProfile Profile(float val2 = 0f, float val3 = 0f, float val4 = 0f)
    {
        var p = new CreatureAiProfile { AiId = 1 };
        p.Vals[1] = val2;
        p.Vals[2] = val3;
        p.Vals[3] = val4;
        return p;
    }

    [TestMethod]
    public void HpBands_PercentValues_DivideBy100()
    {
        Assert.AreEqual(0.5f, Profile(val2: 50f).ValFleeHpSecondary, 0.0001f, "AIID 23: 50 means 50%");
        Assert.AreEqual(0.4f, Profile(val4: 40f).ValReengageThreshold, 0.0001f, "AIID 48: 40 means 40%");
        Assert.AreEqual(0.25f, Profile(val3: 25f).ValFleeHpOrChance, 0.0001f);
    }

    [TestMethod]
    public void HpBands_AtOrAboveFullHp_ClampTo099()
    {
        Assert.AreEqual(0.99f, Profile(val3: 1.0f).ValFleeHpOrChance, 0.0001f,
            "authored 1.0 converges on retail's own flee-immediately expression (AIID 18 = 0.99)");
        Assert.AreEqual(0.99f, Profile(val2: 1.1f).ValFleeHpSecondary, 0.0001f, "AIID 38 val2=1.1");
    }

    /// <summary>
    /// SS-49 tripwire: Math.Clamp(NaN, ...) returns NaN (both comparisons are false for NaN), and
    /// NaN >= 2f is false so the percent branch is skipped too. A NaN band therefore reached the
    /// AI: `HpRatio &lt;= NaN` is always false (NPC can never flee) and, worse, `HpRatio &gt;= NaN`
    /// is always false, so an NPC that entered flee via a sane band could NEVER clear the latch —
    /// it re-extends forever, parked at its anchor with guns silent. Non-finite data must resolve
    /// to a safe, inert value.
    /// </summary>
    [TestMethod]
    public void HpBands_NonFiniteValues_AreNeutralized()
    {
        Assert.AreEqual(0f, CreatureAiProfile.NormalizeHpBand(float.NaN), "NaN must not poison the comparisons");
        Assert.AreEqual(0f, CreatureAiProfile.NormalizeHpBand(float.PositiveInfinity));
        Assert.AreEqual(0f, CreatureAiProfile.NormalizeHpBand(float.NegativeInfinity));
        Assert.AreEqual(0f, Profile(val3: float.NaN).ValFleeHpOrChance, "accessor must be safe too");
    }

    [TestMethod]
    public void HpBands_NegativeValues_ClampToZero()
    {
        Assert.AreEqual(0f, CreatureAiProfile.NormalizeHpBand(-0.5f));
        Assert.AreEqual(0f, Profile(val2: -3f).ValFleeHpSecondary);
    }

    [TestMethod]
    public void HpBands_PercentBoundary_IsExactlyTwo()
    {
        Assert.AreEqual(0.02f, CreatureAiProfile.NormalizeHpBand(2f), 0.0001f, "2 is percent → 0.02");
        Assert.AreEqual(0.99f, CreatureAiProfile.NormalizeHpBand(1.999f), 0.0001f, "just under 2 is a ratio → clamped");
    }

    /// <summary>
    /// SS-49: the flee band clamps to 0.99 so an undamaged NPC always fights. Applying the same
    /// ceiling to the RE-ENGAGE threshold made both comparisons true at exactly 99% HP — an
    /// ordinary integer-HP state (99/100, 495/500) — so the NPC flipped Combat→flee→Combat every
    /// val1 ms, rewriting its wire state each time. Re-engage keeps its own ceiling of 1.0, which
    /// also preserves the authored "only re-engage at full HP" intent of the many val4=1.0 rows.
    /// </summary>
    [TestMethod]
    public void ReengageThreshold_KeepsFullHpCeiling_NoOscillationWithFleeBand()
    {
        var p = Profile(val3: 1.0f, val4: 1.0f);
        Assert.AreEqual(0.99f, p.ValFleeHpOrChance, 0.0001f, "flee band still clamps below full HP");
        Assert.AreEqual(1.0f, p.ValReengageThreshold, 0.0001f,
            "re-engage may require full HP; sharing the 0.99 ceiling caused a flee/re-engage flip-flop");
        Assert.IsTrue(p.ValReengageThreshold > p.ValFleeHpOrChance,
            "the two thresholds must never coincide, or an NPC oscillates at that exact HP");
    }

    [TestMethod]
    public void ReengageThreshold_StillNormalizesPercentAndNonFinite()
    {
        Assert.AreEqual(0.4f, Profile(val4: 40f).ValReengageThreshold, 0.0001f, "AIID 48 percent authoring");
        Assert.AreEqual(0f, Profile(val4: float.NaN).ValReengageThreshold);
    }

    [TestMethod]
    public void HpBands_RatioValues_PassThroughUnchanged()
    {
        Assert.AreEqual(0.3f, Profile(val3: 0.3f).ValFleeHpOrChance, 0.0001f);
        Assert.AreEqual(0f, Profile().ValFleeHpSecondary, "never-flee zero rows stay zero");
        Assert.AreEqual(0.99f, Profile(val3: 0.99f).ValFleeHpOrChance, 0.0001f, "retail AIID 18 identity");
    }
}
