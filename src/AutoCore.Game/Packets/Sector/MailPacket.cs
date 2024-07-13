namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Mail;

public class MailPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.SectorMail;

    public long CoidCharacter { get; set; }
    public BaseMailPacket SubPacket { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;

        writer.Write(CoidCharacter);

        writer.BaseStream.Position += 8;

        writer.Write((uint)SubPacket.Opcode);

        SubPacket.Write(writer);
    }
}
