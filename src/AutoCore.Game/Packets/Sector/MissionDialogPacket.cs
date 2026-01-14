using TNL.Utils;

namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// MissionDialog packet - DEPRECATED/NOT USED.
/// 
/// According to client memory analysis (see MISSION_DIALOG_CLIENT_ANALYSIS.md):
/// - EMSG_Sector_MissionDialog (index 0x6C) → opcode 0x206C (server→client)
/// - EMSG_Sector_MissionDialog_Response (index 0x6D) → opcode 0x206D (client→server)
/// 
/// Mission dialogs are actually triggered via GroupReactionCallPacket (opcode 0x206C),
/// whose bit-packed payload format is documented/verified in src/MISSION_DIALOG_CLIENT_ANALYSIS.md.
///
/// This class does NOT represent the real 0x206C payload and is intentionally unusable.
/// </summary>
[Obsolete("Do not use. MissionDialog (0x206C) is GroupReactionCall, and this class does not match the real payload format.", true)]
public class MissionDialogPacket : BasePacket
{
    public override GameOpcode Opcode => throw new NotSupportedException("MissionDialogPacket is obsolete and does not match the real 0x206C payload. Use GroupReactionCallPacket.");

    /// <summary>
    /// The creature/NPC that is offering the missions.
    /// </summary>
    public TFID Creature { get; set; }
    
    private List<MissionInfo> Missions { get; } = new();

    public int MissionCount => Missions.Count;

    public bool AddMission(MissionInfo info)
    {
        if (Missions.Count >= 8)
            return false;

        Missions.Add(info);
        return true;
    }

    public override void Write(BinaryWriter writer)
    {
        var stream = new BitStream();
        
        // Write creature TFID (who is offering missions)
        stream.Write(Creature.Coid);
        stream.WriteFlag(Creature.Global);
        
        // Write mission count (8 bits, max 255 but we limit to 8)
        stream.WriteInt((uint)(Missions.Count & 0xFF), 8);
        
        // Write each mission
        foreach (var mission in Missions)
        {
            // Mission ID (32 bits)
            stream.Write(mission.Id);
            
            // 4 possible item COIDs for randomized rewards
            for (var i = 0; i < 4; ++i)
                stream.Write(mission.PossibleItemCoids[i]);
        }
        
        // Write the bit-packed buffer to the output stream
        writer.Write(stream.GetBuffer(), 0, (int)stream.GetBytePosition());
    }

    public class MissionInfo
    {
        public int Id { get; set; }
        public long[] PossibleItemCoids { get; set; } = new long[4];
    }
}
