namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class MailNotificationPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailNotification;

    public NotifyState Notification { get; set; }
    public long MailId { get; set; }
    public long ReceiverId { get; set; }
    public long AttachmentId { get; set; }
    public int AttachmentType { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Notification);
        writer.Write(MailId);
        writer.Write(ReceiverId);
        writer.Write(AttachmentId);
        writer.Write(AttachmentType);

        writer.BaseStream.Position += 4;
    }

    public enum NotifyState
    {
        NewMail          = 0,
        AuctionUpdate    = 1,
        AuctionWon       = 2,
        AuctionSold      = 3,
        AuctionOutbid    = 4,
        AuctionCancelled = 6
    }
}
