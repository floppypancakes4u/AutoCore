namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets;

public sealed class InventoryCommandResult
{
    public InventoryCommandResult(
        string message,
        IReadOnlyList<BasePacket> packets = null,
        CharacterInventoryItem addedItem = null,
        int acceptedQuantity = 0,
        int remainingQuantity = 0,
        IReadOnlyList<CharacterInventoryItem> addedItems = null,
        IReadOnlyList<CharacterInventoryItem> updatedItems = null)
    {
        Message = message;
        Packets = packets ?? Array.Empty<BasePacket>();
        AddedItem = addedItem;
        AcceptedQuantity = acceptedQuantity;
        RemainingQuantity = remainingQuantity;
        AddedItems = addedItems ?? Array.Empty<CharacterInventoryItem>();
        UpdatedItems = updatedItems ?? Array.Empty<CharacterInventoryItem>();
    }

    public string Message { get; }
    public IReadOnlyList<BasePacket> Packets { get; }
    public CharacterInventoryItem AddedItem { get; }
    public int AcceptedQuantity { get; }
    public int RemainingQuantity { get; }
    public IReadOnlyList<CharacterInventoryItem> AddedItems { get; }
    public IReadOnlyList<CharacterInventoryItem> UpdatedItems { get; }
}
