using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
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
    public Vector3 Velocity { get; private set; }
    public Vector3 AngularVelocity { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public float Acceleration { get; set; }
    public float Steering { get; set; }
    public float WantedTurretDirection { get; set; }
    public byte Firing { get; set; }
    public VehicleMovedFlags VehicleFlags { get; set; }

    // Server-side combat state (very lightweight)
    private long _lastFireMsFront;
    private long _lastFireMsTurret;
    private long _lastFireMsRear;

    // Chat-based combat feedback (this is NOT the "floating combat text"; it's the combat log/chat channel)
    // We'll experiment with multiple ChatTypes to see what the client renders differently.
    private static readonly Dictionary<long, long> _lastCombatMsgByAttackerMs = new();

    private static void TrySendCombatMessage(Character? attacker, string message, ChatType chatType = ChatType.CombatMessage_Regular)
    {
        try
        {
            var conn = attacker?.OwningConnection;
            if (conn == null)
                return;

            // Simple rate limit per attacker to avoid flooding client chat.
            var now = Environment.TickCount64;
            var key = attacker?.ObjectId.Coid ?? 0;
            if (key != 0)
            {
                if (_lastCombatMsgByAttackerMs.TryGetValue(key, out var last) && now - last < 200)
                    return;
                _lastCombatMsgByAttackerMs[key] = now;
            }

            var msgLen = (short)(Encoding.UTF8.GetByteCount(message) + 1); // include null terminator
            conn.SendGamePacket(new BroadcastPacket
            {
                ChatType = chatType,
                SenderCoid = (ulong)(attacker?.ObjectId.Coid ?? 0),
                IsGM = false,
                Sender = "Combat",
                MessageLength = msgLen,
                Message = message
            });

        }
        catch
        {
            // never let chat break combat loop
        }
    }

    private static void TrySendCombatHitProbe(Character? attacker, int actualDamage)
    {
        // Send minimal strings in different chat channels so we can see which UI the client uses.
        // (If none become "floating text", then floating text likely requires a different opcode/packet than Broadcast.)
        TrySendCombatMessage(attacker, actualDamage.ToString(), ChatType.CombatMessage_Regular);
        TrySendCombatMessage(attacker, actualDamage.ToString(), ChatType.CombatMessage_Health);
    }

    private static void TrySendCombatMissProbe(Character? attacker)
    {
        TrySendCombatMessage(attacker, "Miss", ChatType.CombatMessage_LowImportance);
        TrySendCombatMessage(attacker, "Miss", ChatType.CombatMessage_Regular);
    }
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

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

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

    public void HandleMovement(VehicleMovedPacket packet)
    {
        if (Ghost == null)
            return;

        if (packet.ObjectId != ObjectId)
            throw new Exception("WTF? Someone else moves me?");

        // Update position
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
            TrySendCombatMissProbe(attackerChar);
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


        var actualDamage = Target.TakeDamage(damage);
        TrySendCombatHitProbe(attackerChar, actualDamage);


        try
        {
            attackerChar?.OwningConnection?.SendGamePacket(new DamagePacket
            {
                Target = Target.ObjectId,
                Source = ObjectId,
                Damage = actualDamage,
                DamageType = 0,
                Flags = 0
            });
        }
        catch { }


        if (Target.GetCurrentHP() <= 0)
        {
            // Set the murderer before calling OnDeath so loot can be attributed
            Target.SetMurderer(this);
            Target.OnDeath(DeathType.Silent);
            TrySendCombatMessage(attackerChar, $"Killed {Target.GetType().Name}#{Target.ObjectId.Coid}", ChatType.CombatMessage_HighImportance);

        }
    }
}
