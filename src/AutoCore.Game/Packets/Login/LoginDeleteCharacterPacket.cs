namespace AutoCore.Game.Packets.Login;

using AutoCore.Game.Constants;

public class LoginDeleteCharacterPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.LoginDeleteCharacter;

    public long CharacterCoid { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        CharacterCoid = reader.ReadInt64();
    }
}
