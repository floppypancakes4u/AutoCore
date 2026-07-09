using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerTossTests
{
    [TestMethod]
    public void TossToWorld_ItemNotFound_ReturnsFailureResponse()
    {
        var harness = new InventoryTestHarness();
        var packet = CreatePacket(itemCoid: 999, sourceObjectId: 19464384);

        var result = harness.Inventory.TossToWorld(packet, harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(ItemDropResponsePacket));
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.AreEqual(999, response.ItemCoid);
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_NoVehicle_ReturnsFailureResponse()
    {
        var harness = new InventoryTestHarness();
        harness.Character.AttachCurrentVehicleForTests(null);
        var packet = CreatePacket(itemCoid: 100, sourceObjectId: 0);

        var result = harness.Inventory.TossToWorld(packet, harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    private static ItemDropPacket CreatePacket(long itemCoid, int sourceObjectId = 1)
    {
        var bytes = new byte[ItemDropPacket.MinimumLength];
        BitConverter.GetBytes((uint)GameOpcode.ItemDrop).CopyTo(bytes, 0);
        BitConverter.GetBytes(sourceObjectId).CopyTo(bytes, 4);
        BitConverter.GetBytes(itemCoid).CopyTo(bytes, 8);
        BitConverter.GetBytes(1f).CopyTo(bytes, 0x10);
        BitConverter.GetBytes(2f).CopyTo(bytes, 0x14);
        BitConverter.GetBytes(3f).CopyTo(bytes, 0x18);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();

        var packet = new ItemDropPacket();
        packet.Read(reader);
        return packet;
    }
}
