namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets.Sector;

/// <summary>
/// Result of <see cref="InventoryManager.AddCredits"/>.
/// Client GiveCredits (0x205E) is additive — send <see cref="DeltaPacket"/> while in-sector.
/// On login restore absolute balance via CharacterLevel (0x2017), not CreateCharacterExtended.
/// </summary>
public sealed class AddCreditsResult
{
    public long Previous { get; }
    public long NewBalance { get; }
    public long AppliedDelta { get; }

    /// <summary>Null when no client delta should be sent (applied delta is 0).</summary>
    public GiveCreditsPacket DeltaPacket { get; }

    public AddCreditsResult(long previous, long newBalance, long appliedDelta, GiveCreditsPacket deltaPacket)
    {
        Previous = previous;
        NewBalance = newBalance;
        AppliedDelta = appliedDelta;
        DeltaPacket = deltaPacket;
    }
}
