namespace AutoCore.Game.Structures;

/// <summary>
/// Captured continent + pose (+ vehicle combat pools) for character logout / transfer persistence.
/// Vehicle pose and combat state are written to the vehicle row when present.
/// Combat fields of <c>-1</c> mean never saved / no vehicle.
/// </summary>
public readonly record struct CharacterWorldStateSnapshot(
    long CharacterCoid,
    int ContinentId,
    float PositionX,
    float PositionY,
    float PositionZ,
    float RotationX,
    float RotationY,
    float RotationZ,
    float RotationW,
    long VehicleCoid,
    int CurrentHP = -1,
    int CurrentShield = -1,
    int CurrentPower = -1,
    int CurrentHeat = -1);
