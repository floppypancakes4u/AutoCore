using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Packets.Sector
{
    using Constants;
    using Extensions;

    public class CreateVehiclePacket : CreateSimpleObjectPacket
    {
        public override GameOpcode Opcode => GameOpcode.CreateVehicle;

        public long CoidCurrentOwner { get; set; }
        public int CoidSpawnOwner { get; set; }
        public int[] Tricks { get; } = new int[8];
        public int PrimaryColor { get; set; }
        public int SecondaryColor { get; set; }
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
        public CreateSimpleObjectPacket CreateOrnamentPacket { get; set; }
        public CreateSimpleObjectPacket CreateRaceItemPacket { get; set; }
        public CreatePowerPlantPacket CreatePowerPlantPacket { get; set; }
        public CreateWheelSetPacket CreateWheelSetPacket { get; set; }
        public CreateArmorPacket CreateArmorPacket { get; set; }
        public CreateWeaponPacket CreateWeaponMeleePacket { get; set; }
        public CreateWeaponPacket[] CreateWeaponPackets { get; set; } = new CreateWeaponPacket[3];
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

            if (CreateOrnamentPacket != null)
            {
                CreateOrnamentPacket.Write(writer);
            }
            else
            {
                WriteEmptySimpleObjectPacket(writer);
            }

            // Race Item
            writer.Write(GameOpcode.CreateSimpleObject);

            if (CreateRaceItemPacket != null)
            {
                CreateRaceItemPacket.Write(writer);
            }
            else
            {
                WriteEmptySimpleObjectPacket(writer);
            }
        }
    }
}
