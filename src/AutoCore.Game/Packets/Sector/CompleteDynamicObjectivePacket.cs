namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Server→client mission/objective finish (opcode 0x2070).
/// Client finishes by lookup id at packet+0x10 (objective id preferred over mission id).
/// </summary>
public class CompleteDynamicObjectivePacket : BasePacket
{
    public const int LookupIdOffset = 16;

    public override GameOpcode Opcode => GameOpcode.CompleteDynamicObjective;

    public int MissionId { get; set; }
    public int ObjectiveId { get; set; }

    public override void Write(BinaryWriter writer)
    {
        var lookupId = ObjectiveId > 0 ? ObjectiveId : MissionId;
        writer.BaseStream.Position = LookupIdOffset;
        writer.Write(lookupId);
        writer.BaseStream.Position = LookupIdOffset + 4;
    }
}
