using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public class Vehicle : SimpleObject
{
    #region Properties
    #region Database Vehicle Data
    private VehicleData DBData { get; set; }

    // NPC vehicles have no DB row (no owning character); fall back to defaults rather than NPE.
    public string Name => DBData?.Name ?? string.Empty;
    public uint PrimaryColor => DBData?.PrimaryColor ?? 0u;
    public uint SecondaryColor => DBData?.SecondaryColor ?? 0u;
    public byte Trim => DBData?.Trim ?? (byte)0;
    #endregion

    #region NPC path / AI fields (NPC.md)
    public long CoidCurrentPath { get; set; } = -1;
    public int ExtraPathId { get; set; } = -1;
    public float PatrolDistance { get; set; }
    public bool PathReversing { get; set; }
    public bool PathIsRoad { get; set; }
    public int TemplateId { get; set; } = -1;
    public long SpawnOwnerCoid { get; set; } = -1;

    /// <summary>Server-side AI runtime state; null for player vehicles.</summary>
    public NpcAiState NpcAi { get; set; }
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

    public int CurrentShield { get; private set; }
    public int MaxShield { get; private set; }
    public int ShieldRegenRate { get; private set; }

    /// <summary>Current chassis heat (client vehicle+0x150).</summary>
    public int CurrentHeat { get; private set; }

    /// <summary>Heat capacity from power plant (client vehicle+0x244).</summary>
    public int MaxHeat { get; private set; }

    /// <summary>Cool rate per 3000 ms pool pulse (PP CoolRate + vehicle adjust).</summary>
    public int CoolRate { get; private set; }

    /// <summary>Power regen per 3000 ms pool pulse from power plant.</summary>
    public int PowerRegenRate { get; private set; }

    /// <summary>
    /// Cached RaceItem RaceRegenRate (Tribe Locus). Not applied — vehicle HP does not recharge.
    /// </summary>
    public int HpRegenRate { get; private set; }

    /// <summary>Wall-clock ms accumulated toward the next 3000 ms client pool pulse.</summary>
    internal int PoolTickAccumulatorMs { get; set; }

    /// <summary>Empty-shield debounce counter (client action +0x25).</summary>
    internal int ShieldEmptyDebounce { get; set; }

    /// <summary>Heat-at-max debounce counter (client action +0x24).</summary>
    internal int HeatAtMaxDebounce { get; set; }

    /// <inheritdoc />
    /// Vehicles must scope the ghost before dirtying or SetMaskBits is discarded.
    protected override void DirtyHealthMasks(ulong mask) => EnsureGhostMaskDelivery(mask);

    /// <summary>
    /// Absolute CharacterLevel Health snapshot for the owning client (0x2017), matching /power.
    /// Heat/shield stay on ghost masks only.
    /// </summary>
    protected override void NotifyOwnerHealthHud(bool sendPacket = true)
    {
        if (Owner is Character owner)
            CharacterLevelManager.Instance.SyncOwnedCombatHud(owner, sendPacket);
    }

    /// <summary>
    /// Recompute MaxHP/HP from chassis ArmorAdd + equipped armor ArmorFactor + owner Tech/level/race/class.
    /// Retail: <c>Vehicle_CalcMaxHitPoints</c> @ 0x005002D0 (clonebase MaxHitPoint is almost always 1).
    /// </summary>
    /// <param name="refillCurrent">When true, set current HP to the new maximum (login / equip).</param>
    public void RecalculateMaximumHitPoints(bool refillCurrent = true, bool triggerGhostUpdate = true)
    {
        var chassisArmorAdd = (CloneBaseObject as CloneBaseVehicle)?.VehicleSpecific.ArmorAdd ?? 0;
        short armorFactor = 0;
        if (Armor?.CloneBaseArmor?.ArmorSpecific != null)
            armorFactor = Armor.CloneBaseArmor.ArmorSpecific.ArmorFactor;

        var max = 1;
        if (Owner is Character owner)
        {
            byte race = 0;
            byte classId = 0;
            if (owner.CloneBaseObject is CloneBaseCharacter charCb)
            {
                race = charCb.CharacterSpecific.Race;
                classId = charCb.CharacterSpecific.Class;
            }

            max = VehicleHitPointCalculator.CalculatePlayerMaxHp(
                race,
                classId,
                owner.Level,
                owner.AttributeTech,
                armorFactor,
                chassisArmorAdd);
        }
        else
        {
            // NPC / unowned: keep clonebase stub unless armor adds something usable.
            max = Math.Max(1, GetMaximumHP());
            if (armorFactor > 0 || chassisArmorAdd > 0)
                max = Math.Max(1, armorFactor + chassisArmorAdd);
        }

        var ratio = MaxHP > 0 ? (double)HP / MaxHP : 1.0;
        SetMaximumHP(max, triggerGhostUpdate: false);
        if (refillCurrent)
            SetCurrentHP(max, triggerGhostUpdate: false, notifyOwnerHud: false);
        else
            SetCurrentHP((int)Math.Round(max * ratio), triggerGhostUpdate: false, notifyOwnerHud: false);

        if (triggerGhostUpdate)
        {
            DirtyHealthMasks(GhostObject.HealthMask | GhostObject.HealthMaxMask);
            NotifyOwnerHealthHud();
        }
    }

    /// <summary>
    /// Sets current shield. When <paramref name="triggerGhostUpdate"/> is true, dirties
    /// <see cref="GhostVehicle.ShieldMask"/> for observers and sends owner absolute
    /// <see cref="GameOpcode.MultipleStatUpdate"/> (0x2010 type=1 → client
    /// <c>Vehicle_SetCurrentShield</c>).
    /// </summary>
    public void SetCurrentShield(int shield, bool triggerGhostUpdate = true)
    {
        var newShield = Math.Clamp(shield, 0, Math.Max(MaxShield, 0));
        if (CurrentShield == newShield)
            return;

        CurrentShield = newShield;

        if (triggerGhostUpdate)
            NotifyShieldChanged();
    }

    /// <summary>
    /// Owner HUD + ghost replication after current shield changes.
    /// Retail: ghost ShieldMask (32-bit @ vehicle+0x144) for net objects; absolute
    /// MultipleStatUpdate (0x2010) / StatUpdate (0x20AA) type=1 for reliable owner apply
    /// via <c>FUN_0080B3A0</c> → <c>Vehicle_SetCurrentShield</c>. CharacterLevel has no shield field
    /// (HP/power only).
    /// </summary>
    public void NotifyShieldChanged(bool includeMax = false)
    {
        var mask = GhostVehicle.ShieldMask;
        if (includeMax)
            mask |= GhostVehicle.ShieldMaxMask;
        EnsureGhostMaskDelivery(mask);

        if (Owner is Character owner && owner.OwningConnection != null)
        {
            owner.OwningConnection.SendGamePacket(
                MultipleStatUpdatePacket.ForVehicleShield(ObjectId, CurrentShield));
        }
    }

    /// <summary>
    /// Sets maximum shield, clamps current if needed, and dirties shield ghost masks.
    /// </summary>
    public void SetMaximumShield(int maxShield, bool triggerGhostUpdate = true)
    {
        var newMax = Math.Max(maxShield, 0);

        if (MaxShield == newMax)
        {
            if (CurrentShield > MaxShield)
                SetCurrentShield(MaxShield, triggerGhostUpdate);
            return;
        }

        MaxShield = newMax;
        var oldShield = CurrentShield;
        CurrentShield = Math.Clamp(CurrentShield, 0, MaxShield);

        if (!triggerGhostUpdate)
            return;

        EnsureGhostMaskDelivery(GhostVehicle.ShieldMaxMask);
        if (CurrentShield != oldShield)
            NotifyShieldChanged(includeMax: false);
    }

    /// <summary>
    /// Damage hits shield first, then remaining damage reduces HP.
    /// Returns total applied (shield absorbed + HP lost) for floaters / aggro.
    /// </summary>
    /// <remarks>
    /// Retail client <c>FUN_004f62e0</c> also drains shield before HP when MaxShield &gt; 0.
    /// <c>EMSG_Sector_Damage</c> (0x2023) can apply local HP prediction via
    /// <c>FUN_00812A60</c>; after any hit we push absolute owner Health (CharacterLevel +
    /// HealthMask) so shield-absorbed damage does not leave the HP bar stuck low, and we
    /// re-dirty ShieldMax+Shield so the client race-item shield gauge tracks server state.
    /// </remarks>
    public override int TakeDamage(int damage)
    {
        if (IsInvincible || IsCorpse || damage <= 0)
            return 0;

        // Race item may equip after pools were zeroed; re-seed capacity if needed.
        if (MaxShield <= 0 && RaceItem?.CloneBaseObject != null)
            ApplyRaceItemShieldFromEquipped(startAtFull: true);

        var shieldBefore = CurrentShield;
        var shieldAbsorb = 0;
        if (CurrentShield > 0 && MaxShield > 0)
        {
            shieldAbsorb = Math.Min(damage, CurrentShield);
            // Ghost notify deferred until after full resolution so we can send Max+Current together.
            SetCurrentShield(CurrentShield - shieldAbsorb, triggerGhostUpdate: false);
        }

        var remainder = damage - shieldAbsorb;
        var hpDamage = remainder > 0 ? base.TakeDamage(remainder) : 0;

        if (shieldAbsorb > 0)
        {
            // Ghost max+current for observers; MultipleStatUpdate absolute current for owner HUD.
            NotifyShieldChanged(includeMax: true);
        }

        // Shield-only hits skip SimpleObject.TakeDamage (no CharacterLevel / HealthMask).
        // Client DamagePacket prediction still lowers local HP — push absolute truth.
        if (shieldAbsorb > 0 && hpDamage == 0)
        {
            DirtyHealthMasks(GhostObject.HealthMask);
            NotifyOwnerHealthHud();
        }

        if (shieldAbsorb > 0 || hpDamage > 0)
        {
            Logger.WriteLog(LogType.Debug,
                "TakeDamage: vehicle coid={0} dmg={1} shield {2}->{3}/{4} absorb={5} hpNow={6}/{7} hpDmg={8}",
                ObjectId.Coid,
                damage,
                shieldBefore,
                CurrentShield,
                MaxShield,
                shieldAbsorb,
                GetCurrentHP(),
                GetMaximumHP(),
                hpDamage);
        }

        return shieldAbsorb + hpDamage;
    }

    /// <summary>
    /// Seeds shield capacity from equipped race-item clonebase fields (or zeroes if none).
    /// Also caches HP regen rate from the same RaceItem (Tribe Locus).
    /// </summary>
    public void ApplyRaceItemShieldFromEquipped(bool startAtFull = true)
    {
        if (RaceItem?.CloneBaseObject != null)
        {
            var so = RaceItem.CloneBaseObject.SimpleObjectSpecific;
            MaxShield = Math.Max(0, (int)so.RaceShieldFactor);
            ShieldRegenRate = Math.Max(0, (int)so.RaceShieldRegenerate);
            HpRegenRate = Math.Max(0, (int)so.RaceRegenRate);
            CurrentShield = startAtFull ? MaxShield : Math.Clamp(CurrentShield, 0, MaxShield);
        }
        else
        {
            MaxShield = 0;
            CurrentShield = 0;
            ShieldRegenRate = 0;
            HpRegenRate = 0;
        }

        ShieldEmptyDebounce = 0;
    }

    /// <summary>
    /// Seeds heat capacity / cool rate and character power pool from the equipped power plant.
    /// Client: equip PP → FUN_004fe1b0 sets heat max via FUN_004f7360; power max from PP PowerMaximum.
    /// </summary>
    public void ApplyPowerPlantCapacities(bool startPowerAtFull = true, bool clearHeat = true)
    {
        var specific = PowerPlant?.CloneBasePowerPlant?.PowerPlantSpecific;
        if (specific != null)
        {
            CoolRate = Math.Max(0, (int)specific.CoolRate);
            PowerRegenRate = Math.Max(0, (int)specific.PowerRegenRate);
        }
        else
        {
            CoolRate = 1; // client FUN_004f3840 default when no PP
            PowerRegenRate = 1;
        }

        // Retail: Vehicle_CalcHeatMaximum (tech + race/class + PP HeatMaximum + HeatMaxAdd).
        RecalculateMaximumHeat(triggerGhostUpdate: false);

        if (clearHeat)
        {
            CurrentHeat = 0;
            HeatAtMaxDebounce = 0;
        }
        else
        {
            CurrentHeat = Math.Clamp(CurrentHeat, 0, GetHeatHardCap());
        }

        var owner = Owner as Character;
        if (owner != null && specific != null && specific.PowerMaximum > 0)
        {
            var max = (short)Math.Clamp(specific.PowerMaximum, 0, short.MaxValue);
            CharacterLevelManager.Instance.SetMaxMana(owner, max, sendPacket: false);
            if (startPowerAtFull)
                CharacterLevelManager.Instance.SetCurrentMana(owner, max, sendPacket: false);
            EnsureGhostMaskDelivery(GhostVehicle.PowerMask);
        }

        EnsureGhostMaskDelivery(GhostVehicle.HeatMask);
    }

    /// <summary>
    /// Recompute MaxHeat from power plant, chassis HeatMaxAdd, owner Tech/level/race/class.
    /// Retail: <c>Vehicle_CalcHeatMaximum</c> @ 0x004F7360.
    /// </summary>
    public void RecalculateMaximumHeat(bool triggerGhostUpdate = true)
    {
        var specific = PowerPlant?.CloneBasePowerPlant?.PowerPlantSpecific;
        var ppHeat = specific?.HeatMaximum ?? 0;
        var heatMaxAdd = (CloneBaseObject as CloneBaseVehicle)?.VehicleSpecific.HeatMaxAdd ?? 0;

        int maxHeat;
        if (Owner is Character owner)
        {
            byte race = 0;
            byte classId = 0;
            if (owner.CloneBaseObject is CloneBaseCharacter charCb)
            {
                race = charCb.CharacterSpecific.Race;
                classId = charCb.CharacterSpecific.Class;
            }

            maxHeat = VehicleHeatCalculator.CalculatePlayerMaxHeat(
                race,
                classId,
                owner.Level,
                owner.AttributeTech,
                ppHeat,
                heatMaxAdd);
        }
        else
        {
            // NPC / unowned: power-plant heat only (no Tech term).
            maxHeat = Math.Max(0, ppHeat + heatMaxAdd);
        }

        SetMaximumHeat(maxHeat, triggerGhostUpdate);
    }

    /// <summary>Hard cap is 2× max heat (client FUN_004f7210).</summary>
    public int GetHeatHardCap() => Math.Max(0, MaxHeat * 2);

    /// <summary>
    /// Absolute heat set. Clamps to [0, 2×MaxHeat] (or ≥0 when max is unset) and dirties HeatMask.
    /// </summary>
    public void SetCurrentHeat(int heat, bool triggerGhostUpdate = true)
    {
        var hardCap = GetHeatHardCap();
        var newHeat = hardCap > 0 ? Math.Clamp(heat, 0, hardCap) : Math.Max(0, heat);

        if (CurrentHeat == newHeat)
            return;

        CurrentHeat = newHeat;

        // Client arms the 2-tick hold only when heat equals max (not when already over max).
        if (MaxHeat > 0 && CurrentHeat == MaxHeat)
            HeatAtMaxDebounce = Combat.VehicleCombatPool.EmptyShieldDebounceTicks;

        if (triggerGhostUpdate)
            EnsureGhostMaskDelivery(GhostVehicle.HeatMask);
    }

    public void SetMaximumHeat(int maxHeat, bool triggerGhostUpdate = true)
    {
        MaxHeat = Math.Max(0, maxHeat);
        var hardCap = GetHeatHardCap();
        if (hardCap > 0 && CurrentHeat > hardCap)
            SetCurrentHeat(hardCap, triggerGhostUpdate);
        else if (triggerGhostUpdate)
            EnsureGhostMaskDelivery(GhostVehicle.HeatMask);
    }

    /// <summary>Add (or subtract) heat; used by weapons and the cool tick.</summary>
    public void AddHeat(int delta, bool triggerGhostUpdate = true)
    {
        if (delta == 0)
            return;
        SetCurrentHeat(CurrentHeat + delta, triggerGhostUpdate);
    }

    internal void SetCoolRateForTests(int rate) => CoolRate = Math.Max(0, rate);
    internal void SetPowerRegenRateForTests(int rate) => PowerRegenRate = Math.Max(0, rate);
    internal void SetHpRegenRateForTests(int rate) => HpRegenRate = Math.Max(0, rate);
    internal void SetShieldRegenRateForTests(int rate) => ShieldRegenRate = Math.Max(0, rate);

    // Server-side combat state (very lightweight)
    private long _lastFireMsFront;
    private long _lastFireMsTurret;
    private long _lastFireMsRear;

    // Retail floating numbers use EMSG_Sector_Damage (0x2023) → combat-event list (game+0xAA8).
    // Broadcast freeform floaters use a different list (game+0xAC0) and do not reliably render.
    private static readonly Dictionary<long, long> _lastCombatMsgByAttackerMs = new();

    /// <summary>Clears the per-attacker damage-packet throttle so tests don't leak state across runs.</summary>
    internal static void ClearCombatThrottleForTests() => _lastCombatMsgByAttackerMs.Clear();

    // NPC attackers have no OwningConnection, so a victim-only send is required for the hit to be
    // visible to the player being shot. Deliver to both the attacker's and the victim's connections
    // (deduped), rate-limited by the ATTACKER VEHICLE COID so multiple weapons don't spam floaters.
    internal static void TrySendDamagePacket(
        Character? attacker,
        ClonedObjectBase victim,
        TFID source,
        int actualDamage,
        DamagePacket.DamageEntryFlags flags = default)
    {
        try
        {
            var connections = new List<TNL.TNLConnection>(2);
            var attackerConn = attacker?.OwningConnection;
            if (attackerConn != null)
                connections.Add(attackerConn);

            var victimConn = victim?.GetSuperCharacter(false)?.OwningConnection;
            if (victimConn != null && !ReferenceEquals(victimConn, attackerConn))
                connections.Add(victimConn);

            if (connections.Count == 0)
                return;

            var now = Environment.TickCount64;
            var key = source?.Coid ?? 0; // attacker vehicle COID
            if (key != 0)
            {
                if (_lastCombatMsgByAttackerMs.TryGetValue(key, out var last) && now - last < 100)
                    return;
                _lastCombatMsgByAttackerMs[key] = now;
            }

            foreach (var conn in connections)
            {
                var packet = new DamagePacket { Source = source ?? new TFID() };
                packet.AddHit(victim?.ObjectId ?? new TFID(), actualDamage, flags);
                conn.SendGamePacket(packet, skipOpcode: true);
            }
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

    /// <summary>
    /// Minimum speed (linear or angular, units/s or rad/s) that arms network pose extrapolation.
    /// Matches <see cref="GhostVehicle.IsMovingForPoseStream"/> epsilon family.
    /// </summary>
    internal const float NetworkPoseMotionEpsilon = 0.05f;

    /// <summary>
    /// Dead-reckons this vehicle's pose for one sector tick so TNL keep-dirty rebroadcasts
    /// an <b>advancing</b> pose between C2S <c>VehicleMoved</c> packets. Without this, remotes
    /// hard-snap (client <c>FUN_0053EEC0</c> full-physics path) to the same frozen server pose
    /// every send period and fight local Havok — the choppy remote-player look.
    /// Preserves last C2S throttle/steer. Safe before ghost exists. Returns true when pose moved.
    /// </summary>
    /// <param name="dt">Seconds since last tick; must be in (0, 1).</param>
    public bool AdvanceNetworkPose(float dt)
    {
        if (dt <= 0f || dt >= 1f)
            return false;

        if (IsCorpse)
            return false;

        var v = Velocity;
        var w = AngularVelocity;
        var speedSq = (v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z);
        var angSq = (w.X * w.X) + (w.Y * w.Y) + (w.Z * w.Z);
        var eps = NetworkPoseMotionEpsilon;
        if (speedSq <= eps * eps && angSq <= eps * eps)
            return false;

        if (speedSq > eps * eps)
        {
            Position = new Vector3(
                Position.X + (v.X * dt),
                Position.Y + (v.Y * dt),
                Position.Z + (v.Z * dt));
        }

        // Integrate yaw about world Y from angular velocity (C2S typically fills Y only).
        // Compose a yaw delta so pitch/roll from the last C2S pose are preserved.
        if (MathF.Abs(w.Y) > eps)
        {
            var half = w.Y * dt * 0.5f;
            var yawDelta = new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));
            Rotation = MultiplyQuaternion(yawDelta, Rotation);
        }

        Ghost?.SetMaskBits(GhostObject.PositionMask);
        return true;
    }

    private static Quaternion MultiplyQuaternion(Quaternion a, Quaternion b)
    {
        return new Quaternion(
            (a.W * b.X) + (a.X * b.W) + (a.Y * b.Z) - (a.Z * b.Y),
            (a.W * b.Y) - (a.X * b.Z) + (a.Y * b.W) + (a.Z * b.X),
            (a.W * b.Z) + (a.X * b.Y) - (a.Y * b.X) + (a.Z * b.W),
            (a.W * b.W) - (a.X * b.X) - (a.Y * b.Y) - (a.Z * b.Z));
    }

    /// <summary>
    /// Applies a server-authoritative pose/velocity update (NPC movement tick) and marks the
    /// ghost's PositionMask dirty so it reaches observers on the next update. Safe to call
    /// before the ghost exists.
    /// When <paramref name="dt"/> &gt; 0, fills angular velocity / steering / acceleration from
    /// the previous pose so client interpolation is less steppy (movement smoothness M2).
    /// </summary>
    public void ApplyServerMove(Vector3 position, Quaternion rotation, Vector3 velocity, float dt = 0f)
        => ApplyServerMove(position, rotation, velocity, dt, driveThrottle: null, driveSteering: null, sharpTurn: null);

    /// <summary>
    /// Server pose update with optional explicit drive axes (client <c>MoveToTarget3DPoint</c> /
    /// ghost +0x614/+0x618/+0x61c). When drive inputs are provided, they override dYaw-derived
    /// steering so wheels turn even when pose already faces the path.
    /// </summary>
    public void ApplyServerMove(
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        float dt,
        float? driveThrottle,
        float? driveSteering,
        byte? sharpTurn)
    {
        if (dt > 0f && dt < 1f)
        {
            var prevYaw = YawFromQuaternion(Rotation);
            var nextYaw = YawFromQuaternion(rotation);
            var dYaw = NormalizeRadians(nextYaw - prevYaw);
            AngularVelocity = new Vector3(0f, dYaw / dt, 0f);

            if (driveSteering.HasValue)
                Steering = Math.Clamp(driveSteering.Value, -1f, 1f);
            else
                Steering = Math.Clamp(dYaw / (MathF.PI * 0.5f * Math.Max(dt, 1e-3f)), -1f, 1f);

            var prevSpeed = MathF.Sqrt((Velocity.X * Velocity.X) + (Velocity.Y * Velocity.Y) + (Velocity.Z * Velocity.Z));
            var nextSpeed = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));
            if (driveThrottle.HasValue)
                Acceleration = Math.Clamp(driveThrottle.Value, -1f, 1f);
            else
                Acceleration = ResolvePathThrottle(prevSpeed, nextSpeed, MathF.Abs(dYaw) / Math.Max(dt, 1e-3f));

            // sharpTurn maps to client vehicle+0x61c via VehicleFlags/handbrake packing history —
            // do not set Handbrake (VehicleFlags bit0); thr/steer alone drive wheel visuals when
            // VehicleAction exists. Reserved for a dedicated wire field later.
            _ = sharpTurn;
        }
        else
        {
            AngularVelocity = default;
        }

        if (driveThrottle.HasValue)
            Acceleration = Math.Clamp(driveThrottle.Value, -1f, 1f);
        if (driveSteering.HasValue)
            Steering = Math.Clamp(driveSteering.Value, -1f, 1f);

        Position = position;
        Rotation = rotation;
        Velocity = velocity;

        if (!ShouldSuppressPatrolPoseGhost())
            Ghost?.SetMaskBits(GhostObject.PositionMask);
    }

    /// <summary>
    /// Throttle for network pose: accelerate hard, cruise mid, ease off when braking/turning.
    /// Client Havok needs non-zero throttle while moving (see docs/nullWheels.md M2).
    /// </summary>
    internal static float ResolvePathThrottle(float prevSpeed, float nextSpeed, float absYawRate)
    {
        const float movingEps = 0.05f;
        if (nextSpeed <= movingEps)
            return 0f;

        var delta = nextSpeed - prevSpeed;
        // Hard turn: lift off slightly so the client does not fight the pose with full thr.
        var turnEase = absYawRate > 1.2f ? 0.55f : 1f;

        if (delta > 0.35f)
            return Math.Clamp(1f * turnEase, 0f, 1f);
        if (delta < -0.35f)
            return Math.Clamp(0.25f * turnEase, 0f, 1f); // coast/brake — avoid reverse thr
        return Math.Clamp(0.85f * turnEase, 0f, 1f); // cruise
    }

    /// <summary>
    /// True when continuous pose ghosting should be withheld so the retail client can drive
    /// the vehicle along its assigned path at frame rate (see
    /// <see cref="GhostVehicle.EnableClientSidePathVisual"/>).
    /// </summary>
    internal bool ShouldSuppressPatrolPoseGhost()
    {
        if (!GhostVehicle.EnableClientSidePathVisual)
            return false;
        if (CoidCurrentPath <= 0)
            return false;
        // Only suppress genuine idle patrol; engage/combat still need authoritative pose.
        return NpcAi == null || NpcAi.CombatState == HBAICombatState.IdlePatrol;
    }

    private static float YawFromQuaternion(Quaternion q)
    {
        // Yaw about Y from standard unit quaternion (XZ plane heading).
        var siny = 2f * ((q.W * q.Y) + (q.X * q.Z));
        var cosy = 1f - (2f * ((q.Y * q.Y) + (q.X * q.X)));
        return MathF.Atan2(siny, cosy);
    }

    private static float NormalizeRadians(float a)
    {
        while (a > MathF.PI)
            a -= MathF.PI * 2f;
        while (a < -MathF.PI)
            a += MathF.PI * 2f;
        return a;
    }

    /// <summary>
    /// Applies a wad.xml <c>tVehicleTemplate.intBaseHP</c> override at spawn time. No-op for
    /// non-positive values so callers can pass a template field unconditionally.
    /// </summary>
    internal void ApplyTemplateBaseHp(int baseHp)
    {
        if (baseHp <= 0)
            return;

        HP = MaxHP = baseHp;
    }

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
    /// Rejects C# type mismatches and wrong clonebase / weapon hardpoint (logs loudly).
    /// </summary>
    public bool TryEquipItem(VehicleEquipmentSlot slot, SimpleObject item, out SimpleObject previousItem)
    {
        previousItem = null;
        if (item == null)
            return false;

        if (!IsCompatibleWithEquipmentSlot(slot, item, out var rejectReason))
        {
            Logger.WriteLog(LogType.Error,
                "TryEquipItem BLOCKED: slot={0} itemCoid={1} itemCbid={2} itemType={3} cloneType={4} reason={5}",
                slot,
                item.ObjectId.Coid,
                item.CBID,
                item.GetType().Name,
                item.CloneBaseObject?.GetType().Name ?? "null",
                rejectReason);
            return false;
        }

        previousItem = GetEquippedItem(slot);
        if (!AssignEquipmentSlot(slot, item))
        {
            previousItem = null;
            Logger.WriteLog(LogType.Error,
                "TryEquipItem BLOCKED: AssignEquipmentSlot failed slot={0} itemCoid={1} itemCbid={2} itemType={3}",
                slot, item.ObjectId.Coid, item.CBID, item.GetType().Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="item"/> may occupy <paramref name="slot"/> (runtime type + clonebase).
    /// Clonebase null is allowed (unit tests / pre-load); wrong clonebase type is not.
    /// </summary>
    public static bool IsCompatibleWithEquipmentSlot(
        VehicleEquipmentSlot slot,
        SimpleObject item,
        out string rejectReason)
    {
        rejectReason = null;
        if (item == null)
        {
            rejectReason = "item is null";
            return false;
        }

        // global:: types: this class has properties named Armor/PowerPlant/WheelSet/Weapon*.
        // Clonebase match uses Type enum (not only as-cast) so typed WAD rows and test fakes both work;
        // wrong Type (e.g. Item on a Weapon entity) is blocked.
        switch (slot)
        {
            case VehicleEquipmentSlot.Armor:
                if (item is not global::AutoCore.Game.Entities.Armor)
                {
                    rejectReason = "runtime type is not Armor";
                    return false;
                }
                return CloneBaseTypeMatchesSlot(item, CloneBaseObjectType.Armor, out rejectReason);

            case VehicleEquipmentSlot.PowerPlant:
                if (item is not global::AutoCore.Game.Entities.PowerPlant)
                {
                    rejectReason = "runtime type is not PowerPlant";
                    return false;
                }
                return CloneBaseTypeMatchesSlot(item, CloneBaseObjectType.PowerPlant, out rejectReason);

            case VehicleEquipmentSlot.WheelSet:
                if (item is not global::AutoCore.Game.Entities.WheelSet)
                {
                    rejectReason = "runtime type is not WheelSet";
                    return false;
                }
                return CloneBaseTypeMatchesSlot(item, CloneBaseObjectType.WheelSet, out rejectReason);

            case VehicleEquipmentSlot.WeaponMelee:
            case VehicleEquipmentSlot.WeaponFront:
            case VehicleEquipmentSlot.WeaponTurret:
            case VehicleEquipmentSlot.WeaponRear:
                if (item is not global::AutoCore.Game.Entities.Weapon weapon)
                {
                    rejectReason = "runtime type is not Weapon";
                    return false;
                }
                if (!CloneBaseTypeMatchesSlot(weapon, CloneBaseObjectType.Weapon, out rejectReason))
                    return false;
                if (weapon.CloneBaseWeapon != null
                    && !WeaponCloneMatchesHardpoint(slot, weapon.CloneBaseWeapon, out rejectReason))
                {
                    return false;
                }
                return true;

            case VehicleEquipmentSlot.Ornament:
            case VehicleEquipmentSlot.RaceItem:
                // SimpleObject / Item subtype hardpoints — any non-null graphics object is accepted.
                return true;

            default:
                rejectReason = "unknown equipment slot";
                return false;
        }
    }

    private static bool CloneBaseTypeMatchesSlot(
        SimpleObject item,
        CloneBaseObjectType expected,
        out string rejectReason)
    {
        rejectReason = null;
        if (item.CloneBaseObject == null)
            return true; // unit tests / pre-load

        if (item.CloneBaseObject.Type == expected)
            return true;

        rejectReason =
            $"clonebase Type={item.CloneBaseObject.Type} (cbid={item.CBID}) does not match slot expected {expected}";
        return false;
    }

    /// <summary>
    /// Hardpoint flags from client equip helpers (0x02 front, 0x10 turret, 0x04 rear).
    /// SubType 9 is a cargo <em>auto-resolve</em> hint for melee (VehicleEquipmentSlotResolver),
    /// not a requirement for explicit template/player equip into the melee slot — retail templates
    /// often put ordinary weapons (SubType 0, Flags 0) on WeaponMelee (e.g. CBID 14070 / Template 884).
    /// Flags==0 means unspecified — allow the requested ranged hardpoint.
    /// </summary>
    private static bool WeaponCloneMatchesHardpoint(
        VehicleEquipmentSlot slot,
        CloneBaseWeapon weapon,
        out string rejectReason)
    {
        rejectReason = null;
        var subType = weapon.WeaponSpecific.SubType;
        var flags = weapon.WeaponSpecific.Flags;

        // Explicit equip to melee: any weapon clonebase is valid.
        if (slot == VehicleEquipmentSlot.WeaponMelee)
            return true;

        // Melee-only subtype should not sit on front/turret/rear when flags are absent.
        if (subType == 9 && flags == 0)
        {
            rejectReason = "melee SubType 9 cannot equip on ranged hardpoint";
            return false;
        }

        if (flags == 0)
            return true;

        var ok = slot switch
        {
            VehicleEquipmentSlot.WeaponFront => (flags & VehicleEquipmentSlotResolver.WeaponFlagFront) != 0,
            VehicleEquipmentSlot.WeaponTurret => (flags & VehicleEquipmentSlotResolver.WeaponFlagTurret) != 0,
            VehicleEquipmentSlot.WeaponRear => (flags & VehicleEquipmentSlotResolver.WeaponFlagRear) != 0,
            _ => false,
        };
        if (ok)
            return true;

        rejectReason = $"weapon Flags=0x{flags:X2} SubType={subType} does not match slot {slot}";
        return false;
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
                RecalculateMaximumHitPoints(refillCurrent: false, triggerGhostUpdate: true);
                EnsureGhostMaskDelivery(GhostVehicle.ChangeArmor);
                return true;

            case VehicleEquipmentSlot.PowerPlant:
                PowerPlant = null;
                if (DBData != null) DBData.PowerPlant = 0;
                ApplyPowerPlantCapacities(startPowerAtFull: false, clearHeat: false);
                return true;

            case VehicleEquipmentSlot.Ornament:
                Ornament = null;
                if (DBData != null) DBData.Ornament = 0;
                EnsureGhostMaskDelivery(GhostVehicle.OrnamentMask);
                return true;

            case VehicleEquipmentSlot.RaceItem:
                RaceItem = null;
                if (DBData != null) DBData.RaceItem = 0;
                ApplyRaceItemShieldFromEquipped(startAtFull: false);
                EnsureGhostMaskDelivery(GhostVehicle.ShieldMaxMask | GhostVehicle.ShieldMask);
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
                RecalculateMaximumHitPoints(refillCurrent: false, triggerGhostUpdate: true);
                EnsureGhostMaskDelivery(GhostVehicle.ChangeArmor);
                return true;

            case VehicleEquipmentSlot.PowerPlant:
                if (item is not PowerPlant powerPlant)
                    return false;
                PowerPlant = powerPlant;
                if (DBData != null) DBData.PowerPlant = coid;
                ApplyPowerPlantCapacities(startPowerAtFull: true, clearHeat: true);
                return true;

            case VehicleEquipmentSlot.Ornament:
                Ornament = item;
                if (DBData != null) DBData.Ornament = coid;
                EnsureGhostMaskDelivery(GhostVehicle.OrnamentMask);
                return true;

            case VehicleEquipmentSlot.RaceItem:
                RaceItem = item;
                if (DBData != null) DBData.RaceItem = coid;
                ApplyRaceItemShieldFromEquipped(startAtFull: true);
                EnsureGhostMaskDelivery(GhostVehicle.ShieldMaxMask | GhostVehicle.ShieldMask);
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

        // Shield from equipped shielding (RaceItem) RaceShieldFactor, or 0.
        ApplyRaceItemShieldFromEquipped(startAtFull: true);

        if (DBData.PowerPlant != 0)
        {
            PowerPlant = new PowerPlant();
            if (!PowerPlant.LoadFromDB(context, DBData.PowerPlant))
            {
                return false;
            }
        }

        // Heat/power capacity from PP (owner Character may be attached later at sector enter).
        ApplyPowerPlantCapacities(startPowerAtFull: true, clearHeat: true);

        // Chassis MaxHitPoint is almost always 1 in WAD; real max needs owner + armor.
        // Owner is set by Character.LoadCurrentVehicle before LoadFromDB.
        if (Owner is Character)
            RecalculateMaximumHitPoints(refillCurrent: true, triggerGhostUpdate: false);

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

    /// <summary>
    /// Ensures a clonebase DefaultWheelset is equipped before CreateVehicle serialization.
    /// Path A client capture: nested wheel CBID must be &gt; 0 or SetWheelset never runs and +0x258 stays null.
    /// </summary>
    /// <returns>True when <see cref="WheelSet"/> is present with CBID &gt; 0 after the call.</returns>
    public bool EnsureDefaultWheelSetForWire()
    {
        if (WheelSet != null && WheelSet.CBID > 0)
            return true;

        var defaultWheelsetCbid = (CloneBaseObject as CloneBaseVehicle)?.VehicleSpecific.DefaultWheelset ?? 0;
        if (defaultWheelsetCbid <= 0)
        {
            Logger.WriteLog(LogType.Error,
                "EnsureDefaultWheelSetForWire: vehicle coid={0} cbid={1} has no DefaultWheelset",
                ObjectId?.Coid ?? 0, CBID);
            return false;
        }

        var wheelSet = new WheelSet();
        // Nested TFID must use map-NPC identity space (client ResolveObjectTarget before GiveItemByCbid).
        if (Map != null)
        {
            var counter = Map.LocalCoidCounter;
            var id = MapNpcIdentity.AllocateCoid(ref counter);
            Map.LocalCoidCounter = counter;
            wheelSet.SetCoid(id.Coid, id.Global);
        }
        else
        {
            wheelSet.SetCoid(MapNpcIdentity.CoidBase + Math.Abs(ObjectId?.Coid ?? CBID), true);
        }

        try
        {
            wheelSet.LoadCloneBase(defaultWheelsetCbid);
            wheelSet.SetupCBFields();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "EnsureDefaultWheelSetForWire: failed LoadCloneBase wheelsetCbid={0} for vehicle cbid={1}: {2}",
                defaultWheelsetCbid, CBID, ex.Message);
            return false;
        }

        if (!TryEquipItem(VehicleEquipmentSlot.WheelSet, wheelSet, out _))
        {
            Logger.WriteLog(LogType.Error,
                "EnsureDefaultWheelSetForWire: equip failed vehicle cbid={0} wheelsetCbid={1}",
                CBID, defaultWheelsetCbid);
            return false;
        }

        return WheelSet != null && WheelSet.CBID > 0;
    }

    [ExcludeFromCodeCoverage]
    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateVehiclePacket vehiclePacket)
        {
            // Last-chance equip so foreign CreateVehicle never wires nested wheel CBID 0 / empty when clonebase has a default.
            EnsureDefaultWheelSetForWire();

            // The client links driverCreature+0x250 = vehicle (and the vehicle-side owner ptr)
            // ONLY from this field: Vehicle_applyCreatePacket resolves +0xd8 → Creature::SetVehicle
            // (004c49d0). The ghost CurrentOwner block is parsed but ignored on the bind-only path
            // because CreateVehicle pre-creates the object. Owner 0 → target-frame HP text never
            // renders for NPC vehicles (blank cur/max, 2026-07-11 live).
            vehiclePacket.CoidCurrentOwner = DBData?.CharacterCoid ?? Owner?.ObjectId.Coid ?? 0;
            // CreateVehiclePacket.CoidSpawnOwner is a 32-bit field; map-NPC COIDs are high 64-bit.
            // Ghost carries spawn-owner separately (20-bit). Keep create payload at -1 for now.
            vehiclePacket.CoidSpawnOwner = -1;

            for (var i = 0; i < 8; ++i)
                vehiclePacket.Tricks[i] = -1;

            vehiclePacket.PrimaryColor = PrimaryColor;
            vehiclePacket.SecondaryColor = SecondaryColor;
            var vehicleSpecific = (CloneBaseObject as CloneBaseVehicle)?.VehicleSpecific;
            vehiclePacket.ArmorAdd = vehicleSpecific?.ArmorAdd ?? 0;
            vehiclePacket.PowerMaxAdd = vehicleSpecific?.PowerMaxAdd ?? 0;
            vehiclePacket.HeatMaxAdd = vehicleSpecific?.HeatMaxAdd ?? 0;
            vehiclePacket.CooldownAdd = vehicleSpecific?.CooldownAdd ?? 0;
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
            // Active field vehicles without a wheelset arm client Havok without +0x258 → AV 0x004F5566.
            // NOTE: Do NOT force IsActive=false for NPCs — live test 2026-07-11 froze all NPC motion.
            var hasWheelset = WheelSet != null && WheelSet.CBID > 0;
            vehiclePacket.IsActive = hasWheelset
                && Map != null
                && !Map.MapData.ContinentObject.IsTown;
            if (!hasWheelset && Map != null && !Map.MapData.ContinentObject.IsTown)
            {
                Logger.WriteLog(LogType.Error,
                    "CreateVehicle: forcing IsActive=false; missing wheelset coid={0} vehicleCbid={1}",
                    ObjectId?.Coid ?? 0, CBID);
            }

            vehiclePacket.Trim = Trim;

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

            if (WheelSet != null && WheelSet.CBID > 0)
            {
                vehiclePacket.CreateWheelSet = new CreateWheelSetPacket();
                WheelSet.WriteToPacket(vehiclePacket.CreateWheelSet);
                // Client GiveItemByCbid(0) fails and never SetWheelset; empty path must stay CBID=-1.
                if (vehiclePacket.CreateWheelSet.CBID <= 0)
                {
                    Logger.WriteLog(LogType.Error,
                        "CreateVehicle: nested wheel CBID became {0} after WriteToPacket for vehicle cbid={1}; forcing empty nested",
                        vehiclePacket.CreateWheelSet.CBID, CBID);
                    vehiclePacket.CreateWheelSet = null;
                }
            }
            else if (WheelSet != null)
            {
                Logger.WriteLog(LogType.Error,
                    "CreateVehicle: WheelSet present but CBID={0} for vehicle cbid={1}; emitting empty nested wheelset",
                    WheelSet.CBID, CBID);
            }

            if (Armor != null)
            {
                vehiclePacket.CreateArmor = new CreateArmorPacket();
                Armor.WriteToPacket(vehiclePacket.CreateArmor);
            }

            // Skip non-weapon clonebases (WriteToPacket logs + ignores); empty nest = CBID -1 on wire.
            if (WeaponMelee?.CloneBaseWeapon != null)
            {
                vehiclePacket.CreateWeaponMelee = new CreateWeaponPacket();
                WeaponMelee.WriteToPacket(vehiclePacket.CreateWeaponMelee);
            }

            if (WeaponFront?.CloneBaseWeapon != null)
            {
                vehiclePacket.CreateWeapons[0] = new CreateWeaponPacket();
                WeaponFront.WriteToPacket(vehiclePacket.CreateWeapons[0]);
            }

            if (WeaponTurret?.CloneBaseWeapon != null)
            {
                vehiclePacket.CreateWeapons[1] = new CreateWeaponPacket();
                WeaponTurret.WriteToPacket(vehiclePacket.CreateWeapons[1]);
            }

            if (WeaponRear?.CloneBaseWeapon != null)
            {
                vehiclePacket.CreateWeapons[2] = new CreateWeaponPacket();
                WeaponRear.WriteToPacket(vehiclePacket.CreateWeapons[2]);
            }

            vehiclePacket.CurrentPathId = (int)CoidCurrentPath;
            vehiclePacket.ExtraPathId = ExtraPathId;
            vehiclePacket.PatrolDistance = PatrolDistance;
            vehiclePacket.PathReversing = PathReversing;
            vehiclePacket.PathIsRoad = PathIsRoad;
            vehiclePacket.TemplateId = TemplateId;
            vehiclePacket.MurdererCoid = -1L;
            vehiclePacket.WeaponsCBID[0] = WeaponFront?.CBID ?? -1;
            vehiclePacket.WeaponsCBID[1] = WeaponTurret?.CBID ?? -1;
            vehiclePacket.WeaponsCBID[2] = WeaponRear?.CBID ?? -1;
            vehiclePacket.Name = Name;
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

    /// <summary>
    /// Captures live HP/shield/power/heat into a snapshot and attached <see cref="VehicleData"/>.
    /// No DB I/O — caller persists via world-state save.
    /// </summary>
    public VehicleCombatStateSnapshot CaptureCombatState(Character owner)
    {
        var power = owner != null
            ? (int)CharacterLevelManager.Instance.GetCurrentMana(owner.ObjectId.Coid)
            : -1;

        var snap = new VehicleCombatStateSnapshot(
            GetCurrentHP(),
            CurrentShield,
            power,
            CurrentHeat);

        if (DBData != null)
        {
            DBData.CurrentHP = snap.CurrentHP;
            DBData.CurrentShield = snap.CurrentShield;
            DBData.CurrentPower = snap.CurrentPower;
            DBData.CurrentHeat = snap.CurrentHeat;
        }

        return snap;
    }

    /// <summary>
    /// Applies combat-pool currents after maxes have been recalculated (login).
    /// Values of <c>-1</c> leave the current (typically full) pools unchanged.
    /// </summary>
    public void ApplyCombatState(VehicleCombatStateSnapshot state, Character owner)
    {
        if (state.CurrentHP >= 0)
            SetCurrentHP(state.CurrentHP, triggerGhostUpdate: false, notifyOwnerHud: false);

        if (state.CurrentShield >= 0)
            SetCurrentShield(state.CurrentShield, triggerGhostUpdate: false);

        if (state.CurrentHeat >= 0)
            SetCurrentHeat(state.CurrentHeat, triggerGhostUpdate: false);

        if (state.CurrentPower >= 0 && owner != null)
        {
            var power = (short)Math.Clamp(state.CurrentPower, 0, short.MaxValue);
            CharacterLevelManager.Instance.SetCurrentMana(owner, power, sendPacket: false);
        }
    }

    /// <summary>
    /// Restores combat pools from attached <see cref="VehicleData"/> after max recalculation.
    /// </summary>
    public void RestoreCombatStateFromDb(Character owner)
    {
        if (DBData == null)
            return;

        ApplyCombatState(
            new VehicleCombatStateSnapshot(
                DBData.CurrentHP,
                DBData.CurrentShield,
                DBData.CurrentPower,
                DBData.CurrentHeat),
            owner);
    }

    internal void AttachTestDataForTests(string name = "TestVehicle")
    {
        DBData = new VehicleData
        {
            Coid = ObjectId.Coid,
            Name = name
        };
    }

    internal void SetDbCombatStateForTests(int currentHP, int currentShield, int currentPower, int currentHeat)
    {
        if (DBData == null)
            AttachTestDataForTests();

        DBData.CurrentHP = currentHP;
        DBData.CurrentShield = currentShield;
        DBData.CurrentPower = currentPower;
        DBData.CurrentHeat = currentHeat;
    }

    internal float GetDbPositionXForTests() => DBData?.PositionX ?? float.NaN;
    internal float GetDbPositionZForTests() => DBData?.PositionZ ?? float.NaN;
    internal float GetDbRotationYForTests() => DBData?.RotationY ?? float.NaN;
    internal float GetDbRotationWForTests() => DBData?.RotationW ?? float.NaN;
    internal int GetDbCurrentHPForTests() => DBData?.CurrentHP ?? -1;
    internal int GetDbCurrentShieldForTests() => DBData?.CurrentShield ?? -1;
    internal int GetDbCurrentPowerForTests() => DBData?.CurrentPower ?? -1;
    internal int GetDbCurrentHeatForTests() => DBData?.CurrentHeat ?? -1;

    /// <summary>Test hook: stamps angular velocity without going through ApplyServerMove.</summary>
    internal void SetAngularVelocityForTests(Vector3 angularVelocity) => AngularVelocity = angularVelocity;

    /// <summary>Test hook: stamps linear velocity without going through ApplyServerMove.</summary>
    internal void SetVelocityForTests(Vector3 velocity) => Velocity = velocity;

    [ExcludeFromCodeCoverage]
    public void HandleMovement(VehicleMovedPacket packet)
    {
        if (packet.ObjectId != ObjectId)
            throw new Exception("WTF? Someone else moves me?");

        // Snapshot before apply — ram uses position delta when packet velocity is near-zero.
        var previousPosition = Position;

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

        // Server-side ram: client DoVehicleCollision is LOCAL ONLY (no C2S collision packet).
        // Must run even when Ghost is null so props die + loot while ghosting is not ready.
        Combat.VehicleMapPropRam.Process(this, previousPosition);

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

        // Overheat lock: client FUN_0056aca0 blocks fire while heat >= max.
        if (MaxHeat > 0 && CurrentHeat >= MaxHeat)
            return;

        lastFireRef = nowMs;

        // Weapon heat per shot (client FUN_0056ad00 → FUN_004f7210).
        if (weaponSpec.Heat > 0)
            AddHeat(weaponSpec.Heat);

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
        var actualDamage = Target.TakeDamage(damage, this);

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
            TrySendDamagePacket(attackerChar, Target, ObjectId, actualDamage);
        }

        if (Target.GetCurrentHP() <= 0)
        {
            Target.SetMurderer(this);
            Target.OnDeath(DeathType.Silent);
        }
    }

    /// <summary>
    /// NPC-vehicle death: roll <c>tVehicleTemplate</c> loot, leave the map, and broadcast a destroy
    /// so clients remove the wreck (mirrors <see cref="Creature.OnDeath"/>). Player vehicles keep the
    /// base behavior (corpse state only; no map removal).
    /// </summary>
    public override void OnDeath(DeathType deathType)
    {
        Logger.WriteLog(LogType.Debug,
            "Vehicle.OnDeath coid={0} cbid={1} templateId={2} npcAi={3} murderer={4} inv={5} hp={6}/{7}",
            ObjectId.Coid,
            CBID,
            TemplateId,
            NpcAi != null ? 1 : 0,
            Murderer?.Coid ?? -1,
            IsInvincible ? 1 : 0,
            GetCurrentHP(),
            GetMaximumHP());

        if (NpcAi == null)
        {
            base.OnDeath(deathType);

            // Ghidra: VehicleNet_UnpackGhostVehicle reads the HealthMask block as
            // currentHP + corpse, invokes the live HP setter, then sets vehicle state bit
            // +0x17c/0x100. Re-sending CreateVehicle allocates/materializes an object and does
            // not perform that transition. Scope and dirty the ghost only after base has made
            // IsCorpse true so the client receives one coherent HP=0/corpse=true update.
            EnsureGhostMaskDelivery(GhostObject.HealthMask | GhostObject.HealthMaxMask);

            var ownerConnection = Owner?.GetAsCharacter()?.OwningConnection;
            ownerConnection?.FlushDeathGhostUpdate();
            Logger.WriteLog(LogType.Network,
                "PlayerDeathGhost coid={0} hp={1}/{2} corpse={3} ghost={4} scoped={5} ghosting={6}",
                ObjectId.Coid,
                GetCurrentHP(),
                GetMaximumHP(),
                IsCorpse ? 1 : 0,
                Ghost != null ? 1 : 0,
                Ghost?.GetFirstObjectRef() != null ? 1 : 0,
                ownerConnection?.IsGhosting() == true ? 1 : 0);
            return;
        }

        // base sets corpse/HealthMask and credits the kill objective; SimpleObject's
        // RemoveFromMapOnDeath=false means it does NOT remove the vehicle — we do that below.
        base.OnDeath(deathType);

        var vehicleObjectId = ObjectId;
        var map = Map;
        if (map == null)
            return;

        Character killerCharacter = null;
        if (Murderer.Coid > 0)
            killerCharacter = ObjectManager.Instance.GetObject(Murderer)?.GetSuperCharacter(false);

        // SpawnPoint TriggerEvents (pad Create after combat vehicle dies, etc.) before leave-map.
        NotifySpawnOwnerTriggerEventsOnDeath(map, killerCharacter);

        GenerateAndSpawnTemplateLoot(killerCharacter);

        // Vehicle path: DestroyObject with DeathType only (same CompletelyDestroyObject
        // death FX as creatures). InitCreateObject DoDeath is for map props only.
        BroadcastDeath(map, vehicleObjectId, deathType, Murderer, Ghost, useInitCreateDeath: false);
        SetMap(null);
    }

    void NotifySpawnOwnerTriggerEventsOnDeath(SectorMap map, Character killerCharacter)
    {
        if (SpawnOwnerCoid <= 0)
            return;

        if (map.GetObjectByCoid(SpawnOwnerCoid) is not SpawnPoint spawn)
            return;

        ClonedObjectBase activator = killerCharacter?.CurrentVehicle != null
            ? killerCharacter.CurrentVehicle
            : killerCharacter != null
                ? killerCharacter
                : this;
        spawn.NotifySpawnedChildDied(this, activator);
    }

    private void GenerateAndSpawnTemplateLoot(Character killerCharacter)
    {
        if (TemplateId < 0)
            return;

        var template = AssetManager.Instance.GetVehicleTemplate(TemplateId);
        if (template == null)
            return;

        var level = Owner?.GetAsCreature()?.GetLevel() ?? 1;
        var lootItems = LootManager.Instance.GenerateLoot(
            template.LootTableId, template.LootChance, template.LootRolls, level);
        if (lootItems.Count == 0)
            return;

        var random = new Random();
        foreach (var cbid in lootItems)
        {
            // Equipment can't be ground-picked by the client, so auto-loot it to the killer when known.
            if (LootManager.Instance.RequiresAutoLoot(cbid) && killerCharacter != null)
                LootManager.Instance.AutoLootItem(cbid, killerCharacter);
            else
                SpawnLootOnGround(cbid, random);
        }
    }

    private void SpawnLootOnGround(int cbid, Random random)
    {
        var angle = (float)(random.NextDouble() * 2.0 * Math.PI);
        var distance = 1.0f + (float)random.NextDouble(); // 1-2 units
        var lootPosition = new Vector3(
            Position.X + (float)(Math.Cos(angle) * distance),
            Position.Y,
            Position.Z + (float)(Math.Sin(angle) * distance));

        LootManager.Instance.SpawnLootItem(cbid, lootPosition, Rotation, Map);
    }
}
