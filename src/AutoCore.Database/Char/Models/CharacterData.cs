using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character")]
public class CharacterData
{
    [Key]
    public long Coid { get; set; }
    public uint AccountId { get; set; }
    public long ActiveVehicleCoid { get; set; }
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
    public byte Level { get; set; }
    public bool Deleted { get; set; }

    [ForeignKey("Coid")]
    public SimpleObjectData SimpleObjectBase { get; set; }

    [ForeignKey("ActiveVehicleCoid")]
    public VehicleData ActiveVehicle { get; set; }

    [InverseProperty("Character")]
    public List<CharacterSocial> Socials { get; set; }

    [InverseProperty("Character")]
    public List<VehicleData> Vehicles { get; set; }

    public CharacterData()
    {
        Name = "";
        BodyId = -1;
        HeadId = -1;
        HairId = -1;
        HelmetId = -1;
        HeadDetail1 = -1;
        HeadDetail2 = -1;
        EyesId = -1;
        MouthId = -1;
        Level = 1;
    }
}
