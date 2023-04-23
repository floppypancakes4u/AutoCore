using System.Net;
using System.Net.Sockets;

namespace AutoCore.Auth.Packets.Server;

using AutoCore.Auth.Data;
using AutoCore.Communicator;
using AutoCore.Utils.Packets;

public class SendServerListExtPacket : IOpcodedPacket<ServerOpcode>
{
    public byte LastServerId { get; set; }
    public IEnumerable<ServerInfo> ServerList { get; set; }

    public ServerOpcode Opcode { get; } = ServerOpcode.SendServerListExt;

    public SendServerListExtPacket(IEnumerable<ServerInfo> servers, byte lastServerId = 0)
    {
        ServerList = servers;
        LastServerId = lastServerId;
    }

    public void Read(BinaryReader reader)
    {
        var serverList = new List<ServerInfo>();

        var count = reader.ReadByte();
        LastServerId = reader.ReadByte();

        for (var i = 0; i < count; ++i)
        {
            var serverId = reader.ReadByte();
            var addrLen = reader.ReadByte();

            serverList.Add(new ServerInfo
            {
                ServerId = serverId,
                Ip = new IPAddress(reader.ReadBytes(addrLen)),
                Port = reader.ReadInt32(),
                AgeLimit = reader.ReadByte(),
                PKFlag = reader.ReadByte(),
                CurrentPlayers = reader.ReadUInt16(),
                MaxPlayers = reader.ReadUInt16(),
                Status = reader.ReadByte()
            });
        }

        ServerList = serverList;
    }

    public void Write(BinaryWriter writer)
    {
        if (ServerList == null)
            throw new InvalidOperationException("You must specify a list of ServerInfo before you can serialize it!");

        var count = ServerList.Count();
        if (count >= 16)
            count = 16;

        writer.Write((byte) Opcode);
        writer.Write((byte) count);
        writer.Write(LastServerId);

        var c = 0U;

        foreach (var s in ServerList)
        {
            var addrLen = (byte)(s.Ip.AddressFamily == AddressFamily.InterNetwork ? 4 : 16);

            writer.Write(s.ServerId);
            writer.Write(s.Ip.GetAddressBytes());
            writer.Write(s.Port);
            writer.Write(s.AgeLimit);
            writer.Write(s.PKFlag);
            writer.Write(s.CurrentPlayers);
            writer.Write(s.MaxPlayers);
            writer.Write(s.Status);

            if (++c == count)
                break;
        }
    }

    public override string ToString() => $"SendServerListExtPacket(Count: {ServerList?.Count() ?? -1})";
}
