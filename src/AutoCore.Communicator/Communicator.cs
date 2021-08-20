using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace AutoCore.Communicator
{
    using Utils;
    using Utils.Networking;
    using Utils.Packets;
    using Packets;

    public enum CommunicatorType
    {
        Server,
        ServerClient,
        Client
    }

    public enum CommunicatorOpcode : byte
    {
        LoginRequest       = 0,
        LoginResponse      = 1,
        RedirectRequest    = 2,
        RedirectResponse   = 3,
        ServerInfoRequest  = 4,
        ServerInfoResponse = 5
    }

    public enum CommunicatorActionResult : byte
    {
        Success = 0,
        Failure = 1
    }

    public class Communicator
    {
        public const int SendBufferSize = 512;
        public const SizeType CommunicatorHeaderLen = SizeType.Word;
        public const double ServerInfoUpdateIntervalMs = 30000.0d;

        public CommunicatorType Type { get; }
        public LengthedSocket Socket { get; private set; }
        public List<Communicator> AuthenticatingChildren { get; }
        public Dictionary<byte, Communicator> Clients { get; }
        public List<byte> ToRemoveClients { get; }
        public DateTime LastRequestTime { get; private set; }
        public ServerData ServerData { get; private set; }
        public ServerInfo ServerInfo { get; private set; }
        public bool Connected => Socket?.Connected ?? false;

        private Communicator Server { get; }

        public Action OnError { get; set; }
        public Action<ServerData> OnConnect { get; set; }
        public Func<Communicator, LoginRequestPacket, bool> OnLoginRequest { get; set; }
        public Action<CommunicatorActionResult> OnLoginResponse { get; set; }
        public Func<RedirectRequest, bool> OnRedirectRequest { get; set; }
        public Action<Communicator, RedirectResponsePacket> OnRedirectResponse { get; set; }
        public Action<ServerInfo> OnServerInfoRequest { get; set; }
        public Action OnServerInfoResponse { get; set; }

        public Communicator(CommunicatorType type)
        {
            if (type == CommunicatorType.ServerClient)
                throw new ArgumentOutOfRangeException(nameof(type));

            Type = type;

            Socket = new LengthedSocket(CommunicatorHeaderLen);
            Socket.OnError += OnSocketError;

            switch (Type)
            {
                case CommunicatorType.Server:
                    AuthenticatingChildren = new();
                    Clients = new();
                    ToRemoveClients = new();

                    Socket.OnAccept += OnSocketAccept;
                    break;

                case CommunicatorType.Client:
                    Socket.OnReceive += OnSocketReceive;
                    Socket.OnConnect += OnSocketConnect;
                    break;
            }
        }

        public Communicator(LengthedSocket socket, Communicator server)
        {
            Type = CommunicatorType.ServerClient;
            Server = server;

            Socket = socket;
            Socket.OnReceive += OnSocketReceive;

            Socket.ReceiveAsync();
        }

        public void Start(IPAddress address, int port, int backlog = 0)
        {
            switch (Type)
            {
                case CommunicatorType.Server:
                    Socket.Bind(new IPEndPoint(address, port));
                    Socket.Listen(backlog);
                    Socket.AcceptAsync();
                    break;

                case CommunicatorType.Client:
                    Socket.ConnectAsync(new IPEndPoint(address, port));
                    break;
            }
        }

        public void Update()
        {
            lock (ToRemoveClients)
            {
                foreach (var id in ToRemoveClients)
                {
                    if (Clients.TryGetValue(id, out var comm))
                    {
                        comm.Close();

                        Clients.Remove(id);
                    }
                }

                ToRemoveClients.Clear();
            }
        }
        
        private void ClientAucthenticated(Communicator client)
        {
            if (Type != CommunicatorType.Server)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can't have clients!");
                return;
            }

            Clients.Add(client.ServerData.Id, client);
            AuthenticatingChildren.Remove(client);
        }

        #region Socketing
        private void OnSocketError(SocketAsyncEventArgs args)
        {
            Socket.Close();

            OnError?.Invoke();

            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has encountered an error!");
        }

        private void OnSocketAccept(LengthedSocket socket)
        {
            AuthenticatingChildren.Add(new Communicator(socket, this));

            Socket.AcceptAsync();
        }

        private void OnSocketReceive(byte[] buffer, int length)
        {
            using var br = new BinaryReader(new MemoryStream(buffer, 0, length, false));

            var opcode = (CommunicatorOpcode)br.ReadByte();

            IOpcodedPacket<CommunicatorOpcode> packet = opcode switch
            {
                CommunicatorOpcode.LoginRequest       => new LoginRequestPacket(),
                CommunicatorOpcode.LoginResponse      => new LoginResponsePacket(),
                CommunicatorOpcode.RedirectRequest    => new RedirectRequestPacket(),
                CommunicatorOpcode.RedirectResponse   => new RedirectResponsePacket(),
                CommunicatorOpcode.ServerInfoRequest  => new ServerInfoRequestPacket(),
                CommunicatorOpcode.ServerInfoResponse => new ServerInfoResponsePacket(),

                _ => throw new Exception("Invalid opcode found in the Communicator's OnSocketReceive!")
            };

            packet.Read(br);

            switch (opcode)
            {
                case CommunicatorOpcode.LoginRequest:
                    MsgLoginRequest(packet as LoginRequestPacket);
                    break;

                case CommunicatorOpcode.LoginResponse:
                    MsgLoginResponse(packet as LoginResponsePacket);
                    break;

                case CommunicatorOpcode.RedirectRequest:
                    MsgRedirectRequest(packet as RedirectRequestPacket);
                    break;

                case CommunicatorOpcode.RedirectResponse:
                    MsgRedirectResponse(packet as RedirectResponsePacket);
                    break;

                case CommunicatorOpcode.ServerInfoRequest:
                    MsgServerInfoRequest(packet as ServerInfoRequestPacket);
                    break;

                case CommunicatorOpcode.ServerInfoResponse:
                    MsgServerInfoResponse(packet as ServerInfoResponsePacket);
                    break;
            }

            Socket.ReceiveAsync();
        }

        private void OnSocketConnect(SocketAsyncEventArgs args)
        {
            if (args.SocketError != SocketError.Success)
            {
                OnSocketError(args);
                return;
            }

            if (OnConnect == null)
            {
                return;
            }

            var info = new ServerData();

            OnConnect(info);

            SendPacket(new LoginRequestPacket(info));

            Socket.ReceiveAsync();
        }
        
        private void SendPacket(IOpcodedPacket<CommunicatorOpcode> packet)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(SendBufferSize);
            var writer = new BinaryWriter(new MemoryStream(buffer, true));

            packet.Write(writer);

            Socket.Send(buffer, 0, (int)writer.BaseStream.Position);

            ArrayPool<byte>.Shared.Return(buffer);
        }

        public void Close()
        {
            if (Type == CommunicatorType.Server)
            {
                foreach (var child in AuthenticatingChildren)
                    child.Close();

                foreach (var client in Clients)
                    client.Value.Close();

                AuthenticatingChildren.Clear();
                Clients.Clear();
            }

            Socket.Close();
            Socket = null;
        }
        #endregion

        #region Requests
        public void RequestServerInfo()
        {
            if (Type == CommunicatorType.Server)
            {
                foreach (var client in Clients)
                {
                    if ((DateTime.Now - client.Value.LastRequestTime).TotalMilliseconds > ServerInfoUpdateIntervalMs)
                        client.Value.RequestServerInfo();
                }
            }
            else if (Type == CommunicatorType.ServerClient)
            {
                LastRequestTime = DateTime.Now;

                SendPacket(new ServerInfoRequestPacket());
            }
        }

        public void RequestRedirection(byte serverId, RedirectRequest request)
        {
            if (Type == CommunicatorType.ServerClient)
            {
                SendPacket(new RedirectRequestPacket(request));

                return;
            }

            if (Type == CommunicatorType.Server)
            {
                if (Clients.TryGetValue(serverId, out var client))
                {
                    client.RequestRedirection(serverId, request);
                    return;
                }

                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) was requested for redirection for an unknown server!");
                return;
            }

            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not request redirection!");
            return;
        }
        #endregion

        #region Packet Handlers
        private void MsgLoginRequest(LoginRequestPacket packet)
        {
            if (Type != CommunicatorType.ServerClient)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle login requests!");
                return;
            }

            if (Server.OnLoginRequest == null)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has no OnLoginRequest callback!");
                return;
            }

            var result = Server.OnLoginRequest(this, packet);

            SendPacket(new LoginResponsePacket
            {
                Result = result ? CommunicatorActionResult.Success : CommunicatorActionResult.Failure
            });

            if (!result)
            {
                lock (ToRemoveClients)
                    ToRemoveClients.Add(ServerData.Id);

                return;
            }

            ServerData = packet.Data;

            Server.ClientAucthenticated(this);

            RequestServerInfo();
        }

        private void MsgLoginResponse(LoginResponsePacket packet)
        {
            if (Type != CommunicatorType.Client)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle login responses!");
                return;
            }

            if (packet.Result == CommunicatorActionResult.Failure)
            {
                Close();

                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) could not authenticate with the Communicator server!");
                return;
            }

            OnLoginResponse?.Invoke(packet.Result);
        }

        private void MsgRedirectRequest(RedirectRequestPacket packet)
        {
            if (Type != CommunicatorType.Client)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle redirect requests!");
                return;
            }

            if (OnRedirectRequest == null)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has no OnRedirectRequest callback!");
                return;
            }

            var result = OnRedirectRequest(packet.Request);

            SendPacket(new RedirectResponsePacket
            {
                AccountId = packet.Request.AccountId,
                Result = result ? CommunicatorActionResult.Success : CommunicatorActionResult.Failure
            });
        }

        private void MsgRedirectResponse(RedirectResponsePacket packet)
        {
            if (Type != CommunicatorType.ServerClient)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle redirect responses!");
                return;
            }

            if (Server.OnRedirectResponse == null)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has no OnRedirectResponse callback!");
                return;
            }

            Server.OnRedirectResponse(this, packet);
        }

#pragma warning disable IDE0060 // Remove unused parameter
        private void MsgServerInfoRequest(ServerInfoRequestPacket packet)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (Type != CommunicatorType.Client)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle server info requests!");
                return;
            }

            if (OnServerInfoRequest == null)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has no OnServerInfoRequest callback!");
                return;
            }

            var info = new ServerInfo();

            OnServerInfoRequest(info);

            SendPacket(new ServerInfoResponsePacket(info));
        }

        private void MsgServerInfoResponse(ServerInfoResponsePacket packet)
        {
            if (Type != CommunicatorType.ServerClient)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle server info responses!");
                return;
            }

            if (Server.OnServerInfoResponse == null)
            {
                Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has no OnServerInfoResponse callback!");
                return;
            }

            ServerInfo = packet.Info;

            Server.OnServerInfoResponse();
        }
        #endregion
    }

    public class ServerData
    {
        public byte Id { get; set; }
        public string Password { get; set; }
        public IPAddress Address { get; set; }
    }

    public class ServerInfo
    {
        public byte ServerId { get; set; }
        public IPAddress Ip { get; set; }
        public int Port { get; set; }
        public byte AgeLimit { get; set; }
        public byte PKFlag { get; set; }
        public ushort CurrentPlayers { get; set; }
        public ushort MaxPlayers { get; set; }
        public byte Status { get; set; }
    }

    public class RedirectRequest
    {
        public uint AccountId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public uint OneTimeKey { get; set; }
    }
}
