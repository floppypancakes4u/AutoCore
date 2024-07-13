namespace AutoCore.Game.Structures.Auction;

public class AuctionItemCriteria
{
    public sbyte Race { get; set; }
    public sbyte Class { get; set; }
    public sbyte MinLevel { get; set; }
    public sbyte MaxLevel { get; set; }
    public sbyte ItemType { get; set; }
    public sbyte ItemSubType { get; set; }
    public sbyte LanguageId { get; set; }
    public sbyte BrokenFilter { get; set; }
    public long MinValue { get; set; }
    public long MaxValue { get; set; }
    public long CoidSeller { get; set; }
    public int AuctionHouseFaction { get; set; }
    public string SellerName { get; set; }
    public string ItemName { get; set; }
}
