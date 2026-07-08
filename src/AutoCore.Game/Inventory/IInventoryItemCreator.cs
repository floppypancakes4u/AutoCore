namespace AutoCore.Game.Inventory;

public interface IInventoryItemCreator
{
    InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y);
}
