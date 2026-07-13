using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Game.Map;

/// <summary>Full API regression for <see cref="CharacterMapPresence"/> (target 100% line coverage).</summary>
[TestClass]
public class CharacterMapPresenceRegressionTests
{
    [TestMethod]
    public void EnsureContinent_SameId_DoesNotClear()
    {
        var p = new CharacterMapPresence();
        p.EnsureContinent(7);
        p.Suppress(10);
        p.Materialize(20);
        p.TrackOwnedCombat(30);
        p.MarkDeliverTurnInReady(12448);
        p.EnsureContinent(7);
        Assert.IsTrue(p.IsSuppressed(10));
        Assert.IsTrue(p.IsMaterialized(20));
        Assert.IsTrue(p.OwnsCombat(30));
        Assert.IsTrue(p.IsDeliverTurnInReady(12448));
        Assert.AreEqual(7, p.ContinentId);
    }

    [TestMethod]
    public void EnsureContinent_DifferentId_ClearsAllLedgers()
    {
        var p = new CharacterMapPresence();
        p.EnsureContinent(1);
        p.Suppress(10);
        p.Materialize(20);
        p.TrackOwnedCombat(30);
        p.MarkDeliverTurnInReady(99);
        p.EnsureContinent(2);
        Assert.AreEqual(2, p.ContinentId);
        Assert.IsFalse(p.IsSuppressed(10));
        Assert.IsFalse(p.IsMaterialized(20));
        Assert.IsFalse(p.OwnsCombat(30));
        Assert.IsFalse(p.IsDeliverTurnInReady(99));
    }

    [TestMethod]
    public void Clear_ResetsContinentAndAllSets()
    {
        var p = new CharacterMapPresence();
        p.EnsureContinent(3);
        p.Suppress(1);
        p.Materialize(2);
        p.TrackOwnedCombat(3);
        p.MarkDeliverTurnInReady(4);
        p.Clear();
        Assert.AreEqual(-1, p.ContinentId);
        Assert.IsFalse(p.IsSuppressed(1));
        Assert.IsFalse(p.IsMaterialized(2));
        Assert.IsFalse(p.OwnsCombat(3));
        Assert.IsFalse(p.IsDeliverTurnInReady(4));
    }

    [TestMethod]
    public void Suppress_Materialize_Unsuppress_Interaction()
    {
        var p = new CharacterMapPresence();
        p.Suppress(0); // no-op
        p.Suppress(-1);
        p.Materialize(0);
        p.Unsuppress(0);
        p.MarkDeliverTurnInReady(0);
        Assert.IsFalse(p.IsDeliverTurnInReady(0));

        p.Materialize(50);
        Assert.IsTrue(p.IsMaterialized(50));
        p.Suppress(50);
        Assert.IsTrue(p.IsSuppressed(50));
        Assert.IsFalse(p.IsMaterialized(50));
        p.Unsuppress(50);
        Assert.IsFalse(p.IsSuppressed(50));
        p.Materialize(50);
        Assert.IsFalse(p.IsSuppressed(50));
        Assert.IsTrue(p.IsMaterialized(50));
    }

    [TestMethod]
    public void IsPresentForCharacter_Matrix()
    {
        var p = new CharacterMapPresence();
        Assert.IsFalse(p.IsPresentForCharacter(0, true));
        Assert.IsFalse(p.IsPresentForCharacter(-1, true));
        Assert.IsTrue(p.IsPresentForCharacter(1, famDefaultActive: true));
        Assert.IsFalse(p.IsPresentForCharacter(1, famDefaultActive: false));
        p.Suppress(1);
        Assert.IsFalse(p.IsPresentForCharacter(1, true));
        p.Materialize(1);
        Assert.IsTrue(p.IsPresentForCharacter(1, false));
        p.Suppress(1);
        Assert.IsFalse(p.IsPresentForCharacter(1, false));
    }

    [TestMethod]
    public void OwnedCombat_TrackAndUntrack()
    {
        var p = new CharacterMapPresence();
        p.TrackOwnedCombat(0);
        p.UntrackOwnedCombat(0);
        p.TrackOwnedCombat(77);
        Assert.IsTrue(p.OwnsCombat(77));
        Assert.IsTrue(p.OwnedCombatCoids.Contains(77));
        p.UntrackOwnedCombat(77);
        Assert.IsFalse(p.OwnsCombat(77));
        p.UntrackOwnedCombat(77); // idempotent
    }

    [TestMethod]
    public void SuppressMany_MaterializeMany_NullAndValues()
    {
        var p = new CharacterMapPresence();
        p.SuppressMany(null);
        p.MaterializeMany(null);
        p.SuppressMany(new long[] { 1, 2, 0, -1 });
        Assert.IsTrue(p.IsSuppressed(1));
        Assert.IsTrue(p.IsSuppressed(2));
        Assert.IsTrue(p.SuppressedCoids.Contains(1));
        p.MaterializeMany(new long[] { 1, 3 });
        Assert.IsFalse(p.IsSuppressed(1));
        Assert.IsTrue(p.IsMaterialized(1));
        Assert.IsTrue(p.IsMaterialized(3));
        Assert.IsTrue(p.MaterializedCoids.Contains(3));
        Assert.IsTrue(p.IsSuppressed(2));
    }

    [TestMethod]
    public void DeliverTurnInReady_MarkAndQuery()
    {
        var p = new CharacterMapPresence();
        Assert.IsFalse(p.IsDeliverTurnInReady(12448));
        p.MarkDeliverTurnInReady(12448);
        Assert.IsTrue(p.IsDeliverTurnInReady(12448));
        p.MarkDeliverTurnInReady(12448); // idempotent
        Assert.IsTrue(p.IsDeliverTurnInReady(12448));
        p.EnsureContinent(9);
        Assert.IsFalse(p.IsDeliverTurnInReady(12448));
    }
}
