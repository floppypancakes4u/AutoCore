using System.Net;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Mail;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Global;

[TestClass]
public class TransferAndMailEnvelopePacketTests
{
    [TestMethod]
    public void TransferToSector_Write_IPv4PortFlags()
    {
        Assert.AreEqual(GameOpcode.TransferToSector, new TransferToSectorPacket().Opcode);

        var packet = new TransferToSectorPacket
        {
            IPAddress = IPAddress.Parse("127.0.0.1"),
            Port = 27001,
            Flags = 3
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);

        var bytes = ms.ToArray();
        // Bytes reversed from GetAddressBytes
        Assert.AreEqual(1, bytes[0]);
        Assert.AreEqual(0, bytes[1]);
        Assert.AreEqual(0, bytes[2]);
        Assert.AreEqual(127, bytes[3]);
        Assert.AreEqual(27001u, BitConverter.ToUInt32(bytes, 4));
        Assert.AreEqual(3u, BitConverter.ToUInt32(bytes, 8));
        Assert.AreEqual(12, bytes.Length, "Client FUN_00816100 reads only IP u32 + port u32; flags u32 is unused but present");
    }

    /// <summary>
    /// Client FUN_0092d900 formats the IP dword as (>>24).(>>16).(>>8).(&amp;0xFF).
    /// AutoCore's reversed GetAddressBytes() on a little-endian write produces that dword.
    /// </summary>
    [TestMethod]
    public void TransferToSector_ReversedIpv4_FormatsAsClientDottedDecimal()
    {
        var packet = new TransferToSectorPacket
        {
            IPAddress = IPAddress.Parse("192.168.1.10"),
            Port = 27001,
            Flags = 0
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        var ipDword = BitConverter.ToUInt32(bytes, 0);
        Assert.AreEqual(192u, ipDword >> 24);
        Assert.AreEqual(168u, (ipDword >> 16) & 0xFF);
        Assert.AreEqual(1u, (ipDword >> 8) & 0xFF);
        Assert.AreEqual(10u, ipDword & 0xFF);
        Assert.AreEqual(27001u, BitConverter.ToUInt32(bytes, 4));
    }

    [TestMethod]
    public void TransferToSector_NonIPv4_Throws()
    {
        var packet = new TransferToSectorPacket
        {
            IPAddress = IPAddress.IPv6Loopback,
            Port = 1,
            Flags = 0
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        Assert.ThrowsException<Exception>(() => packet.Write(writer));
    }

    [TestMethod]
    public void GlobalMailPacket_Write_WrapsSubPacket()
    {
        Assert.AreEqual(GameOpcode.Mail, new MailPacket().Opcode);

        var sub = new MailCreateResponsePacket
        {
            Error = MailCreateResponsePacket.CreateError.None
        };
        var packet = new MailPacket
        {
            CoidCharacter = 0x1234L,
            SubPacket = sub
        };

        using var ms = new MemoryStream(new byte[64]);
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        reader.ReadInt32(); // pad
        Assert.AreEqual(0x1234L, reader.ReadInt64());
        reader.ReadInt64(); // pad 8
        Assert.AreEqual((uint)MailOpcode.MailCreateResponse, reader.ReadUInt32());
        Assert.AreEqual(0u, reader.ReadUInt32()); // CreateError.None
    }

    [TestMethod]
    public void ConvoyMissionsRequest_Read_NoOp()
    {
        Assert.AreEqual(GameOpcode.ConvoyMissionsRequest, new ConvoyMissionsRequestPacket().Opcode);
        new ConvoyMissionsRequestPacket().Read(new BinaryReader(new MemoryStream()));
    }

    [TestMethod]
    public void ConvoyMissionsResponse_Write_EmptyAndOneQuest()
    {
        Assert.AreEqual(GameOpcode.ConvoyMissionsResponse, new ConvoyMissionsResponsePacket().Opcode);

        var empty = new ConvoyMissionsResponsePacket();
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            empty.Write(writer);
            Assert.AreEqual(0, BitConverter.ToInt32(ms.ToArray(), 0));
        }

        var packet = new ConvoyMissionsResponsePacket
        {
            CurrentQuests = [new CharacterQuest(1001)]
        };
        using var outMs = new MemoryStream();
        using var outWriter = new BinaryWriter(outMs);
        packet.Write(outWriter);

        var bytes = outMs.ToArray();
        Assert.AreEqual(1, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(1001, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(4 + CharacterQuest.StructureSize, bytes.Length);
    }
}
