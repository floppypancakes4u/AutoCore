using System.Net;

namespace AutoCore.Auth.Network;

using AutoCore.Communicator.Packets;
using AutoCore.Communicator;
using AutoCore.Utils;

public partial class AuthServer
{
    private void StartCommunicator()
    {
        Communicator.OnLoginRequest += AuthenticateGameServer;
        Communicator.OnRedirectResponse += RedirectResponse;
        Communicator.OnServerInfo += UpdateServerInfo;
        Communicator.Start(IPAddress.Any, Config.CommunicatorPort);

        Logger.WriteLog(LogType.Network, "*** Listening for gameservers on port {0}", Config.CommunicatorPort);

        // Add the repeating server info request timed event
        Timer.Add("ServerInfoUpdate", 30000, true, () =>
        {
            Communicator.RequestServerInfo();
        });
    }

    public bool AuthenticateGameServer(Communicator client, LoginRequestPacket packet)
    {
        if (Servers.TryGetValue(packet.Data.Id, out var server))
        {
            if (server.Password != packet.Data.Password)
            {
                Logger.WriteLog(LogType.Error, $"A server tried to log in with an invalid password!");
                return false;
            }

            server.Ip = packet.Data.Address;
            server.Port = packet.Data.Port;

            Logger.WriteLog(LogType.Communicator, $"The Game server (Id: {packet.Data.Id}, Address: {client.Socket.RemoteAddress}, Public Address: {packet.Data.Address}) has authenticated!");
            return true;
        }

        Logger.WriteLog(LogType.Debug, $"A server tried to connect to a non-defined server slot! Remote Address: {client.Socket.RemoteAddress}");
        return false;
    }

    public void UpdateServerInfo(Communicator sender, ServerInfo info)
    {
        lock (Servers)
        {
            if (Servers.TryGetValue(sender.Data.Id, out var server))
            {
                server.AgeLimit = info.AgeLimit;
                server.PKFlag = info.PKFlag;
                server.CurrentPlayers = info.CurrentPlayers;
                server.MaxPlayers = info.MaxPlayers;
            }
        }

        // Let's try to wait all the incoming server infos.
        // Addig this again will overwrite the previous one and reset it's timer
        Timer.Add("BroadcastServerList", 1000, false, BroadcastServerList);
    }

    public void RedirectResponse(Communicator client, RedirectResponsePacket packet)
    {
        AuthClient? authClient;
        lock (Clients)
            authClient = Clients.FirstOrDefault(c => c.Account!.Id == packet.AccountId);

        authClient?.RedirectionResult(client.Data.Id, packet.Success);
    }

    public void RequestRedirection(AuthClient client, byte serverId)
    {
        Communicator.RequestRedirection(serverId, new()
        {
            AccountId = client.Account!.Id,
            Email = client.Account.Email,
            OneTimeKey = client.OneTimeKey,
            Username = client.Account.Username
        });
    }
}
