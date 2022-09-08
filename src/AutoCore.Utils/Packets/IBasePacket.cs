namespace AutoCore.Utils.Packets;

public interface IBasePacket
{
    void Read(BinaryReader reader);

    void Write(BinaryWriter writer);
}
