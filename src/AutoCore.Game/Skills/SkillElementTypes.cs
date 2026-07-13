namespace AutoCore.Game.Skills;

/// <summary>
/// Subset of retail <c>ESkillElementTypes</c> used by the server skill pipeline.
/// See <c>Documentation/PACKET STRUCTURES.md</c>.
/// </summary>
public static class SkillElementTypes
{
    public const int Cost = 1;
    public const int CoolDown = 3;
    public const int Duration = 5;
    public const int Charge = 6;
    public const int Range = 7;
    public const int Heal = 10;
    public const int PenetrationDamageAdd = 68;

    public const int FlagDamageMin = 65536;
    public const int FlagDamageMax = 131072;

    public const int ChannelMask = 0xFFFF;
}
