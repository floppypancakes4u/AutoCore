using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models
{
    [Table("character")]
    public class Character
    {
        [Key]
        public long Coid { get; set; }
        public uint AccountId { get; set; }
        public int CBID { get; set; }
        public string Name { get; set; }
        public int HeadId { get; set; }
        public int BodyId { get; set; }
        public int HeadDetail1 { get; set; }
        public int HeadDetail2 { get; set; }
        public int HelmetId { get; set; }
        public int EyesId { get; set; }
        public int MouthId { get; set; }
        public int HairId { get; set; }
        public uint PrimaryColor { get; set; }
        public uint SecondaryColor { get; set; }
        public uint EyesColor { get; set; }
        public uint HairColor { get; set; }
        public uint SkinColor { get; set; }
        public uint SpecialityColor { get; set; }
        public float ScaleOffset { get; set; }
    }
}
