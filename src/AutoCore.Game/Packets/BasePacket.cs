namespace AutoCore.Game.Packets;

using AutoCore.Game.Constants;
using AutoCore.Utils.Packets;

public abstract class BasePacket : IOpcodedPacket<GameOpcode>
{
    public abstract GameOpcode Opcode { get; }

    public abstract void Read(BinaryReader reader);
    public abstract void Write(BinaryWriter writer);
}
