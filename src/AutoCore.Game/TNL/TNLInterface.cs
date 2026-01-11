using TNL.Entities;

namespace AutoCore.Game.TNL;

using AutoCore.Game.TNL.Ghost;

public class TNLInterface : NetInterface
{
    public const int Version = 175;

    private readonly object _lock = new();

    public bool DoGhosting { get; private set; }
    public ushort FragmentSize { get; private set; }
    public long ConnectionId { get; private set; }
    public Dictionary<long, TNLConnection> MapConnections { get; } = new();
    public bool AllowVersionMismatch { get; set; } = false;
    public int ExpectedVersion { get; set; } = Version;

    static TNLInterface()
    {
        GhostObject.RegisterNetClassReps();
        GhostCreature.RegisterNetClassReps();
        GhostCharacter.RegisterNetClassReps();
        GhostVehicle.RegisterNetClassReps();

        TNLConnection.RegisterNetClassReps();
    }

    public TNLInterface(int port, bool doGhosting)
        : base(port)
    {
        DoGhosting = doGhosting;
        FragmentSize = 220;
        ConnectionId = 0;
    }

    public void Pulse()
    {
        CheckIncomingPackets();
        ProcessConnections();
    }

    public TNLConnection FindConnection(long connectionId)
    {
        if (MapConnections.TryGetValue(connectionId, out var conn))
            return conn;

        return null;
    }

    public override void AddConnection(NetConnection conn)
    {
        if (conn is TNLConnection tConn)
        {
            long connId;

            lock (_lock)
            {
                connId = ConnectionId++;
            }

            tConn.SetPlayerCOID(connId);

            MapConnections.Add(connId, tConn);
            
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"TNLInterface: Adding connection {connId} from {conn.GetNetAddress()}");
        }

        base.AddConnection(conn);
    }

    protected override void RemoveConnection(NetConnection conn)
    {
        if (conn is TNLConnection tConn)
            MapConnections.Remove(tConn.GetPlayerCOID());

        base.RemoveConnection(conn);
    }
}
