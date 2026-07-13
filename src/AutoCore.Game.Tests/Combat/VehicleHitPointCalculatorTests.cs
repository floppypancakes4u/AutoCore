using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class VehicleHitPointCalculatorTests
{
    [TestMethod]
    public void GetTechForPoolCalcs_DefaultsAndClamps()
    {
        Assert.AreEqual(1, VehicleHitPointCalculator.GetTechForPoolCalcs(0));
        Assert.AreEqual(1, VehicleHitPointCalculator.GetTechForPoolCalcs(1));
        Assert.AreEqual(10, VehicleHitPointCalculator.GetTechForPoolCalcs(10));
        Assert.AreEqual(200, VehicleHitPointCalculator.GetTechForPoolCalcs(200));
        Assert.AreEqual(200, VehicleHitPointCalculator.GetTechForPoolCalcs(500));
        Assert.AreEqual(250, VehicleHitPointCalculator.GetTechForPoolCalcs(200, 100));
        Assert.AreEqual(50, VehicleHitPointCalculator.GetTechForPoolCalcs(40, 10));
    }

    [TestMethod]
    public void CalculatePlayerMaxHp_CallistoX_HumanRaider_StarterCeramic()
    {
        // Callisto X ArmorAdd=7, human starter ceramic ArmorFactor=13, race=0 class=3 (raider).
        // Clonebase MaxHitPoint is 1 — live max must use the retail formula, not the stub.
        var max = VehicleHitPointCalculator.CalculatePlayerMaxHp(
            race: 0,
            classId: 3,
            level: 1,
            tech: 0,
            armorFactor: 13,
            chassisArmorAdd: 7);

        Assert.IsTrue(max > 1, "computed max HP must exceed clonebase stub of 1");
        Assert.AreEqual(162, max);
    }

    [TestMethod]
    public void CalculatePlayerMaxHp_HumanCommando_IsLowerThanRaider()
    {
        var raider = VehicleHitPointCalculator.CalculatePlayerMaxHp(0, 3, 1, 0, 13, 7);
        var commando = VehicleHitPointCalculator.CalculatePlayerMaxHp(0, 0, 1, 0, 13, 7);
        Assert.IsTrue(raider > commando);
        Assert.AreEqual(93, commando);
    }
}
