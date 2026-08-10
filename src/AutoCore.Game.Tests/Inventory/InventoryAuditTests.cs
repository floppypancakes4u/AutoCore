using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// Phase 3 item audit trail: each Persist* verb in <see cref="InventoryManager"/> emits one
/// audit event describing the in-memory mutation, regardless of whether persistence succeeds.
/// </summary>
[TestClass]
public class InventoryAuditTests
{
    private InMemoryLogSink _sink = null!;

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
    }

    [TestMethod]
    public void RestoreCargo_EmitsItemAdded_WithCargoContainerAndIdentity()
    {
        var harness = new InventoryTestHarness(characterCoid: 6001);
        var item = new CharacterInventoryItem(555, CloneBaseObjectType.Item, "Widget", 9100, 2, 1, 3);

        var result = harness.Inventory.RestoreCargoWithoutCreate(item, harness.Character.ObjectId.Coid);

        Assert.IsTrue(result.AcceptedQuantity >= 1, result.Message);
        var record = _sink.Single("ItemAdded");
        Assert.IsTrue(record.Audit, "item mutations are audit events");
        Assert.AreEqual(6001L, record.GetProperty("CharacterId"));
        Assert.AreEqual("Cargo", record.GetProperty("Container"));
        Assert.AreEqual(9100L, record.GetProperty("ItemCoid"));
        Assert.AreEqual(555, record.GetProperty("ItemCbid"));
        Assert.AreEqual(3, record.GetProperty("Quantity"));
    }

    [TestMethod]
    public void RemoveCargoByCoid_EmitsItemRemoved()
    {
        var harness = new InventoryTestHarness(characterCoid: 6002);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));
        _sink.Clear();

        var result = harness.Inventory.RemoveCargoByCoid(
            harness.Character.ObjectId.Coid, 1001, itemGlobal: true, emitClientDestroy: false);

        Assert.IsTrue(result.AcceptedQuantity >= 1, result.Message);
        var record = _sink.Single("ItemRemoved");
        Assert.AreEqual("Cargo", record.GetProperty("Container"));
        Assert.AreEqual(1001L, record.GetProperty("ItemCoid"));
        Assert.AreEqual(6002L, record.GetProperty("CharacterId"));
    }

    [TestMethod]
    public void Drop_CargoMove_EmitsItemMoved_WithDestinationSlot()
    {
        var harness = new InventoryTestHarness(characterCoid: 6003);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));
        _sink.Clear();

        harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1001, x: 3, y: 0),
            harness.Character);

        var record = _sink.Single("ItemMoved");
        Assert.AreEqual("Cargo", record.GetProperty("Container"));
        Assert.AreEqual(1001L, record.GetProperty("ItemCoid"));
        Assert.AreEqual((byte)3, record.GetProperty("X"), "move audit must carry the destination slot");
        Assert.AreEqual((byte)0, record.GetProperty("Y"));
    }

    [TestMethod]
    public void ClearCargo_EmitsCargoCleared()
    {
        var harness = new InventoryTestHarness(characterCoid: 6004);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));
        _sink.Clear();

        harness.Inventory.ClearCargo(harness.Character.ObjectId.Coid);

        var record = _sink.Single("CargoCleared");
        Assert.IsTrue(record.Audit);
        Assert.AreEqual(6004L, record.GetProperty("CharacterId"));
    }

    [TestMethod]
    public void Drop_HardpointFromCargo_EmitsItemEquipped_AndItemRemoved()
    {
        var harness = new InventoryTestHarness(characterCoid: 6005);
        const int cbid = 8096;
        harness.RegisterWeapon(cbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Weapon, "Turret", 205, 0, 0, 1));
        _sink.Clear();

        harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        var equipped = _sink.Single("ItemEquipped");
        Assert.AreEqual("Hardpoint", equipped.GetProperty("Container"));
        Assert.AreEqual(205L, equipped.GetProperty("ItemCoid"));
        // ItemCbid comes from the live equip object's clone base; the harness factory does not
        // attach one, so only presence is asserted here.
        Assert.IsNotNull(equipped.GetProperty("ItemCbid"));
        Assert.AreEqual(harness.Vehicle.ObjectId.Coid, equipped.GetProperty("VehicleCoid"));

        var removed = _sink.Single("ItemRemoved");
        Assert.AreEqual("Cargo", removed.GetProperty("Container"), "equip from cargo also removes the cargo stack");
    }

    [TestMethod]
    public void Grab_EquippedItem_EmitsItemUnequipped()
    {
        var harness = new InventoryTestHarness(characterCoid: 6006);
        harness.RegisterWeapon(cbid: 8096, flags: VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, cbid: 8096, coid: 205);
        _sink.Clear();

        harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(205, inventoryType: 2, equipmentCbid: 8096),
            harness.Character);

        var record = _sink.Single("ItemUnequipped");
        Assert.AreEqual("Hardpoint", record.GetProperty("Container"));
        Assert.AreEqual(harness.Vehicle.ObjectId.Coid, record.GetProperty("VehicleCoid"));
    }

    [TestMethod]
    public void PersistFailure_StillEmitsItemAdded_AndInventoryPersistFailed()
    {
        var throwing = new ThrowingInventoryPersistence();
        var inventory = new InventoryManager(throwing);
        var item = new CharacterInventoryItem(555, CloneBaseObjectType.Item, "Widget", 9100, 0, 0, 1);

        var result = inventory.RestoreCargoWithoutCreate(item, 6007);

        Assert.IsTrue(result.AcceptedQuantity >= 1,
            "persist failure must not fail the in-memory mutation (existing swallow behavior)");
        var added = _sink.Single("ItemAdded");
        Assert.AreEqual(9100L, added.GetProperty("ItemCoid"), "the mutation that DID happen must be audited");

        var failed = _sink.Single("InventoryPersistFailed");
        Assert.AreEqual(StructuredLogLevel.Error, failed.Level);
        Assert.AreEqual("DB-001", failed.GetProperty("ErrorCode"));
        Assert.AreEqual("CargoUpsert", failed.GetProperty("Verb"));
        Assert.AreEqual(9100L, failed.GetProperty("ItemCoid"));
        Assert.AreEqual(6007L, failed.GetProperty("CharacterId"));
    }

    /// <summary>Fake persistence whose write verbs always throw (DB outage shape).</summary>
    private sealed class ThrowingInventoryPersistence : IInventoryPersistence
    {
        public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) => Array.Empty<CharacterInventoryItem>();
        public IReadOnlyList<CharacterInventoryItem> LoadLocker(long characterCoid) => Array.Empty<CharacterInventoryItem>();
        public void UpsertCargo(long characterCoid, CharacterInventoryItem item) => throw new InvalidOperationException("db down");
        public void UpsertLocker(long characterCoid, CharacterInventoryItem item) => throw new InvalidOperationException("db down");
        public void MoveCargo(long characterCoid, CharacterInventoryItem item) => throw new InvalidOperationException("db down");
        public void MoveLocker(long characterCoid, CharacterInventoryItem item) => throw new InvalidOperationException("db down");
        public void DeleteCargo(long characterCoid, long itemCoid) => throw new InvalidOperationException("db down");
        public void DeleteLocker(long characterCoid, long itemCoid) => throw new InvalidOperationException("db down");
        public void ClearCargo(long characterCoid) => throw new InvalidOperationException("db down");
        public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0) { }
        public void ReleaseUnusedPlaceholder(long coid) { }
        public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) => throw new InvalidOperationException("db down");
        public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount) { }
        public long LoadCredits(long characterCoid) => 0;
        public void SaveCredits(long characterCoid, long credits) { }
    }
}
