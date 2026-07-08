namespace AutoCore.Game.Chat;

using AutoCore.Game.Inventory;
using AutoCore.Game.Packets;

public sealed class ChatCommandExecutionResult
{
    public ChatCommandExecutionResult(
        bool handled,
        string message,
        IReadOnlyList<BasePacket> packets = null,
        CharacterInventoryItem addedItem = null)
    {
        Handled = handled;
        Message = message;
        Packets = packets ?? Array.Empty<BasePacket>();
        AddedItem = addedItem;
    }

    public bool Handled { get; }
    public string Message { get; }
    public IReadOnlyList<BasePacket> Packets { get; }
    public CharacterInventoryItem AddedItem { get; }
}
