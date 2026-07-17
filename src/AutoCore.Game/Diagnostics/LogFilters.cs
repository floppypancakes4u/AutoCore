namespace AutoCore.Game.Diagnostics;

using System.Text;
using System.Text.Json;
using AutoCore.Utils;

/// <summary>
/// Named log categories loaded from <c>log.filters.json</c> and toggled live via console
/// <c>sector.log</c> / <c>log</c>. Defaults keep loot + map-prop ram + TakeDamage visible and silence
/// high-volume packet / wire / death-net spam. Nested JSON sections (Damage, Props) are optional
/// grouping only — leaf names match filter identifiers.
/// </summary>
public static class LogFilters
{
    public const string DefaultConfigFileName = "log.filters.json";
    public const string ConfigFileEnvVar = "AUTOCORE_LOG_FILTERS_FILE";

    private static readonly FilterDef[] FilterDefs =
    {
        new("OutgoingPackets", "TNLConnection 'Outgoing Packet: …' lines",
            () => OutgoingPackets, v => OutgoingPackets = v, defaultOn: false),
        new("IncomingPackets", "TNLConnection 'Incoming Packet: …' lines",
            () => IncomingPackets, v => IncomingPackets = v, defaultOn: false),
        new("InventoryFlow", "Inventory create/cargo hex dumps",
            () => InventoryFlow, v => InventoryFlow = v, defaultOn: false),
        new("PathPoseForce", "2s PathPoseForce dirtyGhosted spam",
            () => PathPoseForce, v => PathPoseForce = v, defaultOn: false),
        new("CreateVehicleWire", "CreateVehicle wire summary lines",
            () => CreateVehicleWire, v => CreateVehicleWire = v, defaultOn: false),
        new("ForeignOwnerAttach", "ForeignOwnerAttachReapply recreate lines",
            () => ForeignOwnerAttach, v => ForeignOwnerAttach = v, defaultOn: false),
        new("SaveProgress", "Character progress save lines",
            () => SaveProgress, v => SaveProgress = v, defaultOn: false),
        new("GiveXp", "GiveXp detail lines",
            () => GiveXp, v => GiveXp = v, defaultOn: false),
        new("WireDiag", "WireDiag packet hex (also WireDiag.Enabled)",
            () => WireDiag.Enabled, v => WireDiag.Enabled = v, defaultOn: false),
        new("GhostObjectDiag", "GhostObjectDiag create/scope spam",
            () => GhostObjectDiag.Enabled, v => GhostObjectDiag.Enabled = v, defaultOn: false),
        new("Loot", "LootManager generation / spawn / death tracks",
            () => Loot, v => Loot = v, defaultOn: true),
        new("MapPropRam", "VehicleMapPropRam collision hits",
            () => MapPropRam, v => MapPropRam = v, defaultOn: true),

        // Damage section (log.filters.json "Damage": { … })
        new("TakeDamage", "GraphicsObject TakeDamage HP delta lines",
            () => TakeDamage, v => TakeDamage = v, defaultOn: true),
        new("OnDeath", "OnDeath map-prop / Vehicle.OnDeath debug lines",
            () => OnDeath, v => OnDeath = v, defaultOn: false),
        new("RestoreHealth", "RestoreHealth stale-corpse clear lines",
            () => RestoreHealth, v => RestoreHealth = v, defaultOn: false),
        new("DeathNet", "BroadcastDeath per-player DeathNet network lines",
            () => DeathNet, v => DeathNet = v, defaultOn: false),
        new("PlayerDeathGhost", "Player vehicle death ghost flush lines",
            () => PlayerDeathGhost, v => PlayerDeathGhost = v, defaultOn: false),

        // Props section (log.filters.json "Props": { … })
        new("MapPropCorpseDespawn", "MapPropCorpseDespawn schedule/finalize lines",
            () => MapPropCorpseDespawn, v => MapPropCorpseDespawn = v, defaultOn: false),
        new("MapPropDeathLoot", "Map prop death loot position debug lines",
            () => MapPropDeathLoot, v => MapPropDeathLoot = v, defaultOn: false),
    };

    public static bool OutgoingPackets { get; set; }
    public static bool IncomingPackets { get; set; }
    public static bool InventoryFlow { get; set; }
    public static bool PathPoseForce { get; set; }
    public static bool CreateVehicleWire { get; set; }
    public static bool ForeignOwnerAttach { get; set; }
    public static bool SaveProgress { get; set; }
    public static bool GiveXp { get; set; }
    public static bool Loot { get; set; } = true;
    public static bool MapPropRam { get; set; } = true;

    // Damage
    public static bool TakeDamage { get; set; } = true;
    public static bool OnDeath { get; set; }
    public static bool RestoreHealth { get; set; }
    public static bool DeathNet { get; set; }
    public static bool PlayerDeathGhost { get; set; }

    // Props
    public static bool MapPropCorpseDespawn { get; set; }
    public static bool MapPropDeathLoot { get; set; }

    public static void ResetToDefaults()
    {
        foreach (var def in FilterDefs)
            def.Set(def.DefaultOn);
    }

    /// <summary>Load JSON after wire levers so filter defaults can quiet WireDiag without rebuild.</summary>
    public static void ApplyFromConfigFiles(string contentRoot = null)
    {
        ResetToDefaults();

        var file = ResolveConfigPath(contentRoot);
        if (file == null || !File.Exists(file))
        {
            Logger.WriteLog(LogType.Initialize,
                $"LogFilters: no {DefaultConfigFileName} — defaults (Loot/MapPropRam on; packet spam off)");
            return;
        }

        try
        {
            var json = File.ReadAllText(file);
            if (ApplyFromJson(json, out var error))
                Logger.WriteLog(LogType.Initialize, $"LogFilters: loaded {file}\n{FormatStatus()}");
            else
                Logger.WriteLog(LogType.Error, $"LogFilters: failed to load {file}: {error}");
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"LogFilters: error reading {file}: {ex.Message}");
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

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Nested sections (Damage / Props / …) group leaf filter names; section labels
                // are organizational only and do not themselves become filters.
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var child in prop.Value.EnumerateObject())
                    {
                        if (!TryParseBoolElement(child.Value, out var nestedValue))
                        {
                            error = $"property '{prop.Name}.{child.Name}' must be boolean";
                            return false;
                        }

                        if (!TrySet(child.Name, nestedValue, out _))
                            Logger.WriteLog(LogType.Network,
                                $"LogFilters: unknown key '{prop.Name}.{child.Name}' ignored");
                    }

                    continue;
                }

                if (!TryParseBoolElement(prop.Value, out var value))
                {
                    error = $"property '{prop.Name}' must be boolean";
                    return false;
                }

                if (!TrySet(prop.Name, value, out _))
                {
                    // Unknown keys ignored with log (same spirit as wire levers)
                    Logger.WriteLog(LogType.Network, $"LogFilters: unknown key '{prop.Name}' ignored");
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

    public static bool TrySet(string name, bool value, out string error)
    {
        error = null;
        var def = Find(name);
        if (def == null)
        {
            error = $"Unknown log filter '{name}'. Known: " + string.Join(", ", FilterDefs.Select(d => d.Name));
            return false;
        }

        def.Set(value);
        return true;
    }

    public static string FormatStatus()
    {
        var sb = new StringBuilder();
        foreach (var def in FilterDefs)
            sb.AppendLine($"  {def.Name} = {def.Get()}  ({def.Description})");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Console: <c>log list|set &lt;Name&gt; &lt;0|1&gt;|quiet|loot|reset|help</c>
    /// </summary>
    public static void HandleConsoleCommand(string[] parts)
    {
        var sub = parts.Length > 1 ? parts[1] : "list";
        switch (sub.ToLowerInvariant())
        {
            case "list":
            case "status":
            case "show":
                Logger.WriteLog(LogType.Command, "Log filters:\n" + FormatStatus());
                break;

            case "reset":
            case "defaults":
                ResetToDefaults();
                Logger.WriteLog(LogType.Command, "Log filters reset to defaults.\n" + FormatStatus());
                break;

            case "quiet":
                ApplyQuietPreset();
                Logger.WriteLog(LogType.Command, "Log filters: quiet preset (Loot+MapPropRam+TakeDamage).\n" + FormatStatus());
                break;

            case "loot":
                ApplyLootWorkPreset();
                Logger.WriteLog(LogType.Command, "Log filters: loot work preset.\n" + FormatStatus());
                break;

            case "set":
                if (parts.Length < 4 || !TryParseBool(parts[3], out var setVal))
                {
                    Logger.WriteLog(LogType.Command, "Usage: log set <Name> <true|false|0|1>");
                    Logger.WriteLog(LogType.Command, "Filters: " + string.Join(", ", FilterDefs.Select(d => d.Name)));
                    break;
                }

                if (!TrySet(parts[2], setVal, out var setError))
                {
                    Logger.WriteLog(LogType.Command, setError);
                    break;
                }

                Logger.WriteLog(LogType.Command, $"{Find(parts[2])!.Name} = {setVal}");
                break;

            case "help":
            case "?":
                Logger.WriteLog(LogType.Command, """
                    Log filter commands:
                      log list                     Show all filters
                      log set <Name> <true|false>  Flip one category (live)
                      log quiet                    Loot + MapPropRam + TakeDamage (+ errors always)
                      log loot                     Loot work: Loot+MapPropRam on, packet spam off
                      log reset                    Defaults
                      log help                     This text

                    Damage leaves: TakeDamage (default on), OnDeath, RestoreHealth, DeathNet, PlayerDeathGhost
                    Props leaves:  MapPropCorpseDespawn, MapPropDeathLoot  (MapPropRam stays top-level)
                    Nested JSON sections Damage/Props in log.filters.json are optional grouping only.
                    """);
                break;

            default:
                Logger.WriteLog(LogType.Command, $"Unknown log subcommand '{sub}'. Try: log help");
                break;
        }
    }

    /// <summary>Silence packet/wire/death spam; keep loot + ram + TakeDamage.</summary>
    public static void ApplyQuietPreset()
    {
        ResetToDefaults();
        OutgoingPackets = false;
        IncomingPackets = false;
        InventoryFlow = false;
        PathPoseForce = false;
        CreateVehicleWire = false;
        ForeignOwnerAttach = false;
        SaveProgress = false;
        GiveXp = false;
        WireDiag.Enabled = false;
        GhostObjectDiag.Enabled = false;
        Loot = true;
        MapPropRam = true;
        TakeDamage = true;
        OnDeath = false;
        RestoreHealth = false;
        DeathNet = false;
        PlayerDeathGhost = false;
        MapPropCorpseDespawn = false;
        MapPropDeathLoot = false;
    }

    /// <summary>Same as quiet for now — intended while debugging loot/ram.</summary>
    public static void ApplyLootWorkPreset() => ApplyQuietPreset();

    public static void WriteIf(bool enabled, LogType type, string message)
    {
        if (enabled)
            Logger.WriteLog(type, message);
    }

    public static void WriteIf(bool enabled, LogType type, string format, params object[] args)
    {
        if (enabled)
            Logger.WriteLog(type, format, args);
    }

    private static FilterDef Find(string name) =>
        FilterDefs.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

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

    private static bool TryParseBool(string raw, out bool value)
    {
        value = false;
        if (raw is "1" or "true" or "TRUE" or "True" or "yes" or "YES" or "on" or "ON")
        {
            value = true;
            return true;
        }

        if (raw is "0" or "false" or "FALSE" or "False" or "no" or "NO" or "off" or "OFF")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryParseBoolElement(JsonElement el, out bool value)
    {
        value = false;
        if (el.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (el.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        if (el.ValueKind == JsonValueKind.String)
            return TryParseBool(el.GetString(), out value);

        return false;
    }

    private sealed class FilterDef
    {
        public string Name { get; }
        public string Description { get; }
        public bool DefaultOn { get; }
        private readonly Func<bool> _get;
        private readonly Action<bool> _set;

        public FilterDef(string name, string description, Func<bool> get, Action<bool> set, bool defaultOn)
        {
            Name = name;
            Description = description;
            DefaultOn = defaultOn;
            _get = get;
            _set = set;
        }

        public bool Get() => _get();
        public void Set(bool value) => _set(value);
    }
}
