namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class DisconnectAckPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.DisconnectAck;

    public override void Write(BinaryWriter writer)
    {
    }
}
