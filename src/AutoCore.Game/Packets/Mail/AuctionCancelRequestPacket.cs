namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class AuctionCancelRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionCancelRequest;

    public long AuctionId { get; set; }

    public override void Read(BinaryReader reader)
    {
        AuctionId = reader.ReadInt64();
    }
}
