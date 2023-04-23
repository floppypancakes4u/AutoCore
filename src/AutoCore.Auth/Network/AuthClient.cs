namespace AutoCore.Auth.Network;

using AutoCore.Auth.Data;
using AutoCore.Auth.Packets.Server;
using AutoCore.Communicator;
using AutoCore.Database.Auth;
using AutoCore.Database.Auth.Models;
using AutoCore.Utils;
using AutoCore.Utils.Extensions;
using AutoCore.Utils.Networking;
using AutoCore.Utils.Packets;
using AutoCore.Utils.Timer;

public partial class AuthClient
{
    public const int LengthSize = 2;
    public const int SendBufferSize = 512;
    public const int SendBufferCryptoPadding = 8;
    public const int SendBufferChecksumPadding = 8;

    public AsyncLengthedSocket Socket { get; }
    public AuthServer Server { get; }

    public uint OneTimeKey { get; }
    public uint SessionId1 { get; }
    public uint SessionId2 { get; }
    public Account? Account { get; private set; }
    public ClientState State { get; private set; }
    public Timer Timer { get; }

    private readonly PacketQueue _packetQueue = new();

    public AuthClient(AsyncLengthedSocket socket, AuthServer server)
    {
        Socket = socket;
        Server = server;
        State = ClientState.Connected;

        Timer = new Timer();

        Socket.OnError += Close;
        Socket.OnReceive += OnReceive;
        Socket.Start();

        OneTimeKey = Random.Shared.NextUInt();
        SessionId1 = Random.Shared.NextUInt();
        SessionId2 = Random.Shared.NextUInt();

        SendPacket(new ProtocolVersionPacket(OneTimeKey));

        Timer.Add("timeout", 300_000, false, () =>
        {
            Logger.WriteLog(LogType.Network, "*** Client timed out! Ip: {0}", Socket.RemoteAddress);

            Close();
        });

        Logger.WriteLog(LogType.Network, "*** Client connected from {0}", Socket.RemoteAddress);
    }

    public void Update(long delta)
    {
        Timer.Update(delta);

        if (State == ClientState.Disconnected)
            return;

        IBasePacket packet;

        while ((packet = _packetQueue.PopIncoming()) != null)
            HandlePacket(packet);

        while ((packet = _packetQueue.PopOutgoing()) != null)
            SendPacket(packet);
    }
    
    public void Close()
    {
        if (State == ClientState.Disconnected)
            return;

        Logger.WriteLog(LogType.Network, "*** Client disconnected! Ip: {0}", Socket.RemoteAddress);

        Server.Disconnect(this);

        Timer.Remove("timeout");

        State = ClientState.Disconnected;

        Socket.Close();
    }

    public void RedirectionResult(byte serverId, bool result)
    {
        if (!result)
        {
            SendPacket(new PlayFailPacket(FailReason.UnexpectedError));

            Close();

            Logger.WriteLog(LogType.Error, $"Account ({Account!.Username}, {Account.Id}) couldn't be redirected to server: {serverId}!");

            return;
        }

        SendPacket(new PlayOkPacket
        {
            OneTimeKey = OneTimeKey,
            ServerId = serverId,
            UserId = Account!.Id
        });

        using (var context = new AuthContext())
        {
            var account = context.Accounts.Where(a => a.Id == Account.Id).First();

            account.LastServerId = serverId;

            context.SaveChanges();
        }

        Logger.WriteLog(LogType.Network, $"Account ({Account.Username}, {Account.Id}) was redirected to the server: {serverId}!");
    }
}
