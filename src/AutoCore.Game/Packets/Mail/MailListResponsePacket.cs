namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures.Mail;
using AutoCore.Utils.Extensions;

public class MailListResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailListResponse;

    public List<MailListItem> Mails { get; set; } = [];

    public override void Write(BinaryWriter writer)
    {
        if (Mails.Count > ushort.MaxValue)
            throw new InvalidOperationException("Trying to send too much mails in one packet!");

        writer.Write((ushort)Mails.Count);

        writer.BaseStream.Position += 2;

        foreach (var mail in Mails)
        {
            writer.Write(mail.MailId);
            writer.WriteUtf8StringOn(mail.Subject, 50);
            writer.WriteUtf8StringOn(mail.Message, 400);
            writer.WriteUtf8StringOn(mail.SenderName, 17);

            writer.BaseStream.Position += 5;

            writer.Write(mail.Money);
            writer.Write(mail.AttachmentId);
            writer.Write(mail.ExtraInfo);

            writer.BaseStream.Position += 7;

            writer.Write(mail.TimeRemaining);
        }
    }
}
