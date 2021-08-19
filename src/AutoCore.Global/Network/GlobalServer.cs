using System;
using System.Collections.Generic;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace AutoCore.Global.Network
{
    using Communicator;
    using Communicator.Packets;
    using Database.Char;
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
        public LengthedSocket AuthCommunicator { get; private set; }
        //public Dictionary<uint, LoginAccountEntry> IncomingClients { get; } = new();
        public MainLoop Loop { get; }
        public Timer Timer { get; } = new();
        public override bool IsRunning => Loop != null && Loop.Running;
        public TNLInterface Interface { get; private set; }
        private readonly object _interfaceLock = new();

        private readonly PacketRouter<GlobalServer, CommunicatorOpcode> _router = new();

        public GlobalServer()
        {
            Logger.WriteLog(LogType.Initialize, "+++ Initializing Server for Global");

            Configuration.OnLoad += ConfigLoaded;
            Configuration.OnReLoad += ConfigReLoaded;
            Configuration.Load();

            WorldContext.InitializeConnectionString(""); // TODo

            TNLInterface.RegisterNetClassReps();

            lock (_interfaceLock)
                Interface = new TNLInterface(Config.GameConfig.Port, true, 175, false);

            Loop = new MainLoop(this, MainLoopTime);

            PublicAddress = IPAddress.Parse(Config.GameConfig.PublicAddress);

            LengthedSocket.InitializeEventArgsPool(Config.SocketAsyncConfig.MaxClients * Config.SocketAsyncConfig.ConcurrentOperationsByClient);

            CommandProcessor.RegisterCommand("exit", ProcessExitCommand);
            CommandProcessor.RegisterCommand("reload", ProcessReloadCommand);
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

        #region Socketing
        public bool Start()
        {
            // If no config file has been found, these values are 0 by default
            if (Config.GameConfig.Port == 0 || Config.GameConfig.Backlog == 0)
            {
                Logger.WriteLog(LogType.Error, "Invalid config values!");
                return false;
            }

            Loop.Start();

            SetupCommunicator();

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
        #endregion

        #region Communicator
        private void SetupCommunicator()
        {
            if (Config.CommunicatorConfig.Port == 0 || Config.CommunicatorConfig.Address == null)
            {
                Logger.WriteLog(LogType.Error, "Invalid Communicator config data! Can't connect!");
                return;
            }

            ConnectCommunicator();
        }

        public void ConnectCommunicator()
        {
            if (AuthCommunicator?.Connected ?? false)
                AuthCommunicator?.Close();

            try
            {
                AuthCommunicator = new LengthedSocket(SizeType.Word);
                AuthCommunicator.OnConnect += OnCommunicatorConnect;
                AuthCommunicator.OnError += OnCommunicatorError;
                AuthCommunicator.ConnectAsync(new IPEndPoint(IPAddress.Parse(Config.CommunicatorConfig.Address), Config.CommunicatorConfig.Port));
            }
            catch (Exception e)
            {
                Logger.WriteLog(LogType.Error, "Unable to create or start listening on the Auth server socket! Retrying soon... Exception:");
                Logger.WriteLog(LogType.Error, e);
            }

            Logger.WriteLog(LogType.Network, $"*** Connecting to auth server! Address: {Config.CommunicatorConfig.Address}:{Config.CommunicatorConfig.Port}");
        }

        private void OnCommunicatorError(SocketAsyncEventArgs args)
        {
            Timer.Add("CommReconnect", 10000, false, () =>
            {
                if (!AuthCommunicator?.Connected ?? true)
                    ConnectCommunicator();
            });

            Logger.WriteLog(LogType.Error, "Could not connect to the Auth server! Trying again in a few seconds...");
        }

        private void OnCommunicatorConnect(SocketAsyncEventArgs args)
        {
            if (args.SocketError != SocketError.Success)
            {
                OnCommunicatorError(args);
                return;
            }

            Logger.WriteLog(LogType.Network, "*** Connected to the Auth Server!");

            AuthCommunicator.OnReceive += OnCommunicatorReceive;
            SendAuthCommunicatorPacket(new LoginRequestPacket(new()
            {
                Id = Config.ServerInfoConfig.Id,
                Password = Config.ServerInfoConfig.Password,
                Address = PublicAddress
            }));

            AuthCommunicator.ReceiveAsync();
        }

        private void OnCommunicatorReceive(byte[] data, int length)
        {
            var reader = new BinaryReader(new MemoryStream(data, 0, length, false));
            var opcode = (CommunicatorOpcode)reader.ReadByte();

            var packetType = _router.GetPacketType(opcode);
            if (packetType == null)
                return;

            if (Activator.CreateInstance(packetType) is not IOpcodedPacket<CommunicatorOpcode> packet)
                return;

            packet.Read(reader);

            _router.RoutePacket(this, packet);

            AuthCommunicator.ReceiveAsync();
        }

        private void SendAuthCommunicatorPacket(IOpcodedPacket<CommunicatorOpcode> packet)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(SendBufferSize);
            var writer = new BinaryWriter(new MemoryStream(buffer, true));

            packet.Write(writer);

            AuthCommunicator.Send(buffer, 0, (int)writer.BaseStream.Position);

            ArrayPool<byte>.Shared.Return(buffer);
        }

        [PacketHandler(CommunicatorOpcode.LoginResponse)]
        private void MsgLoginResponse(LoginResponsePacket packet)
        {
            if (packet.Result == CommunicatorActionResult.Success)
            {
                Logger.WriteLog(LogType.Network, "Successfully authenticated with the Auth server!");
                return;
            }

            AuthCommunicator?.Close();
            AuthCommunicator = null;

            Logger.WriteLog(LogType.Error, "Could not authenticate with the Auth server! Shutting down internal communication!");
        }

        [PacketHandler(CommunicatorOpcode.ServerInfoRequest)]
        private void MsgGameInfoRequest(ServerInfoRequestPacket packet)
        {
            SendAuthCommunicatorPacket(new ServerInfoResponsePacket(new()
            {
                AgeLimit = Config.ServerInfoConfig.AgeLimit,
                PKFlag = Config.ServerInfoConfig.PKFlag,
                CurrentPlayers = 0,
                Port = Config.GameConfig.Port,
                MaxPlayers = (ushort)Config.SocketAsyncConfig.MaxClients
            }));
        }

        [PacketHandler(CommunicatorOpcode.RedirectRequest)]
        private void MsgRedirectRequest(RedirectRequestPacket packet)
        {
            /*lock (IncomingClients)
            {
                if (IncomingClients.ContainsKey(packet.Request.AccountId))
                    IncomingClients.Remove(packet.Request.AccountId);

                IncomingClients.Add(packet.Request.AccountId, new LoginAccountEntry(packet));
            }*/

            SendAuthCommunicatorPacket(new RedirectResponsePacket
            {
                AccountId = packet.Request.AccountId,
                Result = CommunicatorActionResult.Success
            });
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
