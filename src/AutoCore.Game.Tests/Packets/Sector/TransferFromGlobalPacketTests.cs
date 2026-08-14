using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class TransferFromGlobalPacketTests
{
    [TestMethod]
    public void Opcode_IsTransferFromGlobalStage2()
    {
        Assert.AreEqual(GameOpcode.TransferFromGlobalStage2, new TransferFromGlobalPacket().Opcode);
    }

    [TestMethod]
    public void WriteRead_RoundTripsSecurityKeyAndCoid()
    {
        var original = new TransferFromGlobalPacket
        {
            SecurityKey = 0xDEADBEEFu,
            CharacterCoid = 0x1122334455667788L
        };

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            original.Write(writer);

        ms.Position = 0;
        var roundTrip = new TransferFromGlobalPacket();
        roundTrip.Read(new BinaryReader(ms));

        Assert.AreEqual(original.SecurityKey, roundTrip.SecurityKey);
        Assert.AreEqual(original.CharacterCoid, roundTrip.CharacterCoid);
    }

    [TestMethod]
    public void Stage3_OpcodeAndPositionRoundTrip()
    {
        Assert.AreEqual(GameOpcode.TransferFromGlobalStage3, new TransferFromGlobalStage3Packet().Opcode);

        var original = new TransferFromGlobalStage3Packet
        {
            SecurityKey = 1u,
            CharacterCoid = 2L,
            PositionX = 10.5f,
            PositionY = 20.5f,
            PositionZ = 30.5f
        };

        using var ms = new MemoryStream(new byte[64]);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            original.Write(writer);

        ms.Position = 0;
        var roundTrip = new TransferFromGlobalStage3Packet();
        roundTrip.Read(new BinaryReader(ms));

        Assert.AreEqual(original.SecurityKey, roundTrip.SecurityKey);
        Assert.AreEqual(original.CharacterCoid, roundTrip.CharacterCoid);
        Assert.AreEqual(original.PositionX, roundTrip.PositionX);
        Assert.AreEqual(original.PositionY, roundTrip.PositionY);
        Assert.AreEqual(original.PositionZ, roundTrip.PositionZ);
    }

    [TestMethod]
    public void Read_Truncated_Throws()
    {
        using var ms = new MemoryStream(new byte[2]);
        Assert.ThrowsException<EndOfStreamException>(() => new TransferFromGlobalPacket().Read(new BinaryReader(ms)));
    }

    /// <summary>
    /// Client FUN_00812de0 / FUN_009347b0 send 16-byte Form B frames:
    /// u32 opcode + u32 key + i64 coid. Body after opcode consume is 12 bytes.
    /// </summary>
    [TestMethod]
    public void Stage1AndStage2_BodyIsKeyThenCoid_12Bytes()
    {
        var packet = new TransferFromGlobalPacket
        {
            SecurityKey = 0xA1B2C3D4u,
            CharacterCoid = 0x1122334455667788L
        };

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);

        var bytes = ms.ToArray();
        Assert.AreEqual(12, bytes.Length);
        Assert.AreEqual(0xA1B2C3D4u, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(0x1122334455667788L, BitConverter.ToInt64(bytes, 4));
    }

    /// <summary>
    /// Client FUN_00809ad0 reads Stage3 Form B as opcode, key at +4, XYZ at +0x10/+0x14/+0x18.
    /// AutoCore writes key + coid + XYZ + 4 zero pad (28-byte body).
    /// </summary>
    [TestMethod]
    public void Stage3_BodyIsKeyCoidXyzAnd4ZeroPad()
    {
        var packet = new TransferFromGlobalStage3Packet
        {
            SecurityKey = 0x11u,
            CharacterCoid = 0x22L,
            PositionX = 1.5f,
            PositionY = 2.5f,
            PositionZ = 3.5f
        };

        using var ms = new MemoryStream(new byte[64]);
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);

        var bytes = new byte[28];
        Array.Copy(ms.ToArray(), bytes, 28);
        Assert.AreEqual(0x11u, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(0x22L, BitConverter.ToInt64(bytes, 4));
        Assert.AreEqual(1.5f, BitConverter.ToSingle(bytes, 12), 1e-5f);
        Assert.AreEqual(2.5f, BitConverter.ToSingle(bytes, 16), 1e-5f);
        Assert.AreEqual(3.5f, BitConverter.ToSingle(bytes, 20), 1e-5f);
        Assert.AreEqual(0, bytes[24]);
        Assert.AreEqual(0, bytes[25]);
        Assert.AreEqual(0, bytes[26]);
        Assert.AreEqual(0, bytes[27]);
    }
}
