using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace AutoCore.Communicator;

using AutoCore.Utils;
using AutoCore.Utils.Networking;
using AutoCore.Utils.Packets;
using AutoCore.Communicator.Packets;
using AutoCore.Utils.Memory;
using System.Text;

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
    public const double ServerInfoUpdateIntervalMs = 30000.0d;

    public CommunicatorType Type { get; }
    public AsyncLengthedSocket Socket { get; private set; }
    public List<Communicator>? AuthenticatingChildren { get; }
    public Dictionary<byte, Communicator>? Clients { get; }
    public List<byte>? ToRemoveClients { get; }
    public DateTime LastRequestTime { get; private set; }
    public ServerData Data { get; private set; } = new();
    public bool Connected => Socket?.Connected ?? false;

    private Communicator? Server { get; }

    public Action? OnError { get; set; }
    public Action<ServerData>? OnConnect { get; set; }
    public Func<Communicator, LoginRequestPacket, bool>? OnLoginRequest { get; set; }
    public Action<CommunicatorActionResult>? OnLoginResponse { get; set; }
    public Func<RedirectRequest, bool>? OnRedirectRequest { get; set; }
    public Action<Communicator, RedirectResponsePacket>? OnRedirectResponse { get; set; }
    public Action<ServerInfo>? OnServerInfoRequest { get; set; }
    public Action<Communicator, ServerInfo>? OnServerInfo { get; set; }

    public Communicator(CommunicatorType type)
    {
        if (type == CommunicatorType.ServerClient)
            throw new ArgumentOutOfRangeException(nameof(type));

        Type = type;

        Socket = new AsyncLengthedSocket(AsyncLengthedSocket.HeaderSizeType.Word);
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

    public Communicator(AsyncLengthedSocket socket, Communicator server)
    {
        Type = CommunicatorType.ServerClient;
        Server = server;

        Socket = socket;
        Socket.OnReceive += OnSocketReceive;

        Socket.Start();
    }

    public void Start(IPAddress address, int port, int backlog = int.MaxValue)
    {
        switch (Type)
        {
            case CommunicatorType.Server:
                Socket.StartListening(new IPEndPoint(address, port), backlog);
                break;

            case CommunicatorType.Client:
                Socket.ConnectAsync(new IPEndPoint(address, port));
                break;
        }
    }

    public void Update()
    {
        if (Type != CommunicatorType.Server)
            throw new Exception("Update can only be called on a server Communicator!");

        lock (ToRemoveClients!)
        {
            foreach (var id in ToRemoveClients)
            {
                if (Clients!.TryGetValue(id, out var comm))
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

        Clients!.Add(client.Data.Id, client);
        AuthenticatingChildren!.Remove(client);
    }

    #region Socketing
    private void OnSocketError()
    {
        Socket.Close();

        OnError?.Invoke();

        Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) has encountered an error!");
    }

    private void OnSocketAccept(AsyncLengthedSocket socket)
    {
        AuthenticatingChildren!.Add(new Communicator(socket, this));
    }

    private void OnSocketReceive(NonContiguousMemoryStream incomingStream, int length)
    {
        var startPosition = incomingStream.Position;

        using var br = new BinaryReader(incomingStream, Encoding.UTF8, true);

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

        if (startPosition + length != incomingStream.Position)
            throw new Exception($"Over or under read of the incoming packet! Start position: {startPosition} | Length: {length} | Ending position: {incomingStream.Position}");

        switch (opcode)
        {
            case CommunicatorOpcode.LoginRequest:
                MsgLoginRequest((packet as LoginRequestPacket)!);
                break;

            case CommunicatorOpcode.LoginResponse:
                MsgLoginResponse((packet as LoginResponsePacket)!);
                break;

            case CommunicatorOpcode.RedirectRequest:
                MsgRedirectRequest((packet as RedirectRequestPacket)!);
                break;

            case CommunicatorOpcode.RedirectResponse:
                MsgRedirectResponse((packet as RedirectResponsePacket)!);
                break;

            case CommunicatorOpcode.ServerInfoRequest:
                MsgServerInfoRequest((packet as ServerInfoRequestPacket)!);
                break;

            case CommunicatorOpcode.ServerInfoResponse:
                MsgServerInfoResponse((packet as ServerInfoResponsePacket)!);
                break;
        }
    }

    private void OnSocketConnect()
    {
        if (OnConnect == null || OnServerInfoRequest == null)
            throw new Exception($"Communicator(Type = {Type}) has no OnConenct or OnServerInfoREquest handles given!");

        Data = new ServerData();
        var info = new ServerInfo();

        OnConnect(Data);
        OnServerInfoRequest(info);

        Socket.Start();

        SendPacket(new LoginRequestPacket(Data, info));
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
            foreach (var child in AuthenticatingChildren!)
                child.Close();

            foreach (var client in Clients!)
                client.Value.Close();

            AuthenticatingChildren.Clear();
            Clients.Clear();
        }

        Socket.Close();
    }
    #endregion

    #region Requests
    public void RequestServerInfo()
    {
        if (Type == CommunicatorType.Server)
        {
            foreach (var client in Clients!)
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
            if (Clients!.TryGetValue(serverId, out var client))
            {
                client.RequestRedirection(serverId, request);
                return;
            }

            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) was requested for redirection for an unknown server!");
        }
        else
            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not request redirection!");
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

        if (Server!.OnLoginRequest == null || Server!.OnServerInfo == null)
        {
            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Server.Type}) has no OnLoginRequest or OnServerInfo callback!");
            return;
        }

        if (Server.Clients!.ContainsKey(packet.Data.Id))
        {
            Logger.WriteLog(LogType.Debug, $"Communicator(Type = {Server.Type}) has a client (Id = {packet.Data.Id}) already connected!");
            return;
        }

        var result = Server.OnLoginRequest(this, packet);

        SendPacket(new LoginResponsePacket
        {
            Result = result ? CommunicatorActionResult.Success : CommunicatorActionResult.Failure
        });

        if (!result)
        {
            lock (Server.ToRemoveClients!)
                Server.ToRemoveClients.Add(Data.Id);

            return;
        }

        Data = packet.Data;

        Server.ClientAucthenticated(this);
        Server.OnServerInfo(this, packet.InfoPacket.Info);
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

        SendPacket(new RedirectResponsePacket
        {
            AccountId = packet.Request.AccountId,
            Success = OnRedirectRequest(packet.Request)
        });
    }

    private void MsgRedirectResponse(RedirectResponsePacket packet)
    {
        if (Type != CommunicatorType.ServerClient)
        {
            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Type}) can not handle redirect responses!");
            return;
        }

        if (Server!.OnRedirectResponse == null)
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

        if (Server!.OnServerInfo == null)
        {
            Logger.WriteLog(LogType.Error, $"Communicator(Type = {Server.Type}) has no OnServerInfo callback!");
            return;
        }

        Server.OnServerInfo(this, packet.Info);
    }
    #endregion
}

public class ServerData
{
    public byte Id { get; set; }
    public string Password { get; set; } = string.Empty;
    public IPAddress Address { get; set; } = IPAddress.Any;
    public int Port { get; set; }
}

public class ServerInfo
{
    public byte ServerId { get; set; }
    public IPAddress Ip { get; set; } = IPAddress.Any;
    public int Port { get; set; }
    public byte AgeLimit { get; set; }
    public byte PKFlag { get; set; }
    public ushort CurrentPlayers { get; set; }
    public ushort MaxPlayers { get; set; }
    public byte Status { get; set; }

    public string Password { get; set; } = string.Empty;
}

public class RedirectRequest
{
    public uint AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public uint OneTimeKey { get; set; }
}
