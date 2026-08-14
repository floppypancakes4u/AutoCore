using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Utils.Tests.Logging;

/// <summary>
/// Phase 6 drift guard: every production GameLog event name must appear in
/// docs/logging-event-catalog.md and every catalog EventName must exist in production
/// (or be an allowed dynamic/legacy pattern).
/// </summary>
[TestClass]
public class LogEventCatalogSyncTests
{
    private static readonly Regex GameLogCall = new(
        @"GameLog\.(?:Info|Debug|Trace|Warn|Error|Fatal|Audit|Action)\(\s*""([A-Za-z][A-Za-z0-9]*)""",
        RegexOptions.Compiled);

    private static readonly Regex GameLogOperation = new(
        @"GameLog\.Operation\(\s*""([A-Za-z][A-Za-z0-9]*)""",
        RegexOptions.Compiled);

    /// <summary>
    /// InventoryManager routes item audits through AuditItemMutation("EventName", …).
    /// </summary>
    private static readonly Regex AuditItemMutation = new(
        @"AuditItemMutation\(\s*""([A-Za-z][A-Za-z0-9]*)""",
        RegexOptions.Compiled);

    private static readonly Regex CatalogRow = new(
        @"^\|\s*([A-Za-z][A-Za-z0-9]*)\s*\|",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Event names that appear only as dual-write/legacy — not required in catalog.
    /// </summary>
    private static readonly HashSet<string> ProductionIgnore = new(StringComparer.Ordinal)
    {
        "Legacy",
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

    [TestMethod]
    public void Catalog_And_Production_EventNames_AreInSync()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        var catalogPath = Path.Combine(repoRoot, "docs", "logging-event-catalog.md");

        Assert.IsTrue(File.Exists(catalogPath), $"Missing catalog at {catalogPath}");

        var production = ScanProductionEventNames(srcRoot);
        production.ExceptWith(ProductionIgnore);

        var catalog = ParseCatalogEventNames(File.ReadAllText(catalogPath));

        var missingFromCatalog = production.Except(catalog).OrderBy(x => x).ToList();
        var missingFromCode = catalog.Except(production).OrderBy(x => x).ToList();

        Assert.IsTrue(
            missingFromCatalog.Count == 0,
            "Production GameLog events missing from docs/logging-event-catalog.md: "
            + string.Join(", ", missingFromCatalog));

        Assert.IsTrue(
            missingFromCode.Count == 0,
            "Catalog events with no production GameLog call site: "
            + string.Join(", ", missingFromCode));
    }

    private static HashSet<string> ScanProductionEventNames(string srcRoot)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (!IsProductionSourcePath(path))
                continue;

            var text = File.ReadAllText(path);
            foreach (Match m in GameLogCall.Matches(text))
                names.Add(m.Groups[1].Value);
            foreach (Match m in AuditItemMutation.Matches(text))
                names.Add(m.Groups[1].Value);
            foreach (Match m in GameLogOperation.Matches(text))
            {
                var baseName = m.Groups[1].Value;
                names.Add(baseName + "Started");
                names.Add(baseName + "Completed");
                names.Add(baseName + "Failed");
            }
        }
        return names;
    }

    private static HashSet<string> ParseCatalogEventNames(string markdown)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        // Only rows under the main "## Catalog" table (EventName column).
        var inCatalogSection = false;
        var inTable = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var trimmed = raw.TrimEnd('\r').Trim();

            if (trimmed.StartsWith("## Catalog", StringComparison.Ordinal))
            {
                inCatalogSection = true;
                continue;
            }
            if (inCatalogSection && trimmed.StartsWith("## ", StringComparison.Ordinal))
                break;
            if (!inCatalogSection)
                continue;

            if (trimmed.StartsWith("| EventName", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }
            if (!inTable)
                continue;
            if (trimmed.Length == 0 || trimmed.StartsWith("### ", StringComparison.Ordinal))
                break;
            if (!trimmed.StartsWith("|", StringComparison.Ordinal))
                continue;
            if (trimmed.Contains("---", StringComparison.Ordinal))
                continue;

            var m = CatalogRow.Match(trimmed);
            if (!m.Success)
                continue;
            var name = m.Groups[1].Value;
            if (name is "EventName" or "Pattern" or "Base")
                continue;
            names.Add(name);
        }
        return names;
    }

    private static string FindRepoRoot()
    {
        // Two failure modes look identical from the caller, so report them separately: a
        // missing catalog used to surface as "could not locate repo root", which reads like a
        // harness/path bug and hides the real cause (the doc was deleted or left untracked).
        string srcOnlyRoot = null;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var catalog = Path.Combine(dir.FullName, "docs", "logging-event-catalog.md");
                var src = Path.Combine(dir.FullName, "src");
                if (Directory.Exists(src))
                {
                    if (File.Exists(catalog))
                        return dir.FullName;
                    srcOnlyRoot ??= dir.FullName;
                }
                dir = dir.Parent;
            }
        }

        if (srcOnlyRoot != null)
            throw new InvalidOperationException(
                $"Found repo root '{srcOnlyRoot}' but no docs/logging-event-catalog.md. " +
                "The catalog is a tracked file (see .gitignore); restore it rather than skipping the drift guard.");

        throw new InvalidOperationException(
            $"Could not locate repo root for catalog sync from '{AppContext.BaseDirectory}' " +
            $"or '{Directory.GetCurrentDirectory()}' (looking for a directory containing src/).");
    }
}
