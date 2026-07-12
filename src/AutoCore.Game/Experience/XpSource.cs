namespace AutoCore.Game.Experience;

/// <summary>Why XP was granted (logging / future analytics).</summary>
public enum XpSource : byte
{
    Other = 0,
    Kill = 1,
    Mission = 2,
    Area = 3,
    Reaction = 4,
    Admin = 5,
    Outpost = 6,
}
