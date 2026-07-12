namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Client → server cast-cancel request (0x2032): pad4 + target TFID + skillId.
/// This packet is intentionally not routed until active-cast state exists.
/// </summary>
public sealed class CancelSkillPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.CancelSkill;

    public TFID Target { get; private set; } = new();
    public int SkillId { get; private set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;
        Target = reader.ReadTFID();
        SkillId = reader.ReadInt32();
    }
}
