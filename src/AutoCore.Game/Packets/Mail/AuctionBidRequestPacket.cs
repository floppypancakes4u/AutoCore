namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class AuctionBidRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionBidRequest;

    public long AuctionId { get; set; }
    public long Bid { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        AuctionId = reader.ReadInt64();
        Bid = reader.ReadInt64();
    }
}
