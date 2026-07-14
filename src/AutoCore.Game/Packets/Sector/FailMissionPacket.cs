namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Fail / abandon mission (opcode 0x20B2). Bidirectional:
/// C2S journal abandon confirm, S2C force-fail client journal (CVOGReaction_FailMission).
/// Wire after opcode: pad4 + CharacterCoid i64 + MissionId i32 + pad4 (0x18 total).
/// </summary>
public class FailMissionPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.FailMission;

    public int MissionId { get; set; }
    public long CharacterCoid { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;
        CharacterCoid = reader.ReadInt64();
        MissionId = reader.ReadInt32();
        reader.BaseStream.Position += 4;
    }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;

        writer.Write(CharacterCoid);
        writer.Write(MissionId);

        writer.BaseStream.Position += 4;
    }
}
