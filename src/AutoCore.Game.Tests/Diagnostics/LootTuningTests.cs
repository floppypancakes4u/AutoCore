using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;

[TestClass]
public class LootTuningTests
{
    [TestInitialize]
    public void SetUp() => LootTuning.ResetToDefaults();

    [TestCleanup]
    public void TearDown() => LootTuning.ResetToDefaults();

    [TestMethod]
    public void ScaleChance_RateOne_Unchanged()
    {
        LootTuning.LootRate = 1.0;
        Assert.AreEqual(0.02, LootTuning.ScaleChance(0.02), 1e-9);
        Assert.AreEqual(1.0, LootTuning.ScaleChance(1.0), 1e-9);
    }

    [TestMethod]
    public void ScaleChance_Rate1000_MakesSmallChanceCertain()
    {
        LootTuning.LootRate = 1000.0;
        // 0.02 * 1000 = 20 → clamp to 1.0
        Assert.AreEqual(1.0, LootTuning.ScaleChance(0.02), 1e-9);
        // tinLootChance 10/255 ≈ 0.039 * 1000 → 1.0
        Assert.AreEqual(1.0, LootTuning.ScaleChance(10.0 / 255.0), 1e-9);
    }

    [TestMethod]
    public void ScaleChance_ZeroBase_StaysZero_EvenWithHighRate()
    {
        LootTuning.LootRate = 1000.0;
        Assert.AreEqual(0.0, LootTuning.ScaleChance(0.0), 1e-9);
    }

    [TestMethod]
    public void ScaleChance_RateZero_DisablesDrops()
    {
        LootTuning.LootRate = 0;
        Assert.AreEqual(0.0, LootTuning.ScaleChance(1.0), 1e-9);
    }

    [TestMethod]
    public void ApplyFromJson_SetsLootRate()
    {
        Assert.IsTrue(LootTuning.ApplyFromJson("""{"LootRate": 50.5}""", out var error), error);
        Assert.AreEqual(50.5, LootTuning.LootRate, 1e-9);
    }

    [TestMethod]
    public void ApplyFromJson_SetsIgnoreDropCommoditiesGate()
    {
        Assert.IsTrue(
            LootTuning.ApplyFromJson("""{"LootRate": 1, "IgnoreDropCommoditiesGate": true}""", out var error),
            error);
        Assert.IsTrue(LootTuning.IgnoreDropCommoditiesGate);
    }

    [TestMethod]
    public void ApplyFromJson_InvalidRate_Fails()
    {
        Assert.IsFalse(LootTuning.ApplyFromJson("""{"LootRate":"nope"}""", out _));
        Assert.AreEqual(LootTuning.DefaultLootRate, LootTuning.LootRate);
    }

    [TestMethod]
    public void Passes_ScaledOne_AlwaysTrue()
    {
        LootTuning.LootRate = 1000.0;
        var rng = new Random(1);
        for (var i = 0; i < 20; i++)
            Assert.IsTrue(LootTuning.Passes(0.05, rng));
    }
}
