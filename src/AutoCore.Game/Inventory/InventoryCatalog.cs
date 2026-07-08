namespace AutoCore.Game.Inventory;

using System.Text;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Managers;

public sealed class InventoryCatalog
{
    public const int PageSize = 10;

    private readonly Func<IEnumerable<InventoryCatalogEntry>> _entrySource;

    public InventoryCatalog(Func<IEnumerable<InventoryCatalogEntry>> entrySource)
    {
        _entrySource = entrySource;
    }

    public static InventoryCatalog FromAssetManager()
    {
        return new InventoryCatalog(() => FromCloneBases(AssetManager.Instance.GetAllCloneBases()));
    }

    public static IEnumerable<InventoryCatalogEntry> FromCloneBases(IEnumerable<KeyValuePair<int, CloneBase>> cloneBases)
    {
        foreach (var (cbid, cloneBase) in cloneBases)
        {
            var name = cloneBase.CloneBaseSpecific.UniqueName;
            if (string.IsNullOrWhiteSpace(name))
                name = $"CBID {cbid}";

            yield return new InventoryCatalogEntry(cbid, cloneBase.Type, name);
        }
    }

    public IReadOnlyList<InventoryCatalogEntry> GetAllItems()
    {
        return _entrySource()
            .OrderBy(e => e.Type.ToString())
            .ThenBy(e => e.DisplayName)
            .ThenBy(e => e.Cbid)
            .ToList();
    }

    public IReadOnlyList<InventoryCatalogEntry> GetInventoryItems()
    {
        return GetAllItems()
            .Where(e => InventoryItemTypePolicy.IsInventoryCapable(e.Type))
            .ToList();
    }

    public InventoryCatalogEntry FindAny(int cbid)
    {
        return GetAllItems().FirstOrDefault(e => e.Cbid == cbid);
    }

    public string FormatPage(string pageText)
    {
        if (!TryParsePage(pageText, out var page, out var error))
            return error;

        var items = GetInventoryItems();
        if (items.Count == 0)
            return "No inventory-capable items are loaded.";

        var totalPages = (int)Math.Ceiling(items.Count / (double)PageSize);
        if (page < 1 || page > totalPages)
            return $"Invalid page {page}. Valid pages: 1-{totalPages}.";

        var pageItems = items
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        var builder = new StringBuilder();
        builder.Append($"Items page {page}/{totalPages}");

        foreach (var item in pageItems)
            builder.Append($"\n{item.Cbid} | {item.Type} | {item.DisplayName}");

        var nextPage = Math.Min(page + 1, totalPages);
        builder.Append($"\nUse /listItems {nextPage} or /addItem {pageItems[0].Cbid}");

        return builder.ToString();
    }

    private static bool TryParsePage(string pageText, out int page, out string error)
    {
        page = 1;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(pageText))
            return true;

        if (!int.TryParse(pageText, out page))
        {
            error = $"Invalid listItems page '{pageText}'. Page must be a number.";
            return false;
        }

        return true;
    }
}
