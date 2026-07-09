namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Client→server mission dialog OK/accept (opcode 0x206E).
/// Ghidra Client_NpcDialog_PrepareResponseOpcode @ 0x008abd70 sets dialog+0x650 = 0x206E.
/// Body after opcode strip: missionId i32 + accepted bool + pad3 + pad4 + npc TFID16.
/// </summary>
public class MissionDialogResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MissionDialogResponse;

    public int MissionId { get; set; }
    public bool Accepted { get; set; }
    public TFID MissionGiver { get; set; } = new();

    public override void Read(BinaryReader reader)
    {
        MissionId = reader.ReadInt32();
        Accepted = reader.ReadBoolean();
        reader.BaseStream.Position += 3;
        reader.BaseStream.Position += 4;
        MissionGiver = reader.ReadTFID();
    }
}
