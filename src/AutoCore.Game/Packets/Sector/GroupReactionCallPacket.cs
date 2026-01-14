using TNL.Utils;

namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class GroupReactionCallPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.GroupReactionCall;

    private List<LogicStateChangePacket> Packets { get; } = new();

    public bool AddPacket(LogicStateChangePacket packet)
    {
        if (Packets.Count == 255)
            return false;

        Packets.Add(packet);
        return true;
    }

    // VERIFIED (see src/MISSION_DIALOG_CLIENT_ANALYSIS.md):
    // Wire payload for opcode 0x206C (EMSG_Sector_MissionDialog) is bit-packed:
    // - count: 8 bits
    // - entries[count]:
    //   - entryType: 8 bits
    //   - if entryType == 1: u16 + f32
    //   - else: u19 + u64 + flag + flag (no byte alignment between fields)
    public override void Write(BinaryWriter writer)
    {
        var stream = new BitStream();
        stream.WriteInt((uint)(Packets.Count & 0xFF), 8);

        foreach (var packet in Packets)
        {
            stream.WriteInt((uint)packet.Type, 8);

            if (packet.Type == LogicStateChangeType.Variable)
            {
                stream.WriteInt((uint)(packet.VariableId & 0xFFFF), 16);
                stream.Write(packet.Value);
            }
            else if (packet.Type == LogicStateChangeType.Reaction)
            {
                stream.WriteInt((uint)packet.ReactionCoid, 19);
                stream.Write(packet.Activator.Coid);
                stream.WriteFlag(packet.Activator.Global);
                stream.WriteFlag(packet.SingleClientOnly);
            }
            else
                throw new InvalidDataException($"Unknown LogicStateChangeType {packet.Type}!");
        }

        writer.Write(stream.GetBuffer(), 0, (int)stream.GetBytePosition());
    }
}
