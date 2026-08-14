using System.Buffers;

namespace AutoCore.Auth.Network;

using AutoCore.Auth.Crypto;
using AutoCore.Auth.Data;
using AutoCore.Auth.Packets.Client;
using AutoCore.Auth.Packets.Server;
using AutoCore.Utils;
using AutoCore.Utils.Memory;
using AutoCore.Utils.Packets;

public partial class AuthClient
{
    public void SendPacket(IBasePacket packet)
    {
        if (TestSendHook != null)
        {
            TestSendHook(packet);
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(SendBufferSize + SendBufferCryptoPadding + SendBufferChecksumPadding);

        // SS-16: the pooled buffer was returned only on the success path, so a throw from
        // packet.Write, Encrypt or Send leaked it out of the pool permanently.
        try
        {
            var writer = new BinaryWriter(new MemoryStream(buffer, true));

            packet.Write(writer);

            var length = (int)writer.BaseStream.Position;

            if (packet is not ProtocolVersionPacket)
                CryptoManager.Encrypt(buffer, 0, ref length, buffer.Length);

            Socket.Send(buffer, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Test seam: decrypt framing path using a plain buffer (already length-delimited payload).</summary>
    internal void ProcessDecryptedPayload(byte[] data, int length)
    {
        using var br = new BinaryReader(new MemoryStream(data, 0, length, false));

        var packet = CreatePacket((ClientOpcode)br.ReadByte());

        packet.Read(br);

        _packetQueue.EnqueueIncoming(packet);
    }

    /// <summary>
    /// Parses one inbound client packet.
    /// <para>
    /// SS-11: every byte here is client-controlled, including the opcode. This previously
    /// threw <see cref="ArgumentOutOfRangeException"/> on an unknown opcode from the socket
    /// receive task, which killed that client's receive loop permanently. A bad opcode is
    /// expected input at a network boundary, so the packet is now rejected and logged while
    /// the connection continues.
    /// </para>
    /// </summary>
    private void OnReceive(NonContiguousMemoryStream incomingStream, int length)
    {
        if (length < 1)
        {
            Logger.WriteLog(LogType.Warning,
                $"AuthClient {DescribePeer()} sent a {length}-byte frame with no opcode; dropping.");
            return;
        }

        var data = ArrayPool<byte>.Shared.Rent(length);

        IBasePacket packet;

        // SS-16: the pooled buffer was only returned on the success path, so a malformed
        // packet leaked it out of the pool permanently.
        try
        {
            incomingStream.Read(data, 0, length);

            CryptoManager.Decrypt(data, 0, length);

            using var br = new BinaryReader(new MemoryStream(data, 0, length, false));

            var rawOpcode = br.ReadByte();

            if (!TryCreatePacket((ClientOpcode)rawOpcode, out packet))
            {
                Logger.WriteLog(LogType.Warning,
                    $"AuthClient {DescribePeer()} sent unknown opcode 0x{rawOpcode:X2} ({length} bytes); dropping packet.");
                return;
            }

            packet.Read(br);
        }
        catch (Exception ex) when (ex is EndOfStreamException
                                      or IOException
                                      or ArgumentException
                                      or FormatException
                                      or IndexOutOfRangeException)
        {
            Logger.WriteLog(LogType.Warning,
                $"AuthClient {DescribePeer()} sent a malformed packet ({length} bytes); dropping. " +
                $"{ex.GetType().Name}: {ex.Message}");
            return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(data);
        }

        _packetQueue.EnqueueIncoming(packet);

        // Reset the timeout after every action
        Timer.ResetTimer("timeout");
    }

    /// <summary>Peer identity for diagnostics; never throws on a closed socket.</summary>
    private string DescribePeer()
    {
        try
        {
            return Socket?.RemoteAddress?.ToString() ?? "<unknown peer>";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ObjectDisposedException)
        {
            return "<disconnected peer>";
        }
    }

    /// <summary>
    /// Non-throwing opcode lookup for the network path. <see cref="CreatePacket"/> keeps its
    /// throwing contract for internal callers that treat an unknown opcode as a programming error.
    /// </summary>
    internal static bool TryCreatePacket(
        ClientOpcode opcode,
        [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IBasePacket packet)
    {
        packet = opcode switch
        {
            ClientOpcode.AboutToPlay => new AboutToPlayPacket(),
            ClientOpcode.Login => new LoginPacket(),
            ClientOpcode.Logout => new LogoutPacket(),
            ClientOpcode.ServerListExt => new ServerListExtPacket(),
            ClientOpcode.SCCheck => new SCCheckPacket(),
            _ => null,
        };

        return packet != null;
    }

    internal static IBasePacket CreatePacket(ClientOpcode opcode)
    {
        if (!TryCreatePacket(opcode, out var packet))
            throw new ArgumentOutOfRangeException(nameof(opcode));

        return packet;
    }
}
