namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class RequestClanNameResponsePacket(long characterCoid, string clanName) : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.RequestClanNameResponse;

    public long CharacterCoid { get; set; } = characterCoid;
    public string ClanName { get; set; } = clanName;

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;

        writer.Write(CharacterCoid);
        writer.WriteUtf8StringOn(ClanName, 52);

        writer.BaseStream.Position += 4;
    }
}
