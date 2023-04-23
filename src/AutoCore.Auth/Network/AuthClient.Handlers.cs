using System.Net;

namespace AutoCore.Auth.Network;

using AutoCore.Auth.Data;
using AutoCore.Auth.Packets.Client;
using AutoCore.Auth.Packets.Server;
using AutoCore.Database.Auth;
using AutoCore.Utils;
using AutoCore.Utils.Packets;

public partial class AuthClient
{
    private void HandlePacket(IBasePacket packet)
    {
        if (packet is not IOpcodedPacket<ClientOpcode> authPacket)
            return;

        switch (authPacket.Opcode)
        {
            case ClientOpcode.Login:
                MsgLogin((authPacket as LoginPacket)!);
                break;

            case ClientOpcode.Logout:
                MsgLogout((authPacket as LogoutPacket)!);
                break;

            case ClientOpcode.AboutToPlay:
                MsgAboutToPlay((authPacket as AboutToPlayPacket)!);
                break;

            case ClientOpcode.ServerListExt:
                MsgServerListExt((authPacket as ServerListExtPacket)!);
                break;
        }
    }

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
            Logger.WriteLog(LogType.Security, $"Account ({Account!.Username}, {Account.Id}) has sent an LogoutPacket with invalid session data!");
            return;
        }

        Close();
    }

    private void MsgServerListExt(ServerListExtPacket packet)
    {
        if (SessionId1 != packet.SessionId1 || SessionId2 != packet.SessionId2)
        {
            Logger.WriteLog(LogType.Security, $"Account ({Account!.Username}, {Account.Id}) has sent an ServerListExtPacket with invalid session data!");
            return;
        }

        State = ClientState.ServerList;

        SendPacket(new SendServerListExtPacket(Server.Servers.Values.Where(s => s.Ip != IPAddress.Any), Account!.LastServerId));
    }

    private void MsgAboutToPlay(AboutToPlayPacket packet)
    {
        if (SessionId1 != packet.SessionId1 || SessionId2 != packet.SessionId2)
        {
            Logger.WriteLog(LogType.Security, $"Account ({Account!.Username}, {Account.Id}) has sent an AboutToPlayPacket with invalid session data!");
            return;
        }

        Server.RequestRedirection(this, packet.ServerId);
    }
}
