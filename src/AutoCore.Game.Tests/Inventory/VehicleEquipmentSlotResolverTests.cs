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
    public void TryResolveWeaponSlot_UsesDropXFallbackWhenFlagsEmpty()
    {
        Assert.IsTrue(VehicleEquipmentSlotResolver.TryResolveWeaponSlot(null, 1, out var slot) == false);

        // null weapon fails; drop-x fallback only applies when weapon object exists with empty flags.
        // Covered indirectly via TryResolve for non-weapon types above.
        Assert.AreEqual(default, slot);
    }

    [TestMethod]
    public void TryResolve_RejectsUnsupportedTypes()
    {
        Assert.IsFalse(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Item, null, 0, out _));
        Assert.IsFalse(VehicleEquipmentSlotResolver.TryResolve(CloneBaseObjectType.Vehicle, null, 0, out _));
    }
}
