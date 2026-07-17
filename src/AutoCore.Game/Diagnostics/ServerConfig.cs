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
    // Placeholder fixed-Hz default. Client StepTo (0x4d6c80) uses variable frameDt/N with a ~30 Hz cap
    // (N = floor(frameDt·29.9999998)+1, substep_dt = frameDt/N; see HkVehicleSubstep). Not the retail rule.
    public const int DefaultSubstepHz = 60;
    public const float DefaultGravity = -9.81f;  // Y-up world gravity; refined in Phase 0
    public const bool DefaultDebugLogging = false;
    /// <summary>Retail hkDefaultSuspension is unclamped (C2) — the safety clamp defaults OFF.</summary>
    public const bool DefaultSuspensionForceClampEnabled = false;
    /// <summary>CW composite object/vehicle wheel casts — defaults OFF (terrain-only until opted in).</summary>
    public const bool DefaultCompositeWheelCollisionEnabled = false;
    /// <summary>
    /// Retail applies susp/friction as point impulses at wheel contacts (r×F weight transfer).
    /// That path still tumbles NPCs under the reduced friction model + real mass — defaults
    /// <b>OFF</b> (COM linear only) until the dual-body solver is mass-faithful. Unit tests that
    /// assert r×J enable this flag.
    /// </summary>
    public const bool DefaultChassisPointImpulsesEnabled = false;
    /// <summary>Server-side map-prop ramming (<c>VehicleMapPropRam</c>) — defaults OFF.</summary>
    public const bool DefaultEnableRamming = false;

    private static int _substepHz = DefaultSubstepHz;

    /// <summary>Master switch for the retail Havok physics simulation.</summary>
    public static bool NpcVehiclePhysicsEnabled { get; set; } = DefaultNpcVehiclePhysicsEnabled;

    /// <summary>Which mover NPC vehicles use. <see cref="NpcVehicleControllerTier.Physics"/> also requires <see cref="NpcVehiclePhysicsEnabled"/>.</summary>
    public static NpcVehicleControllerTier ControllerTier { get; set; } = DefaultControllerTier;

    /// <summary>
    /// Placeholder fixed physics substep rate (Hz). Clamped to [1, 480].
    /// Client retail uses frameDt/N via StepTo 0x4d6c80 (max ~1/30 s per substep), not a fixed Hz — see HkVehicleSubstep.
    /// </summary>
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

    /// <summary>
    /// Opt-in stability lever: clamp per-wheel suspension force magnitude to
    /// <c>HkPhysicsConstants.MaxSuspensionForce</c> in <c>HkVehicleSuspension.ComputeForce</c>.
    /// Retail (<c>hkDefaultSuspension::update</c> @ 0x64de50) is unclamped, so this defaults OFF;
    /// enable only as an emergency server-stability override (non-retail behaviour).
    /// </summary>
    public static bool SuspensionForceClampEnabled { get; set; } = DefaultSuspensionForceClampEnabled;

    /// <summary>
    /// When true, <c>NpcVehiclePhysicsController.BuildCollisionQuery</c> wraps the terrain
    /// heightfield in <c>CompositeVehicleCollisionQuery</c> so wheel casts also hit map-prop /
    /// vehicle proxy volumes. Defaults <b>OFF</b> — terrain-only until an operator opts in.
    /// Does not change the master physics opt-in (<see cref="NpcVehiclePhysicsEnabled"/>).
    /// </summary>
    public static bool CompositeWheelCollisionEnabled { get; set; } = DefaultCompositeWheelCollisionEnabled;

    /// <summary>
    /// When true, suspension and axle friction use <c>ApplyPointImpulse</c> at wheel contacts
    /// (retail r×F). Default false = COM-only linear application (live stability).
    /// </summary>
    public static bool ChassisPointImpulsesEnabled { get; set; } = DefaultChassisPointImpulsesEnabled;

    /// <summary>
    /// When true, moving vehicles damage collidable map props via <c>VehicleMapPropRam</c>.
    /// Defaults <b>OFF</b> until an operator opts in.
    /// </summary>
    public static bool EnableRamming { get; set; } = DefaultEnableRamming;

    /// <summary>Reset every setting to retail-safe defaults (tests + startup before load).</summary>
    public static void ResetToDefaults()
    {
        NpcVehiclePhysicsEnabled = DefaultNpcVehiclePhysicsEnabled;
        ControllerTier = DefaultControllerTier;
        _substepHz = DefaultSubstepHz;
        Gravity = DefaultGravity;
        AirDensityOverride = null;
        DebugLogging = DefaultDebugLogging;
        SuspensionForceClampEnabled = DefaultSuspensionForceClampEnabled;
        CompositeWheelCollisionEnabled = DefaultCompositeWheelCollisionEnabled;
        ChassisPointImpulsesEnabled = DefaultChassisPointImpulsesEnabled;
        EnableRamming = DefaultEnableRamming;
    }

    /// <summary>
    /// Effective vehicle mover for <c>NpcTicker</c>. Physics only when both
    /// <see cref="NpcVehiclePhysicsEnabled"/> and <see cref="ControllerTier"/> are Physics.
    /// Wire levers map onto kinematic/soft when ServerConfig stays Hard (back-compat).
    /// </summary>
    public static NpcVehicleControllerTier ResolveVehicleMoverTier()
    {
        if (NpcVehiclePhysicsEnabled && ControllerTier == NpcVehicleControllerTier.Physics)
            return NpcVehicleControllerTier.Physics;

        if (ControllerTier == NpcVehicleControllerTier.Kinematic)
            return NpcVehicleControllerTier.Kinematic;
        if (ControllerTier == NpcVehicleControllerTier.Soft)
            return NpcVehicleControllerTier.Soft;

        // Hard (or physics tier with enabled=false): honor legacy wire levers for tests/A/B.
        if (AutoCore.Game.Npc.NpcVehicleDriveController.Enabled)
            return NpcVehicleControllerTier.Kinematic;
        if (AutoCore.Game.Npc.SoftNpcPathMotion.Enabled)
            return NpcVehicleControllerTier.Soft;

        return NpcVehicleControllerTier.Hard;
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
                    $"ServerConfig: loaded {file} — tier={ControllerTier} enabled={NpcVehiclePhysicsEnabled} substepHz={SubstepHz} enableRamming={EnableRamming}");
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

        if (root?.EnableRamming.HasValue == true)
            EnableRamming = root.EnableRamming.Value;

        var p = root?.NpcVehiclePhysics;
        if (p == null)
            return true; // valid YAML, no npc section → keep physics defaults (enableRamming already applied)

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

        if (p.SuspensionForceClampEnabled.HasValue)
            SuspensionForceClampEnabled = p.SuspensionForceClampEnabled.Value;

        if (p.CompositeWheelCollisionEnabled.HasValue)
            CompositeWheelCollisionEnabled = p.CompositeWheelCollisionEnabled.Value;

        if (p.ChassisPointImpulsesEnabled.HasValue)
            ChassisPointImpulsesEnabled = p.ChassisPointImpulsesEnabled.Value;

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
        public bool? EnableRamming { get; set; }
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
        public bool? SuspensionForceClampEnabled { get; set; }
        public bool? CompositeWheelCollisionEnabled { get; set; }
        public bool? ChassisPointImpulsesEnabled { get; set; }
    }
}
