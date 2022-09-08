using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.World.Models;

[Table("experience_level")]
public class ExperienceLevel
{
    [Key]
    public byte Level { get; set; }
    public uint Experience { get; set; }
    public byte SkillPoints { get; set; }
    public byte AttributePoints { get; set; }
    public byte ResearchPoints { get; set; }
}
