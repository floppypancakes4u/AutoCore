using System.Net;
using System.Linq;

namespace AutoCore.Auth.Network;

using AutoCore.Communicator;
using AutoCore.Auth.Config;
using AutoCore.Auth.Data;
using AutoCore.Database.Auth;
using AutoCore.Auth.Packets.Server;
using AutoCore.Utils;
using AutoCore.Utils.Networking;
using AutoCore.Utils.Server;
using AutoCore.Utils.Threading;
using AutoCore.Utils.Timer;

public partial class AuthServer : BaseServer, ILoopable
{
    public const int MainLoopTime = 100; // Milliseconds

    private List<AuthClient> ClientsToRemove { get; } = new();

    public AuthConfig Config { get; private set; } = new();
    public Communicator Communicator { get; } = new(CommunicatorType.Server);
    public AsyncLengthedSocket ListenerSocket { get; }
    public List<AuthClient> Clients { get; } = new();
    public Dictionary<byte, ServerInfo> Servers { get; } = new();
    public MainLoop Loop { get; }
    public Timer Timer { get; }
    public override bool IsRunning => Loop != null && Loop.Running;

    public AuthServer()
        : base("Auth")
    {
        Logger.WriteLog(LogType.Initialize, "Initializing the Auth server...");

        Loop = new MainLoop(this, MainLoopTime);
        Timer = new Timer();
        ListenerSocket = new AsyncLengthedSocket(AsyncLengthedSocket.HeaderSizeType.Word);

        RegisterConsoleCommands();

        Logger.WriteLog(LogType.Initialize, "The Auth server has been initialized!");
    }

    ~AuthServer() => Shutdown();

    public void Setup(AuthConfig? config)
    {
        if (config != null)
            Config = config;

        SetupServerList();
    }

    public bool Start()
    {
        // Check the configuration
        if (Config.AuthSocketPort == 0 || Config.CommunicatorPort == 0)
        {
            Logger.WriteLog(LogType.Error, "Invalid config values!");
            return false;
        }

        StartListening();
        StartCommunicator();

        // Start the main loop
        Loop.Start();

        // TODO: Set up timed events (query stuff, internal communication, etc...)

        return true;
    }

    public void Disconnect(AuthClient client)
    {
        lock (ClientsToRemove)
            ClientsToRemove.Add(client);
    }

    private void SetupServerList()
    {
        using var context = new AuthContext();

        // If no servers exist in the database, create a default server slot
        if (!context.GlobalServers.Any())
        {
            Logger.WriteLog(LogType.Initialize, "No server slots found in database. Creating default server slot (ID: 1, Password: test)...");
            
            context.GlobalServers.Add(new()
            {
                Id = 1,
                Password = "test",
                Enabled = true
            });
            
            context.SaveChanges();
            Logger.WriteLog(LogType.Initialize, "Default server slot created successfully.");
        }

        // TODO: if new server -> add
        // if update server -> change PW maybe? then DC communicator for it to retry connecting with new password?
        // if remove server -> remove and DC active communicator

        foreach (var globalServer in context.GlobalServers.Where(s => s.Enabled))
        {
            if (Servers.TryGetValue(globalServer.Id, out var server))
            {
                server.Password = globalServer.Password;
            }
            else
            {
                Servers.Add(globalServer.Id, new()
                {
                    ServerId = globalServer.Id,
                    Password = globalServer.Password
                });
            }
        }
    }

    public void Shutdown()
    {
        ListenerSocket.Close();

        Loop.Stop();
    }

    public void MainLoop(long delta)
    {
        Communicator.Update();
        Timer.Update(delta);

        if (Clients.Count == 0)
            return;

        lock (Clients)
        {
            foreach (var c in Clients)
                c.Update(delta);

            if (ClientsToRemove.Count > 0)
            {
                lock (ClientsToRemove)
                {
                    foreach (var client in ClientsToRemove)
                        Clients.Remove(client);

                    ClientsToRemove.Clear();
                }
            }
        }
    }

    public void BroadcastServerList()
    {
        lock (Clients)
        {
            foreach (var c in Clients)
                if (c.State == ClientState.ServerList)
                    c.SendPacket(new SendServerListExtPacket(Servers.Values.Where(s => s.Ip != IPAddress.Any), c.Account!.LastServerId));
        }
    }
}
