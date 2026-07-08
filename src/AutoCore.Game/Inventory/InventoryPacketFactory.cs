namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets.Sector;

public static class InventoryPacketFactory
{
    public static void ConfigureVehicleCargo(CreateVehiclePacket packet, InventoryManager inventory = null)
    {
        packet.InventorySlots = (short)InventoryManager.CargoSlotCount;

        if (packet is not CreateVehicleExtendedPacket extendedPacket)
            return;

        extendedPacket.NumInventorySlots = (short)InventoryManager.CargoSlotCount;
        extendedPacket.InventorySize = InventoryManager.CargoSlotCount;

        if (inventory == null)
            return;

        foreach (var item in inventory.Items)
        {
            var slot = item.InventoryPositionY * InventoryManager.CargoWidth + item.InventoryPositionX;
            if (slot < 0 || slot >= extendedPacket.InventoryCoids.Length)
                continue;

            extendedPacket.InventoryCoids[slot] = item.Coid;
        }
    }

    public static InventoryCargoSendAllPacket CreateCargoSendAll(InventoryManager inventory)
    {
        var packet = new InventoryCargoSendAllPacket
        {
            InventorySize = InventoryManager.CargoPageCount
        };

        foreach (var item in inventory.Items)
        {
            var slot = item.InventoryPositionY * InventoryManager.CargoWidth + item.InventoryPositionX;
            if (slot < 0 || slot >= InventoryManager.CargoSlotCount)
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
