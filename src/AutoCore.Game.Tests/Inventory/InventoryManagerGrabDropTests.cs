using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerGrabDropTests
{
    [TestMethod]
    public void Grab_ExistingCargoItem_SucceedsWithoutPersist()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 2, 1, 1));

        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1001),
            harness.Character);

        var response = (InventoryGrabResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual((byte)2, response.InventoryPositionX);
        Assert.AreEqual((byte)1, response.InventoryPositionY);
        Assert.AreEqual(0, harness.Persistence.Upserted.Count);
    }

    [TestMethod]
    public void Grab_ExistingStackedCargoItem_UsesRequestedQuantity()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Stack", 1001, 0, 0, 5));

        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1001, quantity: 3),
            harness.Character);

        var response = (InventoryGrabResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(3, response.Quantity);
        Assert.IsFalse(response.AddToExistingItem);
    }

    [TestMethod]
    public void Grab_EquippedItem_UnequipsPersistsEquipmentAndSetsPendingDrag()
    {
        var harness = new InventoryTestHarness();
        harness.RegisterWeapon(cbid: 8096, flags: VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, cbid: 8096, coid: 205);

        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        Assert.AreEqual(2, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryUnequipPacket));
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryGrabResponsePacket));
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
        Assert.AreEqual(1, harness.Persistence.EquipmentSaves.Count);
    }

    [TestMethod]
    public void Drop_CargoMove_PersistsMove()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1001, x: 3, y: 0),
            harness.Character);

        Assert.AreEqual(2, result.Packets.Count, "Drop response then CargoSendAll resync");
        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual((byte)3, response.InventoryPositionX);
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryCargoSendAllPacket));
        Assert.AreEqual(1, harness.Persistence.Moved.Count);
    }

    [TestMethod]
    public void Drop_HardpointFromCargo_EquipsDeletesCargoAndSavesEquipment()
    {
        var harness = new InventoryTestHarness();
        const int cbid = 8096;
        harness.RegisterWeapon(cbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Weapon, "Turret", 205, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(result.Packets.Any(p => p is InventoryDropResponsePacket));
        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryEquipPacket));
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryCargoSendAllPacket));
        Assert.IsNull(harness.Inventory.FindByCoid(205));
        Assert.AreEqual(1, harness.Persistence.DeletedItemCoids.Count);
        Assert.AreEqual(1, harness.Persistence.EquipmentSaves.Count);
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
    }

    [TestMethod]
    public void Drop_HardpointSwap_PutsPreviousInCargoWithOldItemId()
    {
        var harness = new InventoryTestHarness();
        const int oldCbid = 7001;
        const int newCbid = 8096;
        harness.RegisterWeapon(oldCbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.RegisterWeapon(newCbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, oldCbid, coid: 300);
        harness.Inventory.TryAdd(new CharacterInventoryItem(newCbid, CloneBaseObjectType.Weapon, "New Turret", 301, 1, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(301, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        var equip = (InventoryEquipPacket)result.Packets[0];
        Assert.AreEqual(300, equip.OldItemId.Coid);
        Assert.IsNotNull(harness.Inventory.FindByCoid(300));
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
    }

    [TestMethod]
    public void Drop_PendingEquippedToOccupiedSlot_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);
        harness.Inventory.TryAdd(new CharacterInventoryItem(99, CloneBaseObjectType.Item, "Blocker", 999, 2, 0, 1));

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 2, y: 0),
            harness.Character);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void Drop_PendingEquippedToCargo_UpsertsCargoAndSendsCargoSendAll()
    {
        var harness = new InventoryTestHarness();
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
        Assert.IsTrue(response.WasSuccessful);
        Assert.IsNotNull(harness.Inventory.FindByCoid(205));
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
    }

    [TestMethod]
    public void Drop_PendingEquippedWrongVehicle_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);
        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var otherVehicle = new Vehicle();
        otherVehicle.SetCoid(9999, true);
        harness.Character.AttachCurrentVehicleForTests(otherVehicle);

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 0, y: 0),
            harness.Character);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void Grab_NullCharacter_ReturnsFailure()
    {
        var inventory = new InventoryManager();
        var result = inventory.Grab(InventoryTestHarness.CreateGrabPacket(1), null);
        var response = (InventoryGrabResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void Drop_UnsupportedInventoryType_ReturnsFailure()
    {
        var harness = new InventoryTestHarness();
        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1, 0, 0, inventoryType: 99),
            harness.Character);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void Drop_HardpointMissingCloneBase_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(999, CloneBaseObjectType.Weapon, "Unknown", 500, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(500, 0, 0, inventoryType: 2),
            harness.Character);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void Drop_HardpointArmorFromCargo_EquipsAndPersists()
    {
        var harness = new InventoryTestHarness();
        const int cbid = 5001;
        harness.RegisterArmor(cbid);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Armor, "Plating", 501, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(501, 0, 0, inventoryType: 2),
            harness.Character);

        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryEquipPacket));
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.Armor));
        Assert.AreEqual(1, harness.Persistence.EnsuredSimpleObjects.Count);
    }

    [TestMethod]
    public void Drop_CargoItemNotFound_Fails()
    {
        var harness = new InventoryTestHarness();
        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(9999, 0, 0),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Drop_InvalidCargoSlot_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 100, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(100, x: 99, y: 99),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Grab_EquippedItemNotFound_Fails()
    {
        var harness = new InventoryTestHarness();
        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(404, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryGrabResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Grab_WorldItemWithoutMap_Fails()
    {
        var harness = new InventoryTestHarness();
        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1000),
            harness.Character);

        Assert.IsFalse(((InventoryGrabResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Drop_NullCharacter_Fails()
    {
        var inventory = new InventoryManager();
        var result = inventory.Drop(InventoryTestHarness.CreateDropPacket(1, 0, 0), null);
        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Drop_HardpointEquipFailureWhenWrongObjectType_Fails()
    {
        var harness = new InventoryTestHarness();
        const int cbid = 8096;
        harness.RegisterWeapon(cbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipFactory.Register(cbid, (coid, global) =>
        {
            var item = new SimpleObject(GraphicsObjectType.Graphics);
            item.SetCoid(coid, global);
            return item;
        });
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Weapon, "NotWeapon", 205, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, 0, 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Drop_HardpointNoVehicle_ReturnsFailure()
    {
        var harness = new InventoryTestHarness();
        harness.Character.AttachCurrentVehicleForTests(null);
        harness.Inventory.TryAdd(new CharacterInventoryItem(8096, CloneBaseObjectType.Weapon, "Turret", 205, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, 1, 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }
}
