namespace AutoCore.Game.Map;

using AutoCore.Game.Structures;

/// <summary>
/// COID policy for all server-spawned, map-visible NPCs (creatures and vehicles).
/// Prevents retail client crashes caused by attaching a server ghost to a client-local
/// map object with the same TFID.
/// </summary>
public static class MapNpcIdentity
{
    /// <summary>
    /// High global COID base so map NPCs never collide with:
    /// client-local map objects (Global=false, low local counters) or
    /// player/vehicle DB COIDs (Global=true, typically low positive IDs).
    /// </summary>
    public const long CoidBase = 0x5000_0000L;

    /// <summary>
    /// Allocates the next map-NPC TFID and advances <paramref name="localCoidCounter"/>.
    /// Always Global=true and always &gt;= <see cref="CoidBase"/>.
    /// </summary>
    public static TFID AllocateCoid(ref long localCoidCounter)
    {
        if (localCoidCounter < 0)
            throw new ArgumentOutOfRangeException(nameof(localCoidCounter), "COID counter must be non-negative.");

        var coid = CoidBase + localCoidCounter;
        localCoidCounter++;
        return new TFID(coid, global: true);
    }

    /// <summary>
    /// Returns true if this TFID uses the map-NPC identity policy (global + high range).
    /// </summary>
    public static bool IsMapNpcIdentity(TFID id)
    {
        if (id is null)
            return false;

        return id.Global && id.Coid >= CoidBase;
    }

    /// <summary>
    /// Returns true if a local (non-global) TFID could collide with historical
    /// server spawn allocation that used Map.LocalCoidCounter with Global=false.
    /// </summary>
    public static bool IsUnsafeLocalSpawnCoid(TFID id, long mapHighestCoid)
    {
        if (id is null || id.Global)
            return false;

        // Old policy: coids were HighestCoid+1, +2, ... with Global=false —
        // the same space the client uses for local map objects.
        return id.Coid > mapHighestCoid;
    }
}
