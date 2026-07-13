namespace AutoCore.Game.Structures;

/// <summary>
/// Live vehicle combat pools for logout persistence / login restore.
/// Values of <c>-1</c> mean "not set" (leave full pools after max recalculation).
/// </summary>
public readonly record struct VehicleCombatStateSnapshot(
    int CurrentHP,
    int CurrentShield,
    int CurrentPower,
    int CurrentHeat)
{
    public static VehicleCombatStateSnapshot Unset { get; } = new(-1, -1, -1, -1);
}
