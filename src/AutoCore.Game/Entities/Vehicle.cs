using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

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
    public Vector3 Velocity { get; private set; }
    public Vector3 AngularVelocity { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public float Acceleration { get; set; }
    public float Steering { get; set; }
    public float WantedTurretDirection { get; set; }
    public byte Firing { get; set; }
    public VehicleMovedFlags VehicleFlags { get; set; }
    public InventoryManager Inventory => Owner?.GetAsCharacter()?.Inventory;

    // Server-side combat state (very lightweight)
    private long _lastFireMsFront;
    private long _lastFireMsTurret;
    private long _lastFireMsRear;

    // Retail floating numbers use EMSG_Sector_Damage (0x2023) → combat-event list (game+0xAA8).
    // Broadcast freeform floaters use a different list (game+0xAC0) and do not reliably render.
    private static readonly Dictionary<long, long> _lastCombatMsgByAttackerMs = new();

    [ExcludeFromCodeCoverage]
    private static void TrySendDamagePacket(
        Character? attacker,
        TFID source,
        TFID target,
        int actualDamage,
        DamagePacket.DamageEntryFlags flags = default)
    {
        try
        {
            var conn = attacker?.OwningConnection;
            if (conn == null)
                return;

            var now = Environment.TickCount64;
            var key = attacker?.ObjectId.Coid ?? 0;
            if (key != 0)
            {
                if (_lastCombatMsgByAttackerMs.TryGetValue(key, out var last) && now - last < 100)
                    return;
                _lastCombatMsgByAttackerMs[key] = now;
            }

            var packet = new DamagePacket { Source = source ?? new TFID() };
            packet.AddHit(target ?? new TFID(), actualDamage, flags);
            conn.SendGamePacket(packet, skipOpcode: true);
        }
        catch
        {
            // never let combat networking break the fire loop
        }
    }
    #endregion

    public Vehicle()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override Vehicle GetAsVehicle() => this;

    public bool TryFindEquippedItem(long coid, out VehicleEquipmentSlot slot, out SimpleObject item)
    {
        if (TryMatchEquippedItem(Armor, VehicleEquipmentSlot.Armor, coid, out slot, out item) ||
            TryMatchEquippedItem(PowerPlant, VehicleEquipmentSlot.PowerPlant, coid, out slot, out item) ||
            TryMatchEquippedItem(Ornament, VehicleEquipmentSlot.Ornament, coid, out slot, out item) ||
            TryMatchEquippedItem(RaceItem, VehicleEquipmentSlot.RaceItem, coid, out slot, out item) ||
            TryMatchEquippedItem(WeaponMelee, VehicleEquipmentSlot.WeaponMelee, coid, out slot, out item) ||
            TryMatchEquippedItem(WeaponFront, VehicleEquipmentSlot.WeaponFront, coid, out slot, out item) ||
            TryMatchEquippedItem(WeaponTurret, VehicleEquipmentSlot.WeaponTurret, coid, out slot, out item) ||
            TryMatchEquippedItem(WeaponRear, VehicleEquipmentSlot.WeaponRear, coid, out slot, out item) ||
            TryMatchEquippedItem(WheelSet, VehicleEquipmentSlot.WheelSet, coid, out slot, out item))
        {
            return true;
        }

        slot = default;
        item = null;
        return false;
    }

    public bool TryFindEquippedItem(long coid, int cbid, out VehicleEquipmentSlot slot, out SimpleObject item)
    {
        if (coid > 0 && TryFindEquippedItem(coid, out slot, out item))
            return true;

        if (cbid > 0 && TryFindEquippedItemByCbid(cbid, out slot, out item))
            return true;

        slot = default;
        item = null;
        return false;
    }

    public bool TryUnequipItem(long coid, out VehicleEquipmentSlot slot, out SimpleObject item)
    {
        if (!TryFindEquippedItem(coid, out slot, out item))
            return false;

        return ClearEquipmentSlot(slot);
    }

    /// <summary>
    /// Equips <paramref name="item"/> into <paramref name="slot"/>, replacing any occupant.
    /// Returns the previous occupant (if any) so callers can move it to cargo.
    /// </summary>
    public bool TryEquipItem(VehicleEquipmentSlot slot, SimpleObject item, out SimpleObject previousItem)
    {
        previousItem = null;
        if (item == null)
            return false;

        previousItem = GetEquippedItem(slot);
        if (!AssignEquipmentSlot(slot, item))
        {
            previousItem = null;
            return false;
        }

        return true;
    }

    public SimpleObject GetEquippedItem(VehicleEquipmentSlot slot)
    {
        return slot switch
        {
            VehicleEquipmentSlot.Armor => Armor,
            VehicleEquipmentSlot.PowerPlant => PowerPlant,
            VehicleEquipmentSlot.Ornament => Ornament,
            VehicleEquipmentSlot.RaceItem => RaceItem,
            VehicleEquipmentSlot.WeaponMelee => WeaponMelee,
            VehicleEquipmentSlot.WeaponFront => WeaponFront,
            VehicleEquipmentSlot.WeaponTurret => WeaponTurret,
            VehicleEquipmentSlot.WeaponRear => WeaponRear,
            VehicleEquipmentSlot.WheelSet => WheelSet,
            _ => null
        };
    }

    public VehicleEquipmentSnapshot CreateEquipmentSnapshot()
    {
        return new VehicleEquipmentSnapshot(
            Ornament?.ObjectId.Coid ?? DBData?.Ornament ?? 0,
            RaceItem?.ObjectId.Coid ?? DBData?.RaceItem ?? 0,
            PowerPlant?.ObjectId.Coid ?? DBData?.PowerPlant ?? 0,
            WheelSet?.ObjectId.Coid ?? DBData?.Wheelset ?? 0,
            Armor?.ObjectId.Coid ?? DBData?.Armor ?? 0,
            WeaponMelee?.ObjectId.Coid ?? DBData?.MeleeWeapon ?? 0,
            WeaponFront?.ObjectId.Coid ?? DBData?.Front ?? 0,
            WeaponTurret?.ObjectId.Coid ?? DBData?.Turret ?? 0,
            WeaponRear?.ObjectId.Coid ?? DBData?.Rear ?? 0);
    }

    private bool ClearEquipmentSlot(VehicleEquipmentSlot slot)
    {
        switch (slot)
        {
            case VehicleEquipmentSlot.Armor:
                Armor = null;
                if (DBData != null) DBData.Armor = 0;
                EnsureGhostMaskDelivery(GhostVehicle.ChangeArmor);
                return true;

            case VehicleEquipmentSlot.PowerPlant:
                PowerPlant = null;
                if (DBData != null) DBData.PowerPlant = 0;
                return true;

            case VehicleEquipmentSlot.Ornament:
                Ornament = null;
                if (DBData != null) DBData.Ornament = 0;
                EnsureGhostMaskDelivery(GhostVehicle.OrnamentMask);
                return true;

            case VehicleEquipmentSlot.RaceItem:
                RaceItem = null;
                if (DBData != null) DBData.RaceItem = 0;
                return true;

            case VehicleEquipmentSlot.WeaponMelee:
                WeaponMelee = null;
                if (DBData != null) DBData.MeleeWeapon = 0;
                EnsureGhostMaskDelivery(GhostVehicle.MeleeWeaponMask);
                return true;

            case VehicleEquipmentSlot.WeaponFront:
                WeaponFront = null;
                if (DBData != null) DBData.Front = 0;
                EnsureGhostMaskDelivery(GhostVehicle.FrontWeaponMask);
                return true;

            case VehicleEquipmentSlot.WeaponTurret:
                WeaponTurret = null;
                if (DBData != null) DBData.Turret = 0;
                EnsureGhostMaskDelivery(GhostVehicle.TurretWeaponMask);
                return true;

            case VehicleEquipmentSlot.WeaponRear:
                WeaponRear = null;
                if (DBData != null) DBData.Rear = 0;
                EnsureGhostMaskDelivery(GhostVehicle.RearWeaponMask);
                return true;

            case VehicleEquipmentSlot.WheelSet:
                WheelSet = null;
                if (DBData != null) DBData.Wheelset = 0;
                EnsureGhostMaskDelivery(GhostVehicle.WheelSetMask);
                return true;

            default:
                return false;
        }
    }

    private bool AssignEquipmentSlot(VehicleEquipmentSlot slot, SimpleObject item)
    {
        var coid = item.ObjectId.Coid;
        switch (slot)
        {
            case VehicleEquipmentSlot.Armor:
                if (item is not Armor armor)
                    return false;
                Armor = armor;
                if (DBData != null) DBData.Armor = coid;
                EnsureGhostMaskDelivery(GhostVehicle.ChangeArmor);
                return true;

            case VehicleEquipmentSlot.PowerPlant:
                if (item is not PowerPlant powerPlant)
                    return false;
                PowerPlant = powerPlant;
                if (DBData != null) DBData.PowerPlant = coid;
                return true;

            case VehicleEquipmentSlot.Ornament:
                Ornament = item;
                if (DBData != null) DBData.Ornament = coid;
                EnsureGhostMaskDelivery(GhostVehicle.OrnamentMask);
                return true;

            case VehicleEquipmentSlot.RaceItem:
                RaceItem = item;
                if (DBData != null) DBData.RaceItem = coid;
                return true;

            case VehicleEquipmentSlot.WeaponMelee:
                if (item is not Weapon melee)
                    return false;
                WeaponMelee = melee;
                if (DBData != null) DBData.MeleeWeapon = coid;
                EnsureGhostMaskDelivery(GhostVehicle.MeleeWeaponMask);
                return true;

            case VehicleEquipmentSlot.WeaponFront:
                if (item is not Weapon front)
                    return false;
                WeaponFront = front;
                if (DBData != null) DBData.Front = coid;
                EnsureGhostMaskDelivery(GhostVehicle.FrontWeaponMask);
                return true;

            case VehicleEquipmentSlot.WeaponTurret:
                if (item is not Weapon turret)
                    return false;
                WeaponTurret = turret;
                if (DBData != null) DBData.Turret = coid;
                EnsureGhostMaskDelivery(GhostVehicle.TurretWeaponMask);
                return true;

            case VehicleEquipmentSlot.WeaponRear:
                if (item is not Weapon rear)
                    return false;
                WeaponRear = rear;
                if (DBData != null) DBData.Rear = coid;
                EnsureGhostMaskDelivery(GhostVehicle.RearWeaponMask);
                return true;

            case VehicleEquipmentSlot.WheelSet:
                if (item is not WheelSet wheelSet)
                    return false;
                WheelSet = wheelSet;
                if (DBData != null) DBData.Wheelset = coid;
                EnsureGhostMaskDelivery(GhostVehicle.WheelSetMask);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Marks a ghost mask dirty only after ensuring this vehicle ghost has a connection
    /// GhostInfo ref. Without that ref, SetMaskBits is collapsed and discarded.
    /// </summary>
    public void EnsureGhostMaskDelivery(ulong mask)
    {
        if (Ghost == null)
            CreateGhost();

        if (Owner?.GetAsCharacter()?.OwningConnection is TNL.TNLConnection connection && Ghost != null)
        {
            if (Ghost.GetFirstObjectRef() == null)
                connection.ObjectLocalScopeAlways(Ghost);
            else
                connection.ObjectInScope(Ghost);
        }

        Ghost?.SetMaskBits(mask);
    }

    private static bool TryMatchEquippedItem(
        SimpleObject equippedItem,
        VehicleEquipmentSlot candidateSlot,
        long coid,
        out VehicleEquipmentSlot slot,
        out SimpleObject item)
    {
        if (equippedItem?.ObjectId.Coid == coid)
        {
            slot = candidateSlot;
            item = equippedItem;
            return true;
        }

        slot = default;
        item = null;
        return false;
    }

    private bool TryFindEquippedItemByCbid(int cbid, out VehicleEquipmentSlot slot, out SimpleObject item)
    {
        foreach (var (candidateSlot, equippedItem) in EnumerateEquippedItems())
        {
            if (equippedItem?.CBID == cbid)
            {
                slot = candidateSlot;
                item = equippedItem;
                return true;
            }
        }

        slot = default;
        item = null;
        return false;
    }

    public IEnumerable<(VehicleEquipmentSlot Slot, SimpleObject Item)> EnumerateEquippedItems()
    {
        yield return (VehicleEquipmentSlot.Armor, Armor);
        yield return (VehicleEquipmentSlot.PowerPlant, PowerPlant);
        yield return (VehicleEquipmentSlot.Ornament, Ornament);
        yield return (VehicleEquipmentSlot.RaceItem, RaceItem);
        yield return (VehicleEquipmentSlot.WeaponMelee, WeaponMelee);
        yield return (VehicleEquipmentSlot.WeaponFront, WeaponFront);
        yield return (VehicleEquipmentSlot.WeaponTurret, WeaponTurret);
        yield return (VehicleEquipmentSlot.WeaponRear, WeaponRear);
        yield return (VehicleEquipmentSlot.WheelSet, WheelSet);
    }

    [ExcludeFromCodeCoverage]
    public override bool LoadFromDB(CharContext context, long coid, bool isInCharacterSelection = false)
    {
        SetCoid(coid, true);

        DBData = context.Vehicles.Include(v => v.SimpleObjectBase).FirstOrDefault(v => v.Coid == coid);

        if (DBData == null)
            return false;

        LoadCloneBase(DBData.SimpleObjectBase.CBID);

        Position = new(DBData.PositionX, DBData.PositionY, DBData.PositionZ);
        Rotation = new(DBData.RotationX, DBData.RotationY, DBData.RotationZ, DBData.RotationW);
        HP = MaxHP = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;

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
        // TODO: or maybe just load/send everything always and no such workarounds are needed?
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

    [ExcludeFromCodeCoverage]
    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostVehicle();
        Ghost.SetParent(this);
    }

    [ExcludeFromCodeCoverage]
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
            InventoryPacketFactory.ConfigureVehicleCargo(vehiclePacket, Inventory);
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
            vehiclePacket.IsActive = Map != null && !Map.MapData.ContinentObject.IsTown;
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

            if (WheelSet != null)
            {
                vehiclePacket.CreateWheelSet = new CreateWheelSetPacket();
                WheelSet.WriteToPacket(vehiclePacket.CreateWheelSet);
            }

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
    }

    [ExcludeFromCodeCoverage]
    public void EnterMap(SectorMap map, Vector3? position = null)
    {
        Position = position.Value;
        Rotation = Quaternion.Default;

        DBData.PositionX = Position.X;
        DBData.PositionY = Position.Y;
        DBData.PositionZ = Position.Z;
        DBData.RotationX = Rotation.X;
        DBData.RotationY = Rotation.Y;
        DBData.RotationZ = Rotation.Z;
        DBData.RotationW = Rotation.W;
    }

    /// <summary>
    /// Writes pose into attached <see cref="VehicleData"/>. No DB I/O.
    /// </summary>
    public void CaptureWorldStateToDb(Vector3 position, Quaternion rotation)
    {
        if (DBData == null)
            return;

        DBData.PositionX = position.X;
        DBData.PositionY = position.Y;
        DBData.PositionZ = position.Z;
        DBData.RotationX = rotation.X;
        DBData.RotationY = rotation.Y;
        DBData.RotationZ = rotation.Z;
        DBData.RotationW = rotation.W;
    }

    internal void AttachTestDataForTests(string name = "TestVehicle")
    {
        DBData = new VehicleData
        {
            Coid = ObjectId.Coid,
            Name = name
        };
    }

    internal float GetDbPositionXForTests() => DBData?.PositionX ?? float.NaN;
    internal float GetDbPositionZForTests() => DBData?.PositionZ ?? float.NaN;
    internal float GetDbRotationYForTests() => DBData?.RotationY ?? float.NaN;
    internal float GetDbRotationWForTests() => DBData?.RotationW ?? float.NaN;

    [ExcludeFromCodeCoverage]
    public void HandleMovement(VehicleMovedPacket packet)
    {
        if (packet.ObjectId != ObjectId)
            throw new Exception("WTF? Someone else moves me?");

        // Update position (even if ghost is not ready — exploration must still track travel).
        Position = packet.Location;
        Rotation = packet.Rotation;
        Velocity = packet.Velocity;
        AngularVelocity = packet.AngularVelocity;
        TargetPosition = packet.TargetPosition;
        Acceleration = packet.Acceleration;
        Steering = packet.Steering;
        WantedTurretDirection = packet.TurretDirection;
        VehicleFlags = packet.VehicleFlags;
        Firing = packet.Firing;

        ExplorationManager.Instance.OnVehicleMoved(this);

        if (Ghost == null)
            return;

        Ghost.SetMaskBits(GhostObject.PositionMask);

        // Update target - check map first (for local NPCs/creatures), then ObjectManager (for global objects)
        if (Target != null)
        {
            if (packet.Target.Coid == -1)
            {
                Target = null;

                Ghost.SetMaskBits(GhostObject.TargetMask);
            }
            else if (packet.Target != Target.ObjectId)
            {
                // Try map first (handles local objects like NPCs/creatures)
                if (Map != null)
                    Target = Map.GetObject(packet.Target.Coid);
                
                // Fallback to ObjectManager (for global objects like players)
                if (Target == null)
                    Target = ObjectManager.Instance.GetObject(packet.Target);

                Ghost.SetMaskBits(GhostObject.TargetMask);
            }
        }
        else if (packet.Target.Coid != -1)
        {

            // Try map first (handles local objects like NPCs/creatures)
            // Use GetObjectByCoid which searches by COID regardless of Global flag
            if (Map != null)
            {
                Target = Map.GetObjectByCoid(packet.Target.Coid);
                
                // If not found, try the standard GetObject method
                if (Target == null)
                    Target = Map.GetObject(packet.Target.Coid);
            }
            
            // Fallback to ObjectManager (for global objects like players)
            if (Target == null)
                Target = ObjectManager.Instance.GetObject(packet.Target);


            Ghost.SetMaskBits(GhostObject.TargetMask);
        }

        ProcessCombatIfFiring();
    }

    // Called from both movement packets AND the server tick, so holding fire works even if VehicleMoved packets are sparse.
    [ExcludeFromCodeCoverage]
    public void ProcessCombatIfFiring()
    {
        if (Ghost == null)
            return;

        // Process combat when firing (server authoritative)
        if (Firing > 0 && Target != null && !Target.IsCorpse && !Target.IsInvincible)
        {

            // (Reuse the existing combat implementation by re-entering HandleMovement�s block via a local call path.)
            // NOTE: The actual combat logic remains unchanged; this method just makes it reachable from the server loop.
            // We intentionally keep all the existing logs in-place by calling the same code path.
            //
            // For now, simply inline-call the same logic by invoking HandleMovement's tail via a no-op pattern:
            // the code below is identical to the previous block, kept in this file for clarity.
            //
            // To keep this patch small, we call into the existing logic by temporarily delegating to a private helper.
            ProcessCombatInternal();
        }
    }

    [ExcludeFromCodeCoverage]
    private void ProcessCombatInternal()
    {
        // Process combat when firing (server authoritative)
        if (Firing <= 0 || Target == null || Target.IsCorpse || Target.IsInvincible)
            return;



        // Determine which weapon is firing (bit flags: 1=front, 2=turret, 4=rear)
        Weapon firingWeapon = null;
        ref var lastFireRef = ref _lastFireMsTurret; // default
        if ((Firing & 1) != 0 && WeaponFront != null)
        {
            firingWeapon = WeaponFront;
            lastFireRef = ref _lastFireMsFront;
        }
        else if ((Firing & 2) != 0 && WeaponTurret != null)
        {
            firingWeapon = WeaponTurret;
            lastFireRef = ref _lastFireMsTurret;
        }
        else if ((Firing & 4) != 0 && WeaponRear != null)
        {
            firingWeapon = WeaponRear;
            lastFireRef = ref _lastFireMsRear;
        }


        if (firingWeapon == null || firingWeapon.CloneBaseWeapon == null)
        {
            return;
        }

        var nowMs = Environment.TickCount64;
        var weaponSpec = firingWeapon.CloneBaseWeapon.WeaponSpecific;

        // Cooldown / rate-of-fire gating
        var cooldownMs = weaponSpec.RechargeTime > 0 ? weaponSpec.RechargeTime : 500;
        if (nowMs - lastFireRef < cooldownMs)
            return;
        lastFireRef = nowMs;

        // Range gating
        var dist = Position.Dist(Target.Position);
        if ((weaponSpec.RangeMin > 0 && dist < weaponSpec.RangeMin) || (weaponSpec.RangeMax > 0 && dist > weaponSpec.RangeMax))
        {
            return;
        }

        // Hit chance
        var attackerLevel = Owner?.GetAsCreature()?.GetLevel() ?? 1;
        var attackerChar = Owner?.GetAsCharacter();
        var attackRating = weaponSpec.OffenseBonus + (weaponSpec.HitBonusPerLevel * attackerLevel);
        var targetDefenseBonus = 0;
        if (Target is Vehicle targetVeh && targetVeh.Armor?.CloneBaseArmor?.ArmorSpecific != null)
            targetDefenseBonus = targetVeh.Armor.CloneBaseArmor.ArmorSpecific.DefenseBonus;

        var hitChance = 0.65f;
        hitChance += (float)(attackRating - targetDefenseBonus) / 200.0f;
        if (weaponSpec.AccucaryModifier > 0)
            hitChance *= weaponSpec.AccucaryModifier;
        hitChance = Math.Clamp(hitChance, 0.05f, 0.95f);

        var rng = new Random(unchecked((int)(nowMs ^ ObjectId.Coid ^ Target.ObjectId.Coid)));
        var roll = (float)rng.NextDouble();
        if (roll > hitChance)
        {
            // No floater on miss: client only shows "Miss" via a local-sim flag we cannot set
            // from the Damage packet (event+0x2A is hardcoded 0 in the recv path).
            return;
        }

        // Damage roll per damage-type (6 channels)
        var dmgByType = new int[6];
        var totalPreMit = 0;
        if (weaponSpec.MinMin.Damage != null && weaponSpec.MaxMax.Damage != null)
        {
            for (var i = 0; i < 6; i++)
            {
                var min = (int)weaponSpec.MinMin.Damage[i];
                var max = (int)weaponSpec.MaxMax.Damage[i];
                if (max < min) (min, max) = (max, min);
                var val = max > min ? rng.Next(min, max + 1) : min;
                dmgByType[i] = Math.Max(0, val);
                totalPreMit += dmgByType[i];
            }
        }

        // Fallback to simple min/max if damage arrays are empty
        if (totalPreMit <= 0)
        {
            var minDmg = weaponSpec.DmgMinMin;
            var maxDmg = weaponSpec.DmgMaxMax;
            if (maxDmg < minDmg) (minDmg, maxDmg) = (maxDmg, minDmg);
            totalPreMit = maxDmg > minDmg ? rng.Next(minDmg, maxDmg + 1) : Math.Max(1, minDmg);
        }

        // Apply damage modifiers
        var scalar = weaponSpec.DamageScalar > 0 ? weaponSpec.DamageScalar : 1.0f;
        var dmgBonus = 1.0f + (weaponSpec.DamageBonusPerLevel * attackerLevel);
        var damage = (int)MathF.Round(Math.Max(1, totalPreMit) * scalar * dmgBonus);


        var hpBefore = Target.GetCurrentHP();
        var actualDamage = Target.TakeDamage(damage);

        if (actualDamage <= 0)
        {
            Logger.WriteLog(LogType.Debug,
                "Combat: TakeDamage returned 0 for {0} coid={1} inv={2} corpse={3} hp={4}/{5} rolled={6}",
                Target.GetType().Name,
                Target.ObjectId.Coid,
                Target.IsInvincible,
                Target.IsCorpse,
                hpBefore,
                Target.GetMaximumHP(),
                damage);
        }
        else
        {
            // Non-zero amount required for client combat-text / local HP apply (FUN_00812A60).
            TrySendDamagePacket(attackerChar, ObjectId, Target.ObjectId, actualDamage);
        }

        if (Target.GetCurrentHP() <= 0)
        {
            Target.SetMurderer(this);
            Target.OnDeath(DeathType.Silent);
        }
    }
}
