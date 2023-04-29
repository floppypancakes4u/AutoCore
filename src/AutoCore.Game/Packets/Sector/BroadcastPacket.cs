namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class BroadcastPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Broadcast;

    public ChatType ChatType { get; set; }
    public ulong SenderCoid { get; set; }
    public bool IsGM { get; set; }
    public short MessageLength { get; set; }
    public string Sender { get; set; }
    public string Message { get; set; }

    public override void Read(BinaryReader reader)
    {
        ChatType = (ChatType)reader.ReadUInt32();
        SenderCoid = reader.ReadUInt64();
        IsGM = reader.ReadBoolean();

        reader.BaseStream.Position += 1;

        MessageLength = reader.ReadInt16();
        Sender = reader.ReadUTF8StringOn(17);
        Message = reader.ReadUTF8NullString(MessageLength);
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)ChatType);
        writer.Write(SenderCoid);
        writer.Write(IsGM);

        writer.BaseStream.Position += 1;

        writer.Write(MessageLength);
        writer.WriteUtf8StringOn(Sender, 17);
        writer.WriteUtf8NullString(Message);
    }
}
