using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Database.World.Models;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// <see cref="MapPopulationReport"/> answers whether the world holds more NPCs than the authored
/// spawn data calls for — the question behind "the map looks VERY populated". Density is not merely
/// cosmetic: each live NPC inside the interest radius takes a share of a fixed packet budget, so it
/// sets per-creature pose rate, which sets how far the client drifts before crossing the 15-unit
/// hard-teleport threshold.
/// </summary>
[TestClass]
public class MapPopulationReportTests
{
    private const int ContId = 9_311;

    [TestMethod]
    public void Build_NullMaps_ReturnsEmpty()
    {
        Assert.AreEqual(0, MapPopulationReport.Build(null).Count);
    }

    [TestMethod]
    public void Build_EmptyMap_ReportsZeroes()
    {
        var map = CreateMap();

        var report = MapPopulationReport.Build(new[] { map });

        Assert.AreEqual(1, report.Count);
        Assert.AreEqual(ContId, report[0].ContinentId);
        Assert.AreEqual(0, report[0].SpawnPoints);
        Assert.AreEqual(0, report[0].LiveChildren);
        Assert.AreEqual(0.0, report[0].LiveToAuthoredMinimum, 0.0001);
    }

    [TestMethod]
    public void Build_CountsCreaturesSeparatelyFromVehicles()
    {
        var map = CreateMap();
        PlaceCreature(map, 1);
        PlaceCreature(map, 2);
        PlaceVehicle(map, 3);

        var report = MapPopulationReport.Build(new[] { map })[0];

        Assert.AreEqual(2, report.Creatures, "two creatures on the map");
        Assert.AreEqual(1, report.Vehicles, "one vehicle on the map");
    }

    /// <summary>Players must never be counted as world population.</summary>
    [TestMethod]
    public void Build_ExcludesPlayerCharactersFromCreatureCount()
    {
        var map = CreateMap();
        PlaceCreature(map, 1);

        var player = new Character { Position = new Vector3(0f, 0f, 0f) };
        player.SetCoid(2, true);
        player.SetMap(map);

        var report = MapPopulationReport.Build(new[] { map })[0];

        Assert.AreEqual(1, report.Creatures,
            "a Character is a player, not world population, even though it derives from Creature");
    }

    /// <summary>Guards against divide-by-zero when nothing is authored.</summary>
    [TestMethod]
    public void LiveToAuthoredMinimum_ZeroAuthored_IsZeroNotInfinity()
    {
        var map = CreateMap();
        PlaceCreature(map, 1);

        var report = MapPopulationReport.Build(new[] { map })[0];

        Assert.AreEqual(0.0, report.LiveToAuthoredMinimum, 0.0001);
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_population_{ContId}",
            DisplayName = "population",
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static void PlaceCreature(SectorMap map, long coid)
    {
        var creature = new Creature { Position = new Vector3(0f, 0f, 0f) };
        creature.SetCoid(coid, false);
        creature.SetMap(map);
    }

    private static void PlaceVehicle(SectorMap map, long coid)
    {
        var vehicle = new Vehicle { Position = new Vector3(0f, 0f, 0f) };
        vehicle.SetCoid(coid, false);
        vehicle.SetMap(map);
    }
}
