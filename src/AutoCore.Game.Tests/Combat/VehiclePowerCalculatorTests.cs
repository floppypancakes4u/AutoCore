using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class VehiclePowerCalculatorTests
{
    [TestMethod]
    public void PowerLevelCoeff_MatchesRetailTable()
    {
        CollectionAssert.AreEqual(
            new[] { 0.6f, 1.0f, 1.0f, 0.75f },
            VehiclePowerCalculator.PowerLevelCoeff);
    }

    [TestMethod]
    public void Calculate_TheoryAddsTwoPerPoint()
    {
        var atOne = VehiclePowerCalculator.CalculatePlayerMaxPower(0, 1, 1, 100);
        var atTwo = VehiclePowerCalculator.CalculatePlayerMaxPower(0, 1, 2, 100);
        Assert.AreEqual(atOne + 2, atTwo);
    }

    [TestMethod]
    public void Calculate_IncludesPlantAndClassLevel()
    {
        // class 0: 0.6*level + theory*2 + plant
        // level 10, theory 1 → pool 1, plant 50: ceil(6 + 2 + 50) = 58
        var max = VehiclePowerCalculator.CalculatePlayerMaxPower(
            classId: 0,
            level: 10,
            theory: 1,
            powerPlantPowerMaximum: 50);
        Assert.AreEqual(58, max);
    }

    [TestMethod]
    public void Calculate_ZeroPlant_StillHasCore()
    {
        var max = VehiclePowerCalculator.CalculatePlayerMaxPower(1, 5, 1, 0);
        // class 1 coeff 1.0: 5 + 2 + 0 = 7
        Assert.AreEqual(7, max);
    }

    [TestMethod]
    public void Calculate_NegativePlant_ClampedToZero()
    {
        var max = VehiclePowerCalculator.CalculatePlayerMaxPower(1, 5, 1, -50);
        Assert.AreEqual(7, max);
    }

    [TestMethod]
    public void Calculate_LevelZero_FlooredToOne()
    {
        var max = VehiclePowerCalculator.CalculatePlayerMaxPower(1, 0, 1, 0);
        // 1 * 1.0 + 2 + 0 = 3
        Assert.AreEqual(3, max);
    }

    [TestMethod]
    public void Calculate_ClassOutOfRange_Clamped()
    {
        var at3 = VehiclePowerCalculator.CalculatePlayerMaxPower(3, 10, 1, 0);
        var at99 = VehiclePowerCalculator.CalculatePlayerMaxPower(99, 10, 1, 0);
        // class 3 coeff 0.75: 10*0.75 + 2 = 9.5 → 10
        Assert.AreEqual(10, at3);
        Assert.AreEqual(at3, at99);
    }
}
