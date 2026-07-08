namespace AutoCore.Game.Chat;

using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;

public sealed class ChatCommandService
{
    public static ChatCommandService Instance { get; } = new();

    public ChatCommandExecutionResult Execute(Character character, string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new ChatCommandExecutionResult(false, string.Empty);

        switch (parts[0])
        {
            case "/listItems":
            case "/listitems":
                return new ChatCommandExecutionResult(
                    true,
                    InventoryCommandService.Instance.ListItems(parts));

            case "/addItem":
            case "/additem":
                var addItemResult = InventoryCommandService.Instance.AddItem(
                    character == null ? null : new InventoryRuntime(character),
                    parts);

                return new ChatCommandExecutionResult(
                    true,
                    addItemResult.Message,
                    addItemResult.Packets,
                    addItemResult.AddedItem);

            default:
                return new ChatCommandExecutionResult(false, string.Empty);
        }
    }
}
