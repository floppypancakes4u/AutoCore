namespace AutoCore.Game.Inventory;

using AutoCore.Game.Entities;

public sealed class InventoryRuntime : IInventoryRuntime
{
    private readonly Character _character;

    public InventoryRuntime(Character character)
    {
        _character = character;
    }

    public bool CanAllocateItem => _character?.Map != null;

    public InventoryManager Inventory => _character?.Inventory;

    public long CharacterCoid => _character?.ObjectId.Coid ?? 0;

    public long AllocateItemCoid()
    {
        return _character.Map.LocalCoidCounter++;
    }
}
