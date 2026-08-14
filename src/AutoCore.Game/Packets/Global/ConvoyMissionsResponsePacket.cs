namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

/// <summary>
/// Server → client <see cref="GameOpcode.ConvoyMissionsResponse"/> (0x8010).
/// Retail Form B body after the opcode prefix:
/// pad4 + i64 coidMember + u16 count + pad2 + u32 pointer slot + u16[] missionIds.
/// The client convoy UI consumes mission IDs only; journal blobs live on CreateCharacterExtended.
/// </summary>
public class ConvoyMissionsResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ConvoyMissionsResponse;

    public long CoidMember { get; set; }

    public List<int> MissionIds { get; set; } = [];

    public override void Write(BinaryWriter writer)
    {
        writer.Write(0);
        writer.Write(CoidMember);

        var count = MissionIds?.Count ?? 0;
        writer.Write((ushort)count);
        writer.Write((ushort)0);
        writer.Write(0);

        if (MissionIds == null)
            return;

        foreach (var missionId in MissionIds)
            writer.Write((ushort)missionId);
    }
}
