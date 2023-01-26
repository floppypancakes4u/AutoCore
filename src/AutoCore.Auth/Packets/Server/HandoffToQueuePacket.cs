namespace AutoCore.Auth.Packets.Server;

using AutoCore.Auth.Data;
using AutoCore.Utils.Packets;

public class HandoffToQueuePacket : IOpcodedPacket<ServerOpcode>
{
    public uint OneTimeKey { get; set; }
    public uint AccountId { get; set; }
    public byte ServerId { get; set; }

    public ServerOpcode Opcode { get; } = ServerOpcode.HandOffToQueue;

    public void Read(BinaryReader reader)
    {
        OneTimeKey = reader.ReadUInt32();
        AccountId = reader.ReadUInt32();
        ServerId = reader.ReadByte();
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte) Opcode);
        writer.Write(OneTimeKey);
        writer.Write(AccountId);
        writer.Write(ServerId);
    }

    public override string ToString() => $"HandoffToQueuePacket({OneTimeKey}, {AccountId}, {ServerId})";
}
