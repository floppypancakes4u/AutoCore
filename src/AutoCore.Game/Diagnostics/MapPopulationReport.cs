namespace AutoCore.Game.Diagnostics;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;

/// <summary>
/// Actual-versus-authored NPC population per map.
/// <para>
/// Exists to answer one question the code alone cannot: is the world over-populated relative to the
/// authored spawn data? The spawn count for a slot comes from
/// <c>SpawnPointTemplate.ResolveSlotPopulationTarget</c>, which reads authored
/// <c>LowerNumberOfSpawns</c>/<c>UpperNumberOfSpawns</c> and rolls uniformly between them — so a
/// slot authored 0/3 averages 1.5 NPCs. Two rules in that method are worth watching:
/// a slot authored <c>0/0</c> returns <b>1</b> rather than 0, and any bound above 10 is clamped.
/// If the authored fields mean something narrower in retail, either rule inflates the world without
/// any single spawn point looking wrong.
/// </para>
/// <para>
/// Population matters beyond appearance. Every additional live NPC inside the interest radius takes
/// a share of a fixed per-connection packet budget, so density sets per-creature pose rate directly,
/// and pose rate sets how far the client drifts before it crosses the 15-unit hard-teleport
/// threshold (<c>cfMaxNetworkOffset</c> @009d000c).
/// </para>
/// </summary>
public static class MapPopulationReport
{
    /// <summary>One map's population accounting.</summary>
    public sealed class MapPopulation
    {
        public int ContinentId { get; init; }

        /// <summary>Spawn points present on the map.</summary>
        public int SpawnPoints { get; init; }

        /// <summary>Spawn points that currently own at least one live child.</summary>
        public int ActiveSpawnPoints { get; init; }

        /// <summary>Live NPCs owned by spawn points.</summary>
        public int LiveChildren { get; init; }

        /// <summary>Sum of authored per-slot minima — the floor the world should hold.</summary>
        public int AuthoredMinimum { get; init; }

        /// <summary>Live NPCs carrying an AI state (i.e. able to move).</summary>
        public int NpcAiEntities { get; init; }

        /// <summary>Live creatures that are not players.</summary>
        public int Creatures { get; init; }

        /// <summary>Live vehicles.</summary>
        public int Vehicles { get; init; }

        /// <summary>
        /// Ratio of live children to authored minimum. Large values mean the roll between
        /// lower/upper is filling far above the authored floor.
        /// </summary>
        public double LiveToAuthoredMinimum =>
            AuthoredMinimum > 0 ? (double)LiveChildren / AuthoredMinimum : 0.0;
    }

    /// <summary>
    /// Builds the accounting for every live map. Kept here rather than exposing MapManager's
    /// internal map enumerator to other assemblies.
    /// </summary>
    public static IReadOnlyList<MapPopulation> BuildForAllMaps() =>
        Build(Managers.MapManager.Instance.AllMaps());

    /// <summary>Builds the per-map accounting. Read-only; safe to call on a live server.</summary>
    public static IReadOnlyList<MapPopulation> Build(IEnumerable<SectorMap> maps)
    {
        var report = new List<MapPopulation>();
        if (maps == null)
            return report;

        foreach (var map in maps)
        {
            if (map == null)
                continue;

            var objects = map.Objects.Values.ToList();
            var spawnPoints = objects.OfType<SpawnPoint>().ToList();

            var liveChildren = 0;
            var activeSpawnPoints = 0;
            var authoredMinimum = 0;
            foreach (var spawn in spawnPoints)
            {
                var owned = spawn.CountOwnedChildren();
                liveChildren += owned;
                if (owned > 0)
                    activeSpawnPoints++;
                authoredMinimum += spawn.AuthoredMinimumPopulation;
            }

            report.Add(new MapPopulation
            {
                ContinentId = map.ContinentId,
                SpawnPoints = spawnPoints.Count,
                ActiveSpawnPoints = activeSpawnPoints,
                LiveChildren = liveChildren,
                AuthoredMinimum = authoredMinimum,
                NpcAiEntities = map.NpcAiEntities.Count,
                Creatures = objects.Count(o => o is Creature && o is not Character),
                Vehicles = objects.Count(o => o is Vehicle),
            });
        }

        return report;
    }
}
