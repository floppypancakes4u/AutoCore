namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class MissionDialogResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MissionDialogResponse;

    public int MissionId { get; set; }
    public long MixedVar { get; set; }
    public TFID MissionGiver { get; set; }

    public override void Read(BinaryReader reader)
    {
        MissionId = reader.ReadInt32();
        MixedVar = reader.ReadInt64();
        MissionGiver = reader.ReadTFID();
    }

    public override void Write(BinaryWriter writer)
    {
        throw new NotSupportedException();
    }
}
