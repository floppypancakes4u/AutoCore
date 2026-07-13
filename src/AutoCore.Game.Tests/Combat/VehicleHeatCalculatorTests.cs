using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class VehicleHeatCalculatorTests
{
    [TestMethod]
    public void CalculatePlayerMaxHeat_TechScaleIsHalf()
    {
        Assert.AreEqual(0.5f, VehicleHeatCalculator.TechScale);
    }

    [TestMethod]
    public void CalculatePlayerMaxHeat_IncludesPowerPlantAndTech()
    {
        // race/class mult 1.0: ceil(1 * (level*1 + techPool*0.5 + ppHeat) + heatMaxAdd)
        // tech=0 → pool 1 → ceil(1 + 0.5 + 100 + 0) = 102
        var max = VehicleHeatCalculator.CalculatePlayerMaxHeat(
            race: 0,
            classId: 0,
            level: 1,
            tech: 0,
            powerPlantHeatMaximum: 100,
            heatMaxAdd: 0);

        Assert.AreEqual(102, max);
    }

    [TestMethod]
    public void CalculatePlayerMaxHeat_TechIncreaseRaisesCap()
    {
        // tech*0.5: even pool is whole, odd is .5 → ceil steps every other Tech point.
        // tech 6 → 3.0 → ceil(1+3+100)=104; tech 7 → 3.5 → ceil(104.5)=105
        var atSix = VehicleHeatCalculator.CalculatePlayerMaxHeat(0, 0, 1, 6, 100, 0);
        var atSeven = VehicleHeatCalculator.CalculatePlayerMaxHeat(0, 0, 1, 7, 100, 0);
        Assert.AreEqual(104, atSix);
        Assert.AreEqual(105, atSeven);
        Assert.IsTrue(atSeven > atSix, "odd Tech pool steps heat cap via ceil");
    }

    [TestMethod]
    public void CalculatePlayerMaxHeat_HeatMaxAddApplied()
    {
        var baseMax = VehicleHeatCalculator.CalculatePlayerMaxHeat(0, 0, 1, 0, 100, 0);
        var withAdd = VehicleHeatCalculator.CalculatePlayerMaxHeat(0, 0, 1, 0, 100, 10);
        Assert.AreEqual(baseMax + 10, withAdd);
    }
}
