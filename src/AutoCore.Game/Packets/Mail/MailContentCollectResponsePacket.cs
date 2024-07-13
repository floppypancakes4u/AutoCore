namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class MailContentCollectResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailContentCollectResponse;

    public CollectError Error { get; set; }
    public long MailId { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Error);
        writer.Write(MailId);
    }

    public enum CollectError
    {
        None             = 0,
        NoAttachments    = 1,
        AttachmentNotFit = 2
    }
}
