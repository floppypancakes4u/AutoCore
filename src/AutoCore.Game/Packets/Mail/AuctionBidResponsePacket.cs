namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class AuctionBidResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionBidResponse;

    public BidError Error { get; set; }
    public long AuctionId { get; set; }
    public long Bid { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Error);
        writer.Write(AuctionId);
        writer.Write(Bid);
    }

    public enum BidError
    {
        None                = 0,
        NotEnoughMoney      = 1,
        Outbid              = 2,
        AuctionNotFound     = 3,
        AuctionExpired      = 4,
        SelfAuction         = 5,
        AlreadyHighBidder   = 6,
        WrongFaction        = 7,
        BelowStartingBid    = 8,
        Unknown             = 9
    }
}
