using System.Reflection;
using System.Text;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Pass 19 — resolve every authored FAM IsTemplate slot through the production
/// <see cref="AssetManager.GetVehicleTemplate"/> path (wad.xml <c>tVehicleTemplate</c>).
/// </summary>
[TestClass]
public class LiveFamTemplateVehicleBaselineTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    private const string WadXmlPath = InstallPath + @"\wad.xml";
    private const string ScrapFam = "sec_f_b_map_hwy_a2_1_scrapvalley";
    private const string TocadoFam = "sec_f_m_map_town_c7_1_tocado_01";
    private const string ArkBayFam = "sec_f_h_map_tut_j2_arkbaytutorial";

    private const string MalachiteFam = "sec_f_b_map_hwy_a3_1_malachite";

    private static readonly (string Fam, string Label, int ContinentId)[] Maps =
    {
        (ScrapFam, "Scrap Valley", 398),
        (TocadoFam, "Tocado (town)", 392),
        (ArkBayFam, "Hestia Ark Bay 313", 707),
        (MalachiteFam, "Malachite", 399),
    };

    /// <summary>
    /// Unload the retail catalog this suite loaded into the process-wide
    /// <see cref="AssetManager"/>. Without it every later test in the assembly resolves
    /// against real WAD data instead of its own fixtures. See <c>LiveAssetIsolationTests</c>.
    /// </summary>
    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void UnloadLiveAssets() => AssetManager.Instance.ClearLiveAssetsForTests();

    [TestMethod]
    public void RealFam_AllReferencedVehicleTemplateIdsResolve()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var unresolved = new List<string>();
            var unique = new HashSet<int>();
            var slots = 0;
            foreach (var (fam, label, continentId) in Maps)
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                foreach (var slot in EnumerateTemplateSlots(mapData, activeOnly: false))
                {
                    slots++;
                    unique.Add(slot.TemplateId);
                    if (AssetManager.Instance.GetVehicleTemplate(slot.TemplateId) == null)
                    {
                        unresolved.Add(
                            $"{label} coid={slot.Coid} template={slot.TemplateId} active={slot.Active}");
                    }
                }
            }

            Console.WriteLine(
                $"referencedSlots={slots} uniqueIds={unique.Count} catalog={catalog.Count} unresolved={unresolved.Count}");
            if (unresolved.Count > 0)
                Console.WriteLine(string.Join(Environment.NewLine, unresolved.Take(20)));

            Assert.AreEqual(0, unresolved.Count,
                "every FAM SpawnType with IsTemplate=true must resolve via GetVehicleTemplate: "
                + string.Join("; ", unresolved.Take(8)));
            Assert.IsTrue(unique.Count > 0, "the sampled FAMs must reference at least one template vehicle");
        });
    }

    [TestMethod]
    public void ScrapValley_TemplateVehicleExpectedVsActualCounts()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var report = BuildResolutionReport(scrap, "Scrap Valley", catalog, wad);
            Console.WriteLine(report.Table);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass19-scrap-templates.md"), report.Table);

            Assert.AreEqual(642, report.ActiveSlots,
                "Pass 17 Scrap Valley active IsTemplate slot count must not regress");
            Assert.AreEqual(0, report.UnresolvedIds.Count,
                "unresolved Scrap Valley template IDs: " + string.Join(",", report.UnresolvedIds));
            Assert.AreEqual(report.ExpectedMinVehicles, report.ResolvableExpectedMin,
                "every authored minimum vehicle must be backed by a resolvable template");
            Assert.AreEqual(100.0, report.CoveragePercent, 0.01,
                $"coverage {report.CoveragePercent:0.0}% unique={report.UniqueIds} resolved={report.ResolvedIds}");
        });
    }

    [TestMethod]
    public void VehicleTemplate_RealSpawnPointIdResolves()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var first = EnumerateTemplateSlots(scrap, activeOnly: true)
                .OrderBy(s => s.Coid).ThenBy(s => s.TemplateId)
                .First();

            var template = AssetManager.Instance.GetVehicleTemplate(first.TemplateId);
            Assert.IsNotNull(template,
                $"first Scrap Valley template slot coid={first.Coid} id={first.TemplateId} must resolve");
            Assert.AreEqual(first.TemplateId, template!.Id);
            Assert.IsTrue(template.VehicleCbid > 0, "retail row must author a chassis CBID");
        });
    }

    [TestMethod]
    public void VehicleTemplate_TemplateIdMatchesRetail()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            foreach (var id in EnumerateTemplateSlots(scrap, activeOnly: true).Select(s => s.TemplateId).Distinct())
            {
                Assert.IsTrue(catalog.ContainsKey(id), $"wad.xml is missing IDVehicleTemplate={id}");
                var loaded = AssetManager.Instance.GetVehicleTemplate(id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(id, loaded!.Id);
                Assert.AreEqual(catalog[id].VehicleCbid, loaded.VehicleCbid);
                Assert.AreEqual(catalog[id].DriverCbid, loaded.DriverCbid);
            }
        });
    }

    [TestMethod]
    public void VehicleTemplate_ChassisCbidMatchesRetail()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var bad = new List<string>();
            foreach (var id in UniqueActiveTemplateIds(scrap))
            {
                var row = AssetManager.Instance.GetVehicleTemplate(id)!;
                if (!wad.CloneBases.TryGetValue(row.VehicleCbid, out var cb) || cb is not CloneBaseVehicle)
                {
                    bad.Add($"id={id} chassis={row.VehicleCbid} type={cb?.Type.ToString() ?? "missing"}");
                }
            }

            Assert.AreEqual(0, bad.Count, "chassis must exist as CloneBaseVehicle: " + string.Join("; ", bad.Take(8)));
        });
    }

    [TestMethod]
    public void VehicleTemplate_WheelSetMatchesRetail()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var missingWheel = new List<string>();
            var fallback = new List<string>();
            foreach (var id in UniqueActiveTemplateIds(scrap))
            {
                var row = AssetManager.Instance.GetVehicleTemplate(id)!;
                var chassis = (CloneBaseVehicle)wad.CloneBases[row.VehicleCbid];
                var wheelCbid = chassis.VehicleSpecific.DefaultWheelset;
                if (wheelCbid <= 0)
                {
                    fallback.Add($"id={id} chassis={row.VehicleCbid} DefaultWheelset={wheelCbid}");
                    continue;
                }

                if (!wad.CloneBases.TryGetValue(wheelCbid, out var wheel) || wheel.Type != CloneBaseObjectType.WheelSet)
                    missingWheel.Add($"id={id} wheel={wheelCbid} type={wheel?.Type.ToString() ?? "missing"}");
            }

            Assert.AreEqual(0, missingWheel.Count,
                "required wheel CBIDs missing: " + string.Join("; ", missingWheel.Take(8)));
            if (fallback.Count > 0)
                Console.WriteLine("templates with no authored DefaultWheelset (wire fallback): " + fallback.Count);
        });
    }

    [TestMethod]
    public void VehicleTemplate_DriverMatchesRetail()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var bad = new List<string>();
            foreach (var id in UniqueActiveTemplateIds(scrap))
            {
                var row = AssetManager.Instance.GetVehicleTemplate(id)!;
                var chassis = (CloneBaseVehicle)wad.CloneBases[row.VehicleCbid];
                var driverCbid = row.DriverCbid > 0 ? row.DriverCbid : chassis.VehicleSpecific.DefaultDriver;
                if (driverCbid <= 0)
                {
                    bad.Add($"id={id} no driver (template={row.DriverCbid} default={chassis.VehicleSpecific.DefaultDriver})");
                    continue;
                }

                if (!wad.CloneBases.TryGetValue(driverCbid, out var driver) || driver is not CloneBaseCreature)
                    bad.Add($"id={id} driver={driverCbid} type={driver?.Type.ToString() ?? "missing"}");
            }

            Assert.AreEqual(0, bad.Count, "drivers must resolve as Creature: " + string.Join("; ", bad.Take(8)));
        });
    }

    [TestMethod]
    public void TemplateVehicleSpawn_MaterializesVehicle()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var (spawn, vehicle) = MaterializeFirstScrapSlot(glm, wad);
            Assert.IsNotNull(vehicle, spawn.LastFailureDiagnostic);
            Assert.AreEqual(spawn.Template.Spawns.First(s => s.IsTemplate).SpawnType, vehicle!.TemplateId);
            Assert.IsTrue(vehicle.CBID > 0);
        });
    }

    [TestMethod]
    public void TemplateVehicleSpawn_MaterializesDriver()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var (_, vehicle) = MaterializeFirstScrapSlot(glm, wad);
            Assert.IsNotNull(vehicle?.Owner);
            Assert.IsInstanceOfType(vehicle!.Owner, typeof(Creature));
            Assert.IsTrue(vehicle.Owner.CBID > 0);
        });
    }

    [TestMethod]
    public void TemplateVehicleSpawn_VehicleMappedDriverGhostless()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var (spawn, vehicle) = MaterializeFirstScrapSlot(glm, wad);
            Assert.AreSame(spawn.Map, vehicle!.Map);
            Assert.IsInstanceOfType(vehicle.Ghost, typeof(GhostVehicle));
            Assert.IsNull(vehicle.Owner!.Map, "Pass 9: driver stays unmapped");
            Assert.IsNull(vehicle.Owner.Ghost, "Pass 9: driver stays ghostless");
        });
    }

    [TestMethod]
    public void TemplateVehicleSpawn_WheelSetSafeForWire()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var (_, vehicle) = MaterializeFirstScrapSlot(glm, wad);
            Assert.IsNotNull(vehicle!.WheelSet, "CreateVehicle nest must carry a wheel set");
            Assert.IsTrue(vehicle.WheelSet.CBID > 0, "wheel CBID must be > 0, never 0");
        });
    }

    [TestMethod]
    public void TemplateVehicleSpawn_PositionUsesPass18Scatter()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var example = scrap.Templates.Values.OfType<SpawnPointTemplate>()
                .Where(s => s.OriginalIsActive && s.RandomlyOffsetSpawnPosition && s.Radius > 0f)
                .Select(s => (Template: s, Slots: s.Spawns.Where(sl => sl.IsTemplate && sl.SpawnType != -1).ToList()))
                .First(e => e.Slots.Sum(sl => SpawnPointTemplate.ResolveSlotPopulationTarget(sl, new Random(1))) >= 2);

            var children = MaterializeFamTemplateSpawn(82_190, example.Template, wad);
            Assert.IsTrue(children.Count >= 2, $"expected multi-vehicle camp, got {children.Count}");
            var unique = children.Select(v => (v.Position.X, v.Position.Z)).Distinct().Count();
            Assert.IsTrue(unique > 1,
                $"Pass 18 scatter must unstack template vehicles; all {children.Count} share X={children[0].Position.X} Z={children[0].Position.Z}");
            Assert.IsTrue(children.All(v =>
                    MathF.Abs(v.Position.X - example.Template.Location.X) <= example.Template.Radius + 1e-3f
                    && MathF.Abs(v.Position.Z - example.Template.Location.Z) <= example.Template.Radius + 1e-3f),
                "scattered vehicles must stay inside the authored square");
        });
    }

    [TestMethod]
    public void TemplateVehicleSpawn_HonorsPass17Population()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var totals = new Dictionary<string, int>();
            foreach (var (fam, label, continentId, expected) in new[]
                     {
                         (ScrapFam, "Scrap Valley", 398, 3049),
                         (TocadoFam, "Tocado (town)", 392, 51),
                         (ArkBayFam, "Hestia Ark Bay 313", 707, 44),
                     })
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                var actual = mapData.Templates.Values.OfType<SpawnPointTemplate>()
                    .Where(s => s.OriginalIsActive)
                    .Sum(s => s.ExpectedMinimumChildren());
                totals[label] = actual;
                Assert.AreEqual(expected, actual, $"{label}: Pass 17 floor must survive template-vehicle work");
            }

            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var templateMin = scrap.Templates.Values.OfType<SpawnPointTemplate>()
                .Where(s => s.OriginalIsActive)
                .Sum(s => s.Spawns.Where(sl => sl.IsTemplate).Sum(ExpectedMinimumForSlot));
            Console.WriteLine($"Scrap Valley template-vehicle subset of 3049 = {templateMin}");
            Assert.IsTrue(templateMin > 0);
            Assert.IsTrue(templateMin < 3049);
        });
    }

    [TestMethod]
    public void RealFam_NoLegitimateTemplateVehicleSilentlyDisappears()
    {
        WithRetailCatalog((glm, wad, catalog) =>
        {
            var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
            var failures = new List<string>();
            var materialized = 0;
            var expected = 0;
            var spawnIndex = 0;
            foreach (var template in scrap.Templates.Values.OfType<SpawnPointTemplate>()
                         .Where(s => s.OriginalIsActive && s.Spawns.Any(sl => sl.IsTemplate && sl.SpawnType != -1)))
            {
                expected += template.Spawns.Where(sl => sl.IsTemplate)
                    .Sum(ExpectedMinimumForSlot);
                List<Vehicle> children;
                try
                {
                    children = MaterializeFamTemplateSpawn(83_000 + spawnIndex, template, wad);
                }
                catch (Exception ex)
                {
                    failures.Add($"coid={template.COID} threw {ex.GetType().Name}: {ex.Message}");
                    spawnIndex++;
                    continue;
                }

                spawnIndex++;
                materialized += children.Count;
                var want = template.Spawns.Where(sl => sl.IsTemplate).Sum(ExpectedMinimumForSlot);
                if (children.Count < want)
                {
                    failures.Add(
                        $"coid={template.COID} expected>={want} got={children.Count} " +
                        $"diag={template}");
                }
            }

            Console.WriteLine($"expectedMin={expected} materialized={materialized} failures={failures.Count}");
            if (failures.Count > 0)
                Console.WriteLine(string.Join(Environment.NewLine, failures.Take(20)));

            Assert.AreEqual(0, failures.Count,
                "no legitimate active template-vehicle slot may silently disappear: "
                + string.Join("; ", failures.Take(6)));
            Assert.IsTrue(materialized >= expected,
                $"rolled population {materialized} must be at least the authored Lower-sum {expected}");
        });
    }

    [TestMethod]
    public void VehicleTemplate_RetailCatalogCountMatchesLoader()
    {
        if (!File.Exists(WadXmlPath))
        {
            Assert.Inconclusive($"wad.xml not at {WadXmlPath}");
            return;
        }

        var loaded = WadXmlWorldDataLoader.LoadVehicleTemplates(WadXmlPath);
        Assert.AreEqual(865, loaded.Count, "retail wad.xml tVehicleTemplate row count");
        Assert.IsTrue(loaded.ContainsKey(1));
        Assert.IsTrue(loaded.ContainsKey(580), "Ark Bay Gunny / Final Exam template 580");
        Assert.AreEqual(2069, loaded[1].VehicleCbid);
        Assert.AreEqual(2071, loaded[1].DriverCbid);
    }

    private static (SpawnPoint Spawn, Vehicle Vehicle) MaterializeFirstScrapSlot(GLMLoader glm, WADLoader wad)
    {
        var scrap = ReadFam(glm, ScrapFam, "Scrap Valley", 398);
        var first = scrap.Templates.Values.OfType<SpawnPointTemplate>()
            .Where(s => s.OriginalIsActive)
            .OrderBy(s => s.COID)
            .First(s => s.Spawns.Any(sl => sl.IsTemplate && sl.SpawnType != -1));
        var children = MaterializeFamTemplateSpawn(82_100, first, wad);
        Assert.IsTrue(children.Count > 0, $"first slot coid={first.COID} produced no vehicles");
        var spawn = children[0].Map!.Objects.Values.OfType<SpawnPoint>().Single();
        return (spawn, children[0]);
    }

    private static List<Vehicle> MaterializeFamTemplateSpawn(int continentId, SpawnPointTemplate template, WADLoader wad)
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_fam_template_{continentId}",
            DisplayName = "fam-template",
            IsTown = false,
            IsPersistent = true,
        }, template.Location);

        var spawn = (SpawnPoint)template.Create();
        spawn.SetCoid(template.COID, false);
        spawn.SetMap(map);
        spawn.Position = template.Location.ToVector3();
        spawn.Spawn();
        return map.Objects.Values.OfType<Vehicle>().ToList();
    }

    private readonly record struct TemplateSlot(
        long Coid,
        int TemplateId,
        byte Lower,
        byte Upper,
        bool Active,
        int ExpectedMin);

    private static IEnumerable<TemplateSlot> EnumerateTemplateSlots(MapData mapData, bool activeOnly)
    {
        foreach (var sp in mapData.Templates.Values.OfType<SpawnPointTemplate>())
        {
            if (activeOnly && !sp.OriginalIsActive)
                continue;
            foreach (var slot in sp.Spawns.Where(s => s.IsTemplate && s.SpawnType != -1))
            {
                yield return new TemplateSlot(
                    sp.COID,
                    slot.SpawnType,
                    slot.LowerNumberOfSpawns,
                    slot.UpperNumberOfSpawns,
                    sp.OriginalIsActive,
                    ExpectedMinimumForSlot(slot));
            }
        }
    }

    private static int ExpectedMinimumForSlot(SpawnPointTemplate.SpawnList slot)
    {
        if (slot == null || slot.SpawnType == -1)
            return 0;
        if (slot.UpperNumberOfSpawns == 0 && slot.LowerNumberOfSpawns == 0)
            return 1;
        if (slot.UpperNumberOfSpawns == 0)
            return 0;
        return Math.Min((int)slot.LowerNumberOfSpawns, 10);
    }

    private static IEnumerable<int> UniqueActiveTemplateIds(MapData mapData)
        => EnumerateTemplateSlots(mapData, activeOnly: true).Select(s => s.TemplateId).Distinct();

    private sealed class ResolutionReport
    {
        public int ActiveSlots;
        public int UniqueIds;
        public int ResolvedIds;
        public int ExpectedMinVehicles;
        public int ResolvableExpectedMin;
        public double CoveragePercent;
        public List<int> UnresolvedIds = new();
        public string Table = string.Empty;
    }

    private static ResolutionReport BuildResolutionReport(
        MapData mapData,
        string label,
        IDictionary<int, VehicleTemplate> catalog,
        WADLoader wad)
    {
        var slots = EnumerateTemplateSlots(mapData, activeOnly: true).ToList();
        var byId = slots.GroupBy(s => s.TemplateId).OrderBy(g => g.Key).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"# {label} template-vehicle baseline");
        sb.AppendLine("| Template ID | Spawn slots | Expected min vehicles | Resolves? | Failure reason |");
        sb.AppendLine("| ---: | ---: | ---: | --- | --- |");

        var unresolved = new List<int>();
        var resolvableMin = 0;
        foreach (var group in byId)
        {
            var min = group.Sum(s => s.ExpectedMin);
            var row = AssetManager.Instance.GetVehicleTemplate(group.Key);
            var reason = "";
            var ok = row != null;
            if (!ok)
            {
                unresolved.Add(group.Key);
                reason = catalog.ContainsKey(group.Key)
                    ? "GetVehicleTemplate miss despite wad.xml row"
                    : "tVehicleTemplate row missing";
            }
            else if (!wad.CloneBases.TryGetValue(row!.VehicleCbid, out var chassis))
            {
                ok = false;
                unresolved.Add(group.Key);
                reason = $"chassis CBID {row.VehicleCbid} missing from clonebase.wad";
            }
            else if (chassis is not CloneBaseVehicle)
            {
                ok = false;
                unresolved.Add(group.Key);
                reason = $"chassis CBID {row.VehicleCbid} type={chassis.Type}";
            }
            else
            {
                resolvableMin += min;
            }

            sb.AppendLine($"| {group.Key} | {group.Count()} | {min} | {(ok ? "yes" : "NO")} | {reason} |");
        }

        var report = new ResolutionReport
        {
            ActiveSlots = slots.Count,
            UniqueIds = byId.Count,
            ResolvedIds = byId.Count - unresolved.Count,
            ExpectedMinVehicles = slots.Sum(s => s.ExpectedMin),
            ResolvableExpectedMin = resolvableMin,
            UnresolvedIds = unresolved,
            Table = sb.ToString(),
        };
        report.CoveragePercent = report.UniqueIds == 0
            ? 100.0
            : 100.0 * report.ResolvedIds / report.UniqueIds;
        sb.AppendLine();
        sb.AppendLine(
            $"totalSlots={report.ActiveSlots} unique={report.UniqueIds} resolved={report.ResolvedIds} " +
            $"unresolved={unresolved.Count} expectedMin={report.ExpectedMinVehicles} " +
            $"lost={report.ExpectedMinVehicles - report.ResolvableExpectedMin} " +
            $"coverage={report.CoveragePercent:0.0}%");
        report.Table = sb.ToString();
        return report;
    }

    private static void WithRetailCatalog(Action<GLMLoader, WADLoader, IDictionary<int, VehicleTemplate>> body)
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")) || !File.Exists(WadXmlPath))
        {
            Assert.Inconclusive($"retail data not at {InstallPath}");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var world = (WorldDBLoader)typeof(AssetManager)
            .GetProperty("WorldDBLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;

        var loadedWadHere = wad.CloneBases.Count == 0;
        var previousTemplates = world.VehicleTemplates;
        if (loadedWadHere)
        {
            wad.Missions.Clear();
            wad.Skills.Clear();
            wad.ArmorPrefixes.Clear();
            wad.PowerPlantPrefixes.Clear();
            wad.WeaponPrefixes.Clear();
            wad.VehiclePrefixes.Clear();
            wad.OrnamentPrefixes.Clear();
            wad.RaceItemPrefixes.Clear();
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");
        }

        var catalog = WadXmlWorldDataLoader.LoadVehicleTemplates(WadXmlPath);
        world.VehicleTemplates = catalog;
        AssetManager.Instance.ClearTestNpcData();

        try
        {
            var glm = new GLMLoader();
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");
            body(glm, wad, catalog);
        }
        finally
        {
            world.VehicleTemplates = previousTemplates;
            AssetManager.Instance.ClearTestNpcData();
            if (loadedWadHere)
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
}
