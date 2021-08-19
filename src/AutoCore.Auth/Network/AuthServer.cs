using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace AutoCore.Auth.Network
{
    using Communicator;
    using Communicator.Packets;
    using Config;
    using Data;
    using Database.Auth;
    using Database.Auth.Models;
    using Packets.Server;
    using Utils;
    using Utils.Config;
    using Utils.Commands;
    using Utils.Networking;
    using Utils.Server;
    using Utils.Threading;
    using Utils.Timer;

    public class AuthServer : BaseServer, ILoopable
    {
        public override string ServerType { get; } = "Authentication";

        public const int MainLoopTime = 100; // Milliseconds

        public Config Config { get; private set; }
        public Communicator Communicator { get; private set; }
        public LengthedSocket AuthCommunicator { get; private set; }
        public LengthedSocket ListenerSocket { get; private set; }
        public List<AuthClient> Clients { get; } = new();
       
        public List<ServerInfo> ServerList { get; } = new();
        public MainLoop Loop { get; }
        public Timer Timer { get; }
        public override bool IsRunning => Loop != null && Loop.Running;

        private readonly List<AuthClient> _clientsToRemove = new();
        private List<CommunicatorClient> GameServerQueue { get; } = new();
        private Dictionary<byte, CommunicatorClient> GameServers { get; } = new();

        public AuthServer()
        {
            Configuration.OnLoad += ConfigLoaded;
            Configuration.OnReLoad += ConfigReLoaded;
            Configuration.Load();

            Loop = new MainLoop(this, MainLoopTime);
            Timer = new Timer();

            SetupServerList();

            LengthedSocket.InitializeEventArgsPool(Config.SocketAsyncConfig.MaxClients * Config.SocketAsyncConfig.ConcurrentOperationsByClient);

            RegisterConsoleCommands();
        }

        ~AuthServer()
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
            var oldConfig = Config;

            Config = new Config();
            Configuration.Bind(Config);

            Logger.UpdateConfig(Config.LoggerConfig);

            AuthContext.InitializeConnectionString(Config.AuthDatabaseConnectionString);

            // Handle reloading the config and updating the list visibility
            if (oldConfig == null || oldConfig.AuthListType == Config.AuthListType)
                return;

            lock (ServerList)
            {
                ServerList.Clear();
                SetupServerList();
                GenerateServerList();
            }
        }
        #endregion

        public void Disconnect(AuthClient client)
        {
            lock (_clientsToRemove)
                _clientsToRemove.Add(client);
        }

        private void SetupServerList()
        {
            if (Config.AuthListType != AuthListType.All)
                return;

            foreach (var s in Config.Servers)
            {
                if (!byte.TryParse(s.Key, out byte id))
                    continue;

                ServerList.Add(new ServerInfo
                {
                    AgeLimit = 0,
                    CurrentPlayers = 0,
                    Port = 0,
                    Ip = IPAddress.None,
                    MaxPlayers = 0,
                    PKFlag = 0,
                    ServerId = id,
                    Status = 0
                });
            }
        }

        #region Socketing
        public bool Start()
        {
            // Check the server configuration
            if (Config.AuthConfig.Port == 0 || Config.AuthConfig.Backlog == 0)
            {
                Logger.WriteLog(LogType.Error, "Invalid config values!");
                return false;
            }

            // Check the communicator configuration
            if (Config.CommunicatorConfig.Port == 0 || Config.CommunicatorConfig.Address == null || Config.CommunicatorConfig.Backlog == 0)
            {
                Logger.WriteLog(LogType.Error, "Invalid Communicator config data!");
                return false;
            }

            // Set up the listener socket
            try
            {
                ListenerSocket = new LengthedSocket(SizeType.Word);
                ListenerSocket.OnError += OnError;
                ListenerSocket.OnAccept += OnAccept;
                ListenerSocket.Bind(new IPEndPoint(IPAddress.Any, Config.AuthConfig.Port));
                ListenerSocket.Listen(Config.AuthConfig.Backlog);
                ListenerSocket.AcceptAsync();

                Logger.WriteLog(LogType.Network, "*** Listening for clients on port {0}", Config.AuthConfig.Port);
            }
            catch (Exception e)
            {
                Logger.WriteLog(LogType.Error, "Unable to create or start listening on the client socket! Exception:");
                Logger.WriteLog(LogType.Error, e);

                return false;
            }

            // Set up communicator
            Communicator = new Communicator(CommunicatorType.Server, IPAddress.Parse(Config.CommunicatorConfig.Address), Config.CommunicatorConfig.Port, Config.CommunicatorConfig.Backlog);

            // Add the repeating server info request timed event
            Timer.Add("ServerInfoUpdate", 1000, true, () =>
            {
                Communicator.RequestServerInfo();
            });

            // Start the main loop
            Loop.Start();

            // TODO: Set up timed events (query stuff, internal communication, etc...)

            return true;
        }

        private static void OnError(SocketAsyncEventArgs args)
        {
            if (args.LastOperation == SocketAsyncOperation.Accept && args.AcceptSocket != null &&
                args.AcceptSocket.Connected)
                args.AcceptSocket.Shutdown(SocketShutdown.Both);
        }

        private void OnAccept(LengthedSocket newSocket)
        {
            ListenerSocket.AcceptAsync();

            if (newSocket == null)
                return;

            lock (Clients)
                Clients.Add(new AuthClient(newSocket, this));
        }
        #endregion

        #region Communicator
        public bool AuthenticateGameServer(LoginRequestPacket packet, CommunicatorClient client)
        {
            lock (GameServers)
            {
                if (GameServers.ContainsKey(packet.Data.Id))
                {
                    DisconnectCommunicator(client);
                    Logger.WriteLog(LogType.Debug, $"A server tried to connect to an already in use server slot! Remote Address: {client.Socket.RemoteAddress}");
                    return false;
                }

                if (!Config.Servers.ContainsKey(packet.Data.Id.ToString()))
                {
                    DisconnectCommunicator(client);
                    Logger.WriteLog(LogType.Debug, $"A server tried to connect to a non-defined server slot! Remote Address: {client.Socket.RemoteAddress}");
                    return false;
                }

                if (Config.Servers[packet.Data.Id.ToString()] != packet.Data.Password)
                {
                    DisconnectCommunicator(client);
                    Logger.WriteLog(LogType.Error, $"A server tried to log in with an invalid password! Remote Address: {client.Socket.RemoteAddress}");
                    return false;
                }

                GameServerQueue.Remove(client);
                GameServers.Add(packet.Data.Id, client);

                Logger.WriteLog(LogType.Network, $"The Game server (Id: {packet.Data.Id}, Address: {client.Socket.RemoteAddress}, Public Address: {packet.Data.Address}) has authenticated! Requesting info...");

                return true;
            }
        }

#pragma warning disable IDE0060 // Remove unused parameter
        public void UpdateServerInfo(CommunicatorClient client, ServerInfoResponsePacket packet)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            GenerateServerList();
            BroadcastServerList();
        }

        public void RedirectResponse(CommunicatorClient client, RedirectResponsePacket packet)
        {
            AuthClient authClient;
            lock (Clients)
                authClient = Clients.FirstOrDefault(c => c.Account.Id == packet.AccountId);

            ServerInfo info;
            lock (ServerList)
                info = ServerList.FirstOrDefault(i => i.ServerId == client.ServerId);

            if (authClient != null && info != null)
                authClient.RedirectionResult(packet.Result, info);
        }

        public void RequestRedirection(AuthClient client, byte serverId)
        {
            lock (GameServers)
                if (GameServers.ContainsKey(serverId))
                    GameServers[serverId].RequestRedirection(client);
        }

        public void DisconnectCommunicator(CommunicatorClient client)
        {
            if (client == null)
                return;

            lock (GameServers)
            {
                GameServerQueue.Remove(client);

                if (client.ServerId != 0)
                    GameServers.Remove(client.ServerId);

                GenerateServerList();
            }

            Timer.Add($"Disconnect-comm-{DateTime.Now.Ticks}", 1000, false, () =>
            {
                client.Socket?.Close();
            });

            Logger.WriteLog(LogType.Network, $"The game server (Id: {client.ServerId}, Address: {client.Socket.RemoteAddress}) has disconnected!");
        }

        private void GenerateServerList()
        {
            lock (ServerList)
            {
                ServerList.Clear();

                foreach (var server in GameServers)
                {
                    ServerList.Add(new ServerInfo
                    {
                        AgeLimit = server.Value.AgeLimit,
                        PKFlag = server.Value.PKFlag,
                        CurrentPlayers = server.Value.CurrentPlayers,
                        MaxPlayers = server.Value.MaxPlayers,
                        Port = server.Value.Port,
                        Ip = server.Value.PublicAddress,
                        ServerId = server.Key,
                        Status = 1
                    });
                }
            }
        }
        #endregion

        public void Shutdown()
        {
            ListenerSocket?.Close();
            ListenerSocket = null;

            Loop.Stop();
        }

        public void MainLoop(long delta)
        {
            Timer.Update(delta);

            if (Clients.Count == 0)
                return;

            lock (Clients)
            {
                foreach (var c in Clients)
                    c.Update(delta);

                if (_clientsToRemove.Count > 0)
                {
                    lock (_clientsToRemove)
                    {
                        foreach (var client in _clientsToRemove)
                            Clients.Remove(client);

                        _clientsToRemove.Clear();
                    }
                }
            }
        }

        public void BroadcastServerList()
        {
            lock (Clients)
                foreach (var c in Clients)
                    if (c.State == ClientState.ServerList)
                        c.SendPacket(new SendServerListExtPacket(ServerList, c.Account.LastServerId));
        }

        #region Commands
        private void RegisterConsoleCommands()
        {
            CommandProcessor.RegisterCommand("exit", ProcessExitCommand);
            CommandProcessor.RegisterCommand("reload", ProcessReloadCommand);
            CommandProcessor.RegisterCommand("create", ProcessCreateCommand);
        }

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

        private void ProcessCreateCommand(string[] parts)
        {
            if (parts.Length < 4)
            {
                Logger.WriteLog(LogType.Command, "Invalid create account command! Usage: create <email> <username> <password>");
                return;
            }

            var email = parts[1];
            var userName = parts[2];
            var password = parts[3];

            try
            {
                using (var context = new AuthContext())
                {
                    var salt = Account.CreateSalt();

                    context.Accounts.Add(new Account
                    {
                        Email = email,
                        Username = userName,
                        Password = Account.Hash(password ?? string.Empty, salt),
                        Salt = salt
                    });
                    context.SaveChanges();
                }

                Logger.WriteLog(LogType.Command, $"Created account: {parts[2]}! (Password: {parts[3]})");
            }
            catch
            {
                Logger.WriteLog(LogType.Error, "Username or email is already taken!");
            }
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
