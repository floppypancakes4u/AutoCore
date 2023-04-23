namespace AutoCore.Communicator.Packets;

using AutoCore.Utils.Packets;

public class RedirectResponsePacket : IOpcodedPacket<CommunicatorOpcode>
{
    public CommunicatorOpcode Opcode { get; } = CommunicatorOpcode.RedirectResponse;

    public uint AccountId { get; set; }
    public bool Success { get; set; }

    public void Read(BinaryReader br)
    {
        AccountId = br.ReadUInt32();
        Success = br.ReadByte() != 0;
    }

    public void Write(BinaryWriter bw)
    {
        bw.Write((byte)Opcode);
        bw.Write(AccountId);
        bw.Write((byte)(Success ? 1 : 0));
    }

    public override string ToString() => $"RedirectResponsePacket(Success: {Success}, {AccountId})";
}
