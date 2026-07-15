using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class CombatHitChanceCalculatorTests
{
    [TestMethod]
    public void EvenMatch_BaselineIsSeventyPercent()
    {
        // Combat=1, Perc=1, level 10 both, no offense/defense → ratings cancel after level terms.
        var chance = CombatHitChanceCalculator.Calculate(
            attackerLevel: 10,
            combat: 1,
            offenseBonus: 0,
            hitBonusPerLevel: 0f,
            accuracyModifier: 0f,
            victimLevel: 10,
            perception: 1,
            defenseBonus: 0);

        Assert.AreEqual(0.70f, chance, 0.0001f);
    }

    [TestMethod]
    public void PlusTwentyCombat_RaisesHitByTenPercent()
    {
        var baseChance = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 10, 1, 0);
        var raised = CombatHitChanceCalculator.Calculate(10, 21, 0, 0, 0, 10, 1, 0);
        Assert.AreEqual(0.10f, raised - baseChance, 0.0001f);
    }

    [TestMethod]
    public void VictimPerception_LowersHitSymmetrically()
    {
        var baseChance = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 10, 1, 0);
        var lowered = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 10, 21, 0);
        Assert.AreEqual(-0.10f, lowered - baseChance, 0.0001f);
    }

    [TestMethod]
    public void LevelGate_PinsWhenTenOrMoreAbove()
    {
        Assert.AreEqual(0.95f, CombatHitChanceCalculator.Calculate(20, 1, 0, 0, 0, 10, 1, 0));
        Assert.AreEqual(0.05f, CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 20, 1, 0));
    }

    [TestMethod]
    public void AccuracyModifier_MultipliesBeforeClamp()
    {
        // Even match 0.70 * 1.1 = 0.77
        var chance = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 1.1f, 10, 1, 0);
        Assert.AreEqual(0.77f, chance, 0.0001f);
    }

    [TestMethod]
    public void OffenseAndDefense_MoveRatingOneForOne()
    {
        var withOffense = CombatHitChanceCalculator.Calculate(10, 1, 20, 0, 0, 10, 1, 0);
        var withDefense = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 10, 1, 20);
        Assert.AreEqual(0.80f, withOffense, 0.0001f);
        Assert.AreEqual(0.60f, withDefense, 0.0001f);
    }

    [TestMethod]
    public void HitBonusPerLevel_AddsRoundedPoints()
    {
        // hitBonusPerLevel 0.5 * level 10 = 5 rating → +0.025
        var chance = CombatHitChanceCalculator.Calculate(10, 1, 0, 0.5f, 0, 10, 1, 0);
        Assert.AreEqual(0.725f, chance, 0.0001f);
    }

    [TestMethod]
    public void LevelGate_AtExactlyNineDelta_DoesNotPin()
    {
        // Δ = 9 is not > 9 → soft formula, not hard pin
        var chance = CombatHitChanceCalculator.Calculate(19, 1, 0, 0, 0, 10, 1, 0);
        Assert.AreNotEqual(0.95f, chance);
        Assert.IsTrue(chance > 0.05f && chance < 0.95f);
        // Δ = -9 is not < -9 → soft formula (mutant <= -9 would pin 0.05)
        var low = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 19, 1, 0);
        Assert.AreNotEqual(0.05f, low);
        Assert.IsTrue(low > 0.05f && low < 0.95f);
    }

    [TestMethod]
    public void ZeroOrNegativeLevels_FlooredToOne()
    {
        var a = CombatHitChanceCalculator.Calculate(0, 1, 0, 0, 0, 0, 1, 0);
        var b = CombatHitChanceCalculator.Calculate(1, 1, 0, 0, 0, 1, 1, 0);
        Assert.AreEqual(b, a, 0.0001f);
    }

    [TestMethod]
    public void Clamp_HighRating_DoesNotExceedMax()
    {
        var chance = CombatHitChanceCalculator.Calculate(10, 200, 500, 0, 0, 10, 1, 0);
        Assert.AreEqual(0.95f, chance, 0.0001f);
    }

    [TestMethod]
    public void AccuracyModifier_ZeroOrNegative_DoesNotMultiply()
    {
        var baseChance = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0, 10, 1, 0);
        var zeroMod = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, 0f, 10, 1, 0);
        var negMod = CombatHitChanceCalculator.Calculate(10, 1, 0, 0, -1f, 10, 1, 0);
        Assert.AreEqual(baseChance, zeroMod, 0.0001f);
        Assert.AreEqual(baseChance, negMod, 0.0001f);
    }
}
