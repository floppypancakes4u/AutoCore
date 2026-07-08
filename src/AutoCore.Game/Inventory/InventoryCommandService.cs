namespace AutoCore.Game.Inventory;

public sealed class InventoryCommandService
{
    public static InventoryCommandService Instance { get; } = new(
        InventoryCatalog.FromAssetManager(),
        new InventoryItemCreator());

    private readonly InventoryCatalog _catalog;
    private readonly IInventoryItemCreator _itemCreator;

    public InventoryCommandService(InventoryCatalog catalog, IInventoryItemCreator itemCreator)
    {
        _catalog = catalog;
        _itemCreator = itemCreator;
    }

    public string ListItems(string[] parts)
    {
        var pageText = parts.Length >= 2 ? parts[1] : null;
        return _catalog.FormatPage(pageText);
    }

    public InventoryCommandResult AddItem(IInventoryRuntime runtime, string[] parts)
    {
        if (parts.Length < 2)
            return new InventoryCommandResult("Invalid addItem command. Usage: /addItem <cbid>.");

        if (!int.TryParse(parts[1], out var cbid))
            return new InventoryCommandResult($"Invalid item id '{parts[1]}'. Item id must be a CBID number.");

        var entry = _catalog.FindAny(cbid);
        if (entry == null)
            return new InventoryCommandResult($"Item CBID {cbid} was not found.");

        if (!InventoryItemTypePolicy.IsInventoryCapable(entry.Type))
            return new InventoryCommandResult($"CBID {cbid} ({entry.Type}) is not an inventory item.");

        if (runtime == null || !runtime.CanAllocateItem)
            return new InventoryCommandResult("Cannot add item: character or map is not available.");

        var coid = runtime.AllocateItemCoid();
        return runtime.Inventory.AddItem(entry, _itemCreator, coid, runtime.CharacterCoid);
    }
}
