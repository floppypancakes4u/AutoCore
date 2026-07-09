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
            return new InventoryCommandResult("Invalid addItem command. Usage: /addItem <cbid|name> [quantity].");

        var quantity = 1;
        if (parts.Length >= 3)
        {
            if (!int.TryParse(parts[2], out quantity))
                return new InventoryCommandResult($"Invalid quantity '{parts[2]}'. Quantity must be a positive number.");

            if (quantity < 1)
                return new InventoryCommandResult("Quantity must be at least 1.");
        }

        var entryResult = ResolveEntry(parts[1]);
        if (!entryResult.Success)
            return new InventoryCommandResult(entryResult.Error);

        var entry = entryResult.Entry;
        if (!InventoryItemTypePolicy.IsInventoryCapable(entry.Type))
            return new InventoryCommandResult($"CBID {entry.Cbid} ({entry.Type}) is not an inventory item.");

        if (runtime == null || !runtime.CanAllocateItem)
            return new InventoryCommandResult("Cannot add item: character or map is not available.");

        var coid = runtime.AllocateItemCoid();
        return runtime.Inventory.AddItem(entry, _itemCreator, coid, runtime.CharacterCoid, quantity);
    }

    private (bool Success, InventoryCatalogEntry Entry, string Error) ResolveEntry(string identifier)
    {
        if (int.TryParse(identifier, out var cbid))
        {
            var entry = _catalog.FindAny(cbid);
            return entry == null
                ? (false, null, $"Item CBID {cbid} was not found.")
                : (true, entry, string.Empty);
        }

        var matches = _catalog.FindAllByName(identifier);
        return matches.Count switch
        {
            0 => (false, null, $"Item name '{identifier}' was not found."),
            1 => (true, matches[0], string.Empty),
            _ => (false, null, $"Item name '{identifier}' is ambiguous ({matches.Count} matches). Use the CBID instead."),
        };
    }
}
