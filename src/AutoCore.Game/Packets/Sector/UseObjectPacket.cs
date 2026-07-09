namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Client→server object/NPC use (opcode 0x2072).
/// Ghidra Client_SendUseObject @ 0x00916740: body after opcode = pad4 + TFID16 + IDObjective i32.
/// </summary>
public class UseObjectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.UseObject;

    public TFID Target { get; set; } = new();
    public int ObjectiveId { get; set; } = -1;

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4; // pad
        Target = reader.ReadTFID();
        ObjectiveId = reader.ReadInt32();
    }
}
