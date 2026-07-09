namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Client→server patrol waypoint notify (opcode 0x20B3).
/// Client_EvalAutoPatrolWaypoint @ 0x00929EC0: pad4 + TFID16 of the waypoint object.
/// </summary>
public class AutoPatrolPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.AutoPatrol;

    /// <summary>World object TFID for the patrol waypoint the client thinks it reached.</summary>
    public TFID Target { get; set; } = new();

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4; // pad (may contain residual float; ignore)
        Target = reader.ReadTFID();
    }
}
