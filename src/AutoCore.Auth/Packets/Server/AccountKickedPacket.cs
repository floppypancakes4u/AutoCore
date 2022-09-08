namespace AutoCore.Auth.Packets.Server;

using AutoCore.Auth.Data;
using AutoCore.Utils.Packets;

public class AccountKickedPacket : IOpcodedPacket<ServerOpcode>
{
    public byte ReasonCode { get; set; }

    public ServerOpcode Opcode { get; } = ServerOpcode.AccountKicked;

    public AccountKickedPacket(byte reasonCode)
    {
        ReasonCode = reasonCode;
    }

    public void Read(BinaryReader reader)
    {
        ReasonCode = reader.ReadByte();
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte) Opcode);
        writer.Write(ReasonCode);
    }

    public override string ToString()
    {
        return $"AccountKickedPacket({ReasonCode})";
    }
}
