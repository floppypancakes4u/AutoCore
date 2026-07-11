using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Nested CreateWheelSet must occupy the client fixed span 0x158 (344 with opcode):
/// FUN_005f5ad0 places WheelSet at +0x458 and Armor at +0x5B0.
/// </summary>
[TestClass]
public class CreateWheelSetPacketTests
{
    /// <summary>Client gap WheelSet→Armor is 0x158; body without outer opcode is 340.</summary>
    public const int NestedWheelSetBodyBytes = 340;

    [TestMethod]
    public void WriteEmptyPacket_MatchesFullDefaultBodySize()
    {
        var emptyMs = new MemoryStream();
        var emptyWriter = new BinaryWriter(emptyMs);
        CreateWheelSetPacket.WriteEmptyPacket(emptyWriter);
        emptyWriter.Flush();

        var fullMs = new MemoryStream();
        var fullWriter = new BinaryWriter(fullMs);
        var packet = new CreateWheelSetPacket
        {
            CBID = -1,
            ObjectId = new TFID(-1, false),
        };
        packet.Write(fullWriter);
        fullWriter.Flush();

        Assert.AreEqual(NestedWheelSetBodyBytes, emptyMs.Length,
            "Empty CreateWheelSet must pad to the full nested body (pre-fix was 212 = 128 short).");
        Assert.AreEqual(emptyMs.Length, fullMs.Length,
            "Empty and full CreateWheelSet bodies must match so equip presence does not shift later fields.");
    }

    [TestMethod]
    public void WriteEmptyPacket_Is128BytesLongerThanSimpleObjectEmpty()
    {
        var simpleMs = new MemoryStream();
        var simpleWriter = new BinaryWriter(simpleMs);
        CreateSimpleObjectPacket.WriteEmptyPacket(simpleWriter);
        simpleWriter.Flush();

        var wheelMs = new MemoryStream();
        var wheelWriter = new BinaryWriter(wheelMs);
        CreateWheelSetPacket.WriteEmptyPacket(wheelWriter);
        wheelWriter.Flush();

        Assert.AreEqual(simpleMs.Length + 128, wheelMs.Length);
    }
}
