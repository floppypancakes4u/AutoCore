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
    public void TossToWorld_CargoItem_SucceedsAndDeletes()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.IsNull(harness.Inventory.FindByCoid(1001));
        CollectionAssert.Contains(harness.Persistence.DeletedItemCoids, 1001L);
    }

    [TestMethod]
    public void TossToWorld_CargoItem_SendsResponseThenCargoSendAll()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        Assert.AreEqual(2, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(ItemDropResponsePacket));
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryCargoSendAllPacket));
    }

    [TestMethod]
    public void TossToWorld_CargoItem_EchoesRequestFields()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));
        var packet = InventoryTestHarness.CreateItemDropPacket(
            1001,
            sourceObjectId: 19464384,
            dropX: 10f,
            dropY: 20f,
            dropZ: 30f,
            tailValue: 999888777L);

        var result = harness.Inventory.TossToWorld(packet, harness.Character);
        var response = (ItemDropResponsePacket)result.Packets[0];

        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(19464384, response.SourceObjectId);
        Assert.AreEqual(1001, response.ItemCoid);
        Assert.AreEqual(10f, response.DropPosition.X);
        Assert.AreEqual(20f, response.DropPosition.Y);
        Assert.AreEqual(30f, response.DropPosition.Z);
        Assert.AreEqual(999888777L, response.TailValue);
    }

    [TestMethod]
    public void TossToWorld_CargoItem_NoWorldSpawnPackets()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        Assert.IsFalse(result.Packets.Any(p =>
            p is CreateSimpleObjectPacket or CreateWeaponPacket or CreateArmorPacket or DestroyObjectPacket));
    }

    [TestMethod]
    public void TossToWorld_ItemNotFound_ReturnsFailureResponse()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        var packet = InventoryTestHarness.CreateItemDropPacket(999, sourceObjectId: 19464384);

        var result = harness.Inventory.TossToWorld(packet, harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(ItemDropResponsePacket));
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.AreEqual(999, response.ItemCoid);
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_NullCharacter_Fails()
    {
        var harness = new InventoryTestHarness();
        var packet = InventoryTestHarness.CreateItemDropPacket(100);

        var result = harness.Inventory.TossToWorld(packet, null);

        Assert.IsFalse(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_NoMap_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(1001),
            harness.Character);

        Assert.IsFalse(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_NoVehicle_ReturnsFailureResponse()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Character.AttachCurrentVehicleForTests(null);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 100, 0, 0, 1));

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(100),
            harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_PacketTooShort_Fails()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var bytes = new byte[16];
        BitConverter.GetBytes((uint)GameOpcode.ItemDrop).CopyTo(bytes, 0);
        BitConverter.GetBytes(1001L).CopyTo(bytes, 8);
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();
        var packet = new ItemDropPacket();
        packet.Read(reader);

        var result = harness.Inventory.TossToWorld(packet, harness.Character);

        Assert.IsFalse(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
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
            InventoryTestHarness.CreateItemDropPacket(205, sourceObjectId: 18477756),
            harness.Character);

        Assert.AreEqual(1, result.Packets.Count);
        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(205, response.ItemCoid);
        Assert.IsNull(harness.Inventory.FindByCoid(205));
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
    }

    [TestMethod]
    public void TossToWorld_PendingEquipped_EchoesSourceObjectId()
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

        var response = (ItemDropResponsePacket)result.Packets[0];
        Assert.AreEqual(18477756, response.SourceObjectId);
    }

    [TestMethod]
    public void TossToWorld_PendingEquippedArmor_Succeeds()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterArmor(cbid: 5001);
        harness.EquipArmor(cbid: 5001, coid: 301);
        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(301, inventoryType: 2, equipmentCbid: 5001),
            harness.Character);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(301),
            harness.Character);

        Assert.IsTrue(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.Armor));
        Assert.IsNull(harness.Inventory.FindByCoid(301));
    }

    [TestMethod]
    public void TossToWorld_PendingEquipped_NoCargoSendAll()
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

        Assert.AreEqual(1, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(ItemDropResponsePacket));
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

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(205),
            harness.Character);

        Assert.IsFalse(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void TossToWorld_EquippedStillOnVehicleWithoutGrab_Fails()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(205),
            harness.Character);

        Assert.IsFalse(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
    }

    [TestMethod]
    public void CreateItemDropFailure_EchoesRequestFields()
    {
        var packet = InventoryTestHarness.CreateItemDropPacket(
            42000,
            sourceObjectId: 19464384,
            dropX: 1f,
            dropY: 2f,
            dropZ: 3f,
            tailValue: 5276635759L);

        var response = InventoryManager.CreateItemDropFailure(packet);

        Assert.IsFalse(response.WasSuccessful);
        Assert.AreEqual(19464384, response.SourceObjectId);
        Assert.AreEqual(42000, response.ItemCoid);
        Assert.AreEqual(1f, response.DropPosition.X);
        Assert.AreEqual(2f, response.DropPosition.Y);
        Assert.AreEqual(3f, response.DropPosition.Z);
        Assert.AreEqual(5276635759L, response.TailValue);
    }
}
