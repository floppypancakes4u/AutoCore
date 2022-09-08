using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoCore.Database.World.Models;

[Table("continent_object")]
public class ContinentObject
{
    [Key]
    public int Id { get; set; }
    public int ContestedMission { get; set; }
    public int Coordinates { get; set; }
    public string DisplayName { get; set; }
    public int Image { get; set; }
    public string MapFileName { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int MaxPlayers { get; set; }
    public int MinVersion { get; set; }
    public int MaxVersion { get; set; }
    public int Objective { get; set; }
    public int OwningFaction { get; set; }
    public float PositionX { get; set; }
    public float PositionZ { get; set; }
    public float Rotation { get; set; }
    public bool IsPersistent { get; set; }
    public bool IsTown { get; set; }
    public bool IsClientOnly { get; set; }
    public bool IsArena { get; set; }
    public bool PlayCreateSounds { get; set; }
    public bool DropCommodities { get; set; }
    public bool DropBrokenItems { get; set; }
}
