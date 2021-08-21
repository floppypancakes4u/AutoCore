using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models
{
    [Table("character_vehicle")]
    public class CharacterVehicle
    {
        [Key]
        public long Coid { get; set; }
        public long CharacterCoid { get; set; }
        public int CBID { get; set; }
    }
}
