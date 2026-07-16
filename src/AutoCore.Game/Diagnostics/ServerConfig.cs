namespace AutoCore.Game.Diagnostics;

using AutoCore.Utils;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Which mover drives NPC vehicles along paths. Ordered least → most faithful.
/// The physics tier is the retail-faithful Havok port (<c>feature-NPC-Retail-Driving</c>);
/// the others are the pre-existing kinematic fallbacks kept for A/B and safety.
/// </summary>
public enum NpcVehicleControllerTier
{
    /// <summary>Raw <c>NpcPathFollower</c> hard stepper (legacy default).</summary>
    Hard = 0,

    /// <summary><c>SoftNpcPathMotion</c> pure-pursuit look-ahead (legacy soft).</summary>
    Soft = 1,

    /// <summary><c>NpcVehicleDriveController</c> kinematic controller.</summary>
    Kinematic = 2,

    /// <summary>Retail Havok single-rigid-body physics simulation.</summary>
    Physics = 3,
}

/// <summary>
/// Server tuning loaded from <c>serverConfig.yaml</c> next to the launcher — a YAML (comment-friendly)
/// sibling to <c>loot.tuning.json</c>. Currently scopes the NPC vehicle physics simulation; other
/// settings migrate here over time.
/// </summary>
/// <remarks>
/// Loader mirrors <see cref="LootTuning"/>: env override → content root → cwd → base dir. Missing file
/// or missing keys leave retail-safe defaults (physics OFF, <see cref="NpcVehicleControllerTier.Hard"/>),
/// so behaviour is unchanged until an operator opts in.
/// </remarks>
public static class ServerConfig
{
    public const string DefaultConfigFileName = "serverConfig.yaml";
    public const string ConfigFileEnvVar = "AUTOCORE_SERVER_CONFIG_FILE";

    // --- Defaults (retail-safe: new physics off, legacy hard mover) ---
    public const bool DefaultNpcVehiclePhysicsEnabled = false;
    public const NpcVehicleControllerTier DefaultControllerTier = NpcVehicleControllerTier.Hard;
    public const int DefaultSubstepHz = 60;      // placeholder until Phase 0.1 REs the client step rate
    public const float DefaultGravity = -9.81f;  // Y-up world gravity; refined in Phase 0
    public const bool DefaultDebugLogging = false;

    private static int _substepHz = DefaultSubstepHz;

    /// <summary>Master switch for the retail Havok physics simulation.</summary>
    public static bool NpcVehiclePhysicsEnabled { get; set; } = DefaultNpcVehiclePhysicsEnabled;

    /// <summary>Which mover NPC vehicles use. <see cref="NpcVehicleControllerTier.Physics"/> also requires <see cref="NpcVehiclePhysicsEnabled"/>.</summary>
    public static NpcVehicleControllerTier ControllerTier { get; set; } = DefaultControllerTier;

    /// <summary>Fixed physics substep rate (Hz). Clamped to [1, 480]. Fed a variable server dt via an accumulator.</summary>
    public static int SubstepHz
    {
        get => _substepHz;
        set => _substepHz = value < 1 ? 1 : (value > 480 ? 480 : value);
    }

    /// <summary>World gravity (world units/s², negative = down along +Y-up).</summary>
    public static float Gravity { get; set; } = DefaultGravity;

    /// <summary>Optional air-density override for aerodynamics; null uses each clonebase's own value.</summary>
    public static float? AirDensityOverride { get; set; }

    /// <summary>Verbose per-vehicle physics logging.</summary>
    public static bool DebugLogging { get; set; } = DefaultDebugLogging;

    /// <summary>Reset every setting to retail-safe defaults (tests + startup before load).</summary>
    public static void ResetToDefaults()
    {
        NpcVehiclePhysicsEnabled = DefaultNpcVehiclePhysicsEnabled;
        ControllerTier = DefaultControllerTier;
        _substepHz = DefaultSubstepHz;
        Gravity = DefaultGravity;
        AirDensityOverride = null;
        DebugLogging = DefaultDebugLogging;
    }

    /// <summary>Load YAML from the default / env path (launcher or sector content root).</summary>
    public static void ApplyFromConfigFiles(string contentRoot = null)
    {
        ResetToDefaults();

        var file = ResolveConfigPath(contentRoot);
        if (file == null || !File.Exists(file))
        {
            Logger.WriteLog(LogType.Initialize,
                $"ServerConfig: no {DefaultConfigFileName} found — NPC vehicle physics defaults (tier={ControllerTier}, enabled={NpcVehiclePhysicsEnabled})");
            return;
        }

        try
        {
            var yaml = File.ReadAllText(file);
            if (ApplyFromYaml(yaml, out var error))
                Logger.WriteLog(LogType.Initialize,
                    $"ServerConfig: loaded {file} — tier={ControllerTier} enabled={NpcVehiclePhysicsEnabled} substepHz={SubstepHz}");
            else
                Logger.WriteLog(LogType.Error, $"ServerConfig: failed to load {file}: {error}");
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"ServerConfig: error reading {file}: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse a YAML document and apply the <c>npcVehiclePhysics</c> section. Missing keys keep defaults.
    /// Returns false (with <paramref name="error"/>) on malformed YAML or invalid values.
    /// </summary>
    public static bool ApplyFromYaml(string yaml, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(yaml))
        {
            error = "empty yaml";
            return false;
        }

        RootDto root;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            root = deserializer.Deserialize<RootDto>(yaml);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var p = root?.NpcVehiclePhysics;
        if (p == null)
            return true; // valid YAML, no section → keep defaults

        if (p.Enabled.HasValue)
            NpcVehiclePhysicsEnabled = p.Enabled.Value;

        if (!string.IsNullOrWhiteSpace(p.ControllerTier))
        {
            if (Enum.TryParse<NpcVehicleControllerTier>(p.ControllerTier, ignoreCase: true, out var tier))
                ControllerTier = tier;
            else
            {
                error = $"controllerTier '{p.ControllerTier}' is not one of hard|soft|kinematic|physics";
                return false;
            }
        }

        if (p.SubstepHz.HasValue)
            SubstepHz = p.SubstepHz.Value;

        if (p.Gravity.HasValue)
            Gravity = p.Gravity.Value;

        if (p.AirDensityOverride.HasValue)
            AirDensityOverride = p.AirDensityOverride.Value;

        if (p.DebugLogging.HasValue)
            DebugLogging = p.DebugLogging.Value;

        return true;
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

    // --- YAML binding DTOs (nullable so unset keys fall back to defaults) ---

    private sealed class RootDto
    {
        public NpcVehiclePhysicsDto NpcVehiclePhysics { get; set; }
    }

    private sealed class NpcVehiclePhysicsDto
    {
        public bool? Enabled { get; set; }
        public string ControllerTier { get; set; }
        public int? SubstepHz { get; set; }
        public float? Gravity { get; set; }
        public float? AirDensityOverride { get; set; }
        public bool? DebugLogging { get; set; }
    }
}
