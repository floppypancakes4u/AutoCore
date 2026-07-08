using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class VehicleEquipmentTests
{
    [TestMethod]
    public void TryEquipItem_Armor_Succeeds()
    {
        var vehicle = new Vehicle();
        var armor = new Armor();
        armor.SetCoid(101, true);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out var previous));
        Assert.IsNull(previous);
        Assert.AreSame(armor, vehicle.GetEquippedItem(VehicleEquipmentSlot.Armor));
    }

    [TestMethod]
    public void TryEquipItem_PowerPlant_Succeeds()
    {
        var vehicle = new Vehicle();
        var powerPlant = new PowerPlant();
        powerPlant.SetCoid(102, true);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.PowerPlant, powerPlant, out _));
        Assert.AreSame(powerPlant, vehicle.GetEquippedItem(VehicleEquipmentSlot.PowerPlant));
    }

    [TestMethod]
    public void TryEquipItem_WeaponTurret_Succeeds()
    {
        var vehicle = new Vehicle();
        var weapon = new Weapon();
        weapon.SetCoid(103, true);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponTurret, weapon, out _));
        Assert.AreSame(weapon, vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponTurret));
    }

    [TestMethod]
    public void TryEquipItem_RaceItemSimpleObject_Succeeds()
    {
        var vehicle = new Vehicle();
        var raceItem = new SimpleObject(GraphicsObjectType.Graphics);
        raceItem.SetCoid(104, true);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.RaceItem, raceItem, out _));
        Assert.AreSame(raceItem, vehicle.GetEquippedItem(VehicleEquipmentSlot.RaceItem));
    }

    [TestMethod]
    public void TryEquipItem_SwapReturnsPreviousOccupant()
    {
        var vehicle = new Vehicle();
        var first = new Armor();
        first.SetCoid(201, true);
        var second = new Armor();
        second.SetCoid(202, true);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, first, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, second, out var previous));
        Assert.AreSame(first, previous);
        Assert.AreSame(second, vehicle.GetEquippedItem(VehicleEquipmentSlot.Armor));
    }

    [TestMethod]
    public void TryEquipItem_WrongTypeRejected()
    {
        var vehicle = new Vehicle();
        var simple = new SimpleObject(GraphicsObjectType.Graphics);
        simple.SetCoid(301, true);

        Assert.IsFalse(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, simple, out var previous));
        Assert.IsNull(previous);
        Assert.IsNull(vehicle.GetEquippedItem(VehicleEquipmentSlot.Armor));
    }

    [TestMethod]
    public void TryUnequipItem_ClearsSlotAndSnapshotFkBecomesZero()
    {
        var vehicle = new Vehicle();
        var weapon = new Weapon();
        weapon.SetCoid(401, true);
        vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _);

        Assert.IsTrue(vehicle.TryUnequipItem(401, out var slot, out var item));
        Assert.AreEqual(VehicleEquipmentSlot.WeaponFront, slot);
        Assert.AreSame(weapon, item);
        Assert.IsNull(vehicle.GetEquippedItem(VehicleEquipmentSlot.WeaponFront));

        var snapshot = vehicle.CreateEquipmentSnapshot();
        Assert.AreEqual(0, snapshot.Front);
    }

    [TestMethod]
    public void TryFindEquippedItem_ByCoidAndCbidFallback()
    {
        var harness = new InventoryTestHarness();
        var weapon = harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, cbid: 8096, coid: 501);

        Assert.IsTrue(harness.Vehicle.TryFindEquippedItem(501, out var slot, out var byCoid));
        Assert.AreEqual(VehicleEquipmentSlot.WeaponTurret, slot);
        Assert.AreSame(weapon, byCoid);

        Assert.IsTrue(harness.Vehicle.TryFindEquippedItem(-1, 8096, out slot, out var byCbid));
        Assert.AreEqual(VehicleEquipmentSlot.WeaponTurret, slot);
        Assert.AreSame(weapon, byCbid);
    }

    [TestMethod]
    public void CreateEquipmentSnapshot_ReflectsEquippedCoids()
    {
        var vehicle = new Vehicle();
        var armor = new Armor();
        armor.SetCoid(601, true);
        var turret = new Weapon();
        turret.SetCoid(602, true);
        var raceItem = new SimpleObject(GraphicsObjectType.Graphics);
        raceItem.SetCoid(603, true);

        vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);
        vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponTurret, turret, out _);
        vehicle.TryEquipItem(VehicleEquipmentSlot.RaceItem, raceItem, out _);

        var snapshot = vehicle.CreateEquipmentSnapshot();
        Assert.AreEqual(601, snapshot.Armor);
        Assert.AreEqual(602, snapshot.Turret);
        Assert.AreEqual(603, snapshot.RaceItem);
        Assert.AreEqual(0, snapshot.Front);
    }

    [TestMethod]
    public void TryEquipItem_AllHardpointSlots_AcceptCorrectTypes()
    {
        var vehicle = new Vehicle();
        var armor = new Armor();
        armor.SetCoid(1, true);
        var powerPlant = new PowerPlant();
        powerPlant.SetCoid(2, true);
        var wheelSet = new WheelSet();
        wheelSet.SetCoid(3, true);
        var ornament = new SimpleObject(GraphicsObjectType.Graphics);
        ornament.SetCoid(4, true);
        var raceItem = new SimpleObject(GraphicsObjectType.Graphics);
        raceItem.SetCoid(5, true);
        var melee = new Weapon();
        melee.SetCoid(6, true);
        var front = new Weapon();
        front.SetCoid(7, true);
        var turret = new Weapon();
        turret.SetCoid(8, true);
        var rear = new Weapon();
        rear.SetCoid(9, true);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.PowerPlant, powerPlant, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WheelSet, wheelSet, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Ornament, ornament, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.RaceItem, raceItem, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponMelee, melee, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, front, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponTurret, turret, out _));
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponRear, rear, out _));

        var equipped = vehicle.EnumerateEquippedItems().Where(t => t.Item != null).ToList();
        Assert.AreEqual(9, equipped.Count);
    }

    [TestMethod]
    public void TryEquipItem_WeaponInArmorSlotRejected()
    {
        var vehicle = new Vehicle();
        var weapon = new Weapon();
        weapon.SetCoid(10, true);
        Assert.IsFalse(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, weapon, out _));
    }

    [TestMethod]
    public void EnsureGhostMaskDelivery_CreatesGhostWithoutConnection()
    {
        var vehicle = new Vehicle();
        vehicle.EnsureGhostMaskDelivery(GhostVehicle.FrontWeaponMask);
        Assert.IsNotNull(vehicle.Ghost);
    }

    [TestMethod]
    public void InventoryProperty_ReturnsOwnerCharacterInventory()
    {
        var harness = new InventoryTestHarness();
        Assert.AreSame(harness.Inventory, harness.Vehicle.Inventory);
    }

    [TestMethod]
    public void TryUnequipItem_AllSlotTypes_ClearsSlot()
    {
        var vehicle = new Vehicle();
        var slots = new (VehicleEquipmentSlot Slot, SimpleObject Item)[]
        {
            (VehicleEquipmentSlot.Armor, new Armor()),
            (VehicleEquipmentSlot.PowerPlant, new PowerPlant()),
            (VehicleEquipmentSlot.WheelSet, new WheelSet()),
            (VehicleEquipmentSlot.Ornament, new SimpleObject(GraphicsObjectType.Graphics)),
            (VehicleEquipmentSlot.RaceItem, new SimpleObject(GraphicsObjectType.Graphics)),
            (VehicleEquipmentSlot.WeaponMelee, new Weapon()),
            (VehicleEquipmentSlot.WeaponFront, new Weapon()),
            (VehicleEquipmentSlot.WeaponTurret, new Weapon()),
            (VehicleEquipmentSlot.WeaponRear, new Weapon()),
        };

        for (var i = 0; i < slots.Length; i++)
        {
            slots[i].Item.SetCoid(700 + i, true);
            vehicle.TryEquipItem(slots[i].Slot, slots[i].Item, out _);
        }

        foreach (var (slot, item) in slots)
        {
            Assert.IsTrue(vehicle.TryUnequipItem(item.ObjectId.Coid, out var clearedSlot, out _));
            Assert.AreEqual(slot, clearedSlot);
            Assert.IsNull(vehicle.GetEquippedItem(slot));
        }
    }

    [TestMethod]
    public void TryEquipItem_NullItemRejected()
    {
        var vehicle = new Vehicle();
        Assert.IsFalse(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, null, out var previous));
        Assert.IsNull(previous);
    }

    [TestMethod]
    public void TryFindEquippedItem_NotFound_ReturnsFalse()
    {
        var vehicle = new Vehicle();
        Assert.IsFalse(vehicle.TryFindEquippedItem(999, out _, out _));
        Assert.IsFalse(vehicle.TryFindEquippedItem(999, 123, out _, out _));
    }

    [TestMethod]
    public void GetEquippedItem_UnknownSlot_ReturnsNull()
    {
        var vehicle = new Vehicle();
        Assert.IsNull(vehicle.GetEquippedItem((VehicleEquipmentSlot)999));
    }
}
