namespace AutoCore.Game.Npc;

/// <summary>
/// Single choke point for NPC aggro decisions. Faction ids come from wad.xml <c>tFactions</c>
/// via <see cref="Entities.ClonedObjectBase.GetIDFaction"/> (root owner chain).
/// <list type="bullet">
///   <item><b>-100 Neutral</b> — never aggro either way (client <c>FindTargetToAttack</c> aborts
///   for self −100 and skips −100 candidates).</item>
///   <item><b>-1 NPC</b> — never aggressor in this server heuristic (retail <c>vtable+0x298</c>
///   is slightly different; see NPC.md §15.2).</item>
///   <item><b>0 / 1 / 2</b> player races — never mutually aggro on the server (retail different-faction
///   AI can; intentional simplification).</item>
///   <item><b>&gt;= 3</b> (Wildlife, Ambient, Scavs, …) — proactive hostile toward any other real
///   faction (&gt;= 0). Ambient (21) is wildlife, not Neutral — Osterakes aggro players.</item>
/// </list>
/// Retail detail: creature hostility is <c>FUN_005c9450</c> (different faction ⇒ hostile) plus the
/// −100 scan gates. Full matrix and gaps: NPC.md §1.6 / §12.2 / §15.
/// </summary>
public static class FactionHostility
{
    /// <summary>
    /// True when either faction is an NPC faction (&gt;= 3) that is hostile to the other. The
    /// relationship is symmetric so callers need not order the arguments.
    /// </summary>
    public static bool IsHostile(int a, int b)
    {
        return IsAggressor(a, b) || IsAggressor(b, a);
    }

    /// <summary>
    /// True when <paramref name="attacker"/> is an NPC faction (&gt;= 3) that will aggro
    /// <paramref name="other"/> — a distinct, real (&gt;= 0) faction. Unset/neutral targets and
    /// same-faction pairs are never aggressed.
    /// </summary>
    private static bool IsAggressor(int attacker, int other)
    {
        return attacker >= 3 && other >= 0 && attacker != other;
    }
}
