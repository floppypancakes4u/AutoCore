using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Architecture;

/// <summary>
/// SS-31 hardening: source-scan tripwire so no future code path can mint persistent coids
/// from the map-local counter (<c>SectorMap.LocalCoidCounter</c>) unnoticed. Persistent coids
/// (player-visible inventory/vehicle/character identity) must be allocated through
/// <c>InventoryRuntime.AllocatePersistentCoid</c> — see docs/id-collisions.md §7.1. The
/// map-local counter is reserved for local map props (NPC spawns, loot, vendor slots) whose
/// identity space is scoped to a single map instance and never persisted per-character.
///
/// Modeled on <c>AutoCore.Utils.Tests.Logging.LogEventCatalogSyncTests</c>
/// (IsProductionSourcePath filter and FindRepoRoot walk-up).
///
/// NOTE: the rule-1 regex is a plain word-boundary match on <c>LocalCoidCounter</c> — it
/// intentionally also matches occurrences inside comments (not just live code references).
/// Files that only mention the counter in a comment (e.g. to explain why they deliberately do
/// NOT use it) are allowlisted deliberately; see the per-entry reasons below.
/// </summary>
[TestClass]
public class CoidAllocationArchitectureTests
{
    private static readonly Regex LocalCoidCounterUsage = new(
        @"\bLocalCoidCounter\b",
        RegexOptions.Compiled);

    private static readonly Regex ForbiddenCoidSync = new(
        @"\bSyncFromCargo\b|\bInventoryCoidCounter\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Files under src/ (forward-slash-normalized, relative to src/) allowed to reference
    /// <c>LocalCoidCounter</c>. Every entry below was verified against a fresh grep of the
    /// current tree — DO NOT add entries speculatively; if a new match appears that isn't
    /// covered by one of the reasons below, route the allocation through
    /// InventoryRuntime.AllocatePersistentCoid instead of widening this list.
    /// </summary>
    private static readonly HashSet<string> LocalCoidCounterAllowlist = new(StringComparer.Ordinal)
    {
        // Owner of the map-local counter itself: declares, initializes, and re-seeds
        // LocalCoidCounter from MapData.HighestCoid.
        "AutoCore.Game/Map/SectorMap.cs",

        // Map-scoped NPC identity space (0x5000_0000+, Global=false) documents/derives from
        // the same local-counter allocation policy as SectorMap's spawn path.
        "AutoCore.Game/Map/MapNpcIdentity.cs",

        // World spawn point allocation: reads/advances Map.LocalCoidCounter for spawned NPC
        // simple-object coids, scoped to the map instance.
        "AutoCore.Game/Entities/SpawnPoint.cs",

        // Vehicle world-spawn coid allocation follows the same map-local offset-allocator
        // pattern as SpawnPoint.
        "AutoCore.Game/Entities/Vehicle.cs",

        // Loot drop coid allocation: map-local simple-object identity for dropped loot piles.
        "AutoCore.Game/Managers/LootManager.cs",

        // Vendor store slot instance coid allocation (map-scoped, not the persistent
        // StoreSlotIdentity.CoidBase display-slot policy).
        "AutoCore.Game/Managers/VendorStoreService.cs",

        // Comment-only: explains why mission use-item progress never mints from
        // Map.LocalCoidCounter (persistent coids go through InventoryRuntime instead).
        "AutoCore.Game/Managers/MissionUseItemProgress.cs",

        // Comment-only: same "never mint from Map.LocalCoidCounter" guidance for cargo grants.
        "AutoCore.Game/Mission/MissionCargoService.cs",

        // Comment-only: InventoryRuntime's own doc comment references Map.LocalCoidCounter to
        // contrast it with the persistent-coid allocator it implements.
        "AutoCore.Game/Inventory/InventoryRuntime.cs",

        // Sim-side clone spawn allocation: same map-local offset-allocator pattern as
        // SpawnPoint/Vehicle, but in AutoCore.Sim rather than AutoCore.Game.
        "AutoCore.Sim/Clone/CloneSpawner.cs",
    };

    private static bool IsProductionSourcePath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || part.Equals("obj", StringComparison.OrdinalIgnoreCase))
                return false;
            // AutoCore.*.Tests project folders
            if (part.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Tests", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Normalizes an absolute file path to a forward-slash-normalized path relative to src/.
    /// </summary>
    private static string ToRelativeSrcPath(string srcRoot, string absolutePath)
    {
        var relative = Path.GetRelativePath(srcRoot, absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
    }

    [TestMethod]
    public void LocalCoidCounter_Usage_IsLimitedToAllowlist()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (!IsProductionSourcePath(path))
                continue;

            var text = File.ReadAllText(path);
            if (!LocalCoidCounterUsage.IsMatch(text))
                continue;

            var relative = ToRelativeSrcPath(srcRoot, path);
            if (!LocalCoidCounterAllowlist.Contains(relative))
                offenders.Add(relative);
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.IsTrue(
            offenders.Count == 0,
            "new production files must mint persistent coids via "
            + "InventoryRuntime.AllocatePersistentCoid — see docs/id-collisions.md §7.1: "
            + string.Join(", ", offenders));
    }

    [TestMethod]
    public void ForbiddenCoidSyncPatterns_DoNotAppearInProduction()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (!IsProductionSourcePath(path))
                continue;

            var text = File.ReadAllText(path);
            if (ForbiddenCoidSync.IsMatch(text))
                offenders.Add(ToRelativeSrcPath(srcRoot, path));
        }

        offenders.Sort(StringComparer.Ordinal);

        Assert.IsTrue(
            offenders.Count == 0,
            "new production files must mint persistent coids via "
            + "InventoryRuntime.AllocatePersistentCoid — see docs/id-collisions.md §7.1: "
            + string.Join(", ", offenders));
    }

    [TestMethod]
    public void LocalCoidCounterAllowlist_Entries_AllExist()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        var missing = LocalCoidCounterAllowlist
            .Where(relative => !File.Exists(Path.Combine(srcRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.IsTrue(
            missing.Count == 0,
            "Stale CoidAllocationArchitectureTests allowlist entries no longer exist on disk: "
            + string.Join(", ", missing));
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var idCollisions = Path.Combine(dir.FullName, "docs", "id-collisions.md");
                var src = Path.Combine(dir.FullName, "src");
                if (File.Exists(idCollisions) && Directory.Exists(src))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new InvalidOperationException("Could not locate repo root for coid allocation architecture test.");
    }
}
