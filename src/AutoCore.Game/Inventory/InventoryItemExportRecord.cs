namespace AutoCore.Game.Inventory;

public sealed record InventoryItemExportRecord(
    int Cbid,
    string ClassName,
    int TypeId,
    string UniqueName,
    string DisplayName,
    string LongDescription,
    int RawStackSize,
    int MaxStackSize,
    bool Stackable,
    byte InvSizeX,
    byte InvSizeY,
    short SubType,
    int CommodityGroupType);

public sealed record InventoryItemExportDocument(
    string ExportedAtUtc,
    string SourcePath,
    int ItemCount,
    IReadOnlyList<InventoryItemExportRecord> Items);
