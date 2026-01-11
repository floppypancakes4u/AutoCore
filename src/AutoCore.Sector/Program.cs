using System.Diagnostics;

namespace AutoCore.Sector;

using AutoCore.Database.Char;
using AutoCore.Database.World;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Sector.Config;
using AutoCore.Sector.Network;
using AutoCore.Utils;
using Microsoft.Extensions.Configuration;

public class Program : ExitableProgram
{
    private static SectorServer Server { get; } = new();

    public static void Main()
    {
        Initialize(ExitHandlerProc);

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.sector.json")
            .AddJsonFile("appsettings.sector.env.json", true);

        var config = new SectorConfig();
        var configRoot = builder.Build();
        configRoot.Bind(config);

        CharContext.InitializeConnectionString(config.CharDatabaseConnectionString);
        WorldContext.InitializeConnectionString(config.WorldDatabaseConnectionString);

        CharContext.EnsureCreated();
        WorldContext.EnsureCreated();

        Server.InitConsole();
        Server.Setup(config);

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

    private static bool ExitHandlerProc(byte sig)
    {
        Logger.WriteLog(LogType.Error, "Shutting down the server...");

        Server.Shutdown();

        Logger.WriteLog(LogType.Error, "Server shutdown completed!");

        Logger.WriteLog(LogType.Error, "Press any key to exit...");

        return false;
    }
}
