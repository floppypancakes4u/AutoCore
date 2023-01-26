namespace AutoCore.Auth.Packets.Client;

using AutoCore.Auth.Data;
using AutoCore.Utils.Packets;

public class SCCheckPacket : IOpcodedPacket<ClientOpcode>
{
    public uint UserId { get; set; }
    public uint CardValue { get; set; }

    public ClientOpcode Opcode { get; } = ClientOpcode.SCCheck;

    public void Read(BinaryReader reader)
    {
        UserId = reader.ReadUInt32();
        CardValue = reader.ReadUInt32();
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte) Opcode);
        writer.Write(UserId);
        writer.Write(CardValue);
    }

    public override string ToString() => $"SCCheckPacket({UserId}, {CardValue})";
}
