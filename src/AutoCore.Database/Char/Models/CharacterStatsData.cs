using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character_stats")]
public class CharacterStatsData
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
}

