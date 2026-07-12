using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.World.Models;

/// <summary>Mission XP fraction of level span (tQuestXPLookup / docs/XP.md).</summary>
[Table("quest_xp_lookup")]
public class QuestXpLookup
{
    [Key]
    public int Index { get; set; }

    /// <summary>Fraction of mission TargetLevel span (rlLevelXP).</summary>
    public float LevelXpFraction { get; set; }
}
