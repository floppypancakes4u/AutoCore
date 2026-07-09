namespace AutoCore.Game.Inventory;

using System.Text.Json;
using System.Text.Json.Serialization;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;

public static class InventoryItemExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static InventoryItemExportDocument ExportFromCloneBases(
        IReadOnlyDictionary<int, CloneBase> cloneBases,
        string sourcePath = "")
    {
        var items = cloneBases
            .Values
            .Where(cb => InventoryItemTypePolicy.IsInventoryCapable(cb.Type))
            .Select(ExportRecord)
            .OrderBy(item => item.ClassName)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.Cbid)
            .ToList();

        return new InventoryItemExportDocument(
            DateTime.UtcNow.ToString("o"),
            sourcePath,
            items.Count,
            items);
    }

    public static InventoryItemExportRecord ExportRecord(CloneBase cloneBase)
    {
        var specific = cloneBase.CloneBaseSpecific;
        var objectSpecific = cloneBase as CloneBaseObject;
        var (maxStackSize, stackable) = NormalizeStackSize(objectSpecific?.SimpleObjectSpecific.StackSize ?? 0);

        var displayName = string.IsNullOrWhiteSpace(specific.ShortDesc)
            ? FormatFallbackDisplayName(specific.UniqueName, cloneBase.CloneBaseSpecific.CloneBaseId)
            : specific.ShortDesc.Trim();

        return new InventoryItemExportRecord(
            specific.CloneBaseId,
            cloneBase.Type.ToString(),
            (int)cloneBase.Type,
            string.IsNullOrWhiteSpace(specific.UniqueName) ? $"cbid_{specific.CloneBaseId}" : specific.UniqueName.Trim(),
            displayName,
            specific.LongDesc?.Trim() ?? string.Empty,
            objectSpecific?.SimpleObjectSpecific.StackSize ?? 0,
            maxStackSize,
            stackable,
            objectSpecific?.SimpleObjectSpecific.InvSizeX ?? 0,
            objectSpecific?.SimpleObjectSpecific.InvSizeY ?? 0,
            objectSpecific?.SimpleObjectSpecific.SubType ?? 0,
            specific.CommodityGroupType);
    }

    public static (int MaxStackSize, bool Stackable) NormalizeStackSize(int rawStackSize)
    {
        if (rawStackSize <= 0)
            return (1, false);

        return (rawStackSize, rawStackSize > 1);
    }

    public static string Serialize(InventoryItemExportDocument document)
    {
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static string FormatFallbackDisplayName(string uniqueName, int cbid)
    {
        if (!string.IsNullOrWhiteSpace(uniqueName))
            return uniqueName.Trim();

        return $"CBID {cbid}";
    }
}
