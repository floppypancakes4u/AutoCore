namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;

public sealed class InventoryItemCreator : IInventoryItemCreator
{
    public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
    {
        var item = ClonedObjectBase.AllocateNewObjectFromCBID(entry.Cbid);
        if (item == null)
            return InventoryItemCreateResult.Unsupported($"item type {entry.Type} is not supported by the server create factory yet.");

        var cloneBase = AssetManager.Instance.GetCloneBase(entry.Cbid);
        if (cloneBase == null)
            return InventoryItemCreateResult.Unsupported("clonebase is not loaded.");

        var packet = CreatePacketFor(entry.Type);

        item.SetCoid(coid, true);
        item.LoadCloneBase(entry.Cbid);
        item.WriteToPacket(packet);

        packet.IsInInventory = true;
        packet.InventoryPositionX = x;
        packet.InventoryPositionY = y;
        packet.Quantity = 1;
        packet.IsBound = false;
        packet.IsIdentified = true;

        var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? cloneBase.CloneBaseSpecific.UniqueName
            : entry.DisplayName;

        return InventoryItemCreateResult.Success(packet, displayName);
    }

    private static CreateSimpleObjectPacket CreatePacketFor(CloneBaseObjectType type)
    {
        // Inventory grants need the created object to be visible to the generic
        // object resolver used by Client_RecvInventoryAddItem.
        return new CreateSimpleObjectPacket();
    }
}
