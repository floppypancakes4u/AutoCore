using AutoCore.Game.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Player Character is a Creature but must never be a weapon soft/hard target —
/// vehicle combat hits the chassis; targeting the body LeaveMaps the player and resets the map.
/// </summary>
[TestClass]
public class VehicleWeaponCombatantTargetTests
{
    [TestMethod]
    public void IsWeaponCombatantTarget_Vehicle_True()
    {
        Assert.IsTrue(Vehicle.IsWeaponCombatantTarget(new Vehicle()));
    }

    [TestMethod]
    public void IsWeaponCombatantTarget_FootCreature_True()
    {
        Assert.IsTrue(Vehicle.IsWeaponCombatantTarget(new Creature()));
    }

    [TestMethod]
    public void IsWeaponCombatantTarget_Character_False()
    {
        Assert.IsFalse(Vehicle.IsWeaponCombatantTarget(new Character()));
    }

    [TestMethod]
    public void IsWeaponCombatantTarget_Null_False()
    {
        Assert.IsFalse(Vehicle.IsWeaponCombatantTarget(null));
    }

    [TestMethod]
    public void ClampFireQueryRange_CapsAtAbsoluteMax()
    {
        Assert.AreEqual(Vehicle.AbsoluteMaxWeaponRange, Vehicle.ClampFireQueryRange(999f));
        Assert.AreEqual(Vehicle.AbsoluteMaxWeaponRange, Vehicle.ClampFireQueryRange(0f));
        Assert.AreEqual(50f, Vehicle.ClampFireQueryRange(50f));
        Assert.AreEqual(120f, Vehicle.AbsoluteMaxWeaponRange);
    }
}
