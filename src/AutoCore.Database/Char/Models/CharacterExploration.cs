using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character_exploration")]
public class CharacterExploration
{
    public long CharacterCoid { get; set; }
    public int ContinentId { get; set; }
    public uint ExploredBits { get; set; }
}
