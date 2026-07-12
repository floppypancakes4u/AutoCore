using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Locks CreateCreature (0x2013) layout against client FUN_0080af70 / CVOGCreature_PostCreateFromPacket:
/// vehicle link at +0xF8, level at +0x114 (offsets include root opcode).
/// </summary>
[TestClass]
public class CreateCreatureLayoutTests
{
    public const int ClientEnhancementIdOffset = CreateCreaturePacket.ClientEnhancementIdOffset;
    public const int ClientVehicleCoidOffset = CreateCreaturePacket.ClientVehicleCoidOffset;
    public const int ClientLevelOffset = CreateCreaturePacket.ClientLevelOffset;
    public const int ClientCreateCreatureSize = CreateCreaturePacket.ClientCreateCreatureSize;

    [TestMethod]
    public void Write_PlacesVehicleCoidAndLevelAtClientOffsets()
    {
        const long driverCoid = 0x5000_1234L;
        const long vehicleCoid = 0x5000_9999L;
        var bytes = SerializeWithRootOpcode(new CreateCreaturePacket
        {
            CBID = 12001,
            ObjectId = new TFID(driverCoid, true),
            CoidCurrentVehicle = vehicleCoid,
            Level = 7,
            EnhancementId = -1,
        });

        Assert.AreEqual(ClientCreateCreatureSize, bytes.Length,
            "CreateCreature including root opcode must match ClientCreateCreatureSize.");
        Assert.AreEqual((uint)GameOpcode.CreateCreature, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(12001, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(driverCoid, BitConverter.ToInt64(bytes, 0x90));
        Assert.AreEqual(1, bytes[0x98], "TFID.Global at nest/simple ObjectId layout");
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientEnhancementIdOffset));
        Assert.AreEqual(vehicleCoid, BitConverter.ToInt64(bytes, ClientVehicleCoidOffset));
        Assert.AreEqual(7, BitConverter.ToInt32(bytes, ClientLevelOffset));
    }

    [TestMethod]
    public void Write_NoVehicleLink_WritesMinusOneAtVehicleOffset()
    {
        var bytes = SerializeWithRootOpcode(new CreateCreaturePacket
        {
            CBID = 1,
            ObjectId = new TFID(2, true),
            CoidCurrentVehicle = -1,
            Level = 1,
        });

        Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, ClientVehicleCoidOffset));
    }

    [TestMethod]
    public void Write_DoesntCountAsSummonAndIsElite_AtClientOffsets()
    {
        var bytes = SerializeWithRootOpcode(new CreateCreaturePacket
        {
            CBID = 3,
            ObjectId = new TFID(4, true),
            CoidCurrentVehicle = 5,
            Level = 2,
            DoesntCountAsSummon = true,
            IsElite = true,
            EnhancementId = 42,
        });

        Assert.AreEqual(42, BitConverter.ToInt32(bytes, ClientEnhancementIdOffset));
        Assert.AreEqual(1, bytes[CreateCreaturePacket.ClientDoesntCountAsSummonOffset]);
        Assert.AreEqual(5L, BitConverter.ToInt64(bytes, ClientVehicleCoidOffset));
        Assert.AreEqual(2, BitConverter.ToInt32(bytes, ClientLevelOffset));
        Assert.AreEqual(1, bytes[0x118], "IsElite at client +0x118");
        Assert.AreEqual(ClientCreateCreatureSize, bytes.Length);
    }

    [TestMethod]
    public void Write_BodyOnly_PadsToBodySize()
    {
        var packet = new CreateCreaturePacket
        {
            CBID = 7,
            ObjectId = new TFID(8, false),
            Level = 1,
        };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        packet.Write(writer);
        if (stream.Position > stream.Length)
            stream.SetLength(stream.Position);
        Assert.AreEqual(CreateCreaturePacket.BodySize, stream.ToArray().Length);
    }

    [TestMethod]
    public void Opcode_IsCreateCreature_0x2013()
    {
        Assert.AreEqual(GameOpcode.CreateCreature, new CreateCreaturePacket().Opcode);
        Assert.AreEqual(0x2013u, (uint)GameOpcode.CreateCreature);
    }

    [TestMethod]
    public void PadBytesNeeded_Exact_ReturnsZero()
    {
        Assert.AreEqual(0, CreateCreaturePacket.PadBytesNeeded(CreateCreaturePacket.BodySize, CreateCreaturePacket.BodySize));
    }

    [TestMethod]
    public void PadBytesNeeded_Short_ReturnsDelta()
    {
        Assert.AreEqual(7, CreateCreaturePacket.PadBytesNeeded(CreateCreaturePacket.BodySize - 7, CreateCreaturePacket.BodySize));
    }

    [TestMethod]
    public void PadBytesNeeded_Oversize_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            CreateCreaturePacket.PadBytesNeeded(CreateCreaturePacket.BodySize + 1, CreateCreaturePacket.BodySize));
    }

    private static byte[] SerializeWithRootOpcode(CreateCreaturePacket packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        if (stream.Position > stream.Length)
            stream.SetLength(stream.Position);
        return stream.ToArray();
    }
}
