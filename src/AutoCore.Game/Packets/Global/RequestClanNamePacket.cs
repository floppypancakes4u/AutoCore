namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class RequestClanNamePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.RequestClanName;

    public long CharacterCoid { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        CharacterCoid = reader.ReadInt64();
    }
}
