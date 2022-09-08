namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class LoginPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Login;

    public long CharacterCoid { get; set; }
    public int StartSectorOverride { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        CharacterCoid = reader.ReadInt64();
        StartSectorOverride = reader.ReadInt32();
    }

    public override void Write(BinaryWriter writer)
    {
        throw new NotImplementedException();
    }
}
