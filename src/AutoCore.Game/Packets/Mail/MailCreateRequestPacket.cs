namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class MailCreateRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailCreateRequest;

    public string Subject { get; set; }
    public string Message { get; set; }
    public string ReceiverName { get; set; }
    public long Money { get; set; }
    public long AttachmentId { get; set; }

    public override void Read(BinaryReader reader)
    {
        Subject = reader.ReadUTF8StringOn(50);
        Message = reader.ReadUTF8StringOn(400);
        ReceiverName = reader.ReadUTF8StringOn(17);

        reader.BaseStream.Position += 1;

        Money = reader.ReadInt64();
        AttachmentId = reader.ReadInt64();
    }
}
