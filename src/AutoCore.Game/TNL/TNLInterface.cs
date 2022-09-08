using TNL.Entities;

namespace AutoCore.Game.TNL;

using AutoCore.Game.TNL.Ghost;

public class TNLInterface : NetInterface
{
    private readonly object _lock = new();

    public bool UnlimitedBandwith { get; }
    public bool Adaptive { get; private set; }
    public int Version { get; private set; }
    public ushort FragmentSize { get; private set; }
    public long ConnectionId { get; private set; }
    public Dictionary<long, TNLConnection> MapConnections { get; } = new();
    public Dictionary<long, GhostObject> Ghosts { get; } = new();

    public static void RegisterNetClassReps()
    {
        GhostObject.RegisterNetClassReps();
        GhostCreature.RegisterNetClassReps();
        GhostCharacter.RegisterNetClassReps();
        GhostVehicle.RegisterNetClassReps();

        TNLConnection.RegisterNetClassReps();
    }

    public TNLInterface(int port, bool adaptive, int version, bool unlimitedBandwith)
        : base(port)
    {
        Adaptive = adaptive;
        Version = version;
        UnlimitedBandwith = unlimitedBandwith;
        FragmentSize = 220;
        ConnectionId = 0;
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
        }

        if (UnlimitedBandwith)
            conn.SetPingTimeouts(3000, 10);

        base.AddConnection(conn);
    }

    protected override void RemoveConnection(NetConnection conn)
    {
        if (conn is TNLConnection tConn)
            MapConnections.Remove(tConn.GetPlayerCOID());

        base.RemoveConnection(conn);
    }

    public void AddGhost(TNLConnection conn, NetObject ghost)
    {
        if (ghost is GhostObject obj)
        {
            Ghosts.Add(conn.GetPlayerCOID(), obj);
        }
    }

    public void RemoveGhost(TNLConnection conn)
    {
        Ghosts.Remove(conn.GetPlayerCOID());
    }

    public void DoScoping()
    {
        foreach (var conn in MapConnections)
        {
            var temp = conn;

            foreach (var obj in from ghost in Ghosts let obj = temp.Value.GetScopeObject() where obj != null && obj != ghost.Value select obj)
                conn.Value.ObjectInScope(obj);
        }
    }
}
