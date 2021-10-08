using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace AutoCore.Game.Entities
{
    using Database.Char;
    using Database.Char.Models;
    using Packets.Sector;

    public class Vehicle : SimpleObject
    {
        #region Database Vehicle Data
        private VehicleData DBData { get; set; }
        #endregion

        public bool LoadFromDB(CharContext context, long coid)
        {
            DBData = context.Vehicles.Include(v => v.SimpleObjectBase).FirstOrDefault(v => v.Coid == coid);

            if (DBData == null)
                return false;

            LoadCloneBase(DBData.SimpleObjectBase.CBID);

            return true;
        }

        public override void WriteToPacket(CreateSimpleObjectPacket packet)
        {
            base.WriteToPacket(packet);

            if (packet is CreateVehiclePacket vehiclePacket)
            {
                vehiclePacket.CoidCurrentOwner = DBData.CharacterCoid;
                vehiclePacket.CoidSpawnOwner = 0;

                for (var i = 0; i < 8; ++i)
                    vehiclePacket.Tricks[i] = 0;

                vehiclePacket.PrimaryColor = DBData.PrimaryColor;
                vehiclePacket.SecondaryColor = DBData.SecondaryColor;
                vehiclePacket.ArmorAdd = 0;
                vehiclePacket.PowerMaxAdd = 0;
                vehiclePacket.HeatMaxAdd = 0;
                vehiclePacket.CooldownAdd = 0;
                vehiclePacket.InventorySlots = 0;
                vehiclePacket.MaxWeightWeaponFront = 0.0f;
                vehiclePacket.MaxWeightWeaponTurret = 0.0f;
                vehiclePacket.MaxWeightWeaponRear = 0.0f;
                vehiclePacket.MaxWeightArmor = 0.0f;
                vehiclePacket.MaxWeightPowerPlant = 0.0f;
                vehiclePacket.SpeedAdd = 0.0f;
                vehiclePacket.BrakesMaxTorqueFrontMultiplier = 0.0f;
                vehiclePacket.BrakesMaxTorqueRearAdjustMultiplier = 0.0f;
                vehiclePacket.SteeringMaxAngleMultiplier = 0.0f;
                vehiclePacket.SteeringFullSpeedLimitMultiplier = 0.0f;
                vehiclePacket.AVDNormalSpinDampeningMultiplier = 0.0f;
                vehiclePacket.AVDCollisionSpinDampeningMultiplier = 0.0f;
                vehiclePacket.KMTravelled = 0.0f;
                vehiclePacket.IsTrailer = false;
                vehiclePacket.IsInventory = false;
                vehiclePacket.IsActive = true;
                vehiclePacket.Trim = DBData.Trim;

                // TODO: sub-packets

                vehiclePacket.CurrentPathId = 0;
                vehiclePacket.ExtraPathId = 0;
                vehiclePacket.PatrolDistance = 0.0f;
                vehiclePacket.PathReversing = false;
                vehiclePacket.PathIsRoad = false;
                vehiclePacket.TemplateId = 0;
                vehiclePacket.MurdererCoid = 0;
                vehiclePacket.WeaponsCBID[0] = 0;
                vehiclePacket.WeaponsCBID[0] = 0;
                vehiclePacket.WeaponsCBID[0] = 0;
                vehiclePacket.Name = DBData.Name;
            }

            if (packet is CreateVehicleExtendedPacket extendedPacket)
            {
            }
        }
    }
}
