using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerCoverageGapsTests
{
    [TestMethod]
    public void ReloadCargo_LoadsPersistedItems()
    {
        var persistence = new RecordingInventoryPersistence();
        persistence.CargoToLoad.Add(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Loaded", 2001, 1, 0, 1));
        var inventory = new InventoryManager(persistence);
        inventory.TryAdd(new CharacterInventoryItem(11, CloneBaseObjectType.Item, "Old", 1001, 0, 0, 1));

        inventory.ReloadCargo(5001);

        Assert.AreEqual(1, inventory.Items.Count);
        Assert.AreEqual(2001, inventory.FindByCoid(2001).Coid);
    }

    [TestMethod]
    public void ReloadCargo_ZeroCharacterCoid_NoOp()
    {
        var persistence = new RecordingInventoryPersistence();
        persistence.CargoToLoad.Add(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Loaded", 2001, 0, 0, 1));
        var inventory = new InventoryManager(persistence);
        inventory.TryAdd(new CharacterInventoryItem(11, CloneBaseObjectType.Item, "Old", 1001, 0, 0, 1));

        inventory.ReloadCargo(0);

        Assert.AreEqual(1, inventory.Items.Count);
        Assert.AreEqual(1001, inventory.FindByCoid(1001).Coid);
    }

    [TestMethod]
    public void Drop_HardpointFromPendingDrag_ReEquipsWithoutCargoDelete()
    {
        var harness = new InventoryTestHarness();
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);
        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryEquipPacket));
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
        Assert.IsNull(harness.Inventory.FindByCoid(205));
    }

    [TestMethod]
    public void Drop_HardpointEquipFactoryNull_Fails()
    {
        var harness = new InventoryTestHarness();
        const int cbid = 8096;
        harness.RegisterWeapon(cbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipFactory.Register(cbid, (_, _) => null);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Weapon, "Turret", 205, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
        Assert.IsNotNull(harness.Inventory.FindByCoid(205));
    }

    [TestMethod]
    public void Drop_HardpointCannotResolveSlot_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(99, CloneBaseObjectType.Item, "NotEquippable", 205, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Drop_HardpointItemNotFound_Fails()
    {
        var harness = new InventoryTestHarness();
        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(404, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void Drop_HardpointPowerPlantFromCargo_EquipsAndPersists()
    {
        var harness = new InventoryTestHarness();
        const int cbid = 6001;
        harness.RegisterPowerPlant(cbid);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.PowerPlant, "Plant", 501, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(501, x: 0, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryEquipPacket));
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.PowerPlant));
        Assert.AreEqual(1, harness.Persistence.EnsuredSimpleObjects.Count);
    }

    [TestMethod]
    public void Drop_HardpointRaceItemFromCargo_Equips()
    {
        var harness = new InventoryTestHarness();
        const int cbid = 6101;
        harness.RegisterRaceItem(cbid);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Item, "Race", 502, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(502, x: 0, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryEquipPacket));
        Assert.IsNotNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.RaceItem));
    }

    [TestMethod]
    public void Drop_HardpointSwapRollbackWhenCargoFull_FailsAndRestoresState()
    {
        var harness = new InventoryTestHarness();
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.RegisterWeapon(7001, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 300);
        harness.Inventory.SetCapacity(1, 1);
        harness.Inventory.TryAdd(new CharacterInventoryItem(7001, CloneBaseObjectType.Weapon, "New", 301, 0, 0, 1));

        InjectPendingEquippedDrag(
            harness.Inventory,
            harness.Vehicle,
            coid: 301,
            slot: VehicleEquipmentSlot.WeaponTurret,
            cbid: 7001,
            type: CloneBaseObjectType.Weapon,
            displayName: "New",
            alreadyUnequipped: true);

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(301, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsFalse(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
        Assert.AreEqual(300, harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret).ObjectId.Coid);
        Assert.IsNotNull(harness.Inventory.FindByCoid(301));
    }

    [TestMethod]
    public void TossToWorld_PendingEquippedNotYetUnequipped_UnequipsAndDeletes()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        harness.RegisterWeapon(8096, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, 8096, coid: 205);

        InjectPendingEquippedDrag(
            harness.Inventory,
            harness.Vehicle,
            coid: 205,
            slot: VehicleEquipmentSlot.WeaponTurret,
            cbid: 8096,
            type: CloneBaseObjectType.Weapon,
            displayName: "Turret",
            alreadyUnequipped: false);

        var result = harness.Inventory.TossToWorld(
            InventoryTestHarness.CreateItemDropPacket(205),
            harness.Character);

        Assert.IsTrue(((ItemDropResponsePacket)result.Packets[0]).WasSuccessful);
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
        Assert.AreEqual(1, harness.Persistence.EquipmentSaves.Count);
    }

    [TestMethod]
    public void BuildHardpointEquipPackets_NullInventory_OmitsCargoSendAll()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(9001, true);

        var packets = InventoryManager.BuildHardpointEquipPackets(
            null,
            vehicle,
            new TFID(205, true),
            null,
            sourceInventoryType: 2);

        Assert.AreEqual(1, packets.Count);
        Assert.IsInstanceOfType(packets[0], typeof(InventoryEquipPacket));
    }

    [TestMethod]
    public void AddItem_WhenSlotOccupied_ReturnsRejectedMessage()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Blocker", 1000, 0, 0, 1));
        var entry = new InventoryCatalogEntry(11, CloneBaseObjectType.Item, "New");

        var result = inventory.AddItem(entry, new AlwaysSucceedItemCreator(), coid: 1001);

        Assert.IsNull(result.AddedItem);
        StringAssert.Contains(result.Message, "full");
    }

    [TestMethod]
    public void Grab_EquippedItemNoVehicle_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Character.AttachCurrentVehicleForTests(null);

        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        Assert.IsFalse(((InventoryGrabResponsePacket)result.Packets[0]).WasSuccessful);
    }

    private static void InjectPendingEquippedDrag(
        InventoryManager inventory,
        Vehicle vehicle,
        long coid,
        VehicleEquipmentSlot slot,
        int cbid,
        CloneBaseObjectType type,
        string displayName,
        bool alreadyUnequipped)
    {
        var dragType = typeof(InventoryManager).GetNestedType("PendingEquippedItemDrag", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PendingEquippedItemDrag type not found.");
        var drag = Activator.CreateInstance(
            dragType,
            vehicle,
            slot,
            cbid,
            type,
            displayName,
            true,
            alreadyUnequipped);

        var field = typeof(InventoryManager).GetField("_pendingEquippedItemDrags", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_pendingEquippedItemDrags field not found.");
        var dict = field.GetValue(inventory);
        var addMethod = dict!.GetType().GetMethod("Add")!;
        addMethod.Invoke(dict, new[] { coid, drag });
    }

    private sealed class AlwaysSucceedItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new(coid, true),
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    Quantity = 1,
                    IsInInventory = true
                },
                entry.DisplayName);
    }
}
