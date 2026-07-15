using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class CombatCritCalculatorTests
{
    [TestMethod]
    public void BaseChance_LevelAndPerception()
    {
        // 0.02 + 0.000125 * (1 + 1) = 0.02025
        Assert.AreEqual(0.02025f, CombatCritCalculator.CalculateChance(1, 1), 0.00001f);
        // 0.02 + 0.000125 * (20 + 50) = 0.02875
        Assert.AreEqual(0.02875f, CombatCritCalculator.CalculateChance(20, 50), 0.00001f);
    }

    [TestMethod]
    public void NegativeChance_FloorsAtFivePercent()
    {
        // Only possible with huge crit defense; pass negative via offense-defense.
        Assert.AreEqual(0.05f, CombatCritCalculator.CalculateChance(1, 1, critOffense: 0f, critDefense: 1f));
    }

    [TestMethod]
    public void NoUpperCap()
    {
        // High perception + level can exceed 0.95 if we force high values via offense.
        var chance = CombatCritCalculator.CalculateChance(100, 200, critOffense: 1f, critDefense: 0f);
        Assert.IsTrue(chance > 0.95f);
    }

    [TestMethod]
    public void Multiplier_LevelTimesPointZeroOnePlusOnePointTwo()
    {
        Assert.AreEqual(1.2f, CombatCritCalculator.GetMultiplier(0), 0.0001f);
        Assert.AreEqual(1.4f, CombatCritCalculator.GetMultiplier(20), 0.0001f);
    }

    [TestMethod]
    public void Multiplier_NegativeLevel_FlooredToZero()
    {
        Assert.AreEqual(1.2f, CombatCritCalculator.GetMultiplier(-5), 0.0001f);
    }

    [TestMethod]
    public void Chance_ZeroLevel_FlooredToOne()
    {
        var a = CombatCritCalculator.CalculateChance(0, 1);
        var b = CombatCritCalculator.CalculateChance(1, 1);
        Assert.AreEqual(b, a, 0.00001f);
    }

    [TestMethod]
    public void Chance_CritOffenseAndDefense_Applied()
    {
        var baseC = CombatCritCalculator.CalculateChance(50, 100); // ~0.03875
        var withOff = CombatCritCalculator.CalculateChance(50, 100, critOffense: 0.05f);
        var withDef = CombatCritCalculator.CalculateChance(50, 100, critDefense: 0.01f);
        Assert.AreEqual(baseC + 0.05f, withOff, 0.00001f);
        Assert.AreEqual(baseC - 0.01f, withDef, 0.00001f);
    }

    [TestMethod]
    public void Chance_ExactlyZero_IsNotFlooredToFivePercent()
    {
        // base at L1/P1 = 0.02025; cancel with defense → 0.0 (not < 0, so no floor)
        var chance = CombatCritCalculator.CalculateChance(1, 1, critOffense: 0f, critDefense: 0.02025f);
        Assert.AreEqual(0f, chance, 0.00001f);
        Assert.AreNotEqual(CombatCritCalculator.CritChanceFloor, chance);
    }
}
