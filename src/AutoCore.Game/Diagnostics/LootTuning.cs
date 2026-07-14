namespace AutoCore.Game.Diagnostics;

using System.Text.Json;
using AutoCore.Utils;

/// <summary>
/// Death-loot rate multiplier loaded from <c>loot.tuning.json</c> next to the launcher.
/// <list type="bullet">
/// <item><description><c>1.0</c> — retail chances unchanged</description></item>
/// <item><description><c>1000.0</c> — each chance is 1000× more likely (clamped to 100%)</description></item>
/// </list>
/// </summary>
public static class LootTuning
{
    public const string DefaultConfigFileName = "loot.tuning.json";
    public const string ConfigFileEnvVar = "AUTOCORE_LOOT_TUNING_FILE";
    public const double DefaultLootRate = 1.0;

    private static double _lootRate = DefaultLootRate;
    private static bool _ignoreDropCommoditiesGate;

    /// <summary>
    /// Multiplier applied to drop probabilities (gear master chance, consumable, credits, commodity).
    /// Values &lt;= 0 yield no chance-based drops. Clamped when scaling individual chances to [0, 1].
    /// </summary>
    public static double LootRate
    {
        get => _lootRate;
        set => _lootRate = value < 0 ? 0 : value;
    }

    /// <summary>
    /// When true, commodity rolls ignore continent <c>bitDropCommodities</c>.
    /// Retail default is false (tutorial Ark Bay 707 has DropCommodities=false).
    /// Useful with a high <see cref="LootRate"/> for salvage testing on gated maps.
    /// </summary>
    public static bool IgnoreDropCommoditiesGate
    {
        get => _ignoreDropCommoditiesGate;
        set => _ignoreDropCommoditiesGate = value;
    }

    /// <summary>Reset to retail defaults (tests + startup before load).</summary>
    public static void ResetToDefaults()
    {
        _lootRate = DefaultLootRate;
        _ignoreDropCommoditiesGate = false;
    }

    /// <summary>
    /// Scale a retail probability in [0,1] by <see cref="LootRate"/>, clamped to [0,1].
    /// A base chance of 0 stays 0 (e.g. "No Loot" tables).
    /// </summary>
    public static double ScaleChance(double baseChance)
    {
        if (baseChance <= 0d || _lootRate <= 0d)
            return 0d;
        if (baseChance >= 1d && _lootRate >= 1d)
            return 1d;
        var scaled = baseChance * _lootRate;
        if (scaled >= 1d)
            return 1d;
        return scaled;
    }

    /// <summary>True if a random roll in [0,1) succeeds against the scaled chance.</summary>
    public static bool Passes(double baseChance, Random random)
    {
        if (random == null)
            throw new ArgumentNullException(nameof(random));

        var scaled = ScaleChance(baseChance);
        if (scaled <= 0d)
            return false;
        if (scaled >= 1d)
            return true;
        return random.NextDouble() < scaled;
    }

    /// <summary>Load JSON from the default / env path (launcher or sector content root).</summary>
    public static void ApplyFromConfigFiles(string contentRoot = null)
    {
        ResetToDefaults();

        var file = ResolveConfigPath(contentRoot);
        if (file == null || !File.Exists(file))
        {
            Logger.WriteLog(LogType.Initialize,
                $"LootTuning: no {DefaultConfigFileName} found — LootRate={LootRate:0.###} (retail)");
            return;
        }

        try
        {
            var json = File.ReadAllText(file);
            if (ApplyFromJson(json, out var error))
                Logger.WriteLog(LogType.Initialize,
                    $"LootTuning: loaded {file} — LootRate={LootRate:0.###} IgnoreDropCommoditiesGate={IgnoreDropCommoditiesGate}");
            else
                Logger.WriteLog(LogType.Error, $"LootTuning: failed to load {file}: {error}");
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"LootTuning: error reading {file}: {ex.Message}");
        }
    }

    public static bool ApplyFromJson(string json, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty json";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "root must be a JSON object";
                return false;
            }

            if (doc.RootElement.TryGetProperty("LootRate", out var rateEl) ||
                doc.RootElement.TryGetProperty("lootRate", out rateEl))
            {
                if (rateEl.ValueKind == JsonValueKind.Number && rateEl.TryGetDouble(out var rate))
                    LootRate = rate;
                else if (rateEl.ValueKind == JsonValueKind.String
                         && double.TryParse(rateEl.GetString(),
                             System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture,
                             out var parsed))
                    LootRate = parsed;
                else
                {
                    error = "LootRate must be a number";
                    return false;
                }
            }

            if (doc.RootElement.TryGetProperty("IgnoreDropCommoditiesGate", out var gateEl) ||
                doc.RootElement.TryGetProperty("ignoreDropCommoditiesGate", out gateEl))
            {
                if (gateEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    IgnoreDropCommoditiesGate = gateEl.GetBoolean();
                else if (gateEl.ValueKind == JsonValueKind.String
                         && bool.TryParse(gateEl.GetString(), out var gateBool))
                    IgnoreDropCommoditiesGate = gateBool;
                else
                {
                    error = "IgnoreDropCommoditiesGate must be a boolean";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ResolveConfigPath(string contentRoot)
    {
        var fromEnv = Environment.GetEnvironmentVariable(ConfigFileEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            var underRoot = Path.Combine(contentRoot, DefaultConfigFileName);
            if (File.Exists(underRoot))
                return underRoot;
        }

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), DefaultConfigFileName);
        if (File.Exists(cwd))
            return cwd;

        var baseDir = Path.Combine(AppContext.BaseDirectory, DefaultConfigFileName);
        if (File.Exists(baseDir))
            return baseDir;

        return null;
    }
}
