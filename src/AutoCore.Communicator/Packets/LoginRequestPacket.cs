using System.Net;
using System.Net.Sockets;

namespace AutoCore.Communicator.Packets;

using AutoCore.Utils.Extensions;
using AutoCore.Utils.Packets;

public class LoginRequestPacket : IOpcodedPacket<CommunicatorOpcode>
{
    public CommunicatorOpcode Opcode { get; } = CommunicatorOpcode.LoginRequest;
    public ServerData Data { get; set; }
    public ServerInfoResponsePacket InfoPacket { get; set; }

    public LoginRequestPacket()
    {
        Data = new();
        InfoPacket = new();
    }

    public LoginRequestPacket(ServerData data, ServerInfo info)
    {
        Data = data;
        InfoPacket = new(info);
    }

    public void Read(BinaryReader reader)
    {
        Data.Id = reader.ReadByte();
        Data.Password = reader.ReadLengthedString();
        Data.Address = new IPAddress(reader.ReadBytes(reader.ReadByte()));
        Data.Port = reader.ReadInt32();

        _ = reader.ReadByte(); // skip opcode of inlined packet
        InfoPacket.Read(reader);
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)Opcode);
        writer.Write(Data.Id);
        writer.WriteLengthedString(Data.Password);
        writer.Write((byte)(Data.Address!.AddressFamily == AddressFamily.InterNetwork ? 4 : 16));
        writer.Write(Data.Address.GetAddressBytes());
        writer.Write(Data.Port);

        InfoPacket.Write(writer);
    }

    public override string ToString() => $"LoginRequestPacket({Data.Id}, {Data.Address}, {Data.Password})";
}
