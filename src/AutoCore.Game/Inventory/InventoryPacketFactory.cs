namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets.Sector;

public static class InventoryPacketFactory
{
    public static void ConfigureVehicleCargo(CreateVehiclePacket packet, InventoryManager inventory = null)
    {
        // CreateVehicle short InventorySlots = UI page count (client FUN_004F3A30).
        var height = inventory?.PageCount ?? InventoryManager.DefaultCargoPageCount;
        var width = inventory?.Width ?? InventoryManager.DefaultCargoWidth;
        var uiPages = VehicleCargoCapacity.UiPagesFromHeight(height);
        var slotCount = inventory?.SlotCount ?? InventoryManager.DefaultCargoSlotCount;

        packet.InventorySlots = (short)uiPages;

        if (packet is not CreateVehicleExtendedPacket extendedPacket)
            return;

        // Extended: NumInventorySlots = pages; InventorySize = how many COID slots to scan.
        extendedPacket.NumInventorySlots = (short)uiPages;
        extendedPacket.InventorySize = (ushort)Math.Min(slotCount, extendedPacket.InventoryCoids.Length);

        if (inventory == null)
            return;

        foreach (var item in inventory.Items)
        {
            var slot = item.InventoryPositionY * width + item.InventoryPositionX;
            if (slot < 0 || slot >= extendedPacket.InventoryCoids.Length)
                continue;

            extendedPacket.InventoryCoids[slot] = item.Coid;
        }
    }

    public static InventoryCargoSendAllPacket CreateCargoSendAll(InventoryManager inventory)
    {
        var height = inventory?.PageCount ?? InventoryManager.DefaultCargoPageCount;
        var width = inventory?.Width ?? InventoryManager.DefaultCargoWidth;
        var slotCount = inventory?.SlotCount ?? InventoryManager.DefaultCargoSlotCount;
        var uiPages = VehicleCargoCapacity.UiPagesFromHeight(height);

        var packet = new InventoryCargoSendAllPacket
        {
            // Client cargo tab count ("Number of Cargo Pages").
            InventorySize = (byte)Math.Min(byte.MaxValue, uiPages)
        };

        if (inventory == null)
            return packet;

        foreach (var item in inventory.Items)
        {
            var slot = item.InventoryPositionY * width + item.InventoryPositionX;
            if (slot < 0 || slot >= slotCount || slot >= packet.Items.Length)
                continue;

            packet.Items[slot] = new InventoryPacketItem
            {
                ItemCoid = item.Coid,
                PositionX = item.InventoryPositionX,
                PositionY = item.InventoryPositionY
            };
        }

        return packet;
    }

    /// <summary>
    /// Fill CreateCharacterExtended.InventoryCoids with locker origins (origin-only, width 6).
    /// Client CVOGCharacter_ApplyCreateFromPacket @ 0x00534bd0 walks 312 COIDs from packet+0x960
    /// and places each resolved object into the locker grid via FUN_00571d30.
    /// Requires Create (IsInInventory) packets for those COIDs to have been sent first.
    /// </summary>
    public static void ConfigureCharacterLocker(CreateCharacterExtendedPacket packet, InventoryManager inventory)
    {
        if (packet == null || inventory == null)
            return;

        var width = inventory.LockerWidth;
        var maxSlots = Math.Min(packet.InventoryCoids.Length, inventory.LockerSlotCount);

        foreach (var item in inventory.LockerItems)
        {
            var slot = item.InventoryPositionY * width + item.InventoryPositionX;
            if (slot < 0 || slot >= maxSlots)
                continue;

            packet.InventoryCoids[slot] = item.Coid;
        }
    }
}
