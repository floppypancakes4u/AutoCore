namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets.Sector;

public static class InventoryPacketFactory
{
    public static void ConfigureVehicleCargo(CreateVehiclePacket packet, InventoryManager inventory = null)
    {
        var slotCount = inventory?.SlotCount ?? InventoryManager.DefaultCargoSlotCount;
        var width = inventory?.Width ?? InventoryManager.DefaultCargoWidth;

        packet.InventorySlots = (short)slotCount;

        if (packet is not CreateVehicleExtendedPacket extendedPacket)
            return;

        extendedPacket.NumInventorySlots = (short)slotCount;
        extendedPacket.InventorySize = (ushort)slotCount;

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
        var pageCount = inventory?.PageCount ?? InventoryManager.DefaultCargoPageCount;
        var width = inventory?.Width ?? InventoryManager.DefaultCargoWidth;
        var slotCount = inventory?.SlotCount ?? InventoryManager.DefaultCargoSlotCount;

        var packet = new InventoryCargoSendAllPacket
        {
            InventorySize = (byte)Math.Min(byte.MaxValue, pageCount)
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
}
