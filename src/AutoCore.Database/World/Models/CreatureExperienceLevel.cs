using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.World.Models;

/// <summary>Base kill XP by creature level (tCreatureExperienceLevel / docs/XP.md).</summary>
[Table("creature_experience_level")]
public class CreatureExperienceLevel
{
    [Key]
    public int CreatureLevel { get; set; }

    public int Experience { get; set; }
}
