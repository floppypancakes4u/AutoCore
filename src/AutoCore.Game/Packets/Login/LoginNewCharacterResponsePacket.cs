namespace AutoCore.Game.Packets.Login;

using AutoCore.Game.Constants;

public class LoginNewCharacterResponsePacket(uint result, long coid) : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.LoginNewCharacterResponse;

    public uint Result { get; set; } = result;
    public long NewCharCoid { get; set; } = coid;

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Result);
        writer.Write(NewCharCoid);
    }
}
