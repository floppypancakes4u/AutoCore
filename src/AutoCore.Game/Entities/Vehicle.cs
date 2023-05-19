using Microsoft.EntityFrameworkCore;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL.Ghost;

public class Vehicle : SimpleObject
{
    #region Properties
    #region Database Vehicle Data
    private VehicleData DBData { get; set; }
    public string Name => DBData.Name;
    public uint PrimaryColor => DBData.PrimaryColor;
    public uint SecondaryColor => DBData.SecondaryColor;
    public byte Trim => DBData.Trim;
    #endregion

    public Armor Armor { get; private set; }
    public PowerPlant PowerPlant { get; private set; }
    public SimpleObject Ornament { get; private set; }
    public SimpleObject RaceItem { get; private set; }
    public Weapon WeaponMelee { get; private set; }
    public Weapon WeaponFront { get; private set; }
    public Weapon WeaponTurret { get; private set; }
    public Weapon WeaponRear { get; private set; }
    public WheelSet WheelSet { get; private set; }
    #endregion

    public Vehicle()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override Vehicle GetAsVehicle() => this;

    public override bool LoadFromDB(CharContext context, long coid, bool isInCharacterSelection = false)
    {
        SetCoid(coid, true);

        DBData = context.Vehicles.Include(v => v.SimpleObjectBase).FirstOrDefault(v => v.Coid == coid);

        if (DBData == null)
            return false;

        LoadCloneBase(DBData.SimpleObjectBase.CBID);

        Position = new(DBData.PositionX, DBData.PositionY, DBData.PositionZ);
        Rotation = new(DBData.RotationX, DBData.RotationY, DBData.RotationZ, DBData.RotationW);

        WheelSet = new WheelSet();
        if (!WheelSet.LoadFromDB(context, DBData.Wheelset))
        {
            return false;
        }

        if (DBData.MeleeWeapon != 0)
        {
            WeaponMelee = new Weapon();
            if (!WeaponMelee.LoadFromDB(context, DBData.MeleeWeapon))
            {
                return false;
            }
        }

        if (DBData.Front != 0)
        {
            WeaponFront = new Weapon();
            if (!WeaponFront.LoadFromDB(context, DBData.Front))
            {
                return false;
            }
        }

        if (DBData.Turret != 0)
        {
            WeaponTurret = new Weapon();
            if (!WeaponTurret.LoadFromDB(context, DBData.Turret))
            {
                return false;
            }
        }

        if (DBData.Rear != 0)
        {
            WeaponRear = new Weapon();
            if (!WeaponRear.LoadFromDB(context, DBData.Rear))
            {
                return false;
            }
        }

        // Skip loading other unnecessary stuff from the DB, if we are displaying this Vehicle in the character selection
        if (isInCharacterSelection)
            return true;

        if (DBData.Armor != 0)
        {
            Armor = new Armor();
            if (!Armor.LoadFromDB(context, DBData.Armor))
            {
                return false;
            }
        }

        if (DBData.Ornament != 0)
        {
            Ornament = new SimpleObject(GraphicsObjectType.Graphics);
            if (!Ornament.LoadFromDB(context, DBData.Ornament))
            {
                return false;
            }
        }

        if (DBData.RaceItem != 0)
        {
            RaceItem = new SimpleObject(GraphicsObjectType.Graphics);
            if (!RaceItem.LoadFromDB(context, DBData.RaceItem))
            {
                return false;
            }
        }

        if (DBData.PowerPlant != 0)
        {
            PowerPlant = new PowerPlant();
            if (!PowerPlant.LoadFromDB(context, DBData.PowerPlant))
            {
                return false;
            }
        }

        return true;
    }

    public override void CreateGhost()
    {
        Ghost = new GhostVehicle();
        Ghost.SetParent(this);
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateVehiclePacket vehiclePacket)
        {
            vehiclePacket.CoidCurrentOwner = DBData.CharacterCoid;
            vehiclePacket.CoidSpawnOwner = -1;

            for (var i = 0; i < 8; ++i)
                vehiclePacket.Tricks[i] = -1;

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
            vehiclePacket.SpeedAdd = 1.0f;
            vehiclePacket.BrakesMaxTorqueFrontMultiplier = 1.0f;
            vehiclePacket.BrakesMaxTorqueRearAdjustMultiplier = 1.0f;
            vehiclePacket.SteeringMaxAngleMultiplier = 1.0f;
            vehiclePacket.SteeringFullSpeedLimitMultiplier = 1.0f;
            vehiclePacket.AVDNormalSpinDampeningMultiplier = 1.0f;
            vehiclePacket.AVDCollisionSpinDampeningMultiplier = 1.0f;
            vehiclePacket.KMTravelled = 0.0f;
            vehiclePacket.IsTrailer = false;
            vehiclePacket.IsInventory = false;
            vehiclePacket.IsActive = true;
            vehiclePacket.Trim = DBData.Trim;

            if (Ornament != null)
            {
                vehiclePacket.CreateOrnament = new CreateSimpleObjectPacket();
                Ornament.WriteToPacket(vehiclePacket.CreateOrnament);
            }

            if (RaceItem != null)
            {
                vehiclePacket.CreateRaceItem = new CreateSimpleObjectPacket();
                RaceItem.WriteToPacket(vehiclePacket.CreateRaceItem);
            }

            if (PowerPlant != null)
            {
                vehiclePacket.CreatePowerPlant = new CreatePowerPlantPacket();
                PowerPlant.WriteToPacket(vehiclePacket.CreatePowerPlant);
            }

            vehiclePacket.CreateWheelSet = new CreateWheelSetPacket();
            WheelSet.WriteToPacket(vehiclePacket.CreateWheelSet);

            if (Armor != null)
            {
                vehiclePacket.CreateArmor = new CreateArmorPacket();
                Armor.WriteToPacket(vehiclePacket.CreateArmor);
            }

            if (WeaponMelee != null)
            {
                vehiclePacket.CreateWeaponMelee = new CreateWeaponPacket();
                WeaponMelee.WriteToPacket(vehiclePacket.CreateWeaponMelee);
            }

            if (WeaponFront != null)
            {
                vehiclePacket.CreateWeapons[0] = new CreateWeaponPacket();
                WeaponFront.WriteToPacket(vehiclePacket.CreateWeapons[0]);
            }

            if (WeaponTurret != null)
            {
                vehiclePacket.CreateWeapons[1] = new CreateWeaponPacket();
                WeaponTurret.WriteToPacket(vehiclePacket.CreateWeapons[1]);
            }

            if (WeaponRear != null)
            {
                vehiclePacket.CreateWeapons[2] = new CreateWeaponPacket();
                WeaponRear.WriteToPacket(vehiclePacket.CreateWeapons[2]);
            }

            vehiclePacket.CurrentPathId = -1;
            vehiclePacket.ExtraPathId = 0;
            vehiclePacket.PatrolDistance = 0.0f;
            vehiclePacket.PathReversing = false;
            vehiclePacket.PathIsRoad = false;
            vehiclePacket.TemplateId = -1;
            vehiclePacket.MurdererCoid = -1L;
            vehiclePacket.WeaponsCBID[0] = WeaponFront?.CBID ?? -1;
            vehiclePacket.WeaponsCBID[1] = WeaponTurret?.CBID ?? -1;
            vehiclePacket.WeaponsCBID[2] = WeaponRear?.CBID ?? -1;
            vehiclePacket.Name = DBData.Name;
        }

        if (packet is CreateVehicleExtendedPacket extendedPacket)
        {
            // TODO
        }
    }
}
