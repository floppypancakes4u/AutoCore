namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

/// <summary>
/// Server → client response to <see cref="ConvoyMissionsRequestPacket"/> (GameOpcode 0x8010).
/// Currently sends the same 72-byte-per-quest structures used in CreateCharacterExtended.
/// </summary>
public class ConvoyMissionsResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ConvoyMissionsResponse;

    public List<CharacterQuest> CurrentQuests { get; set; } = [];

    public override void Write(BinaryWriter writer)
    {
        writer.Write(CurrentQuests.Count);

        foreach (var quest in CurrentQuests)
            quest.Write(writer);
    }
}








