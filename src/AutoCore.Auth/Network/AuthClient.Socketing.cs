using System.Buffers;

namespace AutoCore.Auth.Network;

using AutoCore.Auth.Crypto;
using AutoCore.Auth.Data;
using AutoCore.Auth.Packets.Client;
using AutoCore.Auth.Packets.Server;
using AutoCore.Utils.Memory;
using AutoCore.Utils.Packets;

public partial class AuthClient
{
    public void SendPacket(IBasePacket packet)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(SendBufferSize + SendBufferCryptoPadding + SendBufferChecksumPadding);
        var writer = new BinaryWriter(new MemoryStream(buffer, true));

        packet.Write(writer);

        var length = (int)writer.BaseStream.Position;

        if (packet is not ProtocolVersionPacket)
            CryptoManager.Encrypt(buffer, 0, ref length, buffer.Length);

        Socket.Send(buffer, 0, length);

        ArrayPool<byte>.Shared.Return(buffer);
    }

    private void OnReceive(NonContiguousMemoryStream incomingStream, int length)
    {
        var data = ArrayPool<byte>.Shared.Rent(length);

        incomingStream.Read(data, 0, length);

        CryptoManager.Decrypt(data, 0, length);

        using var br = new BinaryReader(new MemoryStream(data, 0, length, false));

        var packet = CreatePacket((ClientOpcode)br.ReadByte());

        packet.Read(br);

        ArrayPool<byte>.Shared.Return(data);

        _packetQueue.EnqueueIncoming(packet);

        // Reset the timeout after every action
        Timer.ResetTimer("timeout");
    }

    private static IBasePacket CreatePacket(ClientOpcode opcode)
    {
        return opcode switch
        {
            ClientOpcode.AboutToPlay => new AboutToPlayPacket(),
            ClientOpcode.Login => new LoginPacket(),
            ClientOpcode.Logout => new LogoutPacket(),
            ClientOpcode.ServerListExt => new ServerListExtPacket(),
            ClientOpcode.SCCheck => new SCCheckPacket(),
            _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
        };
    }
}
