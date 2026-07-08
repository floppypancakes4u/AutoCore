namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;

public sealed record InventoryCatalogEntry(int Cbid, CloneBaseObjectType Type, string DisplayName);
