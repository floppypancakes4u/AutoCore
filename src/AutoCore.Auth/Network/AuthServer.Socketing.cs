using System.Net;

namespace AutoCore.Auth.Network;

using AutoCore.Utils.Networking;
using AutoCore.Utils;

public partial class AuthServer
{
    private void StartListening()
    {
        ListenerSocket.OnAccept += OnAccept;
        ListenerSocket.StartListening(new IPEndPoint(IPAddress.Any, Config.AuthSocketPort));

        Logger.WriteLog(LogType.Network, "*** Listening for clients on port {0}", Config.AuthSocketPort);
    }

    private void OnAccept(AsyncLengthedSocket newSocket)
    {
        if (newSocket == null)
            return;

        lock (Clients)
            Clients.Add(new AuthClient(newSocket, this));
    }
}
