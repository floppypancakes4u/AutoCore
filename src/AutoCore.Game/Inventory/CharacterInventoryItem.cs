namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;

public sealed record CharacterInventoryItem(
    int Cbid,
    CloneBaseObjectType Type,
    string DisplayName,
    long Coid,
    byte InventoryPositionX,
    byte InventoryPositionY,
    int Quantity);
