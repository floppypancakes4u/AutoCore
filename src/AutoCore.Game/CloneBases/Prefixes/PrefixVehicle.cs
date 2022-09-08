namespace AutoCore.Game.CloneBases.Prefixes;

public class PrefixVehicle : PrefixBase
{
    public float AVDCollisionSpinDampeningAdjust { get; set; }
    public float AVDNormalSpinDampeningAdjust { get; set; }
    public int ArmorAdjust { get; set; }
    public float ArmorAdjustPercent { get; set; }
    public float BrakesMaxTorqueFrontAdjustPercent { get; set; }
    public float BrakesMaxTorqueRearAdjustPercent { get; set; }
    public int CooldownAdjust { get; set; }
    public float CooldownAdjustPercent { get; set; }
    public int HeatMaximumAdjust { get; set; }
    public float HeatMaximumAdjustPercent { get; set; }
    public short InventorySlotsAdjust { get; set; }
    public float MaxWtArmorAdjustPercent { get; set; }
    public float MaxWtPowerplantAdjustPercent { get; set; }
    public float MaxWtWeaponFrontAdjustPercent { get; set; }
    public float MaxWtWeaponRearAdjustPercent { get; set; }
    public float MaxWtWeaponTurretAdjustPercent { get; set; }
    public int PowerAdjust { get; set; }
    public float PowerAdjustPercent { get; set; }
    public float SpeedAdjustPercent { get; set; }
    public float SteeringFullSpeedLimitAdjust { get; set; }
    public float SteeringMaxAngleAdjust { get; set; }
    public int TorqueMaxAdjust { get; set; }
    public float TorqueMaxAdjustPercent { get; set; }

    public PrefixVehicle(BinaryReader reader)
        : base(reader)
    {
        ArmorAdjustPercent = reader.ReadSingle();
        ArmorAdjust = reader.ReadInt32();
        PowerAdjustPercent = reader.ReadSingle();
        PowerAdjust = reader.ReadInt32();
        HeatMaximumAdjustPercent = reader.ReadSingle();
        HeatMaximumAdjust = reader.ReadInt32();
        CooldownAdjustPercent = reader.ReadSingle();
        CooldownAdjust = reader.ReadInt32();
        BrakesMaxTorqueFrontAdjustPercent = reader.ReadSingle();
        BrakesMaxTorqueRearAdjustPercent = reader.ReadSingle();
        SteeringMaxAngleAdjust = reader.ReadSingle();
        SteeringFullSpeedLimitAdjust = reader.ReadSingle();
        AVDNormalSpinDampeningAdjust = reader.ReadSingle();
        AVDCollisionSpinDampeningAdjust = reader.ReadSingle();
        TorqueMaxAdjustPercent = reader.ReadSingle();
        TorqueMaxAdjust = reader.ReadInt32();
        SpeedAdjustPercent = reader.ReadSingle();
        MaxWtWeaponFrontAdjustPercent = reader.ReadSingle();
        MaxWtWeaponTurretAdjustPercent = reader.ReadSingle();
        MaxWtWeaponRearAdjustPercent = reader.ReadSingle();
        MaxWtArmorAdjustPercent = reader.ReadSingle();
        MaxWtPowerplantAdjustPercent = reader.ReadSingle();
        InventorySlotsAdjust = reader.ReadInt16();

        reader.ReadBytes(2);
    }
}
