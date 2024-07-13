namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class LoginAckPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.LoginAck;

    public bool Success { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Success);

        writer.BaseStream.Position += 3;
    }
}
