namespace AutoCore.Communicator.Packets;

using AutoCore.Utils.Extensions;
using AutoCore.Utils.Packets;

public class RedirectRequestPacket : IOpcodedPacket<CommunicatorOpcode>
{
    public CommunicatorOpcode Opcode { get; } = CommunicatorOpcode.RedirectRequest;

    public RedirectRequest Request { get; }

    public RedirectRequestPacket()
    {
        Request = new();
    }

    public RedirectRequestPacket(RedirectRequest request)
    {
        Request = request;
    }

    public void Read(BinaryReader br)
    {
        Request.AccountId = br.ReadUInt32();
        Request.Username = br.ReadLengthedString();
        Request.Email = br.ReadLengthedString();
        Request.OneTimeKey = br.ReadUInt32();
    }

    public void Write(BinaryWriter bw)
    {
        bw.Write((byte)Opcode);
        bw.Write(Request.AccountId);
        bw.WriteLengthedString(Request.Username);
        bw.WriteLengthedString(Request.Email);
        bw.Write(Request.OneTimeKey);
    }
}
