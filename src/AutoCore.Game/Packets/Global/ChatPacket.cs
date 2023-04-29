namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class ChatPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Chat;

    public ChatType ChatType { get; set; }
    public bool IsGM { get; set; }
    public string PrivateRecipientName { get; set; }
    public string Sender { get; set; }
    public short MessageLength { get; set; }
    public string Message { get; set; }

    public override void Read(BinaryReader reader)
    {
        ChatType = (ChatType)reader.ReadUInt32();
        IsGM = reader.ReadBoolean();
        PrivateRecipientName = reader.ReadUTF8StringOn(17);
        Sender = reader.ReadUTF8StringOn(17);

        reader.BaseStream.Position += 1;

        MessageLength = reader.ReadInt16();
        Message = reader.ReadUTF8NullString(MessageLength);
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)ChatType);
        writer.Write(IsGM);
        writer.WriteUtf8StringOn(PrivateRecipientName, 17);
        writer.WriteUtf8StringOn(Sender, 17);

        writer.BaseStream.Position += 1;

        writer.Write(MessageLength);
        writer.WriteUtf8NullString(Message);
    }
}
