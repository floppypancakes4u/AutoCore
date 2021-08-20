using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Sockets;

namespace AutoCore.Auth.Network
{
    using Communicator;
    using Crypto;
    using Data;
    using Database.Auth;
    using Database.Auth.Models;
    using Utils;
    using Utils.Extensions;
    using Utils.Networking;
    using Utils.Packets;
    using Utils.Timer;
    using Packets.Client;
    using Packets.Server;

    public class AuthClient
    {
        public const int LengthSize = 2;
        public const int SendBufferSize = 512;
        public const int SendBufferCryptoPadding = 8;
        public const int SendBufferChecksumPadding = 8;

        public LengthedSocket Socket { get; }
        public AuthServer Server { get; }

        public uint OneTimeKey { get; }
        public uint SessionId1 { get; }
        public uint SessionId2 { get; }
        public Account Account { get; private set; }
        public ClientState State { get; private set; }
        public Timer Timer { get; }

        private readonly PacketQueue _packetQueue = new();

        public AuthClient(LengthedSocket socket, AuthServer server)
        {
            Socket = socket;
            Server = server;
            State = ClientState.Connected;

            Timer = new Timer();

            Socket.OnError += OnError;
            Socket.OnReceive += OnReceive;

            Socket.ReceiveAsync();

            var rnd = new Random();

            OneTimeKey = rnd.NextUInt();
            SessionId1 = rnd.NextUInt();
            SessionId2 = rnd.NextUInt();

            SendPacket(new ProtocolVersionPacket(OneTimeKey));

            Timer.Add("timeout", Server.Config.AuthConfig.ClientTimeout * 1000, false, () =>
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

            Timer.Remove("timeout");

            State = ClientState.Disconnected;

            Socket.Close();

            Server.Disconnect(this);
        }

        public void SendPacket(IBasePacket packet)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(SendBufferSize + SendBufferCryptoPadding + SendBufferChecksumPadding);
            var writer = new BinaryWriter(new MemoryStream(buffer, true));

            packet.Write(writer);

            var length = (int)writer.BaseStream.Position;

            if (packet is not ProtocolVersionPacket)
            {
                CryptoManager.Encrypt(buffer, 0, ref length, buffer.Length);
            }

            Socket.Send(buffer, 0, length);

            ArrayPool<byte>.Shared.Return(buffer);
        }

        public void HandlePacket(IBasePacket packet)
        {
            if (packet is not IOpcodedPacket<ClientOpcode> authPacket)
                return;

            switch (authPacket.Opcode)
            {
                case ClientOpcode.Login:
                    MsgLogin(authPacket as LoginPacket);
                    break;

                case ClientOpcode.Logout:
                    MsgLogout(authPacket as LogoutPacket);
                    break;

                case ClientOpcode.AboutToPlay:
                    MsgAboutToPlay(authPacket as AboutToPlayPacket);
                    break;

                case ClientOpcode.ServerListExt:
                    MsgServerListExt(authPacket as ServerListExtPacket);
                    break;
            }
        }

        public void RedirectionResult(CommunicatorActionResult result, ServerInfo info)
        {
            switch (result)
            {
                case CommunicatorActionResult.Failure:
                    SendPacket(new PlayFailPacket(FailReason.UnexpectedError));

                    Close();

                    Logger.WriteLog(LogType.Error, $"Account ({Account.Username}, {Account.Id}) couldn't be redirected to server: {info.ServerId}!");
                    break;

                case CommunicatorActionResult.Success:
                    SendPacket(new PlayOkPacket
                    {
                        OneTimeKey = OneTimeKey,
                        ServerId = info.ServerId,
                        UserId = Account.Id
                    });

                    using (var context = new AuthContext())
                    {
                        var account = context.Accounts.Where(a => a.Id == Account.Id).First();

                        account.LastServerId = info.ServerId;

                        context.SaveChanges();
                    }

                    Logger.WriteLog(LogType.Network, $"Account ({Account.Username}, {Account.Id}) was redirected to the queue of the server: {info.ServerId}!");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }
        }

        private void OnError(SocketAsyncEventArgs args)
        {
            Close();
        }

        private void OnReceive(byte[] data, int length)
        {
            CryptoManager.Decrypt(data, 0, length);

            using var br = new BinaryReader(new MemoryStream(data, 0, length, false));

            var packet = CreatePacket((ClientOpcode)br.ReadByte());

            packet.Read(br);

            _packetQueue.EnqueueIncoming(packet);

            // Reset the timeout after every action
            Timer.ResetTimer("timeout");

            Socket.ReceiveAsync();
        }

        private static IBasePacket CreatePacket(ClientOpcode opcode)
        {
            return opcode switch
            {
                ClientOpcode.AboutToPlay   => new AboutToPlayPacket(),
                ClientOpcode.Login         => new LoginPacket(),
                ClientOpcode.Logout        => new LogoutPacket(),
                ClientOpcode.ServerListExt => new ServerListExtPacket(),
                ClientOpcode.SCCheck       => new SCCheckPacket(),

                _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
            };
        }

        #region Handlers
        private void MsgLogin(LoginPacket packet)
        {
            using (var context = new AuthContext())
            {
                var account = context.Accounts.FirstOrDefault(a => a.Username == packet.UserName);
                if (account == null || !account.CheckPassword(packet.Password))
                {
                    SendPacket(new LoginFailPacket(FailReason.UserNameOrPassword));

                    Close();

                    return;
                }

                if (account.Locked)
                {
                    SendPacket(new BlockedAccountPacket());

                    Close();

                    return;
                }

                account.LastIP = Socket.RemoteAddress.ToString();
                account.LastLogin = DateTime.Now;

                context.SaveChanges();

                Account = account;
            }

            State = ClientState.LoggedIn;

            SendPacket(new LoginOkPacket
            {
                SessionId1 = SessionId1,
                SessionId2 = SessionId2
            });

            Logger.WriteLog(LogType.Network, "*** Client logged in from {0}", Socket.RemoteAddress);
        }

        private void MsgLogout(LogoutPacket packet)
        {
            if (SessionId1 != packet.SessionId1 || SessionId2 != packet.SessionId2)
            {
                Logger.WriteLog(LogType.Security, $"Account ({Account.Username}, {Account.Id}) has sent an LogoutPacket with invalid session data!");
                return;
            }

            Close();
        }

        private void MsgServerListExt(ServerListExtPacket packet)
        {
            if (SessionId1 != packet.SessionId1 || SessionId2 != packet.SessionId2)
            {
                Logger.WriteLog(LogType.Security, $"Account ({Account.Username}, {Account.Id}) has sent an ServerListExtPacket with invalid session data!");
                return;
            }

            State = ClientState.ServerList;

            SendPacket(new SendServerListExtPacket(Server.ServerList, Account.LastServerId));
        }

        private void MsgAboutToPlay(AboutToPlayPacket packet)
        {
            if (SessionId1 != packet.SessionId1 || SessionId2 != packet.SessionId2)
            {
                Logger.WriteLog(LogType.Security, $"Account ({Account.Username}, {Account.Id}) has sent an AboutToPlayPacket with invalid session data!");
                return;
            }

            Server.RequestRedirection(this, packet.ServerId);
        }
        #endregion
    }
}
