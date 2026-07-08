namespace AutoCore.Game.Inventory;

using AutoCore.Game.Packets;

public sealed class InventoryCommandResult
{
    public InventoryCommandResult(string message, IReadOnlyList<BasePacket> packets = null, CharacterInventoryItem addedItem = null)
    {
        Message = message;
        Packets = packets ?? Array.Empty<BasePacket>();
        AddedItem = addedItem;
    }

    public string Message { get; }
    public IReadOnlyList<BasePacket> Packets { get; }
    public CharacterInventoryItem AddedItem { get; }
}
