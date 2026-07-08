using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryItemCreatorPacketTypeTests
{
    [TestMethod]
    public void CreatePacketFor_UsesTypedCreatePacketsForEquipment()
    {
        Assert.IsInstanceOfType(
            InventoryItemCreator.CreatePacketFor(CloneBaseObjectType.Weapon),
            typeof(CreateWeaponPacket));
        Assert.IsInstanceOfType(
            InventoryItemCreator.CreatePacketFor(CloneBaseObjectType.Armor),
            typeof(CreateArmorPacket));
        Assert.IsInstanceOfType(
            InventoryItemCreator.CreatePacketFor(CloneBaseObjectType.PowerPlant),
            typeof(CreatePowerPlantPacket));
        Assert.IsInstanceOfType(
            InventoryItemCreator.CreatePacketFor(CloneBaseObjectType.WheelSet),
            typeof(CreateWheelSetPacket));
        Assert.IsInstanceOfType(
            InventoryItemCreator.CreatePacketFor(CloneBaseObjectType.Item),
            typeof(CreateSimpleObjectPacket));
        Assert.AreEqual(
            typeof(CreateSimpleObjectPacket),
            InventoryItemCreator.CreatePacketFor(CloneBaseObjectType.Item).GetType());
    }
}
