namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class AuctionCancelResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionCancelResponse;

    public CancelError Error { get; set; }
    public long AuctionId { get; set; }
    public long CancelId { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Error);
        writer.Write(AuctionId);
        writer.Write(CancelId);
    }

    public enum CancelError
    {
        None          = 0,
        NotOwnAuction = 2,
        AuctionHasBid = 3
    }
}
