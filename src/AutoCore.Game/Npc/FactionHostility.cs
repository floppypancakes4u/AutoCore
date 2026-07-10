namespace AutoCore.Game.Npc;

/// <summary>
/// Single choke point for NPC aggro decisions (client CVOGHBAI faction check). Faction ids come
/// from wad.xml <c>tFactions</c> via <see cref="Entities.ClonedObjectBase.GetIDFaction"/>:
/// <list type="bullet">
///   <item>0 Humans / 1 Mutants / 2 Biomeks are the player races — they never mutually aggro.</item>
///   <item>&gt;= 3 are NPC factions — aggressive toward any real faction that is not themselves.</item>
///   <item>-1 (unset) and -100 (neutral) never aggro, in either direction.</item>
/// </list>
/// Pending RE refinement (NPC.md Risk 2); the heuristic below matches observed retail behavior.
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
