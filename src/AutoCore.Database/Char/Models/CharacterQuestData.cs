using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

/// <summary>
/// Persisted active-mission state for one character. Key <c>(CharacterCoid, MissionId)</c>.
/// Objective progress is stored as packed little-endian int32 slots; <c>ObjectiveMax</c> is not
/// persisted — it is re-derived from the mission template via
/// <c>CharacterQuest.PopulateFromAssets()</c> on load.
/// </summary>
[Table("character_mission")]
public class CharacterQuestData
{
    public long CharacterCoid { get; set; }
    public int MissionId { get; set; }
    public byte ActiveObjectiveSequence { get; set; }
    public byte State { get; set; }
    public byte[] ObjectiveProgress { get; set; }
}
