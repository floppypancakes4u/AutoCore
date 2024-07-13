namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class FailMissionPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.FailMission;

    public int MissionId { get; set; }
    public long CharacterCoid { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;

        writer.Write(CharacterCoid);
        writer.Write(MissionId);

        writer.BaseStream.Position += 4;
    }
}
