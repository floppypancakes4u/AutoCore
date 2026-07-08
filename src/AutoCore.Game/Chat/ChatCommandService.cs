using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;

namespace AutoCore.Game.Chat;

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

            case "/setcargo":
            case "/setCargo":
                return SetCargo(character, parts);

            default:
                return new ChatCommandExecutionResult(false, string.Empty);
        }
    }

    private static ChatCommandExecutionResult SetCargo(Character character, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var pageCount) || pageCount < 1)
            return new ChatCommandExecutionResult(true, "Usage: /setcargo <pages> [width]. Example: /setcargo 13 24");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var width = character.Inventory.Width;
        if (parts.Length >= 3)
        {
            if (!int.TryParse(parts[2], out width) || width < 1)
                return new ChatCommandExecutionResult(true, "Width must be a positive integer.");
        }

        character.Inventory.SetCapacity(width, pageCount);
        InventoryPersistence.Instance.SaveCharacterCargoCapacity(
            character.ObjectId.Coid,
            character.Inventory.Width,
            character.Inventory.PageCount);

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            InventoryPacketFactory.CreateCargoSendAll(character.Inventory)
        };

        return new ChatCommandExecutionResult(
            true,
            $"Cargo capacity set to {character.Inventory.Width}x{character.Inventory.PageCount} ({character.Inventory.SlotCount} slots).",
            packets);
    }
}
