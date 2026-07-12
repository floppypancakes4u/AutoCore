namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Client → server skill-cast intent (0x2030). Layout verified from
/// <c>SMSG_Sector_RequestCastSkill</c> / client packet construction:
/// pad4 + target TFID + skillId + target position.
/// This packet is intentionally not routed until learned-skill state and cast validation exist.
/// </summary>
public sealed class RequestCastSkillPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.RequestCastSkill;

    public TFID Target { get; private set; } = new();
    public int SkillId { get; private set; }
    public Vector3 TargetPosition { get; private set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;
        Target = reader.ReadTFID();
        SkillId = reader.ReadInt32();
        TargetPosition = Vector3.ReadNew(reader);
    }
}
