namespace AutoCore.Communicator.Packets;

using AutoCore.Utils.Packets;

public class ServerInfoRequestPacket : IOpcodedPacket<CommunicatorOpcode>
{
    public CommunicatorOpcode Opcode { get; } = CommunicatorOpcode.ServerInfoRequest;

    public void Read(BinaryReader br)
    {
    }

    public void Write(BinaryWriter bw)
    {
        bw.Write((byte)Opcode);
    }

    public override string ToString() => $"ServerInfoRequestPacket()";
}
