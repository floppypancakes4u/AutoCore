using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// PDB Pass 5. Vehicle_applyCreatePacket (RVA 0x00505270) gates the extended tail on
/// opcode == 0x201E. InventorySize is ushort at +0xD7A; entries start at +0xD80.
/// </summary>
[TestClass]
public class CreateVehicleExtendedClientOffsetTests
{
    public const int ClientBaseCreateVehicleSize = 0xD78;
    public const int ClientNumInventorySlotsOffset = 0xD78;
    public const int ClientInventorySizeOffset = 0xD7A;
    public const int ClientInventoryPadOffset = 0xD7C;
    public const int ClientInventoryArrayOffset = 0xD80;
    public const int ClientInventoryEntryCount = 512;
    public const int ClientExtendedSize = ClientInventoryArrayOffset + ClientInventoryEntryCount * 8;

    [TestMethod]
    public void Write_ExtendedTail_StartsAtClientBaseEnd()
    {
        var packet = new CreateVehicleExtendedPacket
        {
            CBID = 12425,
            ObjectId = new TFID(9, true),
            NumInventorySlots = 78,
            InventorySize = 3,
        };
        packet.InventoryCoids[0] = 100;
        packet.InventoryCoids[1] = 200;
        packet.InventoryCoids[2] = -1;

        var bytes = SerializeWithRootOpcode(packet);

        Assert.AreEqual(ClientExtendedSize, bytes.Length,
            "Base 0xD78 + i16 slots + u16 count + pad4 + 512*i64.");
        Assert.AreEqual((uint)GameOpcode.CreateVehicleExtended, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual((short)78, BitConverter.ToInt16(bytes, ClientNumInventorySlotsOffset));
        Assert.AreEqual((ushort)3, BitConverter.ToUInt16(bytes, ClientInventorySizeOffset));
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, ClientInventoryPadOffset));
        Assert.AreEqual(100L, BitConverter.ToInt64(bytes, ClientInventoryArrayOffset));
        Assert.AreEqual(200L, BitConverter.ToInt64(bytes, ClientInventoryArrayOffset + 8));
        Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, ClientInventoryArrayOffset + 16));
    }

    [TestMethod]
    public void Write_AlwaysEmits512InventoryEntries_DefaultMinusOne()
    {
        var packet = new CreateVehicleExtendedPacket
        {
            CBID = 1,
            ObjectId = new TFID(1, true),
        };

        Assert.AreEqual(512, packet.InventoryCoids.Length);
        Assert.IsTrue(packet.InventoryCoids.All(c => c == -1L));

        var bytes = SerializeWithRootOpcode(packet);
        Assert.AreEqual(ClientExtendedSize, bytes.Length);

        for (var i = 0; i < ClientInventoryEntryCount; ++i)
        {
            Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, ClientInventoryArrayOffset + i * 8),
                $"inventory[{i}]");
        }
    }

    [TestMethod]
    public void Write_DoesNotShiftBaseNests_WhenExtendedTailPresent()
    {
        var packet = new CreateVehicleExtendedPacket
        {
            CBID = 5,
            ObjectId = new TFID(6, true),
            CoidCurrentOwner = 7,
        };
        var bytes = SerializeWithRootOpcode(packet);

        Assert.AreEqual(7L, BitConverter.ToInt64(bytes, CreateVehicleClientOffsetTests.ClientOwnerCoidOffset));
        Assert.AreEqual((uint)GameOpcode.CreateWheelSet,
            BitConverter.ToUInt32(bytes, CreateVehicleClientOffsetTests.ClientWheelOpcodeOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, CreateVehicleClientOffsetTests.ClientWheelCbidOffset));
    }

    private static byte[] SerializeWithRootOpcode(CreateVehiclePacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(ms.Position);
        return ms.ToArray();
    }
}
