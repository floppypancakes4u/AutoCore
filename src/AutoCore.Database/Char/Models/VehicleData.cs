using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("vehicle")]
public class VehicleData
{
    [Key]
    public long Coid { get; set; }
    public long CharacterCoid { get; set; }
    public string Name { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Rotation1 { get; set; }
    public float Rotation2 { get; set; }
    public float Rotation3 { get; set; }
    public float Rotation4 { get; set; }
    public long Ornament { get; set; }
    public long RaceItem { get; set; }
    public long PowerPlant { get; set; }
    public long Wheelset { get; set; }
    public long Armor { get; set; }
    public long MeleeWeapon { get; set; }
    public long Front { get; set; }
    public long Turret { get; set; }
    public long Rear { get; set; }
    public uint PrimaryColor { get; set; }
    public uint SecondaryColor { get; set; }
    public byte Trim { get; set; }

    [ForeignKey("Coid")]
    public SimpleObjectData SimpleObjectBase { get; set; }

    [ForeignKey("CharacterCoid")]
    public CharacterData Character { get; set; }
}
