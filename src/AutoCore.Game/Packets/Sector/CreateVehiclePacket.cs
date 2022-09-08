namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Utils.Extensions;

public class CreateVehiclePacket : CreateSimpleObjectPacket
{
    public override GameOpcode Opcode => GameOpcode.CreateVehicle;

    public long CoidCurrentOwner { get; set; }
    public int CoidSpawnOwner { get; set; }
    public int[] Tricks { get; } = new int[8];
    public uint PrimaryColor { get; set; }
    public uint SecondaryColor { get; set; }
    public short ArmorAdd { get; set; }
    public int PowerMaxAdd { get; set; }
    public int HeatMaxAdd { get; set; }
    public short CooldownAdd { get; set; }
    public short InventorySlots { get; set; }
    public float MaxWeightWeaponFront { get; set; }
    public float MaxWeightWeaponTurret { get; set; }
    public float MaxWeightWeaponRear { get; set; }
    public float MaxWeightArmor { get; set; }
    public float MaxWeightPowerPlant { get; set; }
    public float SpeedAdd { get; set; }
    public float BrakesMaxTorqueFrontMultiplier { get; set; }
    public float BrakesMaxTorqueRearAdjustMultiplier { get; set; }
    public float SteeringMaxAngleMultiplier { get; set; }
    public float SteeringFullSpeedLimitMultiplier { get; set; }
    public float AVDNormalSpinDampeningMultiplier { get; set; }
    public float AVDCollisionSpinDampeningMultiplier { get; set; }
    public float KMTravelled { get; set; }
    public bool IsTrailer { get; set; }
    public bool IsInventory { get; set; }
    public bool IsActive { get; set; }
    public byte Trim { get; set; }
    public CreateSimpleObjectPacket CreateOrnament { get; set; }
    public CreateSimpleObjectPacket CreateRaceItem { get; set; }
    public CreatePowerPlantPacket CreatePowerPlant { get; set; }
    public CreateWheelSetPacket CreateWheelSet { get; set; }
    public CreateArmorPacket CreateArmor { get; set; }
    public CreateWeaponPacket CreateWeaponMelee { get; set; }
    public CreateWeaponPacket[] CreateWeapons { get; set; } = new CreateWeaponPacket[3];
    public int CurrentPathId { get; set; }
    public int ExtraPathId { get; set; }
    public float PatrolDistance { get; set; }
    public bool PathReversing { get; set; }
    public bool PathIsRoad { get; set; }
    public int TemplateId { get; set; }
    public long MurdererCoid { get; set; }
    public int[] WeaponsCBID { get; } = new int[3];
    public string Name { get; set; }

    public override void Read(BinaryReader reader)
    {
        throw new NotImplementedException();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        writer.Write(CoidCurrentOwner);
        writer.Write(CoidSpawnOwner);

        for (var i = 0; i < 8; ++i)
            writer.Write(Tricks[i]);

        writer.Write(PrimaryColor);
        writer.Write(SecondaryColor);
        writer.Write(ArmorAdd);

        writer.BaseStream.Position += 2;

        writer.Write(PowerMaxAdd);
        writer.Write(HeatMaxAdd);
        writer.Write(CooldownAdd);
        writer.Write(InventorySlots);
        writer.Write(MaxWeightWeaponFront);
        writer.Write(MaxWeightWeaponTurret);
        writer.Write(MaxWeightWeaponRear);
        writer.Write(MaxWeightArmor);
        writer.Write(MaxWeightPowerPlant);
        writer.Write(SpeedAdd);
        writer.Write(BrakesMaxTorqueFrontMultiplier);
        writer.Write(BrakesMaxTorqueRearAdjustMultiplier);
        writer.Write(SteeringMaxAngleMultiplier);
        writer.Write(SteeringFullSpeedLimitMultiplier);
        writer.Write(AVDNormalSpinDampeningMultiplier);
        writer.Write(AVDCollisionSpinDampeningMultiplier);
        writer.Write(KMTravelled);
        writer.Write(IsTrailer);
        writer.Write(IsInventory);
        writer.Write(IsActive);
        writer.Write(Trim);

        writer.BaseStream.Position += 4;

        // Ornament
        writer.Write(GameOpcode.CreateSimpleObject);

        if (CreateOrnament != null)
        {
            CreateOrnament.Write(writer);
        }
        else
        {
            WriteEmptyPacket(writer);
        }

        // RaceItem
        writer.Write(GameOpcode.CreateSimpleObject);

        if (CreateRaceItem != null)
        {
            CreateRaceItem.Write(writer);
        }
        else
        {
            WriteEmptyPacket(writer);
        }

        // PowerPlant
        writer.Write(GameOpcode.CreatePowerPlant);

        if (CreatePowerPlant != null)
        {
            CreatePowerPlant.Write(writer);
        }
        else
        {
            CreatePowerPlantPacket.WriteEmptyPacket(writer);
        }

        // WheelSet
        writer.Write(GameOpcode.CreateWheelSet);

        CreateWheelSet.Write(writer);

        // Armor
        writer.Write(GameOpcode.CreateArmor);

        if (CreateArmor != null)
        {
            CreateArmor.Write(writer);
        }
        else
        {
            CreateArmorPacket.WriteEmptyPacket(writer);
        }

        // MeleeWeapon
        writer.Write(GameOpcode.CreateWeapon);

        if (CreateWeaponMelee != null)
        {
            CreateWeaponMelee.Write(writer);
        }
        else
        {
            CreateWeaponPacket.WriteEmptyPacket(writer);
        }

        // Front, Turret and Rear weapons
        for (var i = 0; i < 3; ++i)
        {
            writer.Write(GameOpcode.CreateWeapon);

            if (CreateWeapons[i] != null)
            {
                CreateWeapons[i].Write(writer);
            }
            else
            {
                CreateWeaponPacket.WriteEmptyPacket(writer);
            }
        }

        writer.Write(CurrentPathId);
        writer.Write(ExtraPathId);
        writer.Write(PatrolDistance);
        writer.Write(PathReversing);
        writer.Write(PathIsRoad);

        writer.BaseStream.Position += 2;

        writer.Write(TemplateId);

        writer.BaseStream.Position += 4;

        writer.Write(MurdererCoid);

        for (var i = 0; i < 3; ++i)
            writer.Write(CreateWeapons[i]?.CBID ?? -1);

        writer.WriteUtf8StringOn(Name, 33);

        writer.BaseStream.Position += 3;
    }
}
