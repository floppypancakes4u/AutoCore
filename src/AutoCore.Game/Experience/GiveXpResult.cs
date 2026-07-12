namespace AutoCore.Game.Experience;

using AutoCore.Game.Packets.Sector;

/// <summary>Outcome of <see cref="ExperienceService.GiveXp"/>.</summary>
public sealed class GiveXpResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public int AppliedAmount { get; init; }
    public int TotalExperience { get; init; }
    public byte Level { get; init; }
    public byte PreviousLevel { get; init; }
    public bool Leveled { get; init; }
    public GiveXPPacket GiveXpPacket { get; init; }
    public CharacterLevelPacket CharacterLevelPacket { get; init; }

    public static GiveXpResult Fail(string message) => new()
    {
        Success = false,
        Message = message ?? "failed"
    };
}
