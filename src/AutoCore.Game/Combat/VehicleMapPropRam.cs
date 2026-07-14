namespace AutoCore.Game.Combat;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server approximation of client <c>CollisionListener::DoVehicleCollision</c> (<c>FUN_005d9290</c>):
/// moving vehicles damage collidable, non-invincible pure map props on contact.
/// Soft props (ObjectGraphicsPhysics / low MinHitPoints) are destroyed in one hit.
/// TacArc soft-target shooting is intentionally out of scope.
/// </summary>
public static class VehicleMapPropRam
{
    /// <summary>Minimum speed (world units/s) before ram damage applies (client has a speed gate).</summary>
    public const float MinSpeed = 2.5f;

    /// <summary>
    /// Horizontal contact radius (vehicle body + prop footprint approximation).
    /// Kept modest so roadside clusters are not AOE-deleted; closest-only selection
    /// further limits multi-kill (see <see cref="Process"/>).
    /// </summary>
    public const float ContactRadius = 10.0f;

    /// <summary>Client soft-destroy: <c>sinMinHitPoints &lt; 5</c> with physics/creature types.</summary>
    public const short SoftMinHitPointsExclusive = 5;

    /// <summary>Per-prop hit cooldown so one contact does not multi-kill every packet.</summary>
    public const int HitCooldownMs = 250;

    /// <summary>Max ram damage per hit for non-soft props (after speed² scaling).</summary>
    public const int MaxDamagePerHit = 2000;

    /// <summary>One prop per movement packet — matches a single contact, not a sphere wipe.</summary>
    public const int MaxHitsPerProcess = 1;

    /// <summary>
    /// Ground loot spawns this far ahead of the vehicle (travel/facing dir) so the pickup
    /// is not under the chassis (client often misses contact-volume under the player).
    /// </summary>
    public const float LootForwardOffsetMeters = 3.5f;

    // vehicleCoid -> (propCoid -> lastHitMs)
    private static readonly Dictionary<long, Dictionary<long, int>> LastHitMsByVehicle = new();

    /// <summary>Test seam: clear cooldown table.</summary>
    internal static void ResetCooldownsForTests() => LastHitMsByVehicle.Clear();

    /// <summary>
    /// Run after a vehicle position/velocity update. Safe no-op when speed is low or map empty.
    /// Client collision is local-only (no C2S); this is the server simulation of ram.
    /// </summary>
    /// <param name="previousPosition">
    /// Position before this move packet was applied. Used when client velocity is near-zero.
    /// </param>
    /// <param name="dtSeconds">Approx time since previous sample for position-derived speed.</param>
    public static int Process(Vehicle vehicle, Vector3? previousPosition = null, float dtSeconds = 0.05f)
    {
        if (vehicle?.Map == null || vehicle.IsCorpse)
            return 0;

        var speed = ResolveSpeed(vehicle, previousPosition, dtSeconds);
        if (speed < MinSpeed)
            return 0;

        var map = vehicle.Map;
        var vehiclePos = vehicle.Position;
        var radiusSq = ContactRadius * ContactRadius;
        var now = Environment.TickCount;
        var eligible = 0;
        var nearby = 0;

        // Pick the single closest eligible prop in range (not every prop in the sphere).
        GraphicsObject closest = null;
        var closestDistSq = float.MaxValue;

        foreach (var obj in map.Objects.Values.ToArray())
        {
            if (!IsRamEligibleMapProp(obj))
                continue;

            eligible++;
            var prop = (GraphicsObject)obj;
            var distSq = prop.Position.DistSq(vehiclePos);
            if (distSq > radiusSq)
                continue;

            nearby++;
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closest = prop;
            }
        }

        var hits = 0;
        if (closest != null
            && MaxHitsPerProcess > 0
            && TryConsumeHitCooldown(vehicle.ObjectId.Coid, closest.ObjectId.Coid, now))
        {
            hits = ApplyRamHit(vehicle, closest, speed);
        }

        // Helps diagnose "I ram but no log": often eligible=0 (client-only scenery) or speed low.
        if (hits == 0 && LogFilters.MapPropRam && (now % 2000) < 50)
        {
            LogFilters.WriteIf(
                LogFilters.MapPropRam,
                LogType.Debug,
                "MapPropRam scan: vehicle={0} speed={1:0.0} eligibleProps={2} nearby={3} (no hit)",
                vehicle.ObjectId.Coid, speed, eligible, nearby);
        }

        return hits;
    }

    private static int ApplyRamHit(Vehicle vehicle, GraphicsObject prop, float speed)
    {
        var damage = ComputeDamage(prop, speed);
        if (damage <= 0)
            return 0;

        var hpBefore = prop.GetCurrentHP();
        var actual = prop.TakeDamage(damage, vehicle);
        if (actual <= 0)
            return 0;

        LogFilters.WriteIf(
            LogFilters.MapPropRam,
            LogType.Debug,
            "MapPropRam: vehicle={0} prop coid={1} cbid={2} speed={3:0.0} dmg={4} hp {5}->{6}/{7}",
            vehicle.ObjectId.Coid,
            prop.ObjectId.Coid,
            prop.CBID,
            speed,
            actual,
            hpBefore,
            prop.GetCurrentHP(),
            prop.GetMaximumHP());

        if (prop.GetCurrentHP() <= 0)
        {
            // A few meters in front of the vehicle so loot is not under the body.
            prop.DeathLootOverridePosition = ResolveRamLootPosition(vehicle);
            prop.SetMurderer(vehicle);
            prop.OnDeath(DeathType.Silent);
        }

        return 1;
    }

    /// <summary>
    /// Loot spawn for ram kills: vehicle pose + <see cref="LootForwardOffsetMeters"/> along
    /// travel direction (velocity), falling back to facing yaw when nearly stationary.
    /// </summary>
    public static Vector3 ResolveRamLootPosition(Vehicle vehicle, float offsetMeters = LootForwardOffsetMeters)
    {
        if (vehicle == null)
            return new Vector3();

        if (offsetMeters < 0f)
            offsetMeters = 0f;

        var pos = vehicle.Position;
        var (fx, fz) = ResolveHorizontalForward(vehicle);
        return new Vector3(
            pos.X + (fx * offsetMeters),
            pos.Y,
            pos.Z + (fz * offsetMeters));
    }

    /// <summary>Unit forward in XZ: velocity when moving, else rotation yaw.</summary>
    public static (float X, float Z) ResolveHorizontalForward(Vehicle vehicle)
    {
        var vx = vehicle.Velocity.X;
        var vz = vehicle.Velocity.Z;
        var vLenSq = (vx * vx) + (vz * vz);
        // ~0.5 u/s horizontal — use travel dir while ramming.
        if (vLenSq > 0.25f)
        {
            var inv = 1f / MathF.Sqrt(vLenSq);
            return (vx * inv, vz * inv);
        }

        var yaw = VehicleDriveInputs.YawFromQuaternion(vehicle.Rotation);
        // Same convention as NPC drive: forward = (sin(yaw), cos(yaw)).
        return (MathF.Sin(yaw), MathF.Cos(yaw));
    }

    /// <summary>
    /// Prefer packet velocity; if under threshold, estimate from position delta.
    /// </summary>
    public static float ResolveSpeed(Vehicle vehicle, Vector3? previousPosition, float dtSeconds)
    {
        var speed = Speed(vehicle.Velocity);
        if (previousPosition == null || dtSeconds <= 0.001f)
            return speed;

        var moved = previousPosition.Value.Dist(vehicle.Position);
        var estimated = moved / dtSeconds;
        return estimated > speed ? estimated : speed;
    }

    /// <summary>
    /// Pure map <see cref="GraphicsObject"/> only (not Vehicle/Creature/Character/inventory SimpleObject).
    /// Non-invincible, not corpse, collidable clonebase when known.
    /// </summary>
    public static bool IsRamEligibleMapProp(ClonedObjectBase obj)
    {
        if (obj is null || obj.IsCorpse || obj.IsInvincible)
            return false;

        // Exact type: Vehicles/Creatures/SimpleObject inherit GraphicsObject but are not scenery.
        if (obj.GetType() != typeof(GraphicsObject))
            return false;

        var cb = obj.CloneBaseObject;
        if (cb == null)
            return true; // unit tests seed HP without full clonebase type metadata

        if (!IsMapPropObjectType(cb.Type))
            return false;

        return IsCollidable(cb);
    }

    public static bool IsSoftDestructible(GraphicsObject prop)
    {
        var cb = prop?.CloneBaseObject;
        if (cb == null)
        {
            // No clonebase: treat low-HP props as soft (matches fragile scenery).
            return prop.GetMaximumHP() > 0 && prop.GetMaximumHP() <= SoftMinHitPointsExclusive;
        }

        var minHp = cb.SimpleObjectSpecific.MinHitPoints;
        // Client: MinHitPoints < 5 && (type == ObjectGraphicsPhysics || type == Creature)
        if (minHp >= SoftMinHitPointsExclusive)
            return false;

        return cb.Type == CloneBaseObjectType.ObjectGraphicsPhysics
               || cb.Type == CloneBaseObjectType.Creature
               || cb.Type == CloneBaseObjectType.Object
               || cb.Type == CloneBaseObjectType.QuestObject;
    }

    public static int ComputeDamage(GraphicsObject prop, float speed)
    {
        if (prop == null || speed < MinSpeed)
            return 0;

        if (IsSoftDestructible(prop))
            return Math.Max(1, prop.GetCurrentHP());

        // Approximate client speed² * mass * k damage, clamped.
        var raw = speed * speed * 0.35f;
        var damage = (int)MathF.Round(raw);
        if (damage < 1)
            damage = 1;
        if (damage > MaxDamagePerHit)
            damage = MaxDamagePerHit;
        return damage;
    }

    private static bool IsMapPropObjectType(CloneBaseObjectType type) =>
        type is CloneBaseObjectType.Object
            or CloneBaseObjectType.ObjectGraphicsPhysics
            or CloneBaseObjectType.QuestObject
            or CloneBaseObjectType.MissionObject;

    /// <summary>
    /// Collidable when physics object type, or Flags bit 0 set (packed <c>bitCollidable</c> in wad/clonebase).
    /// Invincible is Flags bit 12 (existing server convention).
    /// </summary>
    private static bool IsCollidable(CloneBases.CloneBaseObject cb)
    {
        if (cb.Type == CloneBaseObjectType.ObjectGraphicsPhysics)
            return true;

        // bitCollidable is packed into Flags; bit 0 is the common collidable bit in this pack layout.
        return (cb.SimpleObjectSpecific.Flags & 1) != 0;
    }

    private static float Speed(Vector3 velocity)
    {
        return MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
    }

    private static bool TryConsumeHitCooldown(long vehicleCoid, long propCoid, int nowMs)
    {
        if (!LastHitMsByVehicle.TryGetValue(vehicleCoid, out var byProp))
        {
            byProp = new Dictionary<long, int>();
            LastHitMsByVehicle[vehicleCoid] = byProp;
        }

        if (byProp.TryGetValue(propCoid, out var last) && unchecked(nowMs - last) < HitCooldownMs)
            return false;

        byProp[propCoid] = nowMs;
        return true;
    }
}
