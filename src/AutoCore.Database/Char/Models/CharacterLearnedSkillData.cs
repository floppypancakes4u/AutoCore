using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character_learned_skill")]
public sealed class CharacterLearnedSkillData
{
    public long CharacterCoid { get; set; }
    public int SkillId { get; set; }
    public byte Rank { get; set; }
}
