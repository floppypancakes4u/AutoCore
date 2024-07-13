namespace AutoCore.Game.Packets.Login;

using AutoCore.Game.Constants;

public class LoginResponsePacket(uint result) : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.LoginResponse;

    public uint Result { get; } = result;

    public override void Write(BinaryWriter writer) => writer.Write(Result);
}
