namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class CreateSkillHeartbeat : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.CreateSkillHeartbeat;

    public int LastTickCount { get; set; }
    public int DiceSeed { get; set; }
    public ushort SkillId { get; set; }
    public short SkillLevel { get; set; }
    public TFID Target { get; set; }
    public bool ForceDeath { get; set; }
    public byte SkillType { get; set; }
    public short DurationCountdown { get; set; }
    public TFID Caster { get; set; }

    public override void Read(BinaryReader reader)
    {
        LastTickCount = reader.ReadInt32();
        DiceSeed = reader.ReadInt32();
        SkillId = reader.ReadUInt16();
        SkillLevel = reader.ReadInt16();
        Target = reader.ReadTFID();
        ForceDeath = reader.ReadBoolean();
        SkillType = reader.ReadByte();
        DurationCountdown = reader.ReadInt16();

        reader.BaseStream.Position += 4;

        Caster = reader.ReadTFID();
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(LastTickCount);
        writer.Write(DiceSeed);
        writer.Write(SkillId);
        writer.Write(SkillLevel);
        writer.WriteTFID(Target);
        writer.Write(ForceDeath);
        writer.Write(SkillType);
        writer.Write(DurationCountdown);

        writer.BaseStream.Position += 4;

        writer.WriteTFID(Caster);
    }
}
