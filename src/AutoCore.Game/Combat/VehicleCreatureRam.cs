namespace AutoCore.Game.Combat;

using System.Collections.Generic;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server counterpart of the client's vehicle-vs-creature ram (<c>CollisionListener::
/// DoVehicleCollision</c>, <c>FUN_005d9290</c>): the retail client locally soft-destroys
/// creature-type objects with <c>sinMinHitPoints &lt; 5</c> on ram contact and shows the
/// impact FX. Without this server counterpart the creature stays alive server-side, its
/// ghost keeps streaming to a client that already destroyed it, and the RequestObject /
/// CreateCreature resync loop reads as a movement hitch at the moment of impact.
/// Hard creatures take approximate speed²-scaled ram damage (retail ram-build combat).
/// Only creatures whose faction is hostile to the rammer are eligible — town/quest
/// NPCs (Neutral −100, player races) can never be run over.
/// </summary>
public static class VehicleCreatureRam
{
    /// <summary>Minimum speed (world units/s) before ram damage applies (client speed gate parity).</summary>
    public const float MinSpeed = VehicleMapPropRam.MinSpeed;

    /// <summary>
    /// Horizontal contact radius. Tighter than the map-prop radius: creatures are small and
    /// mobile, and a generous sphere would kill enemies the vehicle merely drove past.
    /// </summary>
    public const float ContactRadius = 6.0f;

    /// <summary>Per-creature hit cooldown so one contact does not multi-hit every movement packet.</summary>
    public const int HitCooldownMs = VehicleMapPropRam.HitCooldownMs;

    /// <summary>Max ram damage per hit for non-soft creatures (after speed² scaling).</summary>
    public const int MaxDamagePerHit = VehicleMapPropRam.MaxDamagePerHit;

    /// <summary>Client soft-destroy threshold: <c>sinMinHitPoints &lt; 5</c> creature dies outright.</summary>
    public const short SoftMinHitPointsExclusive = VehicleMapPropRam.SoftMinHitPointsExclusive;

    // (instanceSerial, vehicleCoid) -> (creatureCoid -> lastHitMs). Serial-prefixed: creature
    // coids are identical across per-player instances of the same continent (SS-30 invariant).
    private static readonly Dictionary<(int Serial, long VehicleCoid), Dictionary<long, int>> LastHitMsByVehicle = new();

    [ThreadStatic]
    private static List<ClonedObjectBase> _spatialQueryBuffer;

    /// <summary>Test seam: clear cooldown table.</summary>
    internal static void ResetCooldownsForTests() => LastHitMsByVehicle.Clear();

    /// <summary>
    /// Run after a vehicle position/velocity update, alongside <see cref="VehicleMapPropRam.Process"/>.
    /// Safe no-op when disabled, slow, or no hostile creature is in contact.
    /// </summary>
    /// <param name="previousPosition">Position before this move packet (speed fallback).</param>
    /// <param name="dtSeconds">Approx time since previous sample for position-derived speed.</param>
    public static int Process(Vehicle vehicle, Vector3? previousPosition = null, float dtSeconds = 0.05f)
    {
        if (vehicle?.Map == null || vehicle.IsCorpse)
            return 0;

        if (!ServerConfig.EnableCreatureRamming)
            return 0;

        var speed = VehicleMapPropRam.ResolveSpeed(vehicle, previousPosition, dtSeconds);
        if (speed < MinSpeed)
            return 0;

        var map = vehicle.Map;
        var vehiclePos = vehicle.Position;
        var now = Environment.TickCount;
        var rammerFaction = vehicle.GetIDFaction();

        var buffer = _spatialQueryBuffer ??= new List<ClonedObjectBase>(64);
        map.Grid.QueryRadius(vehiclePos, ContactRadius, buffer);

        // Single closest ram-eligible creature already inside the contact sphere.
        Creature closest = null;
        var closestDistSq = float.MaxValue;

        foreach (var obj in buffer)
        {
            if (!IsRamEligibleCreature(obj, rammerFaction))
                continue;

            var distSq = obj.Position.DistSq(vehiclePos);
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closest = (Creature)obj;
            }
        }

        if (closest == null
            || !TryConsumeHitCooldown(map.InstanceSerial, vehicle.ObjectId.Coid, closest.ObjectId.Coid, now))
        {
            return 0;
        }

        return ApplyRamHit(vehicle, closest, speed);
    }

    /// <summary>
    /// Living, non-invincible creature whose faction is hostile to the rammer. Neutral (−100),
    /// unset, and player-race factions are never eligible — mirrors weapon target gating.
    /// </summary>
    public static bool IsRamEligibleCreature(ClonedObjectBase obj, int rammerFaction)
    {
        if (obj is not Creature creature || creature.IsCorpse || creature.IsInvincible)
            return false;

        return FactionHostility.IsHostile(creature.GetIDFaction(), rammerFaction);
    }

    /// <summary>
    /// Soft creatures (<c>sinMinHitPoints &lt; 5</c>) die outright — the client already
    /// destroyed them locally. Hard creatures take clamped speed²-scaled damage.
    /// </summary>
    public static int ComputeDamage(Creature creature, float speed)
    {
        if (creature == null || speed < MinSpeed)
            return 0;

        if (IsSoftCreature(creature))
            return Math.Max(1, creature.GetCurrentHP());

        var raw = speed * speed * 0.35f;
        var damage = (int)MathF.Round(raw);
        if (damage < 1)
            damage = 1;
        if (damage > MaxDamagePerHit)
            damage = MaxDamagePerHit;
        return damage;
    }

    /// <summary>Client soft-destroy predicate for creature types (MinHitPoints &lt; 5).</summary>
    public static bool IsSoftCreature(Creature creature)
    {
        var cb = creature?.CloneBaseObject;
        if (cb == null)
            return creature.GetMaximumHP() > 0 && creature.GetMaximumHP() < SoftMinHitPointsExclusive;

        return cb.SimpleObjectSpecific.MinHitPoints < SoftMinHitPointsExclusive;
    }

    private static int ApplyRamHit(Vehicle vehicle, Creature creature, float speed)
    {
        var damage = ComputeDamage(creature, speed);
        if (damage <= 0)
            return 0;

        var actual = creature.TakeDamage(damage, vehicle);
        if (actual <= 0)
            return 0;

        LogFilters.WriteIf(
            LogFilters.MapPropRam,
            LogType.Debug,
            "CreatureRam: vehicle={0} creature coid={1} cbid={2} speed={3:0.0} dmg={4} hp {5}/{6}",
            vehicle.ObjectId.Coid,
            creature.ObjectId.Coid,
            creature.CBID,
            speed,
            actual,
            creature.GetCurrentHP(),
            creature.GetMaximumHP());

        if (creature.GetCurrentHP() <= 0)
        {
            // Same kill flow as weapon fire: murderer credit, Violent death → loot, XP,
            // mission kill progress, destroy broadcast (Creature.OnDeath).
            creature.SetMurderer(vehicle);
            creature.OnDeath(DeathType.Violent);
        }

        return 1;
    }

    private static bool TryConsumeHitCooldown(int instanceSerial, long vehicleCoid, long creatureCoid, int now)
    {
        var key = (instanceSerial, vehicleCoid);
        if (!LastHitMsByVehicle.TryGetValue(key, out var perCreature))
        {
            perCreature = new Dictionary<long, int>();
            LastHitMsByVehicle[key] = perCreature;
        }

        if (perCreature.TryGetValue(creatureCoid, out var lastMs) && now - lastMs < HitCooldownMs)
            return false;

        perCreature[creatureCoid] = now;
        return true;
    }
}
