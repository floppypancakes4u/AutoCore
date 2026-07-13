namespace AutoCore.Game.Inventory;

/// <summary>
/// Retail cargo grid from client <c>FUN_004F3A30</c> @ 0x004F3A30:
/// <c>FUN_00572650(width=6, height=pages*13, pages)</c>.
/// Chassis <c>VehicleSpecific.InventorySlots</c> is the UI page count ("Number of Cargo Pages"),
/// not total cell count. Callisto X new-user chassis has InventorySlots=1 → 6×13 = 78 cells.
/// </summary>
public static class VehicleCargoCapacity
{
    /// <summary>Client hard-codes width 6 when constructing the cargo inventory object.</summary>
    public const int GridWidth = 6;

    /// <summary>Client multiplies page count by 0xD (13) for total height.</summary>
    public const int RowsPerPage = 13;

    /// <summary>
    /// Fixed <see cref="Packets.Sector.InventoryCargoSendAllPacket"/> item array length
    /// (0x1388 total packet size with opcode). Equals 6×13×4 pages.
    /// </summary>
    public const int MaxWireSlotCount = 312;

    /// <summary>Max chassis pages that fit the CargoSendAll wire array.</summary>
    public const int MaxWirePageCount = MaxWireSlotCount / (GridWidth * RowsPerPage); // 4

    public static int ClampPageCount(int chassisInventorySlots)
    {
        if (chassisInventorySlots < 1)
            return 1;
        return Math.Min(chassisInventorySlots, MaxWirePageCount);
    }

    /// <summary>Total grid height (rows) for inventory Y coordinates.</summary>
    public static int HeightForPages(int pageCount) => ClampPageCount(pageCount) * RowsPerPage;

    public static int SlotCountForPages(int pageCount) => GridWidth * HeightForPages(pageCount);

    /// <summary>UI page count from a capacity that stores height in <paramref name="gridHeight"/>.</summary>
    public static int UiPagesFromHeight(int gridHeight)
    {
        if (gridHeight < 1)
            return 1;
        return Math.Max(1, (gridHeight + RowsPerPage - 1) / RowsPerPage);
    }

    /// <summary>
    /// Apply retail cargo dimensions for a chassis <paramref name="inventorySlots"/> value
    /// (pages). InventoryManager.PageCount stores grid height (rows), not UI tab count.
    /// </summary>
    public static void ApplyTo(InventoryManager inventory, int chassisInventorySlots)
    {
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));

        var pages = ClampPageCount(chassisInventorySlots);
        inventory.SetCapacity(GridWidth, HeightForPages(pages));
    }
}
