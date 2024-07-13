namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;

public class MailCreateResponsePacket : BaseMailPacket
{
    public override MailOpcode Opcode => MailOpcode.MailCreateResponse;

    public CreateError Error { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write((uint)Error);
    }

    public enum CreateError
    {
        None            = 0,
        NoItem          = 2,
        TargetIsSender  = 4,
        TargetNotExist  = 5,
        TargetWrongType = 6,
        SaveFailuer     = 7
    }
}
