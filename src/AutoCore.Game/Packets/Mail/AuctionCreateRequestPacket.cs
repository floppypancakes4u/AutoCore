namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class AuctionCreateRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionCreateRequest;

    public long AttachmentId { get; set; }
    public long StartingBid { get; set; }
    public long Buyout { get; set; }
    public int Duration { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        AttachmentId = reader.ReadInt64();
        StartingBid = reader.ReadInt64();
        Buyout = reader.ReadInt64();
        Duration = reader.ReadInt32();

        reader.BaseStream.Position += 4;
    }
}
