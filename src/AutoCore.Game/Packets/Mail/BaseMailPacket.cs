namespace AutoCore.Game.Packets.Mail;

using AutoCore.Game.Constants;
using AutoCore.Utils.Packets;

public abstract class BaseMailPacket : IOpcodedPacket<MailOpcode>
{
    public abstract MailOpcode Opcode { get; }

    public virtual void Read(BinaryReader reader) => throw new NotSupportedException();
    public virtual void Write(BinaryWriter writer) => throw new NotSupportedException();
}
