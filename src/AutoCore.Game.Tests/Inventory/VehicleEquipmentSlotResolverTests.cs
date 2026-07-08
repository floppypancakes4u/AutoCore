using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class VehicleEquipmentSlotResolverTests
{
    [TestMethod]
    public void TryResolve_MapsNonWeaponTypesDirectly()
    {
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Armor, null, 0, out var armor));
        Assert.AreEqual(VehicleEquipmentSlot.Armor, armor);

        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.PowerPlant, null, 0, out var pp));
        Assert.AreEqual(VehicleEquipmentSlot.PowerPlant, pp);

        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.WheelSet, null, 0, out var wheels));
        Assert.AreEqual(VehicleEquipmentSlot.WheelSet, wheels);
    }

    [TestMethod]
    public void TryResolve_MapsRaceItemAndOrnamentTypes()
    {
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.RaceItem, null, 0, out var race));
        Assert.AreEqual(VehicleEquipmentSlot.RaceItem, race);

        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Ornament, null, 0, out var ornament));
        Assert.AreEqual(VehicleEquipmentSlot.Ornament, ornament);
    }

    [TestMethod]
    public void TryResolve_MapsItemTypeToRaceItem_WhenCloneBaseMissing_RetailHazardFallback()
    {
        // Cargo re-equip of CBID 5782 logs type=Item with no subtype available on null clonebase.
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Item, null, 255, out var slot));
        Assert.AreEqual(VehicleEquipmentSlot.RaceItem, slot);
    }

    [TestMethod]
    public void TryResolveItemSlot_UsesClientSubType_Ornament10_RaceItem11()
    {
        var ornament = CreateItemCloneBase(VehicleEquipmentSlotResolver.ItemSubTypeOrnament);
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolveItemSlot(ornament, out var ornamentSlot));
        Assert.AreEqual(VehicleEquipmentSlot.Ornament, ornamentSlot);

        var raceItem = CreateItemCloneBase(VehicleEquipmentSlotResolver.ItemSubTypeRaceItem);
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolveItemSlot(raceItem, out var raceSlot));
        Assert.AreEqual(VehicleEquipmentSlot.RaceItem, raceSlot);
    }

    [TestMethod]
    public void TryResolveWeaponSlot_UsesDropXFallbackWhenFlagsEmpty()
    {
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolveWeaponSlot(null, 1, out var slot) == false);
        Assert.AreEqual(default, slot);
    }

    [TestMethod]
    public void TryResolve_RejectsUnsupportedTypes()
    {
        Assert.IsFalse(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Vehicle, null, 0, out _));
        Assert.IsFalse(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Creature, null, 0, out _));
        Assert.IsFalse(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Commodity, null, 0, out _));
    }

    private static CloneBaseObject CreateItemCloneBase(short subType)
    {
        // CloneBaseObject requires a BinaryReader ctor; build a minimal fake via uninitialized + field set.
        var clone = (CloneBaseObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { SubType = subType };
        return clone;
    }
}
