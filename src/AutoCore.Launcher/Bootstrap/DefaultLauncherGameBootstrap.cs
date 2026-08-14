using System.Diagnostics.CodeAnalysis;

namespace AutoCore.Launcher.Bootstrap;

using AutoCore.Auth.Config;
using AutoCore.Database.Auth;
using AutoCore.Database.Char;
using AutoCore.Database.World;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Global.Config;
using AutoCore.Utils;

/// <summary>
/// Production implementation of game/DB bootstrap used by <see cref="Program"/>.
/// </summary>
public sealed class DefaultLauncherGameBootstrap : ILauncherGameBootstrap
{
    public void InitializeConnectionStrings(AuthConfig auth, GlobalConfig global)
    {
        AuthContext.InitializeConnectionString(auth.AuthDatabaseConnectionString);
        CharContext.InitializeConnectionString(global.CharDatabaseConnectionString);
        WorldContext.InitializeConnectionString(global.WorldDatabaseConnectionString);

        // SS-20: bootstrap admin password comes from configuration; there is no built-in default.
        AuthContext.DefaultAdminPassword = auth.DefaultAdminPassword;
    }

    [ExcludeFromCodeCoverage(Justification = "Live MySQL EnsureCreated; orchestrator/DB contract covered via fakes + Database.Tests.")]
    public void EnsureDatabasesCreated()
    {
        AuthContext.EnsureCreated();
        CharContext.EnsureCreated();
        WorldContext.EnsureCreated();
    }

    [ExcludeFromCodeCoverage(Justification = "Live asset path I/O; AssetManager covered by Game.Tests with test seams.")]
    public bool InitializeAssetManager(string gamePath, bool allowMissingCbid)
        => AssetManager.Instance.Initialize(gamePath, ServerType.Both, allowMissingCbid);

    [ExcludeFromCodeCoverage(Justification = "Live WAD/GLM asset load; AssetManager load paths covered by Game.Tests.")]
    public bool LoadAllAssetData()
        => AssetManager.Instance.LoadAllData();

    public void ApplyEarlyRuntimeConfig()
    {
        AutoCore.Game.Diagnostics.LootTuning.ApplyFromConfigFiles();
        AutoCore.Game.Diagnostics.ServerConfig.ApplyFromConfigFiles();
    }

    public void InitializeLootManager()
        => LootManager.Instance.Initialize();

    public bool InitializeMapManager()
    {
        // Per-player instancing for the starting areas (698/707/708). The Launcher hosts the
        // sector in-process and never runs AutoCore.Sector/Program.cs, so it must enable this
        // itself — otherwise those maps silently stay shared and players see each other.
        // Safe in the combined process: Global's new-character path uses GetMap (shared copy),
        // only the sector entry paths call GetMapForCharacter.
        AutoCore.Game.Map.InstancedContinents.EnableForSector();

        if (MapManager.Instance.Initialize())
            return true;

        Logger.WriteLog(LogType.Error, "MapManager initialization failed. Continuing anyway.");
        return false;
    }

    public void ApplyLateRuntimeConfig()
    {
        AutoCore.Game.Diagnostics.WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles();
        // After wire levers so log.filters.json can quiet WireDiag / GhostObjectDiag without rebuild.
        AutoCore.Game.Diagnostics.LogFilters.ApplyFromConfigFiles();
        // ...but an explicit env override outranks the file, or AUTOCORE_WIRE_DIAG is dead here.
        AutoCore.Game.Diagnostics.WireIsolationLevers.ApplyEnvironmentDiagOverrides();
    }
}
