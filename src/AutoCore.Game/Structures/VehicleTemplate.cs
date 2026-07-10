namespace AutoCore.Game.Structures;

/// <summary>
/// Row model for wad.xml <c>tVehicleTemplate</c>. Used by map spawn lists with
/// <c>IsTemplate=true</c> (spawn type is the template id, not a vehicle CBID).
/// </summary>
public sealed class VehicleTemplate
{
    public int Id { get; set; }
    public int VehicleCbid { get; set; }
    public int DriverCbid { get; set; }
    public int WeaponTurretCbid { get; set; }
    public int WeaponFrontCbid { get; set; }
    public int ArmorCbid { get; set; }
    public int WeaponMeleeCbid { get; set; }
    public int WeaponDropCbid { get; set; }
    public short BaseLevel { get; set; }
    public int BaseHp { get; set; }
    public byte LootChance { get; set; }
    public byte LootRolls { get; set; }
    public int LootTableId { get; set; }
    public int Skill1 { get; set; }
    public byte SkillRank1 { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ShortDesc { get; set; } = string.Empty;
}
