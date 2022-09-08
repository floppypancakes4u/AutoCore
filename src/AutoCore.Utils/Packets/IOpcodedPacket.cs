namespace AutoCore.Utils.Packets;

public interface IOpcodedPacket<out T> : IBasePacket
{
    T Opcode { get; }
}