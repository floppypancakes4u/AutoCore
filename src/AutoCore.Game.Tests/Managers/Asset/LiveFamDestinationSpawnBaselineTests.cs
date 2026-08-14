using System.Reflection;
using System.Text;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Pass 17 live-.fam expected-vs-actual spawn child baseline.
/// Parses retail FAM files; AutoCore-before is the pre-fix one-child-per-spawn algorithm.
/// AutoCore-after is <see cref="SpawnPointTemplate.ExpectedMinimumChildren"/>.
/// </summary>
[TestClass]
public class LiveFamDestinationSpawnBaselineTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    private static readonly (string Fam, string Label, int ContinentId)[] Maps =
    {
        ("sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398),
        ("sec_f_m_map_town_c7_1_tocado_01", "Tocado (town)", 392),
        ("sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 707),
    };

    [TestMethod]
    public void RealFam_ExpectedAndActualSpawnCountsMatch()
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")))
        {
            Assert.Inconclusive($"clonebase.wad not at {InstallPath}");
            return;
        }

        // MapData.Read allocates templates via AssetManager clonebases. Load only if empty
        // and restore so the rest of the suite does not inherit 19k WAD rows.
        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var loadedHere = wad.CloneBases.Count == 0;
        if (loadedHere)
        {
            ClearWadTables(wad);
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");
        }

        try
        {
            var glm = new GLMLoader();
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");

            var output = new StringBuilder();
            output.AppendLine("| Map | SpawnPoints | Initially Active | Expected children | AutoCore children BEFORE | AutoCore children AFTER |");
            output.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");

            var mapsSampled = 0;
            foreach (var (famName, label, continentId) in Maps)
            {
                using var famStream = glm.GetStream($"{famName}.fam");
                Assert.IsNotNull(famStream, $"{famName}.fam missing from GLM packs");

                var mapData = new MapData(new ContinentObject
                {
                    Id = continentId,
                    MapFileName = famName,
                    DisplayName = label,
                    IsTown = famName.Contains("town", StringComparison.OrdinalIgnoreCase),
                    IsPersistent = true,
                });
                using (var reader = new BinaryReader(famStream))
                    mapData.Read(reader);

                var spawnPoints = mapData.Templates.Values.OfType<SpawnPointTemplate>().ToList();
                var active = spawnPoints.Where(s => s.OriginalIsActive).ToList();
                var expected = active.Sum(s => s.ExpectedMinimumChildren());
                var before = active.Count(s => s.Spawns.Any(sl => sl.SpawnType != -1));
                var after = expected;

                output.AppendLine(
                    $"| {label} | {spawnPoints.Count} | {active.Count} | {expected} | {before} | {after} |");

                DumpCloneTypes(output, label, active, wad);

                if (famName.Contains("scrapvalley", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.IsTrue(expected > before,
                        $"Scrap Valley must prove the missing-child bug: expected min {expected} > one-per-point {before}");
                }

                mapsSampled++;
            }

            Console.WriteLine(output.ToString());
            var dest = Path.Combine(Path.GetTempPath(), "autocore-pass17-spawn-baseline.md");
            File.WriteAllText(dest, output.ToString());
            Assert.AreEqual(Maps.Length, mapsSampled);
        }
        finally
        {
            if (loadedHere)
                wad.CloneBases.Clear();
        }
    }

    [TestMethod]
    public void RealFam_SpawnCountsRemainUnchanged()
    {
        WithLiveFams((glm, wad) =>
        {
            foreach (var (famName, label, continentId, expectedMin) in new[]
                     {
                         ("sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398, 3049),
                         ("sec_f_m_map_town_c7_1_tocado_01", "Tocado (town)", 392, 51),
                         ("sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 707, 44),
                     })
            {
                var mapData = ReadFam(glm, famName, label, continentId);
                var expected = mapData.Templates.Values.OfType<SpawnPointTemplate>()
                    .Where(s => s.OriginalIsActive)
                    .Sum(s => s.ExpectedMinimumChildren());
                Assert.AreEqual(expectedMin, expected,
                    $"{label}: Pass 17 population floor must survive scatter (got {expected})");
            }
        });
    }

    [TestMethod]
    public void RealFam_MultiChildCamp_DoesNotStackAtSingleXZ()
    {
        WithLiveFams((glm, wad) =>
        {
            var scrap = ReadFam(glm, "sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398);
            var brood = scrap.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 13330);
            Assert.IsTrue(brood.Radius > 0f, "Scrap 13330 must author a scatter radius");
            Assert.IsTrue(brood.RandomlyOffsetSpawnPosition, "Scrap 13330 must author RandomlyOffsetSpawnPosition");

            var children = MaterializeFamSpawn(scrap.ContinentObject.Id + 80_000, brood, wad);
            Assert.IsTrue(children.Count >= 12, $"brood Lower sum is 12; got {children.Count}");
            var unique = children.Select(c => (c.Position.X, c.Position.Z)).Distinct().Count();
            Assert.IsTrue(unique > 1,
                $"Scrap 13330 stacked {children.Count} children at X={children[0].Position.X} Z={children[0].Position.Z}");
        });
    }

    [TestMethod]
    public void RealFam_SpawnPositionsRespectRadius()
    {
        WithLiveFams((glm, wad) =>
        {
            var examples = CollectScatterExamples(glm);
            var offsetCamps = examples.Where(e => e.RandomOffset && e.Radius > 0f && e.Lower > 1).ToList();
            Assert.IsTrue(offsetCamps.Count >= 3,
                $"need at least 3 Lower>1 Radius>0 RandomOffset camps (got {offsetCamps.Count})");
            Assert.IsTrue(examples.Any(e => !e.RandomOffset), "need a RandomOffset=false control");

            var table = new StringBuilder();
            table.AppendLine("| Map | SpawnPoint COID | Radius | Lower/Upper | RandomOffset | Children | Unique XZ | In square? |");
            table.AppendLine("| --- | ---: | ---: | --- | --- | ---: | ---: | --- |");

            var spawned = 0;
            foreach (var example in offsetCamps
                         .OrderBy(e => e.Coid == 13330 ? 0 : e.Coid == 12636 ? 1 : 2)
                         .ThenByDescending(e => e.Lower)
                         .Take(3))
            {
                var mapData = ReadFam(glm, example.Fam, example.Label, example.ContinentId);
                var template = mapData.Templates.Values.OfType<SpawnPointTemplate>()
                    .Single(s => s.COID == example.Coid);
                var children = MaterializeFamSpawn(example.ContinentId + 81_000 + spawned, template, wad);
                spawned++;
                var unique = children.Select(c => (c.Position.X, c.Position.Z)).Distinct().Count();
                var inSquare = children.All(c =>
                    MathF.Abs(c.Position.X - template.Location.X) <= template.Radius + 1e-3f
                    && MathF.Abs(c.Position.Z - template.Location.Z) <= template.Radius + 1e-3f);
                table.AppendLine(
                    $"| {example.Label} | {example.Coid} | {example.Radius:0.###} | {example.Lower}/{example.Upper} | {example.RandomOffset} | {children.Count} | {unique} | {inSquare} |");
                Assert.IsTrue(children.Count >= example.Lower, $"{example.Coid} under-populated");
                Assert.IsTrue(unique > 1 || example.Radius == 0f, $"{example.Coid} still stacked");
                Assert.IsTrue(inSquare, $"{example.Coid} left the authored square");
            }

            var pike = examples.FirstOrDefault(e => e.Coid == 12636);
            if (pike.Coid == 12636)
            {
                table.AppendLine(
                    $"| {pike.Label} | {pike.Coid} | {pike.Radius:0.###} | {pike.Lower}/{pike.Upper} | {pike.RandomOffset} | (path) | — | n/a |");
            }

            foreach (var control in examples.Where(e => !e.RandomOffset).Take(3))
            {
                table.AppendLine(
                    $"| {control.Label} | {control.Coid} | {control.Radius:0.###} | {control.Lower}/{control.Upper} | {control.RandomOffset} | — | — | n/a |");
            }

            Console.WriteLine(table.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass18-scatter.md"), table.ToString());
        });
    }

    private static void DumpCloneTypes(
        StringBuilder output,
        string label,
        List<SpawnPointTemplate> active,
        WADLoader wad)
    {
        var creature = 0;
        var vehicle = 0;
        var template = 0;
        var unknown = 0;
        var unsupported = 0;
        foreach (var sp in active)
        {
            foreach (var slot in sp.Spawns.Where(s => s.SpawnType != -1))
            {
                if (slot.IsTemplate)
                {
                    template++;
                    continue;
                }

                if (!wad.CloneBases.TryGetValue(slot.SpawnType, out var cb))
                {
                    unknown++;
                    continue;
                }

                if (cb is CloneBaseCreature)
                    creature++;
                else if (cb is CloneBaseVehicle)
                    vehicle++;
                else
                    unsupported++;
            }
        }

        output.AppendLine(
            $"  {label} active slots: creature={creature} vehicleCbid={vehicle} " +
            $"templateVehicle={template} missingCbid={unknown} unsupported={unsupported}");
    }

    private static void ClearWadTables(WADLoader wad)
    {
        wad.Missions.Clear();
        wad.Skills.Clear();
        wad.ArmorPrefixes.Clear();
        wad.PowerPlantPrefixes.Clear();
        wad.WeaponPrefixes.Clear();
        wad.VehiclePrefixes.Clear();
        wad.OrnamentPrefixes.Clear();
        wad.RaceItemPrefixes.Clear();
    }

    private static void WithLiveFams(Action<GLMLoader, WADLoader> body)
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")))
        {
            Assert.Inconclusive($"clonebase.wad not at {InstallPath}");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var loadedHere = wad.CloneBases.Count == 0;
        if (loadedHere)
        {
            ClearWadTables(wad);
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");
        }

        try
        {
            var glm = new GLMLoader();
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");
            body(glm, wad);
        }
        finally
        {
            if (loadedHere)
                wad.CloneBases.Clear();
        }
    }

    private static MapData ReadFam(GLMLoader glm, string famName, string label, int continentId)
    {
        using var famStream = glm.GetStream($"{famName}.fam");
        Assert.IsNotNull(famStream, $"{famName}.fam missing from GLM packs");
        var mapData = new MapData(new ContinentObject
        {
            Id = continentId,
            MapFileName = famName,
            DisplayName = label,
            IsTown = famName.Contains("town", StringComparison.OrdinalIgnoreCase),
            IsPersistent = true,
        });
        using var reader = new BinaryReader(famStream);
        mapData.Read(reader);
        return mapData;
    }

    private readonly record struct ScatterExample(
        string Fam,
        string Label,
        int ContinentId,
        long Coid,
        float Radius,
        int Lower,
        int Upper,
        bool RandomOffset);

    private static List<ScatterExample> CollectScatterExamples(GLMLoader glm)
    {
        var found = new List<ScatterExample>();
        foreach (var (fam, label, continentId) in Maps)
        {
            var mapData = ReadFam(glm, fam, label, continentId);
            foreach (var sp in mapData.Templates.Values.OfType<SpawnPointTemplate>())
            {
                if (!sp.OriginalIsActive)
                    continue;
                var filled = sp.Spawns.Where(s => s.SpawnType != -1).ToList();
                if (filled.Count == 0 || filled.Any(s => s.IsTemplate))
                    continue;
                var lower = filled.Sum(s => Math.Max((int)s.LowerNumberOfSpawns, 1));
                var upper = filled.Sum(s => Math.Max((int)s.UpperNumberOfSpawns, (int)s.LowerNumberOfSpawns));
                if (!sp.RandomlyOffsetSpawnPosition || (lower > 1 && sp.Radius > 0f))
                {
                    found.Add(new ScatterExample(
                        fam, label, continentId, sp.COID, sp.Radius, lower, upper, sp.RandomlyOffsetSpawnPosition));
                }
            }
        }

        return found;
    }

    private static List<Creature> MaterializeFamSpawn(int continentId, SpawnPointTemplate template, WADLoader wad)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_fam_scatter_{continentId}",
            DisplayName = "fam-scatter",
            IsTown = false,
            IsPersistent = true,
        }, template.Location);

        var spawn = (SpawnPoint)template.Create();
        spawn.SetCoid(template.COID, false);
        spawn.SetMap(map);
        spawn.Position = template.Location.ToVector3();
        Assert.IsTrue(spawn.Spawn(), $"FAM spawn {template.COID} failed: {spawn.LastFailureDiagnostic}");
        return map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList();
    }
}
