namespace AutoCore.Dev;

public static class LiveInventoryGrabRunner
{
    public static async Task<int> RunAsync(LiveInventoryGrabOptions options)
    {
        try
        {
            using var api = new DevControlApiClient(options.ApiBaseUrl);

            var health = await api.GetHealthAsync();
            var character = SelectCharacter(health, options.Character);
            Console.WriteLine($"AutoCore dev API: {options.ApiBaseUrl}");
            Console.WriteLine($"Character: {character.CharacterName} (connection {character.ConnectionId})");

            var process = ClientProcessMemory.Open(options.ProcessName);
            Console.WriteLine($"Client process: {process.ProcessName} PID {process.ProcessId}");

            await api.ClearInventoryGrabLogAsync();

            var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            var before = await api.GetInventoryAsync(character.CharacterName);
            var deadline = DateTimeOffset.UtcNow + timeout;
            DevInventoryGrabLogResponse log = new();

            Task<ClientInventoryGrabBreakpointHit>? watcherTask = null;
            if (options.UseBreakpoint)
            {
                var watcher = new ClientInventoryGrabBreakpointWatcher();
                var ready = new TaskCompletionSource<ClientInventoryGrabBreakpointReady>(TaskCreationOptions.RunContinuationsAsynchronously);
                watcherTask = Task.Run(() => watcher.WaitForHit(
                    process.ProcessId,
                    timeout,
                    ready,
                    message => Console.WriteLine($"Watcher: {message}")));

                var readyOrFailure = await Task.WhenAny(ready.Task, watcherTask);
                if (readyOrFailure == watcherTask)
                    _ = await watcherTask;

                var breakpointReady = await ready.Task;
                Console.WriteLine($"Breakpoint armed for Client_RecvInventoryGrab at 0x{breakpointReady.BreakpointAddress:X}.");
            }
            else
            {
                Console.WriteLine("Non-invasive watcher active. Use --breakpoint only when you need client handler bytes.");
            }

            Console.WriteLine("Drop an item into cargo now.");

            while (DateTimeOffset.UtcNow < deadline)
            {
                log = await api.GetInventoryGrabLogAsync();
                if (log.Entries.Any(e => e.Direction == "incoming")
                    && log.Entries.Any(e => e.Direction == "outgoing"))
                    break;

                await Task.Delay(250);
            }

            var after = await api.GetInventoryAsync(character.CharacterName);
            log = await api.GetInventoryGrabLogAsync();
            PrintServerLog(log);

            var outgoing = log.Entries.LastOrDefault(e => e.Direction == "outgoing")
                ?? throw new InvalidOperationException("Server did not capture an outgoing InventoryGrab_Response.");

            var responseBytes = Convert.FromHexString(outgoing.Hex);
            var response = DecodeResponse(responseBytes);
            Console.WriteLine($"Decoded server response: opcode=0x{response.Opcode:X} coid={response.ItemCoid} itemGlobal={response.ItemGlobal} invType={response.InventoryType} qty={response.Quantity} addToExisting={response.AddToExistingItem} slot={response.InventoryPositionX},{response.InventoryPositionY} success={response.WasSuccessful}");

            if (watcherTask != null)
            {
                var hit = await watcherTask;
                Console.WriteLine("Client InventoryGrab_Response breakpoint hit");
                Console.WriteLine($"  Breakpoint: 0x{hit.BreakpointAddress:X}");
                Console.WriteLine($"  Client packet: 0x{hit.PacketAddress:X} {Convert.ToHexString(hit.PacketBytes)}");
                Console.WriteLine($"  Response: opcode=0x{hit.Response.Opcode:X} coid={hit.Response.ItemCoid} itemGlobal={hit.Response.ItemGlobal} invType={hit.Response.InventoryType} qty={hit.Response.Quantity} addToExisting={hit.Response.AddToExistingItem} slot={hit.Response.InventoryPositionX},{hit.Response.InventoryPositionY} success={hit.Response.WasSuccessful}");
                foreach (var debugString in hit.DebugStrings)
                    Console.WriteLine($"  Client debug: {debugString}");
            }

            if (!response.WasSuccessful)
                throw new InvalidOperationException("Client received InventoryGrab_Response with WasSuccessful=false.");

            var existingBeforeGrab = before.Items
                .LastOrDefault(i => i.Coid == response.ItemCoid);

            if (existingBeforeGrab != null)
            {
                Console.WriteLine("PASS live inventory grab");
                Console.WriteLine($"  Existing inventory source: CBID {existingBeforeGrab.Cbid} ({existingBeforeGrab.Type}) {existingBeforeGrab.DisplayName}");
                Console.WriteLine($"  Source slot: {existingBeforeGrab.X},{existingBeforeGrab.Y}");
                Console.WriteLine($"  Response branch: addToExisting={response.AddToExistingItem}");
                Console.WriteLine("  Note: existing inventory grabs move/copy client-side into the drag flow; cargo count is not expected to increase.");
                return 0;
            }

            var grabbedMatches = after.Items
                .Where(i => i.Coid == response.ItemCoid)
                .ToArray();
            var grabbed = grabbedMatches
                .SingleOrDefault(i => i.X == response.InventoryPositionX && i.Y == response.InventoryPositionY)
                ?? grabbedMatches.LastOrDefault();
            if (grabbed == null)
                throw new InvalidOperationException($"Server inventory does not contain grabbed COID {response.ItemCoid}.");

            if (after.Items.Length <= before.Items.Length)
                throw new InvalidOperationException($"Server inventory count did not increase. Before={before.Items.Length}, After={after.Items.Length}.");

            using var memory = new ClientProcessMemory(process.ProcessId);
            var verification = memory.VerifyCargoItem(
                response.ItemCoid,
                (byte)response.InventoryPositionX,
                (byte)response.InventoryPositionY);

            Console.WriteLine("PASS live inventory grab");
            Console.WriteLine($"  Server inventory: CBID {grabbed.Cbid} ({grabbed.Type}) {grabbed.DisplayName}");
            if (grabbedMatches.Length > 1)
                Console.WriteLine($"  Server inventory note: found {grabbedMatches.Length} entries with COID {response.ItemCoid}; used slot {grabbed.X},{grabbed.Y} for verification.");
            Console.WriteLine($"  Client cargo hit: 0x{verification.CargoBlockAddress:X}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL live inventory grab");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void PrintServerLog(DevInventoryGrabLogResponse log)
    {
        foreach (var entry in log.Entries)
            Console.WriteLine($"Server {entry.Direction}: len={entry.Length} {entry.Hex}");
    }

    private static InventoryGrabResponseSnapshot DecodeResponse(byte[] bytes)
    {
        return new InventoryGrabResponseSnapshot
        {
            Opcode = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0,
            ItemCoid = bytes.Length >= 16 ? BitConverter.ToInt64(bytes, 8) : -1,
            ItemGlobal = bytes.Length > 0x10 && bytes[0x10] != 0,
            InventoryType = bytes.Length > 0x18 ? bytes[0x18] : (byte)0,
            Quantity = bytes.Length >= 0x20 ? BitConverter.ToInt32(bytes, 0x1c) : 0,
            AddToExistingItem = bytes.Length > 0x20 && bytes[0x20] != 0,
            InventoryPositionX = bytes.Length >= 0x2c ? BitConverter.ToInt32(bytes, 0x28) : 0,
            InventoryPositionY = bytes.Length >= 0x30 ? BitConverter.ToInt32(bytes, 0x2c) : 0,
            WasSuccessful = bytes.Length > 0x38 && bytes[0x38] != 0
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
