using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

/// <summary>
/// A mission this character has completed. Key <c>(CharacterCoid, MissionId)</c>.
/// Drives prerequisite / repeatable gating and mission-completed logic variables on relog.
/// </summary>
[Table("character_mission_completed")]
public class CharacterCompletedMissionData
{
    public long CharacterCoid { get; set; }
    public int MissionId { get; set; }
}
