namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class AuctionCreateResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.AuctionCreateResponse;

    public CreateError Error { get; set; }
    public long AuctionId { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Error);
        writer.Write(AuctionId);
    }

    public enum CreateError
    {
        None            = 0,
        ItemNotFound    = 2,
        ItemNotTradable = 3
    }
}
