namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class MailListRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailListRequest;

    public override void Read(BinaryReader reader)
    {
    }
}
