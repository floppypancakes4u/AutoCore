namespace AutoCore.Game.Npc;

/// <summary>
/// Single choke point for NPC aggro decisions. Faction ids come from wad.xml <c>tFactions</c>
/// via <see cref="Entities.ClonedObjectBase.GetIDFaction"/> (root owner chain).
/// <list type="bullet">
///   <item><b>-100 Neutral</b> — never aggro either way (client <c>FindTargetToAttack</c> aborts
///   for self −100 and skips −100 candidates).</item>
///   <item><b>-1 NPC</b> — never aggressor in this server heuristic (retail <c>vtable+0x298</c>
///   is slightly different; see NPC.md §15.2).</item>
///   <item><b>&gt;= 0</b> real factions (Humans 0 / Mutants 1 / Biomeks 2 / Wildlife / Ambient …) —
///   hostile to every <b>other</b> real faction. Human militia (0) does not attack Human players.
///   Ambient (21) is wildlife, not Neutral — Osterakes aggro players.</item>
/// </list>
/// Retail: <c>FUN_005c9450</c> (different faction ⇒ hostile) plus the −100 scan gates.
/// </summary>
public static class FactionHostility
{
    /// <summary>
    /// True when the two ids are distinct real factions (&gt;= 0). Symmetric.
    /// </summary>
    public static bool IsHostile(int a, int b)
    {
        return IsAggressor(a, b) || IsAggressor(b, a);
    }

    /// <summary>
    /// True when <paramref name="attacker"/> is a real faction (&gt;= 0) that will aggro
    /// a distinct real <paramref name="other"/>. Unset (−1) and Neutral (−100) never aggress.
    /// </summary>
    private static bool IsAggressor(int attacker, int other)
    {
        return attacker >= 0 && other >= 0 && attacker != other;
    }
}
