namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

/// <summary>
/// Client → server INC airlift request (opcode 0x2073).
/// Client sends current vehicle pose + vehicle COID; destination is server-authoritative
/// from the character's last marked repair station.
/// Full wire size including opcode: 0x28 bytes (FUN_00935300).
/// </summary>
public class RespawnInSectorPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.RespawnInSector;

    /// <summary>Current vehicle position (informational; not used as dest).</summary>
    public Vector3 Position { get; set; }

    /// <summary>Current vehicle rotation (informational).</summary>
    public Quaternion Rotation { get; set; }

    /// <summary>Vehicle object COID (8-byte TFID coid half).</summary>
    public long VehicleCoid { get; set; }

    public override void Read(BinaryReader reader)
    {
        // Opcode already consumed by TNLConnection.HandlePacket.
        Position = Vector3.ReadNew(reader);
        Rotation = Quaternion.Read(reader);
        VehicleCoid = reader.ReadInt64();
    }
}
