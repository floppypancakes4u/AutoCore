using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
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
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Character.AttachCurrentVehicleForTests(null);
        var packet = CreatePacket(itemCoid: 100, sourceObjectId: 0);

        var result = harness.Inventory.TossToWorld(packet, harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_PendingEquippedAfterGrab_DeletesWithoutCargoSendAll()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(cbid: 8096, flags: VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, cbid: 8096, coid: 205);

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var result = harness.Inventory.TossToWorld(
            CreatePacket(itemCoid: 205, sourceObjectId: 18477756),
            harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(205, response.ItemCoid);
        Assert.IsNull(harness.Inventory.FindByCoid(205));
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
    }

    [TestMethod]
    public void TossToWorld_PendingEquippedWrongVehicle_Fails()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);
        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var otherVehicle = new Vehicle();
        otherVehicle.SetCoid(9999, true);
        harness.Character.AttachCurrentVehicleForTests(otherVehicle);

        var result = harness.Inventory.TossToWorld(CreatePacket(205), harness.Character);
        Assert.IsFalse(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
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
