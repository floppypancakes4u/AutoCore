using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryDropTossRegressionTests
{
    [TestMethod]
    public void GrabCargo_TossToWorld_Regression_ItemRemovedAndPersisted()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1001),
            harness.Character);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        Assert.IsTrue(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful,
            "Cargo toss should succeed after grab.");
        Assert.IsNull(harness.Inventory.FindByCoid(1001),
            "Tossed cargo item must be removed from inventory.");
        CollectionAssert.Contains(harness.Persistence.DeletedItemCoids, 1001L,
            "Cargo delete must be persisted.");
    }

    [TestMethod]
    public void GrabEquipped_TossToWorld_Regression_NotInCargoOrSlot()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(205, sourceObjectId: 18477756),
            harness.Character);

        Assert.IsTrue(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful,
            "Equipped module toss should succeed after grab.");
        Assert.IsNull(harness.Inventory.FindByCoid(205),
            "Tossed equipped item must not appear in cargo.");
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret),
            "Tossed equipped item must not remain on the vehicle.");
    }

    [TestMethod]
    public void GrabEquipped_TossToWorld_Regression_ResponseUsesDraggedCoid()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(205),
            harness.Character);

        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.AreEqual(205, response.ItemCoid,
            "ItemDropResponse must echo the dragged item COID, not a spawned world object COID.");
    }

    [TestMethod]
    public void GrabEquipped_DropToCargo_Regression_StillWorksAfterTossPath()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 2, y: 0),
            harness.Character);

        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryCargoSendAllPacket));
        var response = (InventoryDropResponsePacket)result.Packets[1];
        Assert.IsTrue(response.WasSuccessful,
            "Pending equipped drop to cargo must still work.");
        Assert.IsNotNull(harness.Inventory.FindByCoid(205),
            "Dropped equipped item must land in cargo.");
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
    }

    [TestMethod]
    public void GrabCargo_TossToWorld_Regression_CargoSendAllReflectsRemoval()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Keep", 1000, 0, 0, 1));
        harness.Inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "Toss", 1001, 1, 0, 1));

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1001),
            harness.Character);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        var cargoSendAll = result.Packets.OfType<InventoryCargoSendAllPacket>().Single();
        Assert.IsFalse(cargoSendAll.Items.Any(i => i.ItemCoid == 1001),
            "CargoSendAll must not include the tossed item.");
        Assert.IsTrue(cargoSendAll.Items.Any(i => i.ItemCoid == 1000),
            "CargoSendAll must still include remaining cargo items.");
    }

    [TestMethod]
    public void TossToWorld_Regression_NoSpawnOrDestroyPackets()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);

        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Cargo", 1001, 0, 0, 1));
        var cargoResult = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);
        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);
        var equippedResult = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(205),
            harness.Character);

        foreach (var result in new[] { cargoResult, equippedResult })
        {
            Assert.IsFalse(result.Packets.Any(p =>
                    p is CreateSimpleObjectPacket or CreateWeaponPacket or CreateArmorPacket or DestroyObjectPacket),
                "Toss must not spawn or destroy world objects in the current inventory-only delete flow.");
        }
    }
}
