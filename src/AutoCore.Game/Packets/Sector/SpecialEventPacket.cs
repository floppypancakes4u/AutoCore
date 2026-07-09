namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Server → client special presentation event (opcode 0x20A9).
/// Type 0 drives ClientSpecialEvent_Respawn (INC airlift + mid-anim teleport).
/// Layout verified against client FUN_0080cc50 field offsets.
/// </summary>
public enum SpecialEventType : byte
{
    Respawn = 0,
    TeleportOut = 1,
    TeleportIn = 2
}

public class SpecialEventPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.SpecialEvent;

    public SpecialEventType Type { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public TFID Target { get; set; }
    /// <summary>Non-zero enables full Respawn special-event path (client ctor flag at +0x40).</summary>
    public int Flag { get; set; } = 1;

    public override void Write(BinaryWriter writer)
    {
        // Full packet offsets (opcode written by SendGamePacket first):
        // +0x04 type (byte) + pad
        // +0x08 position
        // +0x14 quaternion
        // +0x24 pad 4
        // +0x28 TFID (16)
        // +0x38 pad 8
        // +0x40 flag
        writer.Write((byte)Type);
        writer.BaseStream.Position += 3;

        writer.Write(Position.X);
        writer.Write(Position.Y);
        writer.Write(Position.Z);

        writer.Write(Rotation.X);
        writer.Write(Rotation.Y);
        writer.Write(Rotation.Z);
        writer.Write(Rotation.W);

        writer.BaseStream.Position += 4;

        writer.WriteTFID(Target ?? new TFID());

        writer.BaseStream.Position += 8;

        writer.Write(Flag);
    }
}
