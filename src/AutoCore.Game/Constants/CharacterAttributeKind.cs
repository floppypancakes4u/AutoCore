namespace AutoCore.Game.Constants;

/// <summary>
/// Retail <c>AttributeIncrement</c> (0x205A) body mask values from
/// <c>UI_OnAttributePointClick_Inferred</c> @ 0x008F92E0.
/// </summary>
public enum CharacterAttributeKind : byte
{
    None = 0,
    Combat = 1,
    Theory = 2,
    Tech = 3,
    Perception = 4,
}

public static class CharacterAttributeMasks
{
    public const uint Combat = 0x00000001u;
    public const uint Theory = 0x00000100u;
    public const uint Tech = 0x00010000u;
    public const uint Perception = 0x01000000u;

    public static CharacterAttributeKind FromMask(uint mask) => mask switch
    {
        Combat => CharacterAttributeKind.Combat,
        Theory => CharacterAttributeKind.Theory,
        Tech => CharacterAttributeKind.Tech,
        Perception => CharacterAttributeKind.Perception,
        _ => CharacterAttributeKind.None,
    };

    public static uint ToMask(this CharacterAttributeKind kind) => kind switch
    {
        CharacterAttributeKind.Combat => Combat,
        CharacterAttributeKind.Theory => Theory,
        CharacterAttributeKind.Tech => Tech,
        CharacterAttributeKind.Perception => Perception,
        _ => 0u,
    };
}
