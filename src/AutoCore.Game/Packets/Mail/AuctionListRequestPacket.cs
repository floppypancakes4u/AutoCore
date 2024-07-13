namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures.Auction;
using AutoCore.Utils.Extensions;

public class AuctionListRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionListRequest;

    public int Index { get; set; }
    public AuctionItemCriteria Criteria { get; set; } = new();

    public override void Read(BinaryReader reader)
    {
        Index = reader.ReadInt32();

        Criteria.Race = reader.ReadSByte();
        Criteria.Class = reader.ReadSByte();
        Criteria.MinLevel = reader.ReadSByte();
        Criteria.MaxLevel = reader.ReadSByte();
        Criteria.ItemType = reader.ReadSByte();
        Criteria.ItemSubType = reader.ReadSByte();
        Criteria.LanguageId = reader.ReadSByte();
        Criteria.BrokenFilter = reader.ReadSByte();
        Criteria.MinValue = reader.ReadInt64();
        Criteria.MaxValue = reader.ReadInt64();
        Criteria.CoidSeller = reader.ReadInt64();
        Criteria.AuctionHouseFaction = reader.ReadInt32();
        Criteria.SellerName = reader.ReadUTF8StringOn(17);
        Criteria.ItemName = reader.ReadUTF8StringOn(128);

        reader.BaseStream.Position += 3;
    }
}
