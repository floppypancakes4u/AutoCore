namespace AutoCore.Game.Structures.Auction;

public class AuctionListItem
{
    public long MailId { get; set; }
    public string SenderName { get; set; }
    public string ReceiverName { get; set; }
    public long HighBid { get; set; }
    public long StartingBid { get; set; }
    public long Buyout { get; set; }
    public long AttachmentId { get; set; }
    public int Duration { get; set; }
}
