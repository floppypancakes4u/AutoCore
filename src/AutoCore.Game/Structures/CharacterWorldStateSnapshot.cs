namespace AutoCore.Game.Structures;

/// <summary>
/// Captured continent + pose for character logout / transfer persistence.
/// Vehicle pose is written to both character and vehicle rows when present.
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
    long VehicleCoid);
