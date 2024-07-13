using AutoCore.Game.Constants;

namespace AutoCore.Game.Packets.Mail;

public class MailDeleteResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailDeleteResponse;

    public DeleteError Error { get; set; }
    public long MailId { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Error);
        writer.Write(MailId);
    }

    public enum DeleteError
    {
        None = 0,
        Attachments = 1,
        MailNotFound = 2
    }
}
