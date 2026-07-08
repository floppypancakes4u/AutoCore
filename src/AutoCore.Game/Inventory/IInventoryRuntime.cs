namespace AutoCore.Game.Inventory;

public interface IInventoryRuntime
{
    bool CanAllocateItem { get; }
    InventoryManager Inventory { get; }
    long CharacterCoid { get; }
    long AllocateItemCoid();
}
