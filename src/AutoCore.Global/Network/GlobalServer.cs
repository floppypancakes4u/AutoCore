using System.Net;

namespace AutoCore.Global.Network;

using AutoCore.Communicator;
using AutoCore.Global.Config;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.TNL;
using AutoCore.Utils;
using AutoCore.Utils.Commands;
using AutoCore.Utils.Server;
using AutoCore.Utils.Threading;
using AutoCore.Utils.Timer;

//using EnumServerType = AutoCore.Game.Constants.ServerType;

public class GlobalServer : BaseServer, ILoopable
{
    public const int MainLoopTime = 100; // Milliseconds
    public const int SendBufferSize = 512;

    public GlobalConfig Config { get; private set; } = new();
    public IPAddress PublicAddress { get; private set; }
    public Communicator AuthCommunicator { get; private set; }
    public MainLoop Loop { get; }
    public Timer Timer { get; } = new();
    public override bool IsRunning => Loop != null && Loop.Running;
    public TNLInterface Interface { get; private set; }
    private readonly object _interfaceLock = new();

    public GlobalServer()
        : base("Global")
    {
        TNLInterface.RegisterNetClassReps();

        Loop = new MainLoop(this, MainLoopTime);

        CommandProcessor.RegisterCommand("exit", ProcessExitCommand);
    }

    ~GlobalServer() => Shutdown();

    public void Setup(GlobalConfig config)
    {
        Logger.WriteLog(LogType.Initialize, "Setting up the Global server...");

        if (config != null)
            Config = config;

        Logger.WriteLog(LogType.Initialize, "Initializing the TNL interface...");
        Interface = new TNLInterface(Config.GameConfig.Port, true, 175, false);

        if (!AssetManager.Instance.Initialize(Config.GamePath, ServerType.Global))
            throw new Exception("Unable to load assets!");

        if (!MapManager.Instance.Initialize())
            throw new Exception("Unable to load maps!");

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

            Interface.CheckIncomingPackets();
            Interface.ProcessConnections();
        }

        LoginManager.Instance.Update(delta);
    }

    public bool Start()
    {
        Logger.WriteLog(LogType.Initialize, "Starting the Global server...");

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

        AssetManager.Instance.LoadAllData();

        Loop.Start();

        ConnectCommunicator();

        Logger.WriteLog(LogType.Initialize, "The Global server has been started!");
        Logger.WriteLog(LogType.Network, "*** Listening for clients on port {0}", Config.GameConfig.Port);

        return true;
    }

    public void Shutdown()
    {
        Logger.WriteLog(LogType.None, "Shutting down the server...");

        AuthCommunicator?.Close();
        AuthCommunicator = null;

        lock (_interfaceLock)
        {
            Interface.Close();
            Interface = null;
        }

        Loop.Stop();

        Logger.WriteLog(LogType.None, "The server was shut down!");
    }

    #region Communicator
    public void ConnectCommunicator()
    {
        if (AuthCommunicator?.Connected ?? false)
        {
            AuthCommunicator?.Close();
            AuthCommunicator = null;
        }

        try
        {
            AuthCommunicator = new Communicator(CommunicatorType.Client);
            AuthCommunicator.OnConnect += OnCommunicatorConnect;
            AuthCommunicator.OnError += OnCommunicatorError;
            AuthCommunicator.OnLoginResponse += OnCommunicatorLoginResponse;
            AuthCommunicator.OnRedirectRequest += OnCommunicatorRedirectRequest;
            AuthCommunicator.OnServerInfoRequest += OnCommunicatorServerInfoRequest;
            AuthCommunicator.Start(IPAddress.Parse(Config.CommunicatorAddress), Config.CommunicatorPort);
        }
        catch (Exception e)
        {
            Logger.WriteLog(LogType.Error, "Unable to create or start listening on the Auth server socket! Retrying soon... Exception:");
            Logger.WriteLog(LogType.Error, e);
        }

        Logger.WriteLog(LogType.Communicator, $"Connecting to auth server! Address: {Config.CommunicatorAddress}:{Config.CommunicatorPort}");
    }

    private void OnCommunicatorError()
    {
        Timer.Add("CommReconnect", 10000, false, () =>
        {
            if (!AuthCommunicator?.Connected ?? true)
                ConnectCommunicator();
        });

        Logger.WriteLog(LogType.Error, "Could not connect to the Auth server! Trying again in a few seconds...");
    }

    private void OnCommunicatorConnect(ServerData info)
    {
        info.Id = Config.ServerInfoConfig.Id;
        info.Address = PublicAddress;
        info.Password = Config.ServerInfoConfig.Password;
    }

    private void OnCommunicatorLoginResponse(CommunicatorActionResult result)
    {
        if (result == CommunicatorActionResult.Success)
        {
            Logger.WriteLog(LogType.Communicator, "Successfully logged in to the Auth server!");
            return;
        }

        AuthCommunicator?.Close();
        AuthCommunicator = null;

        Logger.WriteLog(LogType.Error, "Could not authenticate with the Auth server! Shutting down internal communication!");
    }

    private bool OnCommunicatorRedirectRequest(RedirectRequest request)
    {
        return LoginManager.Instance.ExpectLoginToGlobal(request.AccountId, request.Username, request.OneTimeKey);
    }

    private void OnCommunicatorServerInfoRequest(ServerInfo info)
    {
        info.AgeLimit = Config.ServerInfoConfig.AgeLimit;
        info.PKFlag = Config.ServerInfoConfig.PKFlag;
        info.CurrentPlayers = 0;
        info.Port = Config.GameConfig.Port;
        info.MaxPlayers = Config.ServerInfoConfig.MaxPlayers;
    }
    #endregion

    #region Commands
    private void ProcessExitCommand(string[] parts)
    {
        var minutes = 0;

        if (parts.Length > 1)
            minutes = int.Parse(parts[1]);

        Timer.Add("exit", minutes * 60000, false, () =>
        {
            Shutdown();
        });

        Logger.WriteLog(LogType.Command, $"Exiting the server in {minutes} minute(s).");
    }

    /*private void ProcessRestartCommand(string[] parts)
    {
        // TODO: delayed restart, with contacting globals, so they can warn players not to leave the server, or they won't be able to reconnect
    }

    private void ProcessShutdownCommand(string[] parts)
    {
        // TODO: delayed shutdown, with contacting globals, so they can warn players not to leave the server, or they won't be able to reconnect
        // TODO: add timer to report the remaining time until shutdown?
        // TODO: add timer to contact global servers to tell them periodically that we're getting shut down?
    }*/
    #endregion
}
