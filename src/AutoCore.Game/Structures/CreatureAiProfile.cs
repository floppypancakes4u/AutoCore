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

    /// <summary>val4 — stop-flee / re-engage commitment (often ~1). Normalized (SS-44).</summary>
    public float ValReengageThreshold => NormalizeHpBand(Vals[3]);

    /// <summary>
    /// SS-44: authored tCreatureAI HP-band columns mix conventions — some rows are ratios
    /// (0.25), some PERCENT (AIID 23 "Flee, NoFlee 50" → val2=50; AIID 48 "25 flee" → val4=40),
    /// and several author exactly 1.0 where retail's own "Flee Immediately" row (AIID 18) uses
    /// 0.99. Raw consumption made threshold ≥ 1.0 rows flee at FULL HP before ever firing.
    /// Values ≥ 2 are percent (÷100); everything clamps to [0, 0.99] so an undamaged NPC
    /// (ratio 1.0) always fights until first damage.
    /// </summary>
    public static float NormalizeHpBand(float raw)
    {
        var value = raw >= 2f ? raw / 100f : raw;
        return Math.Clamp(value, 0f, 0.99f);
    }

    /// <summary>val5 — call-for-help allow (0 = never).</summary>
    public float ValHelpEnabled => Vals[4];

    /// <summary>val6 — call-for-help chance (0–1).</summary>
    public float ValHelpChance => Vals[5];

    /// <summary>val7 — call-for-help / social range in world units.</summary>
    public float ValHelpRange => Vals[6];
}
