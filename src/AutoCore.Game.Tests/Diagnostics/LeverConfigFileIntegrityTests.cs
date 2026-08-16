using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using System.Text.Json;

/// <summary>
/// Tripwire for silently-dead lever/filter config.
/// <para>
/// Live 2026-08-15: <c>src/AutoCore.Launcher/wire-isolation.levers.json</c> carried a trailing comma
/// before its closing brace. <c>System.Text.Json</c> rejects that, the loader logged one Error line
/// during boot, and the server then ran on <b>compiled defaults for every lever in the file</b>.
/// The practical damage was that <c>"EnableSoftNpcPathMotion": true</c> never applied — its compiled
/// default is <c>false</c>, and <c>NpcTicker</c> gates all foot-creature motion smoothing on that
/// flag — so creatures ran the raw path stepper (no turn-rate limit, no velocity carried through
/// arrivals) in every session, while the checked-in config said otherwise.
/// </para>
/// <para>
/// A config file that fails to parse is indistinguishable from one that parses to the same values,
/// unless someone reads the boot log. These tests make it a build failure instead.
/// </para>
/// </summary>
[TestClass]
public class LeverConfigFileIntegrityTests
{
    private static readonly string[] LeverConfigRelativePaths =
    {
        "wire-isolation.levers.json",
        Path.Combine("src", "AutoCore.Launcher", "wire-isolation.levers.json"),
        Path.Combine("src", "AutoCore.Sector", "wire-isolation.levers.json"),
    };

    private static readonly string[] LogFilterConfigRelativePaths =
    {
        "log.filters.json",
        Path.Combine("src", "AutoCore.Launcher", "log.filters.json"),
        Path.Combine("src", "AutoCore.Sector", "log.filters.json"),
    };

    private static readonly string[] ServerConfigRelativePaths =
    {
        Path.Combine("src", "AutoCore.Launcher", "serverConfig.yaml"),
        Path.Combine("src", "AutoCore.Sector", "serverConfig.yaml"),
    };

    /// <summary>
    /// Same silent-no-op class as the lever JSON, seen live: a stray <c>0</c> on its own line inside
    /// <c>npcVehiclePhysics</c> made the whole file unparseable, so every server setting fell back to
    /// its compiled default after one Error line at boot.
    /// </summary>
    [TestMethod]
    public void ShippedServerConfigs_ParseAndApplyCleanly()
    {
        var repoRoot = FindRepoRoot();
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var relative in ServerConfigRelativePaths)
        {
            var path = Path.Combine(repoRoot, relative);
            if (!File.Exists(path))
                continue;

            checkedCount++;
            try
            {
                if (!AutoCore.Game.Diagnostics.ServerConfig.ApplyFromYaml(File.ReadAllText(path), out var error))
                    failures.Add($"{relative}: {error}");
            }
            catch (Exception ex)
            {
                failures.Add($"{relative}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                AutoCore.Game.Diagnostics.ServerConfig.ResetToDefaults();
            }
        }

        Assert.IsTrue(checkedCount > 0, "found no serverConfig.yaml files to validate");
        Assert.AreEqual(0, failures.Count,
            "serverConfig.yaml must parse and validate — a failure silently reverts the whole file "
            + "to compiled defaults:\n  " + string.Join("\n  ", failures));
    }

    [TestMethod]
    public void ShippedLeverConfigs_AreValidJson()
    {
        AssertAllParse(LeverConfigRelativePaths, "wire isolation lever");
    }

    [TestMethod]
    public void ShippedLogFilterConfigs_AreValidJson()
    {
        AssertAllParse(LogFilterConfigRelativePaths, "log filter");
    }

    /// <summary>
    /// Every key must correspond to a real lever. A renamed or removed lever leaves a key that
    /// parses fine and does nothing — the same silent-no-op class as the trailing comma.
    /// </summary>
    [TestMethod]
    public void ShippedLeverConfigs_ContainOnlyKnownLeverNames()
    {
        var repoRoot = FindRepoRoot();
        var known = new HashSet<string>(
            AutoCore.Game.Diagnostics.WireIsolationLevers.Snapshot().Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(known.Count > 0, "expected the lever board to expose its names");

        var unknown = new List<string>();
        foreach (var relative in LeverConfigRelativePaths)
        {
            var path = Path.Combine(repoRoot, relative);
            if (!File.Exists(path))
                continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!known.Contains(property.Name))
                    unknown.Add($"{relative}: '{property.Name}'");
            }
        }

        Assert.AreEqual(0, unknown.Count,
            "lever config keys that match no lever are silently ignored:\n  "
            + string.Join("\n  ", unknown));
    }

    private static void AssertAllParse(string[] relativePaths, string label)
    {
        var repoRoot = FindRepoRoot();
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(repoRoot, relative);
            if (!File.Exists(path))
                continue;

            checkedCount++;
            try
            {
                using var _ = JsonDocument.Parse(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                failures.Add($"{relative}: {ex.Message}");
            }
        }

        Assert.IsTrue(checkedCount > 0, $"found no {label} config files to validate");
        Assert.AreEqual(0, failures.Count,
            $"{label} config files must parse — a parse failure silently reverts the server to "
            + "compiled defaults:\n  " + string.Join("\n  ", failures));
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                    && File.Exists(Path.Combine(dir.FullName, "src", "AutoCore.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate repo root for lever config integrity test.");
    }
}
