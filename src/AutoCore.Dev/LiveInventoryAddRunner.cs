namespace AutoCore.Dev;

public static class LiveInventoryAddRunner
{
    public static async Task<int> RunAsync(LiveInventoryAddOptions options)
    {
        try
        {
            using var api = new DevControlApiClient(options.ApiBaseUrl);

            var health = await api.GetHealthAsync();
            var character = SelectCharacter(health, options.Character);
            Console.WriteLine($"AutoCore dev API: {options.ApiBaseUrl}");
            Console.WriteLine($"Character: {character.CharacterName} (connection {character.ConnectionId})");

            var initialInventory = await api.GetInventoryAsync(character.CharacterName);
            if (initialInventory.Items.Length != 0)
                throw new InvalidOperationException($"Fresh session required: server runtime cargo already has {initialInventory.Items.Length} item(s).");

            var process = ClientProcessMemory.Open(options.ProcessName);
            Console.WriteLine($"Client process: {process.ProcessName} PID {process.ProcessId}");

            var first = await api.RunChatCommandAsync(character.CharacterName, $"/addItem {options.ItemCbids[0]}");
            var second = await api.RunChatCommandAsync(character.CharacterName, $"/addItem {options.ItemCbids[1]}");

            if (first.AddedItem == null || second.AddedItem == null)
                throw new InvalidOperationException("The dev API did not return add-item metadata.");

            var inventory = await api.GetInventoryAsync(character.CharacterName);
            VerifyServerInventory(inventory, first.AddedItem, second.AddedItem);

            using var memory = new ClientProcessMemory(process.ProcessId);
            var verification = memory.VerifyCargoItems(first.AddedItem.Coid, second.AddedItem.Coid);

            Console.WriteLine("PASS live inventory add");
            Console.WriteLine($"  Item {first.AddedItem.Cbid}: COID {first.AddedItem.Coid} slot {first.AddedItem.X},{first.AddedItem.Y}");
            Console.WriteLine($"  Item {second.AddedItem.Cbid}: COID {second.AddedItem.Coid} slot {second.AddedItem.X},{second.AddedItem.Y}");
            Console.WriteLine($"  Client hits: 0x{verification.FirstCoidAddress:X}, 0x{verification.SecondCoidAddress:X}");
            Console.WriteLine($"  Cargo block: 0x{verification.CargoBlockAddress:X}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL live inventory add");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static DevCharacterDto SelectCharacter(DevHealthResponse health, string? characterName)
    {
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            var matches = health.ConnectedCharacters
                .Where(c => string.Equals(c.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException($"No connected character named '{characterName}' was found."),
                _ => throw new InvalidOperationException($"Multiple connected characters named '{characterName}' were found.")
            };
        }

        return health.ConnectedCharacters.Length switch
        {
            1 => health.ConnectedCharacters[0],
            0 => throw new InvalidOperationException("No connected characters were reported by the dev API."),
            _ => throw new InvalidOperationException("Multiple connected characters were reported. Use --character.")
        };
    }

    private static void VerifyServerInventory(DevInventoryResponse inventory, DevInventoryItemDto first, DevInventoryItemDto second)
    {
        if (inventory.Items.Length != 2)
            throw new InvalidOperationException($"Expected server cargo to contain exactly 2 items, found {inventory.Items.Length}.");

        VerifyItem(inventory, first.Cbid, first.Coid, 0, 0);
        VerifyItem(inventory, second.Cbid, second.Coid, 1, 0);
    }

    private static void VerifyItem(DevInventoryResponse inventory, int cbid, long coid, int x, int y)
    {
        var item = inventory.Items.SingleOrDefault(i => i.Cbid == cbid && i.Coid == coid);
        if (item == null)
            throw new InvalidOperationException($"Server inventory does not contain CBID {cbid} / COID {coid}.");

        if (item.X != x || item.Y != y)
            throw new InvalidOperationException($"Server inventory item {cbid} expected at {x},{y}, found {item.X},{item.Y}.");
    }
}
