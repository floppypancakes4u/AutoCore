namespace AutoCore.Game.Constants;

/// <summary>
/// Client <c>eInventoryTypes</c> (Ghidra / PACKET STRUCTURES.md).
/// Used as <c>ucTypeFrom</c> on grab and <c>ucTypeTo</c> on drop.
/// </summary>
public static class InventoryTypes
{
    public const byte None = 0;
    public const byte Cargo = 1;
    public const byte Hardpoint = 2;
    public const byte Locker = 3;
    public const byte Store = 4;
    public const byte TradeYou = 5;
    public const byte TradeThem = 6;
    public const byte Refinery = 7;
}
