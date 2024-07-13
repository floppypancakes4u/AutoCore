namespace AutoCore.Game.Packets;

using AutoCore.Game.Constants;
using AutoCore.Utils.Packets;

public abstract class BasePacket : IOpcodedPacket<GameOpcode>
{
    public abstract GameOpcode Opcode { get; }

    public virtual void Read(BinaryReader reader) => throw new NotSupportedException();
    public virtual void Write(BinaryWriter writer) => throw new NotSupportedException();
}
