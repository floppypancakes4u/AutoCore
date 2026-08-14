using System.Reflection;
using System.Text;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Isolated live-.fam dump of authored spawn-control fields via
/// <see cref="MapData.Read"/> / <see cref="SpawnPointTemplate.Read"/>.
/// Off unless AUTOCORE_LIVE_FAM_SAMPLE=1 so the AssetManager WAD singleton
/// is not loaded during the normal suite (see SimPerfBenchmarkTests).
/// </summary>
[TestClass]
public class LiveFamSpawnPointSampleTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    private static readonly (string Fam, string Label)[] Maps =
    {
        ("sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313 (707)"),
        ("sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley highway"),
    };

    /// <summary>
    /// Unload the retail catalog this suite loaded into the process-wide
    /// <see cref="AssetManager"/>. Without it every later test in the assembly resolves
    /// against real WAD data instead of its own fixtures. See <c>LiveAssetIsolationTests</c>.
    /// </summary>
    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void UnloadLiveAssets() => AssetManager.Instance.ClearLiveAssetsForTests();

    [TestMethod]
    public void LiveFam_DumpAuthoredSpawnFields_WhenRequested()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("AUTOCORE_LIVE_FAM_SAMPLE"), "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive("Set AUTOCORE_LIVE_FAM_SAMPLE=1 and run this test in isolation.");
            return;
        }

        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")))
        {
            Assert.Inconclusive($"clonebase.wad not at {InstallPath}");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        if (wad.CloneBases.Count == 0)
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");

        var glm = new GLMLoader();
        Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");

        var output = new StringBuilder();
        output.AppendLine($"# Live .fam spawn-point sample");
        output.AppendLine($"install: {InstallPath}");
        output.AppendLine($"clonebases: {wad.CloneBases.Count}");

        var mapsSampled = 0;
        foreach (var (famName, label) in Maps)
        {
            using var famStream = glm.GetStream($"{famName}.fam");
            Assert.IsNotNull(famStream, $"{famName}.fam missing from GLM packs");

            var mapData = new MapData(new ContinentObject
            {
                Id = mapsSampled + 1,
                MapFileName = famName,
                DisplayName = label,
                IsTown = false,
                IsPersistent = true,
            });
            using (var reader = new BinaryReader(famStream))
                mapData.Read(reader);

            mapsSampled++;
            var spawnPoints = mapData.Templates.Values.OfType<SpawnPointTemplate>().ToList();
            Assert.IsTrue(spawnPoints.Count > 0, $"{famName} must contain SpawnPointTemplate records");
            AssertAuthoredModelPresent(famName, spawnPoints);
            output.AppendLine();
            output.AppendLine($"## {label}");
            output.AppendLine($"fam: {famName}.fam version={mapData.MapVersion} spawnPoints={spawnPoints.Count}");

            DumpSummary(output, spawnPoints);
            DumpExamples(output, spawnPoints, wad);
            DumpMultiSlot(output, famName, spawnPoints, wad);
        }

        var dest = Environment.GetEnvironmentVariable("AUTOCORE_LIVE_FAM_OUT");
        if (!string.IsNullOrWhiteSpace(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, output.ToString());
        }

        Console.WriteLine(output.ToString());
        Assert.AreEqual(Maps.Length, mapsSampled, "both sample maps must parse through MapData.Read");
        Assert.IsTrue(output.ToString().Contains("spawnPoints="), "dump must include spawn-point counts");
    }

    /// <summary>
    /// Live files must actually author the count/respawn/path model — not just parse.
    /// Hestia proves inactive/Create-later combat + dialog; Scrap proves Lower/Upper + RespawnTime.
    /// </summary>
    private static void AssertAuthoredModelPresent(string famName, List<SpawnPointTemplate> spawnPoints)
    {
        Assert.IsTrue(
            spawnPoints.Any(s => !s.OriginalIsActive && s.Spawns.Any(sl => sl.SpawnType != -1)),
            $"{famName}: at least one inactive filled spawn (Create/Activate later)");
        Assert.IsTrue(
            spawnPoints.Any(s => s.OriginalIsActive && s.Spawns.Any(sl => sl.SpawnType != -1)),
            $"{famName}: at least one active filled spawn (map-load children)");
        Assert.IsTrue(
            spawnPoints.Any(s => s.MapPathCoid > 0 && s.Spawns.Any(sl => sl.SpawnType != -1)),
            $"{famName}: at least one path-linked spawn (route, not a population table)");

        if (famName.Contains("arkbaytutorial", StringComparison.OrdinalIgnoreCase))
        {
            var gunny = spawnPoints.Single(s => s.COID == 14138);
            Assert.IsFalse(gunny.OriginalIsActive);
            Assert.IsTrue(gunny.Spawns[0].IsTemplate);
            Assert.AreEqual(580, gunny.Spawns[0].SpawnType);
            Assert.AreEqual((byte)1, gunny.Spawns[0].LowerNumberOfSpawns);
            Assert.AreEqual(-1f, gunny.RespawnTime);

            var rogers = spawnPoints.Single(s => s.COID == 14086);
            Assert.IsTrue(rogers.OriginalIsActive);
            Assert.AreEqual(2477, rogers.Spawns[0].SpawnType);
            Assert.IsFalse(rogers.Spawns[0].IsTemplate);
            Assert.IsTrue(rogers.MapPathCoid <= 0);
        }

        if (famName.Contains("scrapvalley", StringComparison.OrdinalIgnoreCase))
        {
            Assert.IsTrue(
                spawnPoints.Any(s => s.Spawns.Any(sl => sl.SpawnType != -1 && sl.UpperNumberOfSpawns > 1)),
                "Scrap Valley must author Lower/Upper > 1 (per-point population)");
            Assert.IsTrue(
                spawnPoints.Any(s => s.RespawnTime > 0f),
                "Scrap Valley must author positive RespawnTime (millisecond refill)");
            Assert.IsTrue(
                spawnPoints.Any(s => s.Spawns.Count(sl => sl.SpawnType != -1) >= 2),
                "Scrap Valley must author multi-filled spawn lists (concurrent per-slot populations)");

            var pike = spawnPoints.Single(s => s.COID == 12636);
            Assert.AreEqual((byte)2, pike.Spawns[0].LowerNumberOfSpawns);
            Assert.AreEqual((byte)3, pike.Spawns[0].UpperNumberOfSpawns);
            Assert.AreEqual(195000f, pike.RespawnTime);
            Assert.AreEqual(12635L, pike.MapPathCoid);
            Assert.AreEqual(13236, pike.Spawns[0].SpawnType);

            // Concurrent slots: client FUN_00566490 keeps both populations (sum 12–18).
            var brood = spawnPoints.Single(s => s.COID == 13330);
            Assert.AreEqual(13564, brood.Spawns[0].SpawnType);
            Assert.AreEqual((byte)10, brood.Spawns[0].LowerNumberOfSpawns);
            Assert.AreEqual((byte)12, brood.Spawns[0].UpperNumberOfSpawns);
            Assert.AreEqual(2753, brood.Spawns[1].SpawnType);
            Assert.AreEqual((byte)2, brood.Spawns[1].LowerNumberOfSpawns);
            Assert.AreEqual((byte)6, brood.Spawns[1].UpperNumberOfSpawns);
            Assert.AreEqual(12, brood.Spawns.Where(s => s.SpawnType != -1).Sum(s => (int)s.LowerNumberOfSpawns));
            Assert.AreEqual(18, brood.Spawns.Where(s => s.SpawnType != -1).Sum(s => (int)s.UpperNumberOfSpawns));
        }
    }

    private static void DumpSummary(StringBuilder output, List<SpawnPointTemplate> spawnPoints)
    {
        var active = spawnPoints.Count(s => s.OriginalIsActive);
        var withPath = spawnPoints.Count(s => s.MapPathCoid > 0);
        var generators = spawnPoints.Count(s => s.UseGenerator);
        var randomOffset = spawnPoints.Count(s => s.RandomlyOffsetSpawnPosition);
        var champions = spawnPoints.Count(s => s.HasChampion);
        var multiSlot = 0;
        var multiCount = 0;
        var chanceNot100 = 0;
        var respawnPositive = 0;
        var activationPositive = 0;

        foreach (var sp in spawnPoints)
        {
            var filled = sp.Spawns.Where(s => s.SpawnType != -1).ToList();
            if (filled.Count > 1)
                multiSlot++;
            if (filled.Any(s => s.UpperNumberOfSpawns > 1 || s.LowerNumberOfSpawns > 1))
                multiCount++;
            if (sp.SpawnChance != 0 && sp.SpawnChance != 100)
                chanceNot100++;
            if (sp.RespawnTime > 0f)
                respawnPositive++;
            if (sp.ActivationRange > 0f)
                activationPositive++;
        }

        output.AppendLine($"active={active} inactive={spawnPoints.Count - active}");
        output.AppendLine($"withMapPath={withPath} useGenerator={generators} randomOffset={randomOffset} hasChampion={champions}");
        output.AppendLine($"multiFilledSlots={multiSlot} anySlotUpperOrLowerGt1={multiCount}");
        output.AppendLine($"spawnChanceNot0or100={chanceNot100} respawnTime>0={respawnPositive} activationRange>0={activationPositive}");
    }

    private static void DumpExamples(StringBuilder output, List<SpawnPointTemplate> spawnPoints, WADLoader wad)
    {
        var combat = spawnPoints.FirstOrDefault(sp =>
            sp.MapPathCoid > 0 &&
            sp.Spawns.Any(s => s.SpawnType > 0 && !s.IsTemplate));
        var dialog = spawnPoints.FirstOrDefault(sp =>
            sp.OriginalIsActive &&
            sp.MapPathCoid <= 0 &&
            sp.Spawns.Any(s => s.SpawnType > 0 && !s.IsTemplate) &&
            !ReferenceEquals(sp, combat));
        var templateVehicle = spawnPoints.FirstOrDefault(sp =>
            sp.Spawns.Any(s => s.IsTemplate && s.SpawnType > 0));

        DumpOne(output, "combat-or-path", combat, wad);
        DumpOne(output, "dialog-or-static", dialog, wad);
        DumpOne(output, "template-vehicle-slot", templateVehicle, wad);
    }

    /// <summary>
    /// Client FUN_00566490 refills every eligible slot independently. Dump one live
    /// multi-filled point with each slot's Lower/Upper so authored total is the sum.
    /// Prefers the point whose filled slots have the largest sum of Upper.
    /// </summary>
    private static void DumpMultiSlot(
        StringBuilder output,
        string famName,
        List<SpawnPointTemplate> spawnPoints,
        WADLoader wad)
    {
        var multi = spawnPoints
            .Where(sp => sp.Spawns.Count(s => s.SpawnType != -1) >= 2)
            .OrderByDescending(sp => sp.Spawns.Where(s => s.SpawnType != -1).Sum(s => (int)s.UpperNumberOfSpawns))
            .ThenByDescending(sp => sp.Spawns.Count(s => s.SpawnType != -1))
            .FirstOrDefault();

        output.AppendLine($"### example multi-slot (concurrent per-slot populations)");
        if (multi == null)
        {
            output.AppendLine("not present on this map");
            return;
        }

        var filled = multi.Spawns.Where(s => s.SpawnType != -1).ToList();
        var sumLower = filled.Sum(s => (int)s.LowerNumberOfSpawns);
        var sumUpper = filled.Sum(s => (int)s.UpperNumberOfSpawns);
        output.AppendLine(
            $"fam={famName} coid={multi.COID} filledSlots={filled.Count} " +
            $"sumLower={sumLower} sumUpper={sumUpper} (authored live range is the sum, not pick-one)");
        DumpOne(output, "multi-slot-detail", multi, wad);

        Assert.IsTrue(filled.Count >= 2, $"{famName} multi-slot example must have >= 2 filled slots");
        Assert.IsTrue(sumUpper >= sumLower, "per-slot Upper must be >= Lower");
        Assert.IsTrue(sumUpper > 1,
            $"{famName} COID {multi.COID}: sum of slot Uppers must exceed 1 (concurrent populations)");
    }

    private static void DumpOne(StringBuilder output, string kind, SpawnPointTemplate? sp, WADLoader wad)
    {
        output.AppendLine($"### example {kind}");
        if (sp == null)
        {
            output.AppendLine("not present on this map");
            return;
        }

        output.AppendLine($"coid={sp.COID} cbid={sp.CBID} active={sp.OriginalIsActive}");
        output.AppendLine($"radius={sp.Radius} respawnTime={sp.RespawnTime} activationRange={sp.ActivationRange}");
        output.AppendLine($"useGenerator={sp.UseGenerator} spawnChance={sp.SpawnChance} randomlyOffset={sp.RandomlyOffsetSpawnPosition}");
        output.AppendLine($"hasChampion={sp.HasChampion} championChance={sp.ChampionChance} maybeChampionName={sp.MaybeChampionName ?? "(null)"}");
        output.AppendLine($"mapPathCoid={sp.MapPathCoid} initialPatrolDistance={sp.InitialPatrolDistance}");
        output.AppendLine($"loot={sp.Loot} lootPercent={sp.LootPercent} lootChance={sp.LootChance} factionDirty={sp.FactionDirty} originalFaction={sp.OriginalFaction}");

        for (var i = 0; i < sp.Spawns.Count; i++)
        {
            var slot = sp.Spawns[i];
            if (slot.SpawnType == -1)
                continue;

            var typeName = "?";
            if (!slot.IsTemplate && wad.CloneBases.TryGetValue(slot.SpawnType, out var cb))
                typeName = cb is CloneBaseCreature ? "creature" : cb.GetType().Name;

            output.AppendLine(
                $"  slot[{i}] type={slot.SpawnType} kind={typeName} template={slot.IsTemplate} " +
                $"lower={slot.LowerNumberOfSpawns} upper={slot.UpperNumberOfSpawns} levelOffset={slot.LevelOffset}");
        }
    }
}
