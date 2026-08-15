namespace AutoCore.Game.Combat;

using System.Collections.Generic;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils.Reliability;

/// <summary>
/// Absolute current-HP write for the retail target frame / HUD.
/// Client: 0x2010 / 0x20AA type=2 → FUN_0080B3A0 → vtbl+0x240 SetCurrentHP.
/// 0x2023 only queues combat text; CharacterLevel 0x2017 only updates m_pCurrentCharacter.
/// </summary>
public static class AbsoluteHealthSync
{
    public static void Send(ClonedObjectBase victim, ClonedObjectBase attacker)
    {
        if (victim?.ObjectId == null)
            return;

        var connections = new HashSet<TNLConnection>();
        AddOwnerConnection(connections, victim);
        AddOwnerConnection(connections, attacker);
        if (connections.Count == 0)
            return;

        var packet = MultipleStatUpdatePacket.ForObjectHealth(
            victim.ObjectId, victim.GetCurrentHP());

        Guard.ForEach(
            connections,
            "absolute health stat update",
            conn => conn.SendGamePacket(packet),
            describe: conn => $"coid={conn?.GetPlayerCOID()}");
    }

    private static void AddOwnerConnection(HashSet<TNLConnection> connections, ClonedObjectBase entity)
    {
        var conn = entity?.GetAsCharacter()?.OwningConnection
                   ?? entity?.GetSuperCharacter(false)?.OwningConnection;
        if (conn != null)
            connections.Add(conn);
    }
}
