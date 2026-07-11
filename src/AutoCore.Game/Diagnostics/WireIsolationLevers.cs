namespace AutoCore.Game.Diagnostics;

using System.Text;
using System.Text.Json;
using AutoCore.Game.Map;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

/// <summary>
/// Central flip-board for Invalid-Packet isolation. Defaults are production-safe (all traffic on, diag off).
/// Configure via: JSON file, environment variables, or console <c>sector.wire</c> commands.
/// </summary>
public static class WireIsolationLevers
{
    public const string DefaultConfigFileName = "wire-isolation.levers.json";
    public const string ConfigFileEnvVar = "AUTOCORE_WIRE_LEVERS_FILE";
    public const string EnvPrefix = "AUTOCORE_WIRE_";

    private static readonly LeverDef[] LeverDefs =
    {
        new("WireDiag", "Log S2C game packets + GhostVehicle packs",
            () => WireDiag.Enabled, v => WireDiag.Enabled = v, envSuffix: "DIAG"),
        new("ScopeGlobalVehicles", "Foreign global vehicle CreateVehicle + ObjectInScope",
            () => SectorMap.ScopeGlobalVehicles, v => SectorMap.ScopeGlobalVehicles = v, envSuffix: "SCOPE_GLOBAL_VEHICLES"),
        new("ScopeGlobalVehicleCreate", "Foreign global CreateVehicle only",
            () => SectorMap.ScopeGlobalVehicleCreate, v => SectorMap.ScopeGlobalVehicleCreate = v, envSuffix: "SCOPE_GLOBAL_VEHICLE_CREATE"),
        new("ScopeGlobalVehicleGhost", "Foreign global ObjectInScope (ghost) only",
            () => SectorMap.ScopeGlobalVehicleGhost, v => SectorMap.ScopeGlobalVehicleGhost = v, envSuffix: "SCOPE_GLOBAL_VEHICLE_GHOST"),
        new("SendGroupReactionCall", "Send GroupReactionCall 0x206C after reactions",
            () => SectorMap.SendGroupReactionCall, v => SectorMap.SendGroupReactionCall = v, envSuffix: "SEND_GROUP_REACTION_CALL"),
        new("EnableAiStateWire", "GhostVehicle AI StateMask body",
            () => GhostVehicle.EnableAiStateWire, v => GhostVehicle.EnableAiStateWire = v, envSuffix: "AI_STATE"),
        new("EnablePathWire", "GhostVehicle optional path block",
            () => GhostVehicle.EnablePathWire, v => GhostVehicle.EnablePathWire = v, envSuffix: "PATH"),
        new("EnableOwnerWire", "GhostVehicle CurrentOwner block",
            () => GhostVehicle.EnableOwnerWire, v => GhostVehicle.EnableOwnerWire = v, envSuffix: "OWNER"),
        new("EnableTemplateSpawnWire", "GhostVehicle template + spawn-owner blocks",
            () => GhostVehicle.EnableTemplateSpawnWire, v => GhostVehicle.EnableTemplateSpawnWire = v, envSuffix: "TEMPLATE_SPAWN"),
        new("EnableMinimalForeignInitialProfile", "Foreign GhostVehicle required-initial-body + pose-only updates",
            () => GhostVehicle.EnableMinimalForeignInitialProfile, v => GhostVehicle.EnableMinimalForeignInitialProfile = v, envSuffix: "MINIMAL_FOREIGN_INITIAL"),
        new("EnableMinimalForeignPathBlock", "Allow path block during minimal foreign initial profile",
            () => GhostVehicle.EnableMinimalForeignPathBlock, v => GhostVehicle.EnableMinimalForeignPathBlock = v, envSuffix: "MINIMAL_FOREIGN_PATH"),
        new("EnableMinimalForeignTemplateSpawnBlock", "Allow template/spawn block during minimal foreign initial profile",
            () => GhostVehicle.EnableMinimalForeignTemplateSpawnBlock, v => GhostVehicle.EnableMinimalForeignTemplateSpawnBlock = v, envSuffix: "MINIMAL_FOREIGN_TEMPLATE_SPAWN"),
        new("EnableMinimalForeignOwnerBlock", "Allow owner block during minimal foreign initial profile",
            () => GhostVehicle.EnableMinimalForeignOwnerBlock, v => GhostVehicle.EnableMinimalForeignOwnerBlock = v, envSuffix: "MINIMAL_FOREIGN_OWNER"),
        new("EnableInitialHardpointPack", "Pack WheelSet hardpoint on ghost initial (seeds create-buffer +0x45c)",
            () => GhostVehicle.EnableInitialHardpointPack, v => GhostVehicle.EnableInitialHardpointPack = v, envSuffix: "INITIAL_HARDPOINT"),
        new("EnableDeferredForeignPose", "Omit pose on foreign ghost initial; ship pose on later delta (owner-on race)",
            () => GhostVehicle.EnableDeferredForeignPose, v => GhostVehicle.EnableDeferredForeignPose = v, envSuffix: "DEFER_FOREIGN_POSE"),
    };

    /// <summary>Load JSON (if present) then env overrides. Call once at process start.</summary>
    public static void ApplyFromEnvironmentAndConfigFiles(string contentRoot = null)
    {
        ResetToDefaults();

        var file = ResolveConfigPath(contentRoot);
        if (file != null && File.Exists(file))
        {
            try
            {
                var json = File.ReadAllText(file);
                if (ApplyFromJson(json, out var error))
                    Logger.WriteLog(LogType.Network, $"WireIsolationLevers: loaded {file}");
                else
                    Logger.WriteLog(LogType.Error, $"WireIsolationLevers: failed to load {file}: {error}");
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"WireIsolationLevers: error reading {file}: {ex.Message}");
            }
        }

        ApplyFromEnvironmentVariables();
        Logger.WriteLog(LogType.Network, "WireIsolationLevers active:\n" + FormatStatus());
    }

    public static void ResetToDefaults()
    {
        WireDiag.Enabled = false;
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = false;
        SectorMap.SendGroupReactionCall = true;
        GhostVehicle.EnableAiStateWire = true;
        GhostVehicle.EnablePathWire = true;
        GhostVehicle.EnableOwnerWire = true;
        GhostVehicle.EnableTemplateSpawnWire = true;
        GhostVehicle.EnableMinimalForeignInitialProfile = false;
        GhostVehicle.EnableMinimalForeignPathBlock = false;
        GhostVehicle.EnableMinimalForeignTemplateSpawnBlock = false;
        GhostVehicle.EnableMinimalForeignOwnerBlock = false;
        GhostVehicle.EnableInitialHardpointPack = false;
        GhostVehicle.EnableDeferredForeignPose = false;
    }

    public static void ApplyFromEnvironmentVariables()
    {
        // WireDiag also has its own helper; apply all levers including DIAG.
        foreach (var def in LeverDefs)
        {
            var raw = Environment.GetEnvironmentVariable(EnvPrefix + def.EnvSuffix);
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!TryParseBool(raw, out var value))
            {
                Logger.WriteLog(LogType.Error,
                    $"WireIsolationLevers: invalid env {EnvPrefix}{def.EnvSuffix}={raw} (use 0/1 true/false)");
                continue;
            }

            def.Set(value);
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

            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.True)
                    map[prop.Name] = true;
                else if (prop.Value.ValueKind == JsonValueKind.False)
                    map[prop.Name] = false;
                else if (prop.Value.ValueKind == JsonValueKind.String
                         && TryParseBool(prop.Value.GetString(), out var parsed))
                    map[prop.Name] = parsed;
                else
                {
                    error = $"property '{prop.Name}' must be boolean";
                    return false;
                }
            }

            ApplyFromDictionary(map);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void ApplyFromDictionary(IReadOnlyDictionary<string, bool> values)
    {
        if (values == null)
            return;

        foreach (var pair in values)
        {
            if (!TrySet(pair.Key, pair.Value, out _))
                Logger.WriteLog(LogType.Error, $"WireIsolationLevers: unknown key '{pair.Key}' ignored");
        }
    }

    public static bool TrySet(string name, bool value, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "lever name required";
            return false;
        }

        var def = Find(name);
        if (def == null)
        {
            error = $"Unknown lever '{name}'. Known: {string.Join(", ", LeverDefs.Select(d => d.Name))}";
            return false;
        }

        def.Set(value);
        return true;
    }

    public static bool TryGet(string name, out bool value)
    {
        value = false;
        var def = Find(name);
        if (def == null)
            return false;
        value = def.Get();
        return true;
    }

    public static IReadOnlyList<WireLeverStatus> Snapshot()
    {
        return LeverDefs.Select(d => new WireLeverStatus
        {
            Name = d.Name,
            Value = d.Get(),
            Description = d.Description,
            EnvVar = EnvPrefix + d.EnvSuffix,
        }).ToList();
    }

    public static string FormatStatus()
    {
        var sb = new StringBuilder();
        foreach (var entry in Snapshot())
        {
            sb.Append("  ");
            sb.Append(entry.Name);
            sb.Append(" = ");
            sb.Append(entry.Value ? "true" : "false");
            sb.Append("  (");
            sb.Append(entry.Description);
            sb.Append("; env ");
            sb.Append(entry.EnvVar);
            sb.Append(')');
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static bool TryParseBool(string raw, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();
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

    /// <summary>
    /// Console handler: <c>wire list|status|reset|set &lt;Name&gt; &lt;0|1&gt;|diag &lt;on|off&gt;|help</c>.
    /// </summary>
    public static void HandleConsoleCommand(string[] parts)
    {
        // parts[0] is command name (wire / sector.wire); subcommand at [1]
        var sub = parts.Length > 1 ? parts[1] : "list";
        switch (sub.ToLowerInvariant())
        {
            case "list":
            case "status":
            case "show":
                Logger.WriteLog(LogType.Command, "Wire isolation levers:\n" + FormatStatus());
                break;

            case "reset":
            case "defaults":
                ResetToDefaults();
                Logger.WriteLog(LogType.Command, "Wire isolation levers reset to production defaults.\n" + FormatStatus());
                break;

            case "diag":
                if (parts.Length < 3 || !TryParseBool(parts[2], out var diagOn))
                {
                    Logger.WriteLog(LogType.Command, "Usage: wire diag <on|off|0|1>");
                    break;
                }

                TrySet("WireDiag", diagOn, out _);
                Logger.WriteLog(LogType.Command, $"WireDiag = {WireDiag.Enabled}");
                break;

            case "set":
                if (parts.Length < 4 || !TryParseBool(parts[3], out var setVal))
                {
                    Logger.WriteLog(LogType.Command, "Usage: wire set <LeverName> <true|false|0|1>");
                    Logger.WriteLog(LogType.Command, "Levers: " + string.Join(", ", LeverDefs.Select(d => d.Name)));
                    break;
                }

                if (!TrySet(parts[2], setVal, out var setError))
                {
                    Logger.WriteLog(LogType.Command, setError);
                    break;
                }

                TryGet(parts[2], out var now);
                Logger.WriteLog(LogType.Command, $"{Find(parts[2])!.Name} = {now}");
                break;

            case "help":
            case "?":
                Logger.WriteLog(LogType.Command, """
                    Wire isolation commands:
                      wire list                     Show all levers
                      wire set <Name> <true|false>  Flip one lever (live; no rebuild)
                      wire diag <on|off>            Shortcut for WireDiag
                      wire reset                    Production defaults
                      wire help                     This text

                    Matrix shortcuts (examples):
                      wire set ScopeGlobalVehicles false     # B1
                      wire set ScopeGlobalVehicleGhost false # B2
                      wire set EnablePathWire false          # B4
                      wire set EnableOwnerWire false         # B5
                      wire set EnableTemplateSpawnWire false # B6
                      wire set EnableAiStateWire false       # B3
                      wire set SendGroupReactionCall false   # B7
                    """);
                break;

            default:
                Logger.WriteLog(LogType.Command, $"Unknown wire subcommand '{sub}'. Try: wire help");
                break;
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

        var cwd = Path.Combine(Environment.CurrentDirectory, DefaultConfigFileName);
        if (File.Exists(cwd))
            return cwd;

        // Also check next to the entry assembly (Launcher bin folder).
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var underBase = Path.Combine(baseDir, DefaultConfigFileName);
            if (File.Exists(underBase))
                return underBase;
        }

        return null;
    }

    private static LeverDef Find(string name)
    {
        return LeverDefs.FirstOrDefault(d =>
            string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.EnvSuffix, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(EnvPrefix + d.EnvSuffix, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LeverDef
    {
        public string Name { get; }
        public string Description { get; }
        public string EnvSuffix { get; }
        private readonly Func<bool> _get;
        private readonly Action<bool> _set;

        public LeverDef(string name, string description, Func<bool> get, Action<bool> set, string envSuffix)
        {
            Name = name;
            Description = description;
            EnvSuffix = envSuffix;
            _get = get;
            _set = set;
        }

        public bool Get() => _get();
        public void Set(bool value) => _set(value);
    }
}

public sealed class WireLeverStatus
{
    public string Name { get; set; }
    public bool Value { get; set; }
    public string Description { get; set; }
    public string EnvVar { get; set; }
}
