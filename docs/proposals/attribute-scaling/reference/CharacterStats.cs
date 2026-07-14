using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

/// <summary>
/// Persistent character stat block (backs the /level, /xp, /tech, ... debug commands
/// and, eventually, real progression). One row per character, keyed by the character Coid.
///
/// Schema mirrors SCAR's ensure-character-stats.sql. Attributes default to 1 (Tech 1 =
/// baseline). HP is derived, not stored: HP = 100 + (AttributeTech - 1) * 3.
/// </summary>
[Table("character_stats")]
public class CharacterStats
{
    [Key]
    public long CharacterCoid { get; set; }

    public long Currency { get; set; } = 0;
    public int Experience { get; set; } = 0;

    public short CurrentMana { get; set; } = 100;
    public short MaxMana { get; set; } = 100;

    public short AttributeTech { get; set; } = 1;
    public short AttributeCombat { get; set; } = 1;
    public short AttributeTheory { get; set; } = 1;
    public short AttributePerception { get; set; } = 1;

    public short AttributePoints { get; set; } = 0;
    public short SkillPoints { get; set; } = 0;
    public short ResearchPoints { get; set; } = 0;

    [ForeignKey("CharacterCoid")]
    public CharacterData Character { get; set; }

    /// <summary>Derived max HP: 100 base at Tech 1, +3 per Tech level above 1.</summary>
    [NotMapped]
    public int MaxHp => 100 + (AttributeTech - 1) * 3;
}
