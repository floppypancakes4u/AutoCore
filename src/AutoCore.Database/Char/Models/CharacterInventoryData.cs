using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character_inventory")]
public class CharacterInventoryData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long CharacterCoid { get; set; }
    public long ItemCoid { get; set; }
    public int Cbid { get; set; }
    public byte Type { get; set; }
    public byte SlotX { get; set; }
    public byte SlotY { get; set; }
    public int Quantity { get; set; } = 1;

    [ForeignKey(nameof(CharacterCoid))]
    public CharacterData Character { get; set; }
}
