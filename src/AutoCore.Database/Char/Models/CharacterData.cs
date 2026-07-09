using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.Char.Models;

[Table("character")]
public class CharacterData
{
    [Key]
    public long Coid { get; set; }
    public uint AccountId { get; set; }
    public long? ActiveVehicleCoid { get; set; }
    public string Name { get; set; } = string.Empty;
    public int HeadId { get; set; } = -1;
    public int BodyId { get; set; } = -1;
    public int HeadDetail1 { get; set; } = -1;
    public int HeadDetail2 { get; set; } = -1;
    public int HelmetId { get; set; } = -1;
    public int EyesId { get; set; } = -1;
    public int MouthId { get; set; } = -1;
    public int HairId { get; set; } = -1;
    public uint PrimaryColor { get; set; }
    public uint SecondaryColor { get; set; }
    public uint EyesColor { get; set; }
    public uint HairColor { get; set; }
    public uint SkinColor { get; set; }
    public uint SpecialityColor { get; set; }
    public int LastTownId { get; set; } = -1;
    public int LastStationId { get; set; } = -1;
    public int LastStationMapId { get; set; } = -1;
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float RotationW { get; set; }
    public float ScaleOffset { get; set; }
    public byte Level { get; set; } = 1;

    /// <summary>
    /// Server-authoritative money as a single int64 (Globes/Bars/Scrip/Clink groups of 1000).
    /// Client UI is restored after login via CharacterLevel (0x2017); do not write non-zero
    /// values into CreateCharacterExtended.Credits (that crashes the retail client).
    /// </summary>
    public long Credits { get; set; }

    /// <summary>Optional debt mirror (server-side only; not written on login spawn).</summary>
    public long CreditDebt { get; set; }

    public bool Deleted { get; set; }

    /// <summary>
    /// Cargo grid width (columns). Chassis systems can later overwrite this per character.
    /// </summary>
    public int CargoWidth { get; set; } = 24;

    /// <summary>
    /// Cargo grid page/row count. Chassis systems can later overwrite this per character.
    /// </summary>
    public int CargoPageCount { get; set; } = 13;

    [ForeignKey("Coid")]
    public SimpleObjectData SimpleObjectBase { get; set; }

    [ForeignKey("ActiveVehicleCoid")]
    public VehicleData ActiveVehicle { get; set; }

    [InverseProperty("Character")]
    public List<CharacterSocial> Socials { get; set; }

    [InverseProperty("Character")]
    public List<VehicleData> Vehicles { get; set; }

    [InverseProperty(nameof(CharacterInventoryData.Character))]
    public List<CharacterInventoryData> InventoryItems { get; set; }
}
