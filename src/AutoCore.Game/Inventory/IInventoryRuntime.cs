namespace AutoCore.Game.Inventory;

public interface IInventoryRuntime
{
    bool CanAllocateItem { get; }
    InventoryManager Inventory { get; }
    long AllocateItemCoid();
}
