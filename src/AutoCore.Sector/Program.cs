using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AutoCore.Sector;

using AutoCore.Database.Auth;
using AutoCore.Database.Char;
using AutoCore.Database.World;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Managers;
using AutoCore.Sector.Config;
using AutoCore.Sector.Network;
using AutoCore.Utils;
using AutoCore.Utils.Reliability;
using Microsoft.Extensions.Configuration;

public class Program : ExitableProgram
{
    private static SectorServer Server { get; } = new();

    /// <summary>
    /// Process host entry: binds ports, MySQL, and assets. Config validation is covered by
    /// <see cref="SectorConfigValidation"/> unit tests; live Main is a deliberate §4 exclusion.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Process host entry — binds shared ports/DB; validated via SectorConfigValidation.")]
    public static int Main()
    {
        // SS-07: register last-resort diagnostics before anything can fail.
        CrashHandler.Install("Sector");

        try
        {
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            // Startup failures (invalid config, DB unreachable, missing assets) are genuinely
            // unrecoverable and must still terminate — but diagnosably.
            Logger.WriteException(LogType.Fatal, "Sector server startup", ex);
            return 1;
        }
    }

    private static void Run()
    {
        Initialize(ExitHandlerProc);

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.sector.json")
            .AddJsonFile("appsettings.sector.env.json", true);

        var configRoot = builder.Build();
        var config = SectorConfigValidation.Bind(configRoot);
        SectorConfigValidation.Validate(config);

        // SS-07: Sector declared LoggerConfig but never applied it, so its configured file
        // logging was silently inert and crash diagnostics went to the console only.
        Logger.UpdateConfig(config.LoggerConfig);

        CharContext.InitializeConnectionString(config.CharDatabaseConnectionString);
        WorldContext.InitializeConnectionString(config.WorldDatabaseConnectionString);
        if (!string.IsNullOrWhiteSpace(config.AuthDatabaseConnectionString))
            AuthContext.InitializeConnectionString(config.AuthDatabaseConnectionString);

        CharContext.EnsureCreated();
        WorldContext.EnsureCreated();

        Server.InitConsole();
        Server.Setup(config);

        WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles();
        LogFilters.ApplyFromConfigFiles();
        // An explicit AUTOCORE_WIRE_DIAG / AUTOCORE_GHOST_OBJECT_DIAG outranks log.filters.json.
        WireIsolationLevers.ApplyEnvironmentDiagOverrides();

        if (!AssetManager.Instance.Initialize(config.GamePath, ServerType.Sector, false))
        {
            Logger.WriteLog(LogType.Error, "Unable to initialize Asset Manager! Check the GamePath configuration.");
            throw new Exception("Unable to initialize Asset Manager!");
        }

        if (!AssetManager.Instance.LoadAllData())
        {
            Logger.WriteLog(LogType.Error, "Critical asset loading failed! Cannot continue without WAD or GLM files.");
            throw new Exception("Critical asset loading failed!");
        }

        // Loot rate from loot.tuning.json (1.0 = retail; higher = more drops).
        LootTuning.ApplyFromConfigFiles();

        // Server tuning from serverConfig.yaml (NPC vehicle physics, etc.).
        ServerConfig.ApplyFromConfigFiles();

        // Initialize the loot manager (builds item index from CloneBase data)
        LootManager.Instance.Initialize();

        // Per-player instancing for the starting areas (698/707/708). Sector only — Global
        // must never allocate instances (its maps only seed new-character LastTownId/pose).
        AutoCore.Game.Map.InstancedContinents.EnableForSector();

        if (!MapManager.Instance.Initialize())
        {
            Logger.WriteLog(LogType.Error, "MapManager initialization failed. Continuing anyway.");
        }

        if (!Server.Start())
        {
            Logger.WriteLog(LogType.Error, "Unable to start the server!");

            return;
        }

        Server.ProcessCommands();

        GC.Collect();

        Process.GetCurrentProcess().WaitForExit();
    }

    [ExcludeFromCodeCoverage(Justification = "Process-exit handler tied to live Server singleton.")]
    private static bool ExitHandlerProc(byte sig)
    {
        Logger.WriteLog(LogType.Initialize, "Shutting down the server...");

        // SS-07: this runs on the console control-handler thread. An exception escaping here
        // is unhandled and would turn an orderly shutdown into a crash.
        Guard.Run("Sector server shutdown", Server.Shutdown);

        Logger.WriteLog(LogType.Initialize, "Server shutdown completed!");

        Logger.WriteLog(LogType.Error, "Press any key to exit...");

        return false;
    }
}
