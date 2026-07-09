namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Client → server first-time tips/hints bitmap (opcode <see cref="GameOpcode.UpdateFirstTimeFlagsRequest"/> = 0x20B1).
/// Client sender: FUN_0092c6d0; total size 0x14 including opcode.
/// Layout after opcode: 4×uint32. Full replace of account flags.
/// Bit 31 of <see cref="FirstFlags1"/> is the hide-tips checkbox; tip N is bit (N%32) of dword (N/32).
/// </summary>
public class UpdateFirstTimeFlagsRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.UpdateFirstTimeFlagsRequest;

    public uint FirstFlags1 { get; set; }
    public uint FirstFlags2 { get; set; }
    public uint FirstFlags3 { get; set; }
    public uint FirstFlags4 { get; set; }

    public override void Read(BinaryReader reader)
    {
        // Opcode already consumed by TNLConnection.HandlePacket.
        FirstFlags1 = reader.ReadUInt32();
        FirstFlags2 = reader.ReadUInt32();
        FirstFlags3 = reader.ReadUInt32();
        FirstFlags4 = reader.ReadUInt32();
    }
}
