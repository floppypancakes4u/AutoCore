using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class VehicleCargoCapacityTests
{
    [TestMethod]
    public void CallistoX_OneInventorySlot_IsOnePageOfSixByThirteen()
    {
        // Callisto X (and all new-user chassis) have VehicleSpecific.InventorySlots = 1.
        Assert.AreEqual(1, VehicleCargoCapacity.ClampPageCount(1));
        Assert.AreEqual(13, VehicleCargoCapacity.HeightForPages(1));
        Assert.AreEqual(78, VehicleCargoCapacity.SlotCountForPages(1));
        Assert.AreEqual(6, VehicleCargoCapacity.GridWidth);
        Assert.AreEqual(13, VehicleCargoCapacity.RowsPerPage);
    }

    [TestMethod]
    public void TypicalFourSlotChassis_IsThreeHundredTwelveCells()
    {
        Assert.AreEqual(4, VehicleCargoCapacity.ClampPageCount(4));
        Assert.AreEqual(52, VehicleCargoCapacity.HeightForPages(4));
        Assert.AreEqual(312, VehicleCargoCapacity.SlotCountForPages(4));
    }

    [TestMethod]
    public void ApplyTo_SetsRetailGridOnInventoryManager()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, chassisInventorySlots: 1);

        Assert.AreEqual(6, inventory.Width);
        Assert.AreEqual(13, inventory.PageCount);
        Assert.AreEqual(78, inventory.SlotCount);
    }

    [TestMethod]
    public void ConfigureVehicleCargo_SendsPageCountNotTotalSlots()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);
        var packet = new CreateVehicleExtendedPacket();

        InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

        // Client FUN_004F3A30 treats the short as UI page count.
        Assert.AreEqual(1, packet.InventorySlots);
        Assert.AreEqual(1, packet.NumInventorySlots);
        Assert.AreEqual(78, packet.InventorySize);
    }

    [TestMethod]
    public void CreateCargoSendAll_InventorySizeIsUiPageCount()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);

        var packet = InventoryPacketFactory.CreateCargoSendAll(inventory);

        Assert.AreEqual(1, packet.InventorySize);
    }

    [TestMethod]
    public void DefaultInventoryManager_IsStarterOnePage()
    {
        var inventory = new InventoryManager();
        Assert.AreEqual(6, inventory.Width);
        Assert.AreEqual(13, inventory.PageCount);
        Assert.AreEqual(78, inventory.SlotCount);
    }
}
