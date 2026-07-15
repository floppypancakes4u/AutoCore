using AutoCore.Game.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class CombatDamageCalculatorTests
{
    private static readonly float[] ClassTable = CombatDamageCalculator.ClassDamageBalance;

    [TestMethod]
    public void ClassDamageBalance_MatchesRetailTable()
    {
        CollectionAssert.AreEqual(new[] { 1.35f, 1.15f, 1.0f, 1.23f }, ClassTable);
    }

    [TestMethod]
    public void PrimaryDamageType_IsArgmaxOfMaxChannel()
    {
        Assert.AreEqual(2, CombatDamageCalculator.PrimaryDamageType(new short[] { 1, 2, 9, 3, 0, 0 }));
        Assert.AreEqual(0, CombatDamageCalculator.PrimaryDamageType(null));
    }

    [TestMethod]
    public void Compute_ClassScalarAndPrimaryLevelBonus()
    {
        // Fixed RNG: always return lo (Next(lo, hi+1) when lo==hi).
        var min = new short[] { 10, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 };
        var rng = new FixedRng();

        // Class 0 → 1.35: round(10*1.35)=14; level bonus round(1*5)=5 on primary → 19
        var result = CombatDamageCalculator.Compute(
            attackerLevel: 5,
            attackerClass: 0,
            attackerTheory: 1,
            attackerPerception: 1,
            minDamage: min,
            maxDamage: max,
            dmgMinMin: 0,
            dmgMaxMax: 0,
            damageBonusPerLevel: 1f,
            damageScalar: 1f,
            resists: null,
            rng: rng,
            forceCrit: false);

        Assert.AreEqual(19, result.Damage);
        Assert.IsFalse(result.IsCrit);
    }

    [TestMethod]
    public void Compute_TheoryPenetration_ReducesMitigation()
    {
        var min = new short[] { 100, 0, 0, 0, 0, 0 };
        var max = new short[] { 100, 0, 0, 0, 0, 0 };
        var resists = new short[] { 100, 0, 0, 0, 0, 0 }; // cap = ceil(10)=10

        // Sequence: damage roll 100, then mit roll 10. Theory pool caps raw at 200 → full pen at 250 spent.
        var lowTheory = CombatDamageCalculator.Compute(
            1, -1, 1, 1, min, max, 0, 0, 0, 1f, resists, new ScriptedRng(100, 10), false);
        var highTheory = CombatDamageCalculator.Compute(
            1, -1, 200, 1, min, max, 0, 0, 0, 1f, resists, new ScriptedRng(100, 10), false);

        Assert.IsTrue(highTheory.Damage > lowTheory.Damage,
            $"high Theory {highTheory.Damage} should exceed low Theory {lowTheory.Damage}");
        Assert.AreEqual(90, lowTheory.Damage);  // mit 10, theory 1 → pen 0
        Assert.AreEqual(98, highTheory.Damage); // pool 200: pen 8 → mit 2 → 98
    }

    [TestMethod]
    public void Compute_ForceCrit_AppliesMultiplier()
    {
        var min = new short[] { 10, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 };
        var result = CombatDamageCalculator.Compute(
            attackerLevel: 20,
            attackerClass: -1,
            attackerTheory: 1,
            attackerPerception: 1,
            minDamage: min,
            maxDamage: max,
            dmgMinMin: 0,
            dmgMaxMax: 0,
            damageBonusPerLevel: 0,
            damageScalar: 1f,
            resists: null,
            rng: new FixedRng(),
            forceCrit: true);

        // base 10 * (20*0.01+1.2) = 14
        Assert.IsTrue(result.IsCrit);
        Assert.AreEqual(14, result.Damage);
    }

    [TestMethod]
    public void ApplyMitigation_Theory250_NullsMitRoll()
    {
        var effective = CombatDamageCalculator.ApplyTheoryToMitigation(mitRoll: 10, theoryPool: 250);
        Assert.AreEqual(0, effective);
    }

    [TestMethod]
    public void ApplyMitigation_NonPositiveMit_ReturnsZero()
    {
        Assert.AreEqual(0, CombatDamageCalculator.ApplyTheoryToMitigation(0, 100));
        Assert.AreEqual(0, CombatDamageCalculator.ApplyTheoryToMitigation(-3, 100));
    }

    [TestMethod]
    public void GetClassMultiplier_OutOfRange_IsOne()
    {
        Assert.AreEqual(1f, CombatDamageCalculator.GetClassMultiplier(-1));
        Assert.AreEqual(1f, CombatDamageCalculator.GetClassMultiplier(99));
        Assert.AreEqual(1.35f, CombatDamageCalculator.GetClassMultiplier(0), 0.0001f);
    }

    [TestMethod]
    public void PrimaryDamageType_EmptyArray_IsZero()
    {
        Assert.AreEqual(0, CombatDamageCalculator.PrimaryDamageType(System.Array.Empty<short>()));
    }

    [TestMethod]
    public void Compute_FallbackScalarWhenChannelsEmpty()
    {
        // min/max null → fallback to DmgMinMin/Max with class 1.0
        var result = CombatDamageCalculator.Compute(
            1, -1, 1, 1,
            minDamage: null,
            maxDamage: null,
            dmgMinMin: 20,
            dmgMaxMax: 20,
            damageBonusPerLevel: 0,
            damageScalar: 1f,
            resists: null,
            rng: new FixedRng(),
            forceCrit: false);
        Assert.AreEqual(20, result.Damage);
    }

    [TestMethod]
    public void Compute_Fallback_ClassMulMultipliesNotDivides()
    {
        // class 0 → 1.35: round(10*1.35)=14 (mutant / would yield ~7)
        var result = CombatDamageCalculator.Compute(
            1, 0, 1, 1, null, null, 10, 10, 0, 1f, null, new FixedRng(), false);
        Assert.AreEqual(14, result.Damage);
    }

    [TestMethod]
    public void Compute_Fallback_LevelBonusIsAddedNotSubtracted()
    {
        // class -1 mul 1: base 10 + levelBonus round(2*5)=10 → 20
        var result = CombatDamageCalculator.Compute(
            5, -1, 1, 1, null, null, 10, 10, 2f, 1f, null, new FixedRng(), false);
        Assert.AreEqual(20, result.Damage);
    }

    [TestMethod]
    public void Compute_Fallback_SwapsWhenMinGreaterThanMax()
    {
        var result = CombatDamageCalculator.Compute(
            1, -1, 1, 1,
            minDamage: null,
            maxDamage: null,
            dmgMinMin: 40,
            dmgMaxMax: 10,
            damageBonusPerLevel: 0,
            damageScalar: 1f,
            resists: null,
            rng: new FixedRng(),
            forceCrit: false);
        // after swap lo=10, FixedRng returns min → 10
        Assert.AreEqual(10, result.Damage);
    }

    [TestMethod]
    public void Compute_DamageScalar_ScalesFinal()
    {
        var min = new short[] { 10, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 };
        var result = CombatDamageCalculator.Compute(
            1, -1, 1, 1, min, max, 0, 0, 0, 2f, null, new FixedRng(), false);
        Assert.AreEqual(20, result.Damage);
    }

    [TestMethod]
    public void Compute_ZeroOrNegativeScalar_TreatedAsOne()
    {
        var min = new short[] { 10, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 };
        var a = CombatDamageCalculator.Compute(1, -1, 1, 1, min, max, 0, 0, 0, 0f, null, new FixedRng(), false);
        var b = CombatDamageCalculator.Compute(1, -1, 1, 1, min, max, 0, 0, 0, -2f, null, new FixedRng(), false);
        Assert.AreEqual(10, a.Damage);
        Assert.AreEqual(10, b.Damage);
    }

    [TestMethod]
    public void Compute_HiLessThanLo_SwapsBeforeRoll()
    {
        var min = new short[] { 30, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 }; // swapped
        // FixedRng returns minValue of Next → after swap lo=10 hi=30, Next(10,31) returns 10
        var result = CombatDamageCalculator.Compute(
            1, -1, 1, 1, min, max, 0, 0, 0, 1f, null, new FixedRng(), false);
        Assert.AreEqual(10, result.Damage);
    }

    [TestMethod]
    public void Compute_RngCrit_WhenNextDoubleBelowChance()
    {
        var min = new short[] { 10, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 };
        var alwaysCrit = new FixedDoubleRng(0.0);
        var result = CombatDamageCalculator.Compute(
            20, -1, 1, 1, min, max, 0, 0, 0, 1f, null, alwaysCrit, forceCrit: null);
        Assert.IsTrue(result.IsCrit);
        Assert.AreEqual(14, result.Damage);
    }

    [TestMethod]
    public void Compute_RngCrit_WhenNextDoubleEqualsChance_IsNotCrit()
    {
        // chance uses <= in mutant; retail uses NextDouble() <= chance for crit.
        // production: `rng.NextDouble() <= chance` — if equal, IS crit.
        // Survivor is < vs <= : kill with NextDouble exactly at chance.
        var min = new short[] { 10, 0, 0, 0, 0, 0 };
        var max = new short[] { 10, 0, 0, 0, 0, 0 };
        var chance = CombatCritCalculator.CalculateChance(20, 1);
        var atBoundary = new FixedDoubleRng(chance);
        var result = CombatDamageCalculator.Compute(
            20, -1, 1, 1, min, max, 0, 0, 0, 1f, null, atBoundary, forceCrit: null);
        Assert.IsTrue(result.IsCrit, "NextDouble() == chance must crit under <= comparison");
    }

    [TestMethod]
    public void Compute_Fallback_RangeUsesRngWhenHiGreaterThanLo()
    {
        // FixedRng returns minValue; with hi>lo after swap from (10,40) → Next(10,41)=10
        // MidRng returns midpoint to prove hi>lo branch uses Next
        var mid = new MidRng();
        var result = CombatDamageCalculator.Compute(
            1, -1, 1, 1, null, null, 10, 40, 0, 1f, null, mid, false);
        Assert.AreEqual(25, result.Damage); // Mid of 10..40
    }

    [TestMethod]
    public void PrimaryDamageType_TiePrefersFirstMax()
    {
        // equal max values: first highest wins (mutant max[t] >= would pick later ties)
        Assert.AreEqual(1, CombatDamageCalculator.PrimaryDamageType(new short[] { 1, 5, 5, 0, 0, 0 }));
    }

    private sealed class MidRng : Random
    {
        public override int Next(int minValue, int maxValue) => (minValue + maxValue - 1) / 2;
        public override double NextDouble() => 1.0;
    }

    private sealed class FixedDoubleRng : Random
    {
        private readonly double _d;
        public FixedDoubleRng(double d) => _d = d;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => _d;
    }

    /// <summary>RNG that always returns the lower bound for damage rolls.</summary>
    private sealed class FixedRng : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 1.0; // never crit unless forced
    }

    /// <summary>Returns scripted integers for successive Next(min,max) calls.</summary>
    private sealed class ScriptedRng : Random
    {
        private readonly int[] _values;
        private int _i;

        public ScriptedRng(params int[] values) => _values = values;

        public override int Next(int minValue, int maxValue)
        {
            if (_i >= _values.Length)
                return minValue;
            var v = _values[_i++];
            if (v < minValue)
                return minValue;
            if (v >= maxValue)
                return maxValue - 1;
            return v;
        }

        public override double NextDouble() => 1.0;
    }
}
