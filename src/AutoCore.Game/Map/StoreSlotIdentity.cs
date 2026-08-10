namespace AutoCore.Game.Map;

using AutoCore.Game.Structures;

/// <summary>
/// COID policy for vendor store display slots (the browse-window line items sent to the
/// client as simple objects when a store is opened). Prevents client-side collisions between
/// slot TFIDs and other Global=true identity spaces:
/// player/vehicle DB-sequence COIDs are low-positive; <see cref="MapNpcIdentity"/> occupies
/// 0x5000_0000 and up; this range starts at 0x6000_0000 and therefore collides with neither.
/// </summary>
public static class StoreSlotIdentity
{
    /// <summary>
    /// High global COID base so vendor store slots never collide with player/vehicle DB
    /// COIDs (Global=true, typically low positive IDs) or with <see cref="MapNpcIdentity"/>
    /// (Global=true, 0x5000_0000 and up).
    /// </summary>
    public const long CoidBase = 0x6000_0000L;

    /// <summary>
    /// Allocates the next store-slot TFID and advances <paramref name="localCoidCounter"/>.
    /// Always Global=true and always &gt;= <see cref="CoidBase"/>.
    /// </summary>
    public static TFID AllocateSlotCoid(ref long localCoidCounter)
    {
        if (localCoidCounter < 0)
            throw new ArgumentOutOfRangeException(nameof(localCoidCounter), "COID counter must be non-negative.");

        var coid = CoidBase + localCoidCounter;
        localCoidCounter++;
        return new TFID(coid, global: true);
    }

    /// <summary>
    /// Returns true if this TFID uses the store-slot identity policy (global + high range).
    /// </summary>
    public static bool IsStoreSlotIdentity(TFID id)
    {
        if (id is null)
            return false;

        return id.Global && id.Coid >= CoidBase;
    }
}
