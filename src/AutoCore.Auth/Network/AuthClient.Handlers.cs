using System.Net;

namespace AutoCore.Auth.Network;

using AutoCore.Auth.Data;
using AutoCore.Auth.Packets.Client;
using AutoCore.Auth.Packets.Server;
using AutoCore.Database.Auth;
using AutoCore.Utils;
using AutoCore.Utils.Logging;
using AutoCore.Utils.Packets;
using AutoCore.Utils.Reliability;

public partial class AuthClient
{
    internal void HandlePacket(IBasePacket packet)
    {
        if (packet is not IOpcodedPacket<ClientOpcode> authPacket)
            return;

        // Ambient session scope so every log line inside auth dispatch is attributable.
        using var sessionScope = Account == null
            ? LogContext.Push(("SessionId", SessionId))
            : LogContext.Push(("SessionId", SessionId), ("AccountId", Account.Id));

        // SS-26: this is the auth server's dispatch boundary for client-controlled TCP input.
        // Without the guard, one malformed packet whose handler throws propagates into the
        // socket receive path and can tear down the auth pump for every client.
        Guard.Run($"auth packet dispatch ({authPacket.Opcode})", () =>
        {
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

                case ClientOpcode.SCCheck:
                    Logger.WriteLog(LogType.Debug,
                        "AuthClient {0} SCCheck ignored (anti-cheat handshake unused).",
                        DescribePeer());
                    break;
            }
        });
    }

    private void MsgLogin(LoginPacket packet)
    {
        using (var context = CreateAuthContext())
        {
            var account = context.Accounts.FirstOrDefault(a => a.Username == packet.UserName);
            if (account == null || !account.CheckPassword(packet.Password))
            {
                // NEVER log the password. Client sees one merged failure; server-side the
                // reasons stay distinguishable for brute-force triage.
                GameLog.Warn("AuthLoginFailed", "AUTH-001",
                    ("Reason", account == null ? "UnknownAccount" : "BadPassword"),
                    ("Username", packet.UserName));

                SendPacket(new LoginFailPacket(FailReason.UserNameOrPassword));

                Close();

                return;
            }

            if (account.Locked)
            {
                GameLog.Warn("AuthLoginFailed", "AUTH-001",
                    ("Reason", "Locked"),
                    ("Username", packet.UserName),
                    ("AccountId", account.Id));

                SendPacket(new BlockedAccountPacket());

                Close();

                return;
            }

            // RemoteAddress may be null for test clients without a connected socket.
            try
            {
                account.LastIP = Socket.RemoteAddress?.ToString();
            }
            catch (ObjectDisposedException)
            {
                account.LastIP = null;
            }

            account.LastLogin = DateTime.Now;

            context.SaveChanges();

            Account = account;
        }

        // Single-session: a second Auth login wins; older Auth TCP sessions are kicked now.
        // Game-side TNL sessions are superseded separately in LoginManager on Global/Sector login.
        Server.KickOtherSessions(Account!.Id, this);

        State = ClientState.LoggedIn;

        GameLog.Info("AuthLoginSucceeded",
            ("AccountId", Account!.Id),
            ("Username", Account.Username),
            ("SessionId", SessionId));

        SendPacket(new LoginOkPacket
        {
            SessionId1 = SessionId1,
            SessionId2 = SessionId2
        });

        try
        {
            Logger.WriteLog(LogType.Network, "*** Client logged in from {0}", Socket.RemoteAddress);
        }
        catch (ObjectDisposedException)
        {
            Logger.WriteLog(LogType.Network, "*** Client logged in");
        }
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

        GameLog.Info("AuthRedirectRequested",
            ("AccountId", Account!.Id),
            ("ServerId", packet.ServerId));

        Server.RequestRedirection(this, packet.ServerId);
    }
}
