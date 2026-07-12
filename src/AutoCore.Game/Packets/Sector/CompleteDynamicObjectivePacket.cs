namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Server→client mission/objective finish (opcode 0x2070).
/// Client dispatch: Client_RecvObjectiveState @ 0x0080FF00 always calls
/// CVOGReaction_CompleteObjective(lookupId @ +0x10, -1, -1, force=1) then bulk UI refresh.
/// Prefer objective id over mission id for the lookup field. Do not send on dialog turn-in
/// (client already completed locally via MissionDialogHandleButton).
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
