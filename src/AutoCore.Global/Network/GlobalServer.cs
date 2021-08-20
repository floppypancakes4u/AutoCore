using System;
using System.Collections.Generic;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace AutoCore.Global.Network
{
    using Communicator;
    using Communicator.Packets;
    using Database.Char;
    using Database.World;
    using Config;
    using Game.TNL;
    using Utils;
    using Utils.Config;
    using Utils.Commands;
    using Utils.Networking;
    using Utils.Packets;
    using Utils.Server;
    using Utils.Threading;
    using Utils.Timer;

    public class GlobalServer : BaseServer, ILoopable
    {
        public override string ServerType { get; } = "Global";

        public const int MainLoopTime = 100; // Milliseconds
        public const int SendBufferSize = 512;

        public Config Config { get; private set; }
        public IPAddress PublicAddress { get; }
        //public LengthedSocket AuthCommunicator { get; private set; }
        public Communicator AuthCommunicator { get; private set; }
        //public Dictionary<uint, LoginAccountEntry> IncomingClients { get; } = new();
        public MainLoop Loop { get; }
        public Timer Timer { get; } = new();
        public override bool IsRunning => Loop != null && Loop.Running;
        public TNLInterface Interface { get; private set; }
        private readonly object _interfaceLock = new();

        public GlobalServer()
        {
            Configuration.OnLoad += ConfigLoaded;
            Configuration.OnReLoad += ConfigReLoaded;
            Configuration.Load();

            Logger.WriteLog(LogType.Initialize, "Initializing the Global server...");

            TNLInterface.RegisterNetClassReps();

            lock (_interfaceLock)
                Interface = new TNLInterface(Config.GameConfig.Port, true, 175, false);

            Loop = new MainLoop(this, MainLoopTime);

            PublicAddress = IPAddress.Parse(Config.GameConfig.PublicAddress);

            CharContext.InitializeConnectionString(Config.CharDatabaseConnectionString);
            WorldContext.InitializeConnectionString(Config.WorldDatabaseConnectionString);

            LengthedSocket.InitializeEventArgsPool(Config.SocketAsyncConfig.MaxClients * Config.SocketAsyncConfig.ConcurrentOperationsByClient);

            CommandProcessor.RegisterCommand("exit", ProcessExitCommand);
            CommandProcessor.RegisterCommand("reload", ProcessReloadCommand);

            Logger.WriteLog(LogType.Initialize, "The Global server has been initialized!");
        }

        ~GlobalServer()
        {
            Shutdown();
        }

        #region Configuration
        private static void ConfigReLoaded()
        {
            Logger.WriteLog(LogType.Initialize, "Config file reloaded by external change!");

            // Totally reload the configuration, because it's automatic reload case can only handle one reload. Our code's bug?
            Configuration.Load();
        }

        private void ConfigLoaded()
        {
            Config = new Config();
            Configuration.Bind(Config);

            Logger.UpdateConfig(Config.LoggerConfig);
        }
        #endregion

        public void MainLoop(long delta)
        {
            Timer.Update(delta);

            lock (_interfaceLock)
            {
                if (Interface == null)
                    return;

                Interface.CheckIncomingPackets();
                Interface.ProcessConnections();
            }
        }

        public bool Start()
        {
            // If no config file has been found, these values are 0 by default
            if (Config.GameConfig.Port == 0 || Config.GameConfig.Backlog == 0)
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

            Timer.Add("SessionExpire", 10000, true, () =>
            {
                var toRemove = new List<uint>();

                /*lock (IncomingClients)
                {
                    toRemove.AddRange(IncomingClients.Where(ic => ic.Value.ExpireTime < DateTime.Now).Select(ic => ic.Key));

                    foreach (var rem in toRemove)
                        IncomingClients.Remove(rem);
                }*/
            });

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
            Logger.WriteLog(LogType.Communicator, "Logging in to the auth server...");

            info.Id = Config.ServerInfoConfig.Id;
            info.Address = PublicAddress;
            info.Password = Config.ServerInfoConfig.Password;
        }

        private void OnCommunicatorLoginResponse(CommunicatorActionResult result)
        {
            if (result == CommunicatorActionResult.Success)
                return;

            AuthCommunicator?.Close();
            AuthCommunicator = null;

            Logger.WriteLog(LogType.Error, "Could not authenticate with the Auth server! Shutting down internal communication!");
        }

        private bool OnCommunicatorRedirectRequest(RedirectRequest request)
        {
            /*lock (IncomingClients)
            {
                if (IncomingClients.ContainsKey(packet.Request.AccountId))
                    IncomingClients.Remove(packet.Request.AccountId);

                IncomingClients.Add(packet.Request.AccountId, new LoginAccountEntry(packet));
            }*/

            return true;
        }

        private void OnCommunicatorServerInfoRequest(ServerInfo info)
        {
            info.AgeLimit = Config.ServerInfoConfig.AgeLimit;
            info.PKFlag = Config.ServerInfoConfig.PKFlag;
            info.CurrentPlayers = 0;
            info.Port = Config.GameConfig.Port;
            info.MaxPlayers = (ushort)Config.SocketAsyncConfig.MaxClients;
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

        private static void ProcessReloadCommand(string[] parts)
        {
            if (parts.Length > 1 && parts[1] == "config")
            {
                Configuration.Load();
                return;
            }

            Logger.WriteLog(LogType.Command, "Invalid reload command!");
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
}
