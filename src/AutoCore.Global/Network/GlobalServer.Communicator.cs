using System.Net;

namespace AutoCore.Global.Network;

using AutoCore.Communicator;
using AutoCore.Game.Managers;
using AutoCore.Utils;

public partial class GlobalServer
{
    private void ConnectCommunicator()
    {
        CloseCommunicator();

        try
        {
            AuthCommunicator.OnConnect += OnCommunicatorConnect;
            AuthCommunicator.OnError += OnCommunicatorError;
            AuthCommunicator.OnLoginResponse += OnCommunicatorLoginResponse;
            AuthCommunicator.OnRedirectRequest += OnCommunicatorRedirectRequest;
            AuthCommunicator.OnServerInfoRequest += OnCommunicatorServerInfoRequest;
            AuthCommunicator.Start(IPAddress.Parse(Config.CommunicatorAddress), Config.CommunicatorPort);
        }
        catch (Exception e)
        {
            Logger.WriteLog(LogType.Error, "Unable to create or start listening on the Auth server socket! Retrying soon... Exception:");
            Logger.WriteLog(LogType.Error, e);
        }

        Logger.WriteLog(LogType.Communicator, $"Connecting to auth server! Address: {Config.CommunicatorAddress}:{Config.CommunicatorPort}");
    }

    private void CloseCommunicator()
    {
        if (AuthCommunicator.Connected)
            AuthCommunicator.Close();
    }

    private void OnCommunicatorError()
    {
        Timer.Add("CommReconnect", 10000, false, () =>
        {
            if (!AuthCommunicator.Connected)
                ConnectCommunicator();
        });

        Logger.WriteLog(LogType.Error, "Could not connect to the Auth server! Trying again in a few seconds...");
    }

    private void OnCommunicatorConnect(ServerData info)
    {
        info.Id = Config.ServerInfoConfig.Id;
        info.Address = PublicAddress;
        info.Port = Config.GameConfig.Port;
        info.Password = Config.ServerInfoConfig.Password;
    }

    private void OnCommunicatorLoginResponse(bool success)
    {
        if (success)
        {
            Logger.WriteLog(LogType.Communicator, "Successfully logged in to the Auth server!");
            return;
        }

        CloseCommunicator();

        Logger.WriteLog(LogType.Error, "Could not authenticate with the Auth server! Shutting down internal communication!");
    }

    private bool OnCommunicatorRedirectRequest(RedirectRequest request) => LoginManager.Instance.ExpectLoginToGlobal(request.AccountId, request.Username, request.OneTimeKey);

    private void OnCommunicatorServerInfoRequest(ServerInfo info)
    {
        info.AgeLimit = Config.ServerInfoConfig.AgeLimit;
        info.PKFlag = Config.ServerInfoConfig.PKFlag;
        info.CurrentPlayers = 0;
        info.Port = Config.GameConfig.Port;
        info.MaxPlayers = Config.ServerInfoConfig.MaxPlayers;
    }
}
