namespace AutoCore.Game.Combat;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Structures;

/// <summary>
/// Soft/hard TacArc target acquisition matching client <c>FUN_0056c0a0</c> structure:
/// hard target first when valid; soft targets filtered then sorted by ascending distance.
/// </summary>
public static class WeaponFireTargetAcquisition
{
    /// <summary>Lightweight candidate for pure acquisition (mapped from map entities by Vehicle).</summary>
    public readonly struct Candidate
    {
        public Candidate(
            long coid,
            Vector3 position,
            int faction,
            bool isCorpse,
            bool isInvincible,
            bool isDamageable,
            bool isCombatant = false,
            bool ignoresHostility = false)
        {
            Coid = coid;
            Position = position;
            Faction = faction;
            IsCorpse = isCorpse;
            IsInvincible = isInvincible;
            IsDamageable = isDamageable;
            IsCombatant = isCombatant;
            IgnoresHostility = ignoresHostility;
        }

        public long Coid { get; }
        public Vector3 Position { get; }
        public int Faction { get; }
        public bool IsCorpse { get; }
        public bool IsInvincible { get; }
        /// <summary>Has positive HP and/or is a demolishable/world object allowed as soft target.</summary>
        public bool IsDamageable { get; }
        /// <summary>
        /// Creature/Vehicle (true combat target). Soft fill prefers combatants over map props so
        /// scenery with HP does not steal the single soft slot when maxTargets is 1.
        /// </summary>
        public bool IsCombatant { get; }
        /// <summary>
        /// Pure map props (ram-eligible scenery): skip faction hostility so fences/rails/billboards
        /// can be shot even when their map faction matches the player.
        /// </summary>
        public bool IgnoresHostility { get; }
    }

    public readonly struct HitTarget
    {
        public HitTarget(long coid, Vector3 position, float distanceFromShooter, bool isPrimary)
        {
            Coid = coid;
            Position = position;
            DistanceFromShooter = distanceFromShooter;
            IsPrimary = isPrimary;
        }

        public long Coid { get; }
        public Vector3 Position { get; }
        public float DistanceFromShooter { get; }
        public bool IsPrimary { get; }
    }

    /// <summary>
    /// Acquire up to max targets for one weapon slot.
    /// </summary>
    /// <param name="includeHardTarget">Turret only — seed hard target at index 0 when in-arc.</param>
    public static List<HitTarget> Acquire(
        Vector3 shooterPos,
        Vector3 aimUnit,
        int shooterFaction,
        long shooterCoid,
        long? ownerCoid,
        WeaponSpecific weaponSpec,
        IReadOnlyList<Candidate> candidates,
        long? hardTargetCoid,
        bool includeHardTarget)
    {
        var maxTargets = WeaponFireTargetLimits.GetMaxTargets(weaponSpec.Flags, weaponSpec.SprayTargets);
        var result = new List<HitTarget>(maxTargets);
        HashSet<long> taken = new();

        if (includeHardTarget && hardTargetCoid is long hardCoid)
        {
            var hard = FindByCoid(candidates, hardCoid);
            if (hard.HasValue &&
                IsEligible(hard.Value, shooterFaction, shooterCoid, ownerCoid, excludeCoid: null) &&
                TryMeasure(shooterPos, aimUnit, weaponSpec, hard.Value, out var hardDist))
            {
                result.Add(new HitTarget(hard.Value.Coid, hard.Value.Position, hardDist, isPrimary: true));
                taken.Add(hard.Value.Coid);
            }
        }

        if (result.Count >= maxTargets)
            return result;

        // Soft targets: eligible + in arc/range, sorted by ascending distance.
        var soft = new List<(Candidate c, float dist)>();
        foreach (var c in candidates)
        {
            if (taken.Contains(c.Coid))
                continue;
            if (!IsEligible(c, shooterFaction, shooterCoid, ownerCoid, excludeCoid: null))
                continue;
            if (!TryMeasure(shooterPos, aimUnit, weaponSpec, c, out var dist))
                continue;
            soft.Add((c, dist));
        }

        // Soft fill by ascending distance only (client FUN_0056c0a0). Combatant tier preference
        // was a server hack when every HP-bearing GraphicsObject was a soft target; with
        // ram-eligible-only props it made distant NPCs steal shots from fences under the gun.
        soft.Sort((a, b) => a.dist.CompareTo(b.dist));
        foreach (var (c, dist) in soft)
        {
            if (result.Count >= maxTargets)
                break;
            var isPrimary = result.Count == 0;
            result.Add(new HitTarget(c.Coid, c.Position, dist, isPrimary));
            taken.Add(c.Coid);
        }

        return result;
    }

    /// <summary>
    /// Omnidirectional splash around impact; excludes already-hit COIDs.
    /// Falloff distance is measured from impact (caller applies SprayFalloff).
    /// </summary>
    public static List<HitTarget> AcquireExplosion(
        Vector3 impact,
        float explosionRadius,
        int shooterFaction,
        long shooterCoid,
        long? ownerCoid,
        IReadOnlyList<Candidate> candidates,
        IReadOnlyCollection<long> alreadyHit)
    {
        var result = new List<HitTarget>();
        if (explosionRadius <= 0f)
            return result;

        var r2 = explosionRadius * explosionRadius;
        foreach (var c in candidates)
        {
            if (alreadyHit.Contains(c.Coid))
                continue;
            if (!IsEligible(c, shooterFaction, shooterCoid, ownerCoid, excludeCoid: null))
                continue;

            var distSq = c.Position.DistSq(impact);
            if (distSq > r2)
                continue;

            var dist = MathF.Sqrt(distSq);
            result.Add(new HitTarget(c.Coid, c.Position, dist, isPrimary: false));
        }

        result.Sort((a, b) => a.DistanceFromShooter.CompareTo(b.DistanceFromShooter));
        return result;
    }

    public static bool IsEligible(
        Candidate c,
        int shooterFaction,
        long shooterCoid,
        long? ownerCoid,
        long? excludeCoid)
    {
        if (c.Coid == shooterCoid)
            return false;
        if (ownerCoid.HasValue && c.Coid == ownerCoid.Value)
            return false;
        if (excludeCoid.HasValue && c.Coid == excludeCoid.Value)
            return false;
        if (c.IsCorpse || c.IsInvincible || !c.IsDamageable)
            return false;
        // Map props (guard rails, billboards, fences): no faction hostility — inanimate scenery.
        if (c.IgnoresHostility)
            return true;
        // Hostility stand-in for client owner vfunc +0x298: different faction.
        // Callers must pass GetIDFaction() (owner-chain), not chassis SimpleObject.Faction —
        // NPC vehicles often share chassis faction with players while the driver is hostile.
        if (c.Faction == shooterFaction)
            return false;
        return true;
    }

    private static bool TryMeasure(
        Vector3 shooterPos,
        Vector3 aimUnit,
        WeaponSpecific weaponSpec,
        Candidate c,
        out float dist)
    {
        dist = shooterPos.Dist(c.Position);
        if (!TacArcGeometry.IsInRange(dist, weaponSpec.RangeMin, weaponSpec.RangeMax))
            return false;
        if (!TacArcGeometry.IsInArc(shooterPos, aimUnit, c.Position, weaponSpec.ValidArc))
            return false;
        return true;
    }

    private static Candidate? FindByCoid(IReadOnlyList<Candidate> candidates, long coid)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Coid == coid)
                return candidates[i];
        }
        return null;
    }
}
