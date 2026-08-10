using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryEquipPersistRollbackTests
{
    [TestMethod]
    public void EquipFromCargo_PersistEquipThrows_RestoresCargoRowAndSlot()
    {
        // SS-31: a guard throw in EnsureSimpleObject must not strand the item
        // (deleted from cargo, equipped only in memory, nothing persisted).
        var harness = new InventoryTestHarness();
        const int cbid = 8096;
        harness.RegisterWeapon(cbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.Inventory.TryAdd(new CharacterInventoryItem(cbid, CloneBaseObjectType.Weapon, "Turret", 205, 0, 0, 1));
        harness.Persistence.EnsureSimpleObjectFailure =
            _ => new InvalidOperationException("SS-31 coid collision (test)");

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(205, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        Assert.IsTrue(harness.Inventory.Items.Any(i => i.Coid == 205),
            "equip persist failure must restore the cargo row");
        Assert.IsNull(harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret),
            "equip persist failure must leave the hardpoint slot empty");
        Assert.AreEqual(1, harness.Persistence.DeletedItemCoids.Count(c => c == 205),
            "the initial cargo delete should have been issued before the failed persist");
        Assert.AreEqual(2, harness.Persistence.Upserted.Count(u => u.Item.Coid == 205),
            "the rollback must re-upsert the cargo row after the failed persist");
        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful, "drop must report failure to the client");
    }

    [TestMethod]
    public void EquipFromCargo_PersistEquipThrows_WithSwappedItem_RestoresPreviousHardpointItem()
    {
        // SS-31: when a slot swap is in flight, a persist throw must restore the
        // previously-equipped item, not leave the hardpoint empty or double-cargo the swap.
        var harness = new InventoryTestHarness();
        const int oldCbid = 7001;
        const int newCbid = 8096;
        harness.RegisterWeapon(oldCbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.RegisterWeapon(newCbid, VehicleEquipmentSlotResolver.WeaponFlagTurret);
        harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, oldCbid, coid: 300);
        harness.Inventory.TryAdd(new CharacterInventoryItem(newCbid, CloneBaseObjectType.Weapon, "New Turret", 301, 1, 0, 1));
        harness.Persistence.EnsureSimpleObjectFailure =
            _ => new InvalidOperationException("SS-31 coid collision (test)");

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(301, x: 1, y: 0, inventoryType: 2),
            harness.Character);

        var equipped = harness.Vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret);
        Assert.IsNotNull(equipped, "the previous hardpoint item must be restored");
        Assert.AreEqual(300, equipped.ObjectId.Coid, "the previous hardpoint item must be restored");
        Assert.IsTrue(harness.Inventory.Items.Any(i => i.Coid == 301),
            "the dropped item's cargo row must be restored");
        Assert.IsFalse(harness.Inventory.Items.Any(i => i.Coid == 300),
            "the swapped-out cargo row must be removed again after rollback");
        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful, "drop must report failure to the client");
    }
}
