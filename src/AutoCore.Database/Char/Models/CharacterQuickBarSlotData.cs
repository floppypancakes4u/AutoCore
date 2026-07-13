using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character_quickbar")]
public sealed class CharacterQuickBarSlotData
{
    public long CharacterCoid { get; set; }
    public byte Slot { get; set; }
    public long ItemCoid { get; set; } = -1;
    public int SkillId { get; set; }
}
