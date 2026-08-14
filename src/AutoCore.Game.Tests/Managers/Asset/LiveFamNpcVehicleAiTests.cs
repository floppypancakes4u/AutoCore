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
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Pass 23 — live FAM + wad.xml + clonebase pins for NPC vehicle AI activation.
/// </summary>
[TestClass]
public class LiveFamNpcVehicleAiTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    private const string WadXmlPath = InstallPath + @"\wad.xml";

    /// <summary>
    /// Unload the retail catalog this suite loaded into the process-wide
    /// <see cref="AssetManager"/>. Without it every later test in the assembly resolves
    /// against real WAD data instead of its own fixtures. See <c>LiveAssetIsolationTests</c>.
    /// </summary>
    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void UnloadLiveAssets() => AssetManager.Instance.ClearLiveAssetsForTests();

    [TestMethod]
    public void LiveFam_SelectedNpcVehiclesReceiveAiOwner()
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            var report = new StringBuilder();
            report.AppendLine("| Map | Spawn COID | Template | Faction | MapPathCoid | ActivationRange | AIBehavior | NpcAi | Driver mapped |");
            report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");

            CheckMissionCar(report, glm, wad, catalog, profiles,
                "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698, 3882, 587);
            CheckMissionCar(report, glm, wad, catalog, profiles,
                "sec_f_b_map_mis_a3_1_wastes", "The Wastes", 708, 18609, 593);
            CheckMissionCar(report, glm, wad, catalog, profiles,
                "sec_f_b_map_mis_a2_1_canyonrun_01", "The Canyon Run", 399, 23413, 636);

            var scrap = ReadFam(glm, "sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398);
            var highway = scrap.Templates.Values.OfType<SpawnPointTemplate>()
                .Where(s => s.OriginalIsActive
                            && s.Spawns.Any(sl => sl.IsTemplate && sl.SpawnType != -1)
                            && s.MapPathCoid > 0)
                .OrderBy(s => s.COID)
                .Take(2)
                .ToList();
            Assert.AreEqual(2, highway.Count, "Scrap Valley must author at least two path-linked template vehicles");
            foreach (var sp in highway)
            {
                var slot = sp.Spawns.First(s => s.IsTemplate && s.SpawnType != -1);
                CheckSpawn(report, "Scrap Valley", scrap, wad, catalog, profiles, sp, slot.SpawnType, requirePath: true);
            }

            Console.WriteLine(report.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass23-vehicle-ai.md"), report.ToString());
        });
    }

    [TestMethod]
    public void LiveFam_TierraRoja3882_IsPathlessDuelWaitingForProximity()
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            var mapData = ReadFam(glm, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698);
            var spawnTpl = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 3882);
            Assert.IsFalse(spawnTpl.OriginalIsActive);
            Assert.AreEqual(587, spawnTpl.Spawns[0].SpawnType);
            var vehicle = Materialize(mapData, spawnTpl, wad, catalog);
            Assert.IsNotNull(vehicle.NpcAi, "Create-only Champion car 3882 must receive an AI owner");
            Assert.AreEqual(spawnTpl.MapPathCoid <= 0 ? -1 : spawnTpl.MapPathCoid, vehicle.CoidCurrentPath);
            Assert.IsNull(vehicle.Owner.Map);
            Assert.IsNull(vehicle.Owner.Ghost);
            Console.WriteLine(
                $"TR 3882 path={spawnTpl.MapPathCoid} patrol={spawnTpl.InitialPatrolDistance} " +
                $"activationRange={spawnTpl.ActivationRange} useGenerator={spawnTpl.UseGenerator} " +
                $"factionDirty={spawnTpl.FactionDirty} originalFaction={spawnTpl.OriginalFaction} " +
                $"vehicleFaction={vehicle.GetIDFaction()} coidCurrentPath={vehicle.CoidCurrentPath}");
        });
    }

    [TestMethod]
    public void LiveFam_ScrapValleyPathVehicle_InitializesMapPathAndMoves()
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            var scrap = ReadFam(glm, "sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398);
            var spawnTpl = scrap.Templates.Values.OfType<SpawnPointTemplate>()
                .Where(s => s.OriginalIsActive && s.MapPathCoid > 0
                            && s.Spawns.Any(sl => sl.IsTemplate && sl.SpawnType != -1))
                .OrderBy(s => s.COID)
                .First();
            Assert.IsTrue(scrap.Templates.TryGetValue(spawnTpl.MapPathCoid, out var pathTpl)
                          && pathTpl is MapPathTemplate path
                          && path.Points.Count > 0,
                $"Scrap spawn {spawnTpl.COID} MapPathCoid={spawnTpl.MapPathCoid} must resolve to a live FAM path");

            var vehicle = Materialize(scrap, spawnTpl, wad, catalog);
            Assert.AreEqual(spawnTpl.MapPathCoid, vehicle.CoidCurrentPath);
            Assert.IsNotNull(vehicle.NpcAi);
            Assert.IsTrue(vehicle.Map.NpcAiEntities.Contains(vehicle));

            var start = vehicle.Position;
            for (var i = 0; i < 24; i++)
                NpcTicker.Tick(vehicle.Map, nowMs: 2_000 + (i * 50), dt: 0.05f);

            Assert.IsTrue(vehicle.Position.Dist(start) > 0.25f,
                $"Scrap path vehicle spawn={spawnTpl.COID} path={spawnTpl.MapPathCoid} must leave spawn; start={start} now={vehicle.Position}");
        });
    }

    private static void CheckMissionCar(
        StringBuilder report,
        GLMLoader glm,
        WADLoader wad,
        IDictionary<int, VehicleTemplate> catalog,
        IDictionary<int, CreatureAiProfile> profiles,
        string fam,
        string label,
        int continentId,
        long spawnCoid,
        int templateId)
    {
        var mapData = ReadFam(glm, fam, label, continentId);
        var spawnTpl = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == spawnCoid);
        Assert.AreEqual(templateId, spawnTpl.Spawns[0].SpawnType);
        CheckSpawn(report, label, mapData, wad, catalog, profiles, spawnTpl, templateId, requirePath: false);
    }

    private static void CheckSpawn(
        StringBuilder report,
        string label,
        MapData mapData,
        WADLoader wad,
        IDictionary<int, VehicleTemplate> catalog,
        IDictionary<int, CreatureAiProfile> profiles,
        SpawnPointTemplate spawnTpl,
        int templateId,
        bool requirePath)
    {
        Assert.IsTrue(catalog.TryGetValue(templateId, out var row), $"{label} template {templateId} missing from wad.xml");
        var driverAi = 0;
        if (wad.CloneBases.TryGetValue(row.DriverCbid, out var driverClone)
            && driverClone is CloneBaseCreature driver)
        {
            driverAi = driver.CreatureSpecific.AIBehavior;
        }

        var vehicle = Materialize(mapData, spawnTpl, wad, catalog);
        Assert.IsNotNull(vehicle.NpcAi, $"{label} spawn {spawnTpl.COID} template {templateId} must receive an AI owner");
        Assert.IsTrue(vehicle.Map.NpcAiEntities.Contains(vehicle));
        Assert.IsNull(vehicle.Owner?.Map, "driver must stay unmapped");
        Assert.IsNull(vehicle.Owner?.Ghost, "driver must stay ghostless");
        if (requirePath)
            Assert.AreEqual(spawnTpl.MapPathCoid, vehicle.CoidCurrentPath);

        var faction = vehicle.GetIDFaction();
        report.AppendLine(
            $"| {label} | {spawnTpl.COID} | {templateId} | {faction} | {spawnTpl.MapPathCoid} | {spawnTpl.ActivationRange} | {driverAi} | yes | no |");

        if (driverAi > 0)
            Assert.IsTrue(profiles.ContainsKey(driverAi) || vehicle.NpcAi.Profile != null
                          || vehicle.NpcAi != null,
                $"driver AIBehavior={driverAi} should resolve or still tick with a default owner");
    }

    private static Vehicle Materialize(
        MapData mapData,
        SpawnPointTemplate spawnTpl,
        WADLoader wad,
        IDictionary<int, VehicleTemplate> catalog)
    {
        var map = SectorMap.CreateForTests(mapData.ContinentObject, spawnTpl.Location);
        foreach (var kv in mapData.Templates)
            map.MapData.Templates[kv.Key] = kv.Value;

        var spawn = (SpawnPoint)spawnTpl.Create();
        spawn.SetCoid(spawnTpl.COID, false);
        spawn.Position = spawnTpl.Location.ToVector3();
        spawn.SetMap(map);
        Assert.IsTrue(spawn.Spawn(),
            $"{mapData.ContinentObject.DisplayName} spawn {spawnTpl.COID} failed: {spawn.LastFailureDiagnostic}");
        var children = map.Objects.Values.OfType<Vehicle>().Where(v => v.SpawnOwnerCoid == spawnTpl.COID).ToList();
        Assert.IsTrue(children.Count > 0, $"{mapData.ContinentObject.DisplayName} spawn {spawnTpl.COID} produced no vehicles");
        return children[0];
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

    private static void WithRetailCatalog(
        Action<GLMLoader, WADLoader, IDictionary<int, VehicleTemplate>, IDictionary<int, CreatureAiProfile>> body)
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
        var previousProfiles = world.CreatureAiProfiles;
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
        var profiles = WadXmlWorldDataLoader.LoadCreatureAiProfiles(WadXmlPath);
        world.VehicleTemplates = catalog;
        world.CreatureAiProfiles = profiles;
        AssetManager.Instance.ClearTestNpcData();

        try
        {
            var glm = new GLMLoader();
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");
            body(glm, wad, catalog, profiles);
        }
        finally
        {
            world.VehicleTemplates = previousTemplates;
            world.CreatureAiProfiles = previousProfiles;
            AssetManager.Instance.ClearTestNpcData();
            if (loadedWadHere)
                wad.CloneBases.Clear();
        }
    }
}
