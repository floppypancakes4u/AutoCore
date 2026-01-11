using System.Net;

namespace AutoCore.Global.Network;

using AutoCore.Communicator;
using AutoCore.Global.Config;
using AutoCore.Game.Managers;
using AutoCore.Game.TNL;
using AutoCore.Utils;
using AutoCore.Utils.Server;
using AutoCore.Utils.Threading;
using AutoCore.Utils.Timer;

public partial class GlobalServer : BaseServer, ILoopable
{
    public const int MainLoopTime = 100; // Milliseconds
    public const int SendBufferSize = 512;

    public GlobalConfig Config { get; private set; } = new();
    public IPAddress PublicAddress { get; private set; }
    public Communicator AuthCommunicator { get; } = new(CommunicatorType.Client);
    public MainLoop Loop { get; }
    public Timer Timer { get; } = new();
    public override bool IsRunning => Loop != null && Loop.Running;
    public TNLInterface Interface { get; private set; }
    private readonly object _interfaceLock = new();

    public GlobalServer()
        : base("Global")
    {
        Loop = new MainLoop(this, MainLoopTime);

        RegisterCommands();
    }

    ~GlobalServer() => Shutdown();

    public void Setup(GlobalConfig config)
    {
        Logger.WriteLog(LogType.Initialize, "Setting up the Global server...");

        if (config != null)
            Config = config;

        Logger.WriteLog(LogType.Initialize, "Initializing the TNL interface...");
        Interface = new TNLInterface(Config.GameConfig.Port, false)
        {
            AllowVersionMismatch = Config.GameConfig.AllowVersionMismatch,
            ExpectedVersion = Config.GameConfig.ExpectedVersion > 0 ? Config.GameConfig.ExpectedVersion : TNLInterface.Version
        };

        Logger.WriteLog(LogType.Initialize, "Initializing the network...");
        PublicAddress = IPAddress.Parse(Config.GameConfig.PublicAddress);

        Logger.WriteLog(LogType.Initialize, "The Global server has been setup!");
    }

    public void MainLoop(long delta)
    {
        Timer.Update(delta);

        if (Interface == null)
            return;

        lock (_interfaceLock)
        {
            if (Interface == null)
                return;

            Interface.Pulse();
        }

        LoginManager.Instance.Update(delta);
    }

    public bool Start()
    {
        // If no config file has been found, these values are 0 by default
        if (Config.GameConfig.Port == 0)
        {
            Logger.WriteLog(LogType.Error, "Invalid config values!");
            return false;
        }

        if (Config.CommunicatorPort == 0 || Config.CommunicatorAddress == null)
        {
            Logger.WriteLog(LogType.Error, "Invalid Communicator config data! Can't connect!");
            return false;
        }

        Loop.Start();

        ConnectCommunicator();

        Logger.WriteLog(LogType.Network, "*** Listening for clients on port {0}", Config.GameConfig.Port);

        return true;
    }

    public void Shutdown()
    {
        Logger.WriteLog(LogType.None, "Shutting down the server...");

        CloseCommunicator();

        lock (_interfaceLock)
        {
            Interface.Close();
            Interface = null;
        }

        Loop.Stop();

        Logger.WriteLog(LogType.None, "The server was shut down!");
    }
}
