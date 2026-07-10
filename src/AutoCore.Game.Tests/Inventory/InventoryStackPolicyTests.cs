using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryStackPolicyTests
{
    [TestMethod]
    public void Normalize_ZeroOrOne_IsNonStackable()
    {
        Assert.AreEqual((1, false), InventoryStackPolicy.GetLimits(null));
        Assert.AreEqual((1, false), InventoryStackPolicy.GetLimits(CreateWithStackSize(0)));
        Assert.AreEqual((1, false), InventoryStackPolicy.GetLimits(CreateWithStackSize(1)));
    }

    [TestMethod]
    public void Normalize_GreaterThanOne_IsStackable()
    {
        Assert.AreEqual((99, true), InventoryStackPolicy.GetLimits(CreateWithStackSize(99)));
    }

    [TestMethod]
    public void IsPartialSplit_OnlyWhenRequestedIsStrictlyBetweenZeroAndSource()
    {
        Assert.IsFalse(InventoryStackPolicy.IsPartialSplitRequest(sourceQuantity: 5, requestedCount: 0));
        Assert.IsFalse(InventoryStackPolicy.IsPartialSplitRequest(sourceQuantity: 5, requestedCount: 5));
        Assert.IsFalse(InventoryStackPolicy.IsPartialSplitRequest(sourceQuantity: 5, requestedCount: 6));
        Assert.IsFalse(InventoryStackPolicy.IsPartialSplitRequest(sourceQuantity: 1, requestedCount: 1));
        Assert.IsTrue(InventoryStackPolicy.IsPartialSplitRequest(sourceQuantity: 5, requestedCount: 3));
    }

    [TestMethod]
    public void MergeAmount_RespectsMaxStack()
    {
        Assert.AreEqual(2, InventoryStackPolicy.ComputeMergeAmount(currentQuantity: 8, incomingQuantity: 5, maxStack: 10));
        Assert.AreEqual(5, InventoryStackPolicy.ComputeMergeAmount(currentQuantity: 1, incomingQuantity: 5, maxStack: 99));
        Assert.AreEqual(0, InventoryStackPolicy.ComputeMergeAmount(currentQuantity: 10, incomingQuantity: 5, maxStack: 10));
    }

    private static CloneBaseObject CreateWithStackSize(ushort stackSize)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = 10 };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { StackSize = stackSize };
        return clone;
    }
}
