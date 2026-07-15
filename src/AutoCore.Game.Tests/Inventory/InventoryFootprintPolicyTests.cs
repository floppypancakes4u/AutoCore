using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryFootprintPolicyTests
{
    [TestMethod]
    public void TryResolve_WithPositiveInvSize_ReturnsFootprint()
    {
        var clone = CreateWithSize(2, 2);

        Assert.IsTrue(InventoryFootprintPolicy.TryResolve(clone, out var sizeX, out var sizeY));
        Assert.AreEqual((byte)2, sizeX);
        Assert.AreEqual((byte)2, sizeY);
    }

    [TestMethod]
    public void TryResolve_WithThreeByTwoWeapon_ReturnsFootprint()
    {
        var clone = CreateWithSize(3, 2);

        Assert.IsTrue(InventoryFootprintPolicy.TryResolve(clone, out var sizeX, out var sizeY));
        Assert.AreEqual((byte)3, sizeX);
        Assert.AreEqual((byte)2, sizeY);
    }

    [TestMethod]
    public void TryResolve_NullCloneBase_Fails()
    {
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(null, out var sizeX, out var sizeY));
        Assert.AreEqual((byte)0, sizeX);
        Assert.AreEqual((byte)0, sizeY);
    }

    [TestMethod]
    public void TryResolve_ZeroSize_Fails()
    {
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(CreateWithSize(0, 0), out _, out _));
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(CreateWithSize(0, 1), out _, out _));
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(CreateWithSize(2, 0), out _, out _));
    }

    [TestMethod]
    public void TryResolve_NonObjectCloneBase_Fails()
    {
        var clone = (CloneBase)RuntimeHelpers.GetUninitializedObject(typeof(CloneBase));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Reaction, CloneBaseId = 1 };

        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(clone, out _, out _));
    }

    [TestMethod]
    public void TryResolveFromLookup_MissingCbid_Fails()
    {
        var lookup = new Fakes.FakeCloneBaseLookup();

        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(lookup, cbid: 99, out _, out _));
    }

    [TestMethod]
    public void TryResolveFromLookup_Registered_Succeeds()
    {
        var lookup = new Fakes.FakeCloneBaseLookup();
        lookup.Register(50, CreateWithSize(1, 1));

        Assert.IsTrue(InventoryFootprintPolicy.TryResolve(lookup, 50, out var sizeX, out var sizeY));
        Assert.AreEqual((byte)1, sizeX);
        Assert.AreEqual((byte)1, sizeY);
    }

    [TestMethod]
    public void TryResolveFromLookup_NullLookupOrNonPositiveCbid_Fails()
    {
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(null, 10, out _, out _));
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(new Fakes.FakeCloneBaseLookup(), 0, out _, out _));
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(new Fakes.FakeCloneBaseLookup(), -1, out _, out _));

        // cbid 0 must be rejected even if a lookup entry exists (guards cbid <= 0, not only < 0).
        var lookup = new Fakes.FakeCloneBaseLookup();
        lookup.Register(0, CreateWithSize(2, 2));
        Assert.IsFalse(InventoryFootprintPolicy.TryResolve(lookup, 0, out _, out _));
    }

    private static CloneBaseObject CreateWithSize(byte invSizeX, byte invSizeY)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = 10 };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { InvSizeX = invSizeX, InvSizeY = invSizeY };
        return clone;
    }
}
