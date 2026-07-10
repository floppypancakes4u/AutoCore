namespace AutoCore.Game.Map;

using System;
using System.Collections.Generic;
using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

/// <summary>
/// Pure interest-management policy: given a scope centre and the entities on a map, decides which
/// ones should be in scope (ghosted) for one connection. Selection is driven entirely by its
/// arguments — including an <c>isGhosted</c> predicate that supplies the hysteresis memory — so it
/// can be unit-tested without a TNL connection.
///
/// Tiers, in priority order:
///  1. Players — always in scope, regardless of distance.
///  2. Mission givers — in scope within an extended add radius, retained out to an extended drop
///     radius (hysteresis) so they do not flicker.
///  3. Everything else nearby — already-ghosted entities within the drop radius are retained first
///     ("scope first, no flicker"); new entities within the add radius are then added nearest-first
///     until the soft cap is reached.
///
/// The town/field filter mirrors the retired SectorMap.ObjectsInRange semantics: vehicles are
/// hidden in towns and characters are hidden in the field. The scope object itself always passes.
/// </summary>
public static class InterestSelector
{
    // Internal-settable so tests can pin/override the tuning without recompiling.
    internal static float BaseScopeAddRadius = 400f;
    internal static float BaseScopeDropRadius = 460f;
    internal static float MissionGiverAddRadius = 800f;
    internal static float MissionGiverDropRadius = 920f;
    internal static int ScopeSoftCap = 700;

    public static void Select(
        ClonedObjectBase self,
        Vector3 center,
        bool isTown,
        IReadOnlyList<ClonedObjectBase> players,
        IReadOnlyList<ClonedObjectBase> missionGivers,
        IReadOnlyList<ClonedObjectBase> nearby,
        Func<ClonedObjectBase, bool> isGhosted,
        List<ClonedObjectBase> output)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(missionGivers);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(isGhosted);
        ArgumentNullException.ThrowIfNull(output);

        output.Clear();
        var selected = new HashSet<ClonedObjectBase>();

        // Tier 0: the scope object itself, always (bypasses the town/field filter).
        if (self != null && selected.Add(self))
            output.Add(self);

        // Tier 1: players unconditionally.
        var playerSet = new HashSet<ClonedObjectBase>();
        foreach (var player in players)
        {
            if (player == null)
                continue;

            playerSet.Add(player);
            if (selected.Add(player))
                output.Add(player);
        }

        // Tier 2: mission givers within the extended radius, retained out to the extended drop
        // radius when already ghosted.
        var missionGiverSet = new HashSet<ClonedObjectBase>();
        foreach (var giver in missionGivers)
        {
            if (giver == null)
                continue;

            missionGiverSet.Add(giver);
            if (selected.Contains(giver))
                continue;

            var radius = isGhosted(giver) ? MissionGiverDropRadius : MissionGiverAddRadius;
            if (DistSqXZ(center, giver.Position) <= radius * radius && selected.Add(giver))
                output.Add(giver);
        }

        // Tier 3: everything else nearby, soft-capped.
        var addSq = BaseScopeAddRadius * BaseScopeAddRadius;
        var dropSq = BaseScopeDropRadius * BaseScopeDropRadius;

        var kept = new List<(ClonedObjectBase Entity, float DistSq)>();
        var candidates = new List<(ClonedObjectBase Entity, float DistSq)>();

        foreach (var entity in nearby)
        {
            if (entity == null || selected.Contains(entity))
                continue;

            // Players and mission givers are owned by tiers 1/2 — never reconsider them here.
            if (playerSet.Contains(entity) || missionGiverSet.Contains(entity))
                continue;

            if (IsFilteredOut(entity, isTown))
                continue;

            var distSq = DistSqXZ(center, entity.Position);
            if (isGhosted(entity))
            {
                // Scope first: retained while inside the drop radius (hysteresis), dropped beyond.
                if (distSq <= dropSq)
                    kept.Add((entity, distSq));
            }
            else if (distSq <= addSq)
            {
                candidates.Add((entity, distSq));
            }
        }

        kept.Sort(static (a, b) => a.DistSq.CompareTo(b.DistSq));
        candidates.Sort(static (a, b) => a.DistSq.CompareTo(b.DistSq));

        var budget = ScopeSoftCap;

        // Kept ghosts consume budget before any new adds so a nearer new NPC cannot displace an
        // already-visible one (which would flicker).
        foreach (var (entity, _) in kept)
        {
            if (budget <= 0)
                break;

            if (selected.Add(entity))
            {
                output.Add(entity);
                budget--;
            }
        }

        foreach (var (entity, _) in candidates)
        {
            if (budget <= 0)
                break;

            if (selected.Add(entity))
            {
                output.Add(entity);
                budget--;
            }
        }
    }

    /// <summary>
    /// Town/field visibility filter mirroring the retired ObjectsInRange semantics: vehicles are
    /// hidden in towns, characters are hidden in the field. Plain creatures are never filtered.
    /// </summary>
    private static bool IsFilteredOut(ClonedObjectBase entity, bool isTown)
    {
        if (entity is Vehicle)
            return isTown;

        if (entity is Character)
            return !isTown;

        return false;
    }

    private static float DistSqXZ(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}
