using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// Heavy regression for retail cargo sizing (client FUN_004F3A30 @ 0x004F3A30):
/// width=6, height=pages×13, pages=chassis InventorySlots.
/// Callisto X / new-user chassis use InventorySlots=1 → 78 cells, 1 UI tab.
/// Typical mid chassis InventorySlots=4 → 312 cells (CargoSendAll fixed wire size).
/// </summary>
[TestClass]
public class VehicleCargoCapacityRegressionTests
{
    private const int CallistoLikeCbid = 910_001;
    private const int FourPageChassisCbid = 910_002;
    private const int EightPageChassisCbid = 910_003;
    private const int ZeroSlotChassisCbid = 910_004;

    [TestInitialize]
    public void Init()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterVehicleCloneBase(CallistoLikeCbid, inventorySlots: 1);
        AssetManagerTestHelper.RegisterVehicleCloneBase(FourPageChassisCbid, inventorySlots: 4);
        AssetManagerTestHelper.RegisterVehicleCloneBase(EightPageChassisCbid, inventorySlots: 8);
        AssetManagerTestHelper.RegisterVehicleCloneBase(ZeroSlotChassisCbid, inventorySlots: 0);
    }

    [TestCleanup]
    public void Cleanup()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    #region Formula / constants (client FUN_004F3A30)

    [TestMethod]
    public void Constants_MatchClientHardcodes()
    {
        Assert.AreEqual(6, VehicleCargoCapacity.GridWidth, "PUSH 6 in FUN_004F3A30");
        Assert.AreEqual(13, VehicleCargoCapacity.RowsPerPage, "LEA/IMUL path yields pages*0xD");
        Assert.AreEqual(312, VehicleCargoCapacity.MaxWireSlotCount, "CargoSendAll fixed array 6*13*4");
        Assert.AreEqual(4, VehicleCargoCapacity.MaxWirePageCount);
        Assert.AreEqual(78, VehicleCargoCapacity.GridWidth * VehicleCargoCapacity.RowsPerPage);
    }

    [TestMethod]
    public void ClampPageCount_FloorsAtOne_AndCapsAtWireMax()
    {
        Assert.AreEqual(1, VehicleCargoCapacity.ClampPageCount(0));
        Assert.AreEqual(1, VehicleCargoCapacity.ClampPageCount(-5));
        Assert.AreEqual(1, VehicleCargoCapacity.ClampPageCount(1));
        Assert.AreEqual(2, VehicleCargoCapacity.ClampPageCount(2));
        Assert.AreEqual(3, VehicleCargoCapacity.ClampPageCount(3));
        Assert.AreEqual(4, VehicleCargoCapacity.ClampPageCount(4));
        Assert.AreEqual(4, VehicleCargoCapacity.ClampPageCount(5), "5+ pages exceed 312-slot wire");
        Assert.AreEqual(4, VehicleCargoCapacity.ClampPageCount(8));
        Assert.AreEqual(4, VehicleCargoCapacity.ClampPageCount(99));
    }

    [TestMethod]
    public void SlotCountForPages_CoversRetailChassisDistribution()
    {
        // WAD distribution: 1 (newuser), 2, 3, 4 (most common), 5, 8
        Assert.AreEqual(78, VehicleCargoCapacity.SlotCountForPages(1), "Callisto / new-user");
        Assert.AreEqual(156, VehicleCargoCapacity.SlotCountForPages(2));
        Assert.AreEqual(234, VehicleCargoCapacity.SlotCountForPages(3));
        Assert.AreEqual(312, VehicleCargoCapacity.SlotCountForPages(4), "typical mid chassis");
        Assert.AreEqual(312, VehicleCargoCapacity.SlotCountForPages(8), "clamped to wire max");
    }

    [TestMethod]
    public void HeightForPages_IsPagesTimesThirteen()
    {
        for (var pages = 1; pages <= 4; pages++)
            Assert.AreEqual(pages * 13, VehicleCargoCapacity.HeightForPages(pages));
    }

    [TestMethod]
    public void UiPagesFromHeight_RoundTripsRetailHeights()
    {
        Assert.AreEqual(1, VehicleCargoCapacity.UiPagesFromHeight(0));
        Assert.AreEqual(1, VehicleCargoCapacity.UiPagesFromHeight(1));
        Assert.AreEqual(1, VehicleCargoCapacity.UiPagesFromHeight(13));
        Assert.AreEqual(2, VehicleCargoCapacity.UiPagesFromHeight(14));
        Assert.AreEqual(2, VehicleCargoCapacity.UiPagesFromHeight(26));
        Assert.AreEqual(4, VehicleCargoCapacity.UiPagesFromHeight(52));
        Assert.AreEqual(5, VehicleCargoCapacity.UiPagesFromHeight(53), "53 rows spans into a 5th page before clamp");
    }

    [TestMethod]
    public void ApplyTo_NullInventory_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => VehicleCargoCapacity.ApplyTo(null, 1));
    }

    [TestMethod]
    public void ApplyTo_AllPageCounts_SetWidthHeightAndSlotCount()
    {
        var inventory = new InventoryManager();
        foreach (var pages in new[] { 1, 2, 3, 4, 8 })
        {
            VehicleCargoCapacity.ApplyTo(inventory, pages);
            var expectedPages = VehicleCargoCapacity.ClampPageCount(pages);
            Assert.AreEqual(6, inventory.Width, $"pages={pages}");
            Assert.AreEqual(expectedPages * 13, inventory.PageCount, $"pages={pages} height");
            Assert.AreEqual(6 * expectedPages * 13, inventory.SlotCount, $"pages={pages} slots");
        }
    }

    #endregion

    #region Character + chassis apply

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_CallistoLike_Sets78Slots()
    {
        var character = MakeCharacterWithVehicle(CallistoLikeCbid, coid: 70_001);

        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);

        Assert.AreEqual(6, character.Inventory.Width);
        Assert.AreEqual(13, character.Inventory.PageCount);
        Assert.AreEqual(78, character.Inventory.SlotCount);
    }

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_FourPageChassis_Sets312Slots()
    {
        var character = MakeCharacterWithVehicle(FourPageChassisCbid, coid: 70_010);

        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);

        Assert.AreEqual(6, character.Inventory.Width);
        Assert.AreEqual(52, character.Inventory.PageCount);
        Assert.AreEqual(312, character.Inventory.SlotCount);
    }

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_EightPageChassis_ClampsToFourPages()
    {
        var character = MakeCharacterWithVehicle(EightPageChassisCbid, coid: 70_020);

        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);

        Assert.AreEqual(312, character.Inventory.SlotCount);
        Assert.AreEqual(52, character.Inventory.PageCount);
    }

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_ZeroChassisSlots_TreatsAsOnePage()
    {
        var character = MakeCharacterWithVehicle(ZeroSlotChassisCbid, coid: 70_030);

        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);

        Assert.AreEqual(78, character.Inventory.SlotCount);
    }

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_NoVehicle_DefaultsToOnePage()
    {
        var character = new Character();
        character.SetCoid(70_040, true);
        character.AttachInventoryForTests(new InventoryManager());
        // Leave oversized capacity then shrink via apply with no vehicle.
        character.Inventory.SetCapacity(24, 50);

        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);

        Assert.AreEqual(6, character.Inventory.Width);
        Assert.AreEqual(13, character.Inventory.PageCount);
        Assert.AreEqual(78, character.Inventory.SlotCount);
    }

    [TestMethod]
    public void ApplyCargoCapacityFromCurrentVehicle_Persist_WritesDbAndPersistence()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = MakeCharacterWithVehicle(CallistoLikeCbid, coid: 70_050, inventory);
        character.AttachTestDataForTests();

        character.ApplyCargoCapacityFromCurrentVehicle(persist: true);

        Assert.AreEqual(6, character.Inventory.Width);
        Assert.AreEqual(13, character.Inventory.PageCount);
        Assert.AreEqual(1, persistence.CapacitySaves.Count);
        Assert.AreEqual((70_050L, 6, 13), persistence.CapacitySaves[0]);
    }

    [TestMethod]
    public void ApplyCargoCapacity_ShrinksInvalidatesOutOfRangeOccupancy()
    {
        var inventory = new InventoryManager();
        // Simulate legacy wrong 24×13 capacity with an item far outside Callisto grid.
        inventory.SetCapacity(24, 13);
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "Far", 9001, 20, 10, 1)));

        VehicleCargoCapacity.ApplyTo(inventory, 1);

        // Item remains in list but new free-slot scan only uses 6×13; cannot place at old coords.
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(
            2, CloneBaseObjectType.Item, "New", 9002, 20, 10, 1)),
            "x=20 is outside retail width 6");
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
            3, CloneBaseObjectType.Item, "Ok", 9003, 5, 12, 1)));
    }

    #endregion

    #region Character selection resolution

    [TestMethod]
    public void ResolveChassisCargoPages_UsesRegisteredCloneBaseInventorySlots()
    {
        Assert.AreEqual(1, CharacterSelectionManager.ResolveChassisCargoPages(CallistoLikeCbid));
        Assert.AreEqual(4, CharacterSelectionManager.ResolveChassisCargoPages(FourPageChassisCbid));
        Assert.AreEqual(4, CharacterSelectionManager.ResolveChassisCargoPages(EightPageChassisCbid));
        Assert.AreEqual(1, CharacterSelectionManager.ResolveChassisCargoPages(ZeroSlotChassisCbid));
    }

    [TestMethod]
    public void ResolveChassisCargoPages_UnknownCbid_DefaultsToOne()
    {
        Assert.AreEqual(1, CharacterSelectionManager.ResolveChassisCargoPages(999_999_999));
    }

    #endregion

    #region Wire: CreateVehicle + CargoSendAll

    [TestMethod]
    public void ConfigureVehicleCargo_Callisto_SendsOnePageAndSeventyEightScanSize()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);
        var packet = new CreateVehicleExtendedPacket();

        InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

        Assert.AreEqual(1, packet.InventorySlots);
        Assert.AreEqual(1, packet.NumInventorySlots);
        Assert.AreEqual(78, packet.InventorySize);
    }

    [TestMethod]
    public void ConfigureVehicleCargo_FourPages_SendsFourAndThreeHundredTwelve()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 4);
        var packet = new CreateVehicleExtendedPacket();

        InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

        Assert.AreEqual(4, packet.InventorySlots);
        Assert.AreEqual(4, packet.NumInventorySlots);
        Assert.AreEqual(312, packet.InventorySize);
    }

    [TestMethod]
    public void ConfigureVehicleCargo_NeverSendsTotalSlotsAsPageShort()
    {
        // Regression: old bug set InventorySlots = SlotCount (312), client treated as pages.
        foreach (var pages in new[] { 1, 2, 3, 4 })
        {
            var inventory = new InventoryManager();
            VehicleCargoCapacity.ApplyTo(inventory, pages);
            var packet = new CreateVehicleExtendedPacket();
            InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

            Assert.AreNotEqual(inventory.SlotCount, packet.InventorySlots,
                $"pages={pages}: must not advertise total cells as page count");
            Assert.AreEqual(pages, packet.InventorySlots);
            Assert.IsTrue(packet.InventorySlots <= VehicleCargoCapacity.MaxWirePageCount);
        }
    }

    [TestMethod]
    public void ConfigureVehicleCargo_PlacesCoidsAtYTimesWidthPlusX()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 2); // height 26
        // Corner of page 0 and page 1
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 100, 0, 0, 1));
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "B", 101, 5, 12, 1));
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "C", 102, 0, 13, 1));
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "D", 103, 5, 25, 1));

        var packet = new CreateVehicleExtendedPacket();
        InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

        Assert.AreEqual(100, packet.InventoryCoids[0]);
        Assert.AreEqual(101, packet.InventoryCoids[5 + 12 * 6]);
        Assert.AreEqual(102, packet.InventoryCoids[0 + 13 * 6]);
        Assert.AreEqual(103, packet.InventoryCoids[5 + 25 * 6]);
    }

    [TestMethod]
    public void CreateCargoSendAll_UiPageCount_AndLinearSlotLayout()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 2);
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 200, 3, 14, 1));

        var packet = InventoryPacketFactory.CreateCargoSendAll(inventory);

        Assert.AreEqual(2, packet.InventorySize);
        Assert.AreEqual(312, packet.Items.Length);
        var idx = 14 * 6 + 3;
        Assert.AreEqual(200, packet.Items[idx].ItemCoid);
        Assert.AreEqual((byte)3, packet.Items[idx].PositionX);
        Assert.AreEqual((byte)14, packet.Items[idx].PositionY);
    }

    [TestMethod]
    public void CreateCargoSendAll_Callisto_OnlyFirstSeventyEightSlotsAddressable()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "Last", 300, 5, 12, 1)));
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "Beyond", 301, 0, 13, 1)),
            "Y=13 is page 2 row 0 — invalid for 1-page Callisto");

        var packet = InventoryPacketFactory.CreateCargoSendAll(inventory);
        Assert.AreEqual(1, packet.InventorySize);
        Assert.AreEqual(300, packet.Items[5 + 12 * 6].ItemCoid);
        Assert.AreEqual(-1, packet.Items[78].ItemCoid);
    }

    [TestMethod]
    public void CreateCargoSendAll_Write_PreservesFixedWireSizeForMaxPages()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 4);

        var packet = InventoryPacketFactory.CreateCargoSendAll(inventory);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);

        // opcode(4) + size(1) + pad(3) + 312 * 16 = 5000 = 0x1388
        Assert.AreEqual(0x1388, stream.Length);
        Assert.AreEqual(4, packet.InventorySize);
    }

    #endregion

    #region Fill / free-slot / bounds

    [TestMethod]
    public void CallistoGrid_FillsExactlySeventyEightSlots()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);

        for (var i = 0; i < 78; i++)
        {
            Assert.IsTrue(inventory.TryGetFirstFreeCargoSlot(out var x, out var y), $"slot {i}");
            Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
                1000 + i, CloneBaseObjectType.Item, $"I{i}", 2000 + i, x, y, 1)), $"add {i} at {x},{y}");
        }

        Assert.IsTrue(inventory.IsFull);
        Assert.IsFalse(inventory.TryGetFirstFreeCargoSlot(out _, out _));
        Assert.AreEqual(78, inventory.GetOccupiedSlotCount());
    }

    [TestMethod]
    public void FourPageGrid_FillsExactlyThreeHundredTwelveSlots()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 4);

        for (var i = 0; i < 312; i++)
        {
            Assert.IsTrue(inventory.TryGetFirstFreeCargoSlot(out var x, out var y));
            Assert.IsTrue(x is >= 0 and < 6);
            Assert.IsTrue(y is >= 0 and < 52);
            Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
                1, CloneBaseObjectType.Item, "x", 10_000 + i, x, y, 1)));
        }

        Assert.IsTrue(inventory.IsFull);
        Assert.AreEqual(312, inventory.SlotCount);
    }

    [TestMethod]
    public void FreeSlotScan_IsRowMajor_WidthSix()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);

        Assert.IsTrue(inventory.TryGetFirstFreeCargoSlot(out var x0, out var y0));
        Assert.AreEqual(0, x0);
        Assert.AreEqual(0, y0);

        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        Assert.IsTrue(inventory.TryGetFirstFreeCargoSlot(out var x1, out var y1));
        Assert.AreEqual(1, x1);
        Assert.AreEqual(0, y1);

        // Fill first row
        for (byte x = 1; x < 6; x++)
            inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "r", 10 + x, x, 0, 1));

        Assert.IsTrue(inventory.TryGetFirstFreeCargoSlot(out var x2, out var y2));
        Assert.AreEqual(0, x2);
        Assert.AreEqual(1, y2);
    }

    [TestMethod]
    public void RejectsCoordinates_OutsideSixByThirteenPerPage()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);

        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "W", 1, 6, 0, 1)), "x=6 OOB");
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "H", 2, 0, 13, 1)), "y=13 OOB on 1 page");
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "OK", 3, 5, 12, 1)));
    }

    [TestMethod]
    public void PageBoundary_Y12AndY13_ForTwoPageChassis()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 2);

        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "P0", 1, 0, 12, 1)));
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "P1", 2, 0, 13, 1)));
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "Past", 3, 0, 26, 1)));
    }

    #endregion

    #region LoadItems respects capacity

    [TestMethod]
    public void LoadItems_DropsEntriesOutsideCallistoGrid()
    {
        var inventory = new InventoryManager();
        VehicleCargoCapacity.ApplyTo(inventory, 1);

        inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "In", 1, 0, 0, 1),
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "OutX", 2, 10, 0, 1),
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "OutY", 3, 0, 20, 1),
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "Edge", 4, 5, 12, 1),
        });

        Assert.AreEqual(2, inventory.Items.Count);
        Assert.IsNotNull(inventory.FindByCoid(1));
        Assert.IsNotNull(inventory.FindByCoid(4));
        Assert.IsNull(inventory.FindByCoid(2));
        Assert.IsNull(inventory.FindByCoid(3));
    }

    #endregion

    #region Helpers

    private static Character MakeCharacterWithVehicle(int vehicleCbid, long coid, InventoryManager inventory = null)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.AttachInventoryForTests(inventory ?? new InventoryManager());

        var vehicle = new Vehicle();
        vehicle.SetCoid(coid + 1, true);
        vehicle.LoadCloneBase(vehicleCbid);
        character.SetCurrentVehicleForTests(vehicle);
        return character;
    }

    #endregion
}
