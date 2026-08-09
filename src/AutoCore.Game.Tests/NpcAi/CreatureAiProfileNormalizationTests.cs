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

    [TestMethod]
    public void HpBands_RatioValues_PassThroughUnchanged()
    {
        Assert.AreEqual(0.3f, Profile(val3: 0.3f).ValFleeHpOrChance, 0.0001f);
        Assert.AreEqual(0f, Profile().ValFleeHpSecondary, "never-flee zero rows stay zero");
        Assert.AreEqual(0.99f, Profile(val3: 0.99f).ValFleeHpOrChance, 0.0001f, "retail AIID 18 identity");
    }
}
