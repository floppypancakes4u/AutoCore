namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures.Auction;
using AutoCore.Utils.Extensions;

public class AuctionListResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionListResponse;

    public bool FullList { get; set; }
    public ushort TotalObjs { get; set; }
    public ushort CurrentPage { get; set; }
    public ushort NumObjs { get; set; }
    public List<AuctionListItem> Items { get; set; } = [];

    public override void Write(BinaryWriter writer)
    {
        writer.Write(FullList);

        writer.BaseStream.Position += 1;

        writer.Write(TotalObjs);
        writer.Write(CurrentPage);
        writer.Write(NumObjs);

        writer.BaseStream.Position += 2;

        foreach (var item in Items)
        {
            writer.Write(item.MailId);
            writer.WriteUtf8StringOn(item.SenderName, 17);
            writer.WriteUtf8StringOn(item.ReceiverName, 17);

            writer.BaseStream.Position += 6;

            writer.Write(item.HighBid);
            writer.Write(item.StartingBid);
            writer.Write(item.Buyout);
            writer.Write(item.AttachmentId);
            writer.Write(item.Duration);
        }
    }
}
