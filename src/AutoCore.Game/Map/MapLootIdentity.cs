namespace AutoCore.Game.Map;

using System.Threading;

/// <summary>
/// COID policy for world ground loot.
/// <para>
/// Ground loot is delivered as a <b>local</b> (Global=false) CreateSimpleObject, and the retail
/// client resolves that against its local object hash
/// (<c>CVOGClonedObjectList::Fetch</c> @0x004BAE70, which picks <c>m_phashLocal</c> when the TFID's
/// global flag is clear). In <c>Process_EMSG_Sector_CreateSimpleObject</c> @0x00812360 a create
/// whose COID is already known is <b>not</b> a create: the client calls <c>ProcessSectorUpdate</c>
/// and the existing object keeps its original CBID — so the new drop renders under the previous
/// item's name. If the COID matches an authored map object instead, the client repositions and
/// respawns that prop (<c>MoveRBToLocation</c> / <c>DoRespawnOfObject</c>) rather than spawning any
/// loot.
/// </para>
/// <para>
/// Loot previously minted from <c>SectorMap.LocalCoidCounter</c>, which map teardown rewinds to
/// <c>MapData.HighestCoid + 1</c>. Every rewind re-issues COIDs that connected clients may still
/// hold, which is exactly the aliasing above. Loot therefore gets its own process-wide monotonic
/// range that is never rewound and never overlaps the authored map range — the same defence
/// <see cref="MapNpcIdentity"/> applies to server-spawned NPCs.
/// </para>
/// </summary>
public static class MapLootIdentity
{
    /// <summary>
    /// Base for ground-loot COIDs. Sits far above any authored <c>MapData.HighestCoid</c> (a map
    /// file int32 counted in thousands) and below <see cref="MapNpcIdentity.CoidBase"/>, so the
    /// loot range overlaps neither authored map objects nor server-spawned NPCs.
    /// </summary>
    public const long CoidBase = 0x2000_0000L;

    private static long _next;

    /// <summary>
    /// Allocates the next ground-loot COID. Process-wide and monotonic: a COID is never handed out
    /// twice for the lifetime of the server, so no connected client can be holding a stale object
    /// under it. Always &gt;= <see cref="CoidBase"/>.
    /// </summary>
    public static long AllocateCoid() => CoidBase + Interlocked.Increment(ref _next);

    /// <summary>True when this COID was minted by the ground-loot allocator.</summary>
    public static bool IsLootIdentity(long coid) => coid >= CoidBase && coid < MapNpcIdentity.CoidBase;

    /// <summary>Test seam: rewinds the shared counter so assertions on exact COIDs stay stable.</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _next, 0);
}
