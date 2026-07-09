namespace AutoCore.Dev;

public static class LiveInventoryDropRunner
{
    public static async Task<int> RunAsync(LiveInventoryDropOptions options)
    {
        try
        {
            using var api = new DevControlApiClient(options.ApiBaseUrl);

            var health = await api.GetHealthAsync();
            var character = SelectCharacter(health, options.Character);
            Console.WriteLine($"AutoCore dev API: {options.ApiBaseUrl}");
            Console.WriteLine($"Character: {character.CharacterName} (connection {character.ConnectionId})");
            Console.WriteLine($"Mode: {options.Mode}");

            var process = ClientProcessMemory.Open(options.ProcessName);
            Console.WriteLine($"Client process: {process.ProcessName} PID {process.ProcessId}");
            Console.WriteLine("Non-invasive drop watcher active.");

            await api.ClearInventoryDropLogAsync();

            var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            var deadline = DateTimeOffset.UtcNow + timeout;
            DevInventoryGrabLogResponse log = new();

            if (options.Mode == LiveInventoryDropMode.Toss)
            {
                Console.WriteLine("Drag a cargo item onto the world to toss it now.");
                return await RunTossModeAsync(api, character, deadline);
            }

            Console.WriteLine("Drag an inventory item to a different empty cargo slot now.");
            return await RunCargoMoveModeAsync(api, character, deadline);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL live inventory drop");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task<int> RunTossModeAsync(
        DevControlApiClient api,
        DevCharacterDto character,
        DateTimeOffset deadline)
    {
        DevInventoryGrabLogResponse log = new();

        while (DateTimeOffset.UtcNow < deadline)
        {
            log = await api.GetInventoryDropLogAsync();
            if (log.Entries.Any(e => e.Direction == "incoming"))
                break;

            await Task.Delay(250);
        }

        log = await api.GetInventoryDropLogAsync();
        PrintServerLog(log);

        var incoming = log.Entries.LastOrDefault(e => e.Direction == "incoming")
            ?? throw new InvalidOperationException("Server did not capture an incoming toss-related packet.");

        var requestBytes = Convert.FromHexString(incoming.Hex);
        var opcode = requestBytes.Length >= 4 ? BitConverter.ToUInt32(requestBytes, 0) : 0;
        Console.WriteLine($"Captured incoming opcode: 0x{opcode:X8} ({DescribeTossOpcode(opcode)})");
        PrintDecodedRequest(requestBytes);

        foreach (var outgoing in log.Entries.Where(e => e.Direction == "outgoing"))
        {
            var outgoingBytes = Convert.FromHexString(outgoing.Hex);
            var outgoingOpcode = outgoingBytes.Length >= 4 ? BitConverter.ToUInt32(outgoingBytes, 0) : 0;
            Console.WriteLine($"Captured outgoing opcode: 0x{outgoingOpcode:X8} ({DescribeTossOpcode(outgoingOpcode)}) len={outgoing.Length}");
        }

        Console.WriteLine("PASS live inventory toss capture");
        Console.WriteLine("  Review the server log above to determine packet layout before implementing world toss.");
        return 0;
    }

    private static async Task<int> RunCargoMoveModeAsync(
        DevControlApiClient api,
        DevCharacterDto character,
        DateTimeOffset deadline)
    {
        var before = await api.GetInventoryAsync(character.CharacterName);
        DevInventoryGrabLogResponse log = new();

        while (DateTimeOffset.UtcNow < deadline)
        {
            log = await api.GetInventoryDropLogAsync();
            if (log.Entries.Any(e => e.Direction == "incoming")
                && log.Entries.Any(e => e.Direction == "outgoing"))
                break;

            await Task.Delay(250);
        }

        log = await api.GetInventoryDropLogAsync();
        PrintServerLog(log);

        var incoming = log.Entries.LastOrDefault(e => e.Direction == "incoming")
            ?? throw new InvalidOperationException("Server did not capture an incoming InventoryDrop.");
        var outgoing = log.Entries.LastOrDefault(e => e.Direction == "outgoing")
            ?? throw new InvalidOperationException("Server did not capture an outgoing InventoryDrop_Response.");

        var request = DecodeRequest(Convert.FromHexString(incoming.Hex));
        var response = DecodeResponse(Convert.FromHexString(outgoing.Hex));
        Console.WriteLine($"Decoded server request: opcode=0x{request.Opcode:X} coid={request.ItemCoid} itemGlobal={request.ItemGlobal} invType={request.InventoryType} slot={request.InventoryPositionX},{request.InventoryPositionY}");
        Console.WriteLine($"Decoded server response: opcode=0x{response.Opcode:X} coid={response.ItemCoid} itemGlobal={response.ItemGlobal} invType={response.InventoryType} slot={response.InventoryPositionX},{response.InventoryPositionY} success={response.WasSuccessful} swapped={response.HasSwappedOrConcatenatedItem}");

        if (!response.WasSuccessful)
            throw new InvalidOperationException("Client received InventoryDrop_Response with WasSuccessful=false.");

        var beforeItem = before.Items.LastOrDefault(i => i.Coid == response.ItemCoid)
            ?? throw new InvalidOperationException($"Before inventory did not contain dropped COID {response.ItemCoid}.");

        var after = await api.GetInventoryAsync(character.CharacterName);
        var afterItem = after.Items.LastOrDefault(i => i.Coid == response.ItemCoid)
            ?? throw new InvalidOperationException($"After inventory does not contain dropped COID {response.ItemCoid}.");

        if (afterItem.X != response.InventoryPositionX || afterItem.Y != response.InventoryPositionY)
            throw new InvalidOperationException($"Server inventory slot mismatch. Expected {response.InventoryPositionX},{response.InventoryPositionY}; got {afterItem.X},{afterItem.Y}.");

        Console.WriteLine("PASS live inventory drop");
        Console.WriteLine($"  Item: CBID {afterItem.Cbid} ({afterItem.Type}) {afterItem.DisplayName}");
        Console.WriteLine($"  Moved: {beforeItem.X},{beforeItem.Y} -> {afterItem.X},{afterItem.Y}");

        return 0;
    }

    private static void PrintServerLog(DevInventoryGrabLogResponse log)
    {
        foreach (var entry in log.Entries)
            Console.WriteLine($"Server {entry.Direction}: len={entry.Length} {entry.Hex}");
    }

    private static void PrintDecodedRequest(byte[] bytes)
    {
        var request = DecodeRequest(bytes);
        Console.WriteLine($"Decoded request: opcode=0x{request.Opcode:X} coid={request.ItemCoid} itemGlobal={request.ItemGlobal} invType={request.InventoryType} slot={request.InventoryPositionX},{request.InventoryPositionY}");

        if (bytes.Length > 0x1b)
            Console.WriteLine($"Tail bytes: {Convert.ToHexString(bytes.AsSpan(0x1b))}");
    }

    private static string DescribeTossOpcode(uint opcode) => opcode switch
    {
        0x2036 => "InventoryDrop",
        0x2037 => "InventoryDropResponse",
        0x203A => "InventoryDropMM",
        0x203B => "InventoryDropMMResponse",
        0x2049 => "InventoryDestroyItem",
        0x2057 => "ItemDrop",
        0x2058 => "ItemDropResponse",
        0x2020 => "DestroyObject",
        0x2021 => "CreateSimpleObject",
        _ => "unknown"
    };

    private static InventoryDropRequestSnapshot DecodeRequest(byte[] bytes)
    {
        return new InventoryDropRequestSnapshot
        {
            Opcode = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0,
            ItemCoid = bytes.Length >= 16 ? BitConverter.ToInt64(bytes, 8) : -1,
            ItemGlobal = bytes.Length > 0x10 && bytes[0x10] != 0,
            InventoryPositionX = bytes.Length > 0x18 ? bytes[0x18] : byte.MaxValue,
            InventoryPositionY = bytes.Length > 0x19 ? bytes[0x19] : byte.MaxValue,
            InventoryType = bytes.Length > 0x1a ? bytes[0x1a] : (byte)0
        };
    }

    private static InventoryDropResponseSnapshot DecodeResponse(byte[] bytes)
    {
        return new InventoryDropResponseSnapshot
        {
            Opcode = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0,
            ItemCoid = bytes.Length >= 16 ? BitConverter.ToInt64(bytes, 8) : -1,
            ItemGlobal = bytes.Length > 0x10 && bytes[0x10] != 0,
            InventoryPositionX = bytes.Length > 0x18 ? bytes[0x18] : byte.MaxValue,
            InventoryPositionY = bytes.Length > 0x19 ? bytes[0x19] : byte.MaxValue,
            InventoryType = bytes.Length > 0x1a ? bytes[0x1a] : (byte)0,
            WasSuccessful = bytes.Length > 0x22 && bytes[0x22] != 0,
            HasSwappedOrConcatenatedItem = bytes.Length > 0x23 && bytes[0x23] != 0
        };
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
}

public sealed class InventoryDropRequestSnapshot
{
    public uint Opcode { get; set; }
    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; }
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }
    public byte InventoryType { get; set; }
}

public sealed class InventoryDropResponseSnapshot
{
    public uint Opcode { get; set; }
    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; }
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }
    public byte InventoryType { get; set; }
    public bool WasSuccessful { get; set; }
    public bool HasSwappedOrConcatenatedItem { get; set; }
}
