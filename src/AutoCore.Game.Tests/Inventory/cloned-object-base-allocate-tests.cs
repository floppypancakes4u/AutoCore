using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class ClonedObjectBaseAllocateTests
{
    [TestCleanup]
    public void Cleanup() => AssetManagerTestHelper.ClearRegisteredCloneBases();

    [TestMethod]
    public void AllocateNewObjectFromCBID_ReturnsNullWhenCloneBaseMissing()
    {
        Assert.IsNull(ClonedObjectBase.AllocateNewObjectFromCBID(999999));
    }

    [TestMethod]
    public void AllocateNewObjectFromCBID_ItemCommodityAndMoneyReturnSimpleObject()
    {
        AssetManagerTestHelper.RegisterCloneBase(100, CloneBaseObjectType.Item);
        AssetManagerTestHelper.RegisterCloneBase(101, CloneBaseObjectType.Commodity);
        AssetManagerTestHelper.RegisterCloneBase(102, CloneBaseObjectType.Money);

        Assert.IsInstanceOfType(ClonedObjectBase.AllocateNewObjectFromCBID(100), typeof(SimpleObject));
        Assert.IsInstanceOfType(ClonedObjectBase.AllocateNewObjectFromCBID(101), typeof(SimpleObject));
        Assert.IsInstanceOfType(ClonedObjectBase.AllocateNewObjectFromCBID(102), typeof(SimpleObject));
    }

    [TestMethod]
    public void AllocateNewObjectFromCBID_WeaponArmorAndWheelSetReturnTypedObjects()
    {
        AssetManagerTestHelper.RegisterCloneBase(200, CloneBaseObjectType.Weapon);
        AssetManagerTestHelper.RegisterCloneBase(201, CloneBaseObjectType.Armor);
        AssetManagerTestHelper.RegisterCloneBase(202, CloneBaseObjectType.WheelSet);

        Assert.IsInstanceOfType(ClonedObjectBase.AllocateNewObjectFromCBID(200), typeof(Weapon));
        Assert.IsInstanceOfType(ClonedObjectBase.AllocateNewObjectFromCBID(201), typeof(Armor));
        Assert.IsInstanceOfType(ClonedObjectBase.AllocateNewObjectFromCBID(202), typeof(WheelSet));
    }

    [TestMethod]
    public void AllocateNewObjectFromCBID_UnsupportedTypeReturnsNull()
    {
        AssetManagerTestHelper.RegisterCloneBase(300, CloneBaseObjectType.Creature);

        Assert.IsNull(ClonedObjectBase.AllocateNewObjectFromCBID(300));
    }
}
