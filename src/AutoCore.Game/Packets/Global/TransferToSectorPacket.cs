using System.Net;
using System.Net.Sockets;

namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class TransferToSectorPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.TransferToSector;

    public IPAddress IPAddress { get; set; }
    public uint Port { get; set; }
    public uint Flags { get; set; }

    public override void Write(BinaryWriter writer)
    {
        if (IPAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new Exception("Unsupported AddressFamily!");

        writer.Write(IPAddress.GetAddressBytes().Reverse().ToArray());
        writer.Write(Port);
        writer.Write(Flags);
    }
}
