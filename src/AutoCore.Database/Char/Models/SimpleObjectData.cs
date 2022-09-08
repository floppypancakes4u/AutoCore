using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("simple_object")]
public class SimpleObjectData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Coid { get; set; }
    public byte Type { get; set; }
    public int CBID { get; set; }
    public int Faction { get; set; }
    public int TeamFaction { get; set; }
}
