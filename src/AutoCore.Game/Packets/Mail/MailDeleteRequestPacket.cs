namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class MailDeleteRequestPacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailDeleteRequest;

    public long MailId { get; set; }
    public long ReceiverId { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        MailId = reader.ReadInt64();
        ReceiverId = reader.ReadInt64();
    }
}
