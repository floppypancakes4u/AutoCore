using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class VehicleCoverageGapsTests
{
    [TestMethod]
    public void TryFindEquippedItem_ByCbidOnly_FindsItem()
    {
        var harness = new InventoryTestHarness();
        var weapon = harness.EquipWeapon(VehicleEquipmentSlot.WeaponTurret, cbid: 8096, coid: 501);
        AttachCloneBaseCbid(weapon, 8096);

        Assert.IsTrue(harness.Vehicle.TryFindEquippedItem(-1, 8096, out var slot, out var item));
        Assert.AreEqual(VehicleEquipmentSlot.WeaponTurret, slot);
        Assert.AreSame(weapon, item);
    }

    [TestMethod]
    public void CreateEquipmentSnapshot_UsesDbDataFallbackWhenSlotEmpty()
    {
        var vehicle = new Vehicle();
        AttachDbData(vehicle, new VehicleData
        {
            Ornament = 11,
            RaceItem = 12,
            PowerPlant = 13,
            Wheelset = 14,
            Armor = 15,
            MeleeWeapon = 16,
            Front = 17,
            Turret = 18,
            Rear = 19
        });

        var snapshot = vehicle.CreateEquipmentSnapshot();

        Assert.AreEqual(11, snapshot.Ornament);
        Assert.AreEqual(12, snapshot.RaceItem);
        Assert.AreEqual(13, snapshot.PowerPlant);
        Assert.AreEqual(14, snapshot.Wheelset);
        Assert.AreEqual(15, snapshot.Armor);
        Assert.AreEqual(16, snapshot.MeleeWeapon);
        Assert.AreEqual(17, snapshot.Front);
        Assert.AreEqual(18, snapshot.Turret);
        Assert.AreEqual(19, snapshot.Rear);
    }

    [TestMethod]
    public void TryEquipItem_UpdatesDbDataFields()
    {
        var vehicle = new Vehicle();
        var dbData = new VehicleData();
        AttachDbData(vehicle, dbData);

        var armor = new Armor();
        armor.SetCoid(501, true);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _));
        Assert.AreEqual(501, dbData.Armor);

        Assert.IsTrue(vehicle.TryUnequipItem(501, out _, out _));
        Assert.AreEqual(0, dbData.Armor);
    }

    [TestMethod]
    public void TryEquipItem_InvalidSlot_ReturnsFalse()
    {
        var vehicle = new Vehicle();
        var armor = new Armor();
        armor.SetCoid(1, true);

        Assert.IsFalse(vehicle.TryEquipItem((VehicleEquipmentSlot)999, armor, out var previous));
        Assert.IsNull(previous);
    }

    [TestMethod]
    public void TryUnequipItem_InvalidCoid_ReturnsFalse()
    {
        var vehicle = new Vehicle();
        var armor = new Armor();
        armor.SetCoid(1, true);
        vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);

        Assert.IsFalse(vehicle.TryUnequipItem(999, out _, out _));
    }

    [TestMethod]
    public void ClearEquipmentSlot_AllTypes_UpdatesDbDataAndGhostMask()
    {
        var vehicle = new Vehicle();
        var dbData = new VehicleData();
        AttachDbData(vehicle, dbData);

        var slots = new (VehicleEquipmentSlot Slot, SimpleObject Item)[]
        {
            (VehicleEquipmentSlot.Armor, CreateArmor(701)),
            (VehicleEquipmentSlot.PowerPlant, CreatePowerPlant(702)),
            (VehicleEquipmentSlot.Ornament, CreateSimple(703)),
            (VehicleEquipmentSlot.RaceItem, CreateSimple(704)),
            (VehicleEquipmentSlot.WeaponMelee, CreateWeapon(705)),
            (VehicleEquipmentSlot.WeaponFront, CreateWeapon(706)),
            (VehicleEquipmentSlot.WeaponTurret, CreateWeapon(707)),
            (VehicleEquipmentSlot.WeaponRear, CreateWeapon(708)),
            (VehicleEquipmentSlot.WheelSet, CreateWheelSet(709)),
        };

        foreach (var (slot, item) in slots)
        {
            Assert.IsTrue(vehicle.TryEquipItem(slot, item, out _), $"Equip failed for {slot}");
            vehicle.EnsureGhostMaskDelivery(1);
            Assert.IsTrue(vehicle.TryUnequipItem(item.ObjectId.Coid, out var clearedSlot, out _), $"Unequip failed for {slot}");
            Assert.AreEqual(slot, clearedSlot);
        }

        Assert.AreEqual(0, dbData.Armor);
        Assert.AreEqual(0, dbData.PowerPlant);
        Assert.AreEqual(0, dbData.Ornament);
        Assert.AreEqual(0, dbData.RaceItem);
        Assert.AreEqual(0, dbData.MeleeWeapon);
        Assert.AreEqual(0, dbData.Front);
        Assert.AreEqual(0, dbData.Turret);
        Assert.AreEqual(0, dbData.Rear);
        Assert.AreEqual(0, dbData.Wheelset);
    }

    [TestMethod]
    public void VehicleProperties_ReadFromDbData()
    {
        var vehicle = new Vehicle();
        AttachDbData(vehicle, new VehicleData
        {
            Name = "Interceptor",
            PrimaryColor = 11,
            SecondaryColor = 22,
            Trim = 3
        });

        Assert.AreEqual("Interceptor", vehicle.Name);
        Assert.AreEqual(11u, vehicle.PrimaryColor);
        Assert.AreEqual(22u, vehicle.SecondaryColor);
        Assert.AreEqual((byte)3, vehicle.Trim);
        Assert.AreSame(vehicle, vehicle.GetAsVehicle());
    }

    [TestMethod]
    public void TryEquipItem_WrongTypeForMeleeSlot_Rejected()
    {
        var vehicle = new Vehicle();
        var armor = new Armor();
        armor.SetCoid(1, true);
        Assert.IsFalse(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponMelee, armor, out _));
    }

    private static void AttachDbData(Vehicle vehicle, VehicleData dbData)
    {
        typeof(Vehicle)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vehicle, dbData);
    }

    private static void AttachCloneBaseCbid(Weapon weapon, int cbid)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific
        {
            Type = (int)CloneBaseObjectType.Weapon,
            CloneBaseId = cbid
        };
        typeof(ClonedObjectBase)
            .GetProperty(nameof(ClonedObjectBase.CloneBaseObject), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(weapon, clone);
    }

    private static Armor CreateArmor(long coid)
    {
        var armor = new Armor();
        armor.SetCoid(coid, true);
        return armor;
    }

    private static PowerPlant CreatePowerPlant(long coid)
    {
        var item = new PowerPlant();
        item.SetCoid(coid, true);
        return item;
    }

    private static Weapon CreateWeapon(long coid)
    {
        var item = new Weapon();
        item.SetCoid(coid, true);
        return item;
    }

    private static WheelSet CreateWheelSet(long coid)
    {
        var item = new WheelSet();
        item.SetCoid(coid, true);
        return item;
    }

    private static SimpleObject CreateSimple(long coid)
    {
        var item = new SimpleObject(GraphicsObjectType.Graphics);
        item.SetCoid(coid, true);
        return item;
    }
}
