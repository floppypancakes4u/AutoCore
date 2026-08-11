namespace AutoCore.Game.Structures;

using AutoCore.Game.Constants;

/// <summary>
/// Row model for wad.xml <c>tCreatureAI</c>: behavior profile referenced by
/// <c>CreatureSpecific.AIBehavior</c> (AIID → AICode + val1..val20 tuning, NPC.md §10.2/§10.7).
/// </summary>
public sealed class CreatureAiProfile
{
    public int AiId { get; set; }
    public HBAICode AiCode { get; set; }
    public string DescInternal { get; set; } = string.Empty;

    /// <summary>val1..val20 as loaded; empty columns are 0 (NPC.md §10.7).</summary>
    public float[] Vals { get; } = new float[20];

    /// <summary>val1 — flee / engage timer in milliseconds (e.g. 8000).</summary>
    public float ValFleeOrEngageTimerMs => Vals[0];

    /// <summary>val2 — secondary flee HP band (often 0 or ~0.3). Normalized (SS-44).</summary>
    public float ValFleeHpSecondary => NormalizeHpBand(Vals[1]);

    /// <summary>val3 — primary flee trigger (HP ratio and/or chance). Normalized (SS-44).</summary>
    public float ValFleeHpOrChance => NormalizeHpBand(Vals[2]);

    /// <summary>
    /// val4 — stop-flee / re-engage commitment (often ~1). Normalized (SS-44) but with a ceiling
    /// of 1.0, not the flee band's 0.99 (SS-49): sharing the ceiling made both the flee test
    /// (<c>HpRatio &lt;= band</c>) and the re-engage test (<c>HpRatio &gt;= threshold</c>) true at
    /// exactly 99% HP — an ordinary integer-HP state — so the NPC flipped Combat→flee→Combat every
    /// val1 ms, rewriting its wire state each cycle. The 1.0 ceiling also preserves the authored
    /// "only re-engage at full HP" intent of the 24 rows that write val4 = 1.0.
    /// </summary>
    public float ValReengageThreshold => NormalizeHpBand(Vals[3], maxValue: 1f);

    /// <summary>
    /// SS-44: authored tCreatureAI HP-band columns mix conventions — some rows are ratios
    /// (0.25), some PERCENT (AIID 23 "Flee, NoFlee 50" → val2=50; AIID 48 "25 flee" → val4=40),
    /// and several author exactly 1.0 where retail's own "Flee Immediately" row (AIID 18) uses
    /// 0.99. Raw consumption made threshold ≥ 1.0 rows flee at FULL HP before ever firing.
    /// Values ≥ 2 are percent (÷100); everything clamps to [0, 0.99] so an undamaged NPC
    /// (ratio 1.0) always fights until first damage.
    /// </summary>
    public static float NormalizeHpBand(float raw, float maxValue = MaxFleeBand)
    {
        // SS-49: Math.Clamp(NaN, ...) returns NaN — both of its comparisons are false for NaN —
        // and NaN >= 2f is false too, so a non-finite band used to reach the AI untouched.
        // `HpRatio <= NaN` is always false (never flees) and `HpRatio >= NaN` is always false, so
        // an NPC already fleeing could never clear its latch: parked forever, guns silent.
        // Non-finite data resolves to 0 = "never flee / re-engage immediately", the inert choice.
        if (!float.IsFinite(raw))
            return 0f;

        var value = raw >= PercentThreshold ? raw / 100f : raw;
        return Math.Clamp(value, 0f, maxValue);
    }

    /// <summary>Values at or above this are authored as percentages (AIID 23 val2=50, AIID 48 val4=40).</summary>
    private const float PercentThreshold = 2f;

    /// <summary>Flee-band ceiling: an undamaged NPC (ratio 1.0) must always fight until first damage.</summary>
    private const float MaxFleeBand = 0.99f;

    /// <summary>val5 — call-for-help allow (0 = never).</summary>
    public float ValHelpEnabled => Vals[4];

    /// <summary>val6 — call-for-help chance (0–1).</summary>
    public float ValHelpChance => Vals[5];

    /// <summary>val7 — call-for-help / social range in world units.</summary>
    public float ValHelpRange => Vals[6];
}
