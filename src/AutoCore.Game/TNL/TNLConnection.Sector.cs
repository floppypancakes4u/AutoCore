namespace AutoCore.Game.TNL;

using System.Text.Json;
using System.Linq;
using AutoCore.Database.Char;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

public partial class TNLConnection
{
    private void HandleTransferFromGlobalPacket(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        // TODO: validate security key with info received from communicator or DB value or something...
        using var context = new CharContext();

        CurrentCharacter = ObjectManager.Instance.GetOrLoadCharacter(packet.CharacterCoid, context);
        if (CurrentCharacter == null)
        {
            Disconnect("Invalid character");

            return;
        }

        if (!LoginManager.Instance.LoginToSector(this, CurrentCharacter.AccountId))
        {
            Disconnect("Invalid Username or password!");

            return;
        }

        var mapInfoPacket = new MapInfoPacket();

        var map = MapManager.Instance.GetMap(CurrentCharacter.LastTownId);

        CurrentCharacter.SetOwningConnection(this);
        CurrentCharacter.GMLevel = Account.Level;
        CurrentCharacter.SetMap(map);
        CurrentCharacter.CurrentVehicle.SetMap(map);

        map.Fill(mapInfoPacket);

        SendGamePacket(mapInfoPacket, skipOpcode: true);
    }

    private void HandleTransferFromGlobalStage2Packet(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        var character = ObjectManager.Instance.GetCharacter(packet.CharacterCoid);
        if (character == null)
        {
            Disconnect("Invalid character");

            return;
        }

        SendGamePacket(new TransferFromGlobalStage3Packet
        {
            SecurityKey = packet.SecurityKey,
            CharacterCoid = packet.CharacterCoid,
            PositionX = character.Position.X,
            PositionY = character.Position.Y,
            PositionZ = character.Position.Z
        });
    }

    private void HandleTransferFromGlobalStage3Packet(BinaryReader reader)
    {
        var packet = new TransferFromGlobalStage3Packet();
        packet.Read(reader);

        var character = ObjectManager.Instance.GetCharacter(packet.CharacterCoid);
        if (character == null)
        {
            Disconnect("Invalid character");

            return;
        }

        if (!Ghosting)
            ActivateGhosting();

        character.CreateGhost();
        character.CurrentVehicle.CreateGhost();

        SetScopeObject(character.Ghost);

        ObjectLocalScopeAlways(character.Ghost);
        ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);

        // Prime character stats cache before building packets
        Managers.CharacterStatManager.Instance.GetOrLoad(character.ObjectId.Coid);

        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
    }

    private void HandleCreatureMovedPacket(BinaryReader reader)
    {
        var packet = new CreatureMovedPacket();
        packet.Read(reader);

        CurrentCharacter.HandleMovement(packet);
    }

    private void HandleVehicleMovedPacket(BinaryReader reader)
    {
        var packet = new VehicleMovedPacket();
        packet.Read(reader);

        CurrentCharacter.CurrentVehicle.HandleMovement(packet);
    }

    private void HandleUpdateFirstTimeFlagsRequest(BinaryReader reader)
    {
        // #region agent log
        try { var logData = new { location = "TNLConnection.Sector.cs:123", message = "HandleUpdateFirstTimeFlagsRequest entry", data = new { accountId = Account?.Id, hasAccount = Account != null }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "A" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
        // #endregion

        var packet = new UpdateFirstTimeFlagsRequestPacket();
        packet.Read(reader);

        // #region agent log
        try { var logData = new { location = "TNLConnection.Sector.cs:129", message = "Packet read complete", data = new { FirstFlags1 = packet.FirstFlags1, FirstFlags2 = packet.FirstFlags2, FirstFlags3 = packet.FirstFlags3, FirstFlags4 = packet.FirstFlags4 }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "B" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
        // #endregion

        if (Account == null)
        {
            // #region agent log
            try { var logData = new { location = "TNLConnection.Sector.cs:135", message = "Account is null, cannot save", data = new { }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "D" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
            // #endregion
            Logger.WriteLog(LogType.Error, "HandleUpdateFirstTimeFlagsRequest: Account is null");
            return;
        }

        // #region agent log
        try { var logData = new { location = "TNLConnection.Sector.cs:142", message = "Before DB update", data = new { accountId = Account.Id, currentFlags1 = Account.FirstFlags1, currentFlags2 = Account.FirstFlags2, currentFlags3 = Account.FirstFlags3, currentFlags4 = Account.FirstFlags4 }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "C" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
        // #endregion

        using var context = new CharContext();
        var account = context.Accounts.FirstOrDefault(a => a.Id == Account.Id);
        
        if (account == null)
        {
            // #region agent log
            try { var logData = new { location = "TNLConnection.Sector.cs:150", message = "Account not found in DB", data = new { accountId = Account.Id }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "E" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
            // #endregion
            Logger.WriteLog(LogType.Error, $"HandleUpdateFirstTimeFlagsRequest: Account {Account.Id} not found in database");
            return;
        }

        account.FirstFlags1 = packet.FirstFlags1;
        account.FirstFlags2 = packet.FirstFlags2;
        account.FirstFlags3 = packet.FirstFlags3;
        account.FirstFlags4 = packet.FirstFlags4;

        // #region agent log
        try { var logData = new { location = "TNLConnection.Sector.cs:161", message = "Before SaveChanges", data = new { accountId = account.Id, newFlags1 = account.FirstFlags1, newFlags2 = account.FirstFlags2, newFlags3 = account.FirstFlags3, newFlags4 = account.FirstFlags4 }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "C" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
        // #endregion

        try
        {
            context.SaveChanges();
            
            // #region agent log
            try { var logData = new { location = "TNLConnection.Sector.cs:168", message = "SaveChanges succeeded", data = new { accountId = account.Id }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "C" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
            // #endregion

            // Update the in-memory Account object
            Account.FirstFlags1 = account.FirstFlags1;
            Account.FirstFlags2 = account.FirstFlags2;
            Account.FirstFlags3 = account.FirstFlags3;
            Account.FirstFlags4 = account.FirstFlags4;

            // #region agent log
            try { var logData = new { location = "TNLConnection.Sector.cs:177", message = "After SaveChanges and memory update", data = new { accountId = Account.Id, savedFlags1 = Account.FirstFlags1, savedFlags2 = Account.FirstFlags2, savedFlags3 = Account.FirstFlags3, savedFlags4 = Account.FirstFlags4 }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "C" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
            // #endregion

            Logger.WriteLog(LogType.Network, $"HandleUpdateFirstTimeFlagsRequest: Successfully updated FirstTimeFlags for account {Account.Id}");
        }
        catch (Exception ex)
        {
            // #region agent log
            try { var logData = new { location = "TNLConnection.Sector.cs:185", message = "SaveChanges exception", data = new { accountId = account.Id, error = ex.Message, stackTrace = ex.StackTrace }, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sessionId = "debug-session", runId = "run1", hypothesisId = "C" }; File.AppendAllText(@"c:\Users\josh\Documents\GitHub\AutoCore\.cursor\debug.log", System.Text.Json.JsonSerializer.Serialize(logData) + "\n"); } catch { }
            // #endregion
            Logger.WriteLog(LogType.Error, $"HandleUpdateFirstTimeFlagsRequest: Exception saving to database: {ex.Message}");
        }
    }

    private void HandleMissionDialogResponse(BinaryReader reader)
    {
        // Source of truth: src/MISSION_DIALOG_CLIENT_ANALYSIS.md
        // - MissionDialog (server→client): 0x206C (handled via GroupReactionCallPacket)
        // - MissionDialog_Response (client→server): 0x206D
        //
        // NOTE: The exact 0x206D payload format is not yet fully reverse engineered.
        // This handler uses our current best-effort parser and logs values for iterative refinement.

        var packet = new MissionDialogResponsePacket();

        try
        {
            packet.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"HandleMissionDialogResponse: Failed to parse packet: {ex}");
            return;
        }

        Logger.WriteLog(LogType.Debug, $"HandleMissionDialogResponse: MissionId={packet.MissionId}, MixedVar={packet.MixedVar}, MissionGiver={packet.MissionGiver}");

        if (CurrentCharacter == null)
            return;

        // Best-effort: treat this as a mission accept/selection and ensure the mission exists in CurrentQuests.
        if (packet.MissionId > 0 && !CurrentCharacter.CurrentQuests.Any(q => q.MissionId == packet.MissionId))
        {
            CurrentCharacter.CurrentQuests.Add(new CharacterQuest(packet.MissionId, 0));
        }

        // Refresh mission list UI (client is observed to request via ConvoyMissionsRequest).
        SendGamePacket(new ConvoyMissionsResponsePacket
        {
            CurrentQuests = CurrentCharacter.CurrentQuests.ToList()
        });
    }

    private void HandleItemPickupPacket(BinaryReader reader)
    {
        if (CurrentCharacter == null || CurrentCharacter.Map == null)
        {
            Logger.WriteLog(LogType.Error, "HandleItemPickupPacket: Character or Map is null");
            return;
        }

        var packet = new ItemPickupPacket();
        packet.Read(reader);

        // Find the item in the map
        var item = CurrentCharacter.Map.GetObjectByCoid(packet.ItemId.Coid);
        if (item == null)
        {
            Logger.WriteLog(LogType.Debug, $"HandleItemPickupPacket: Item {packet.ItemId.Coid} not found in map");
            return;
        }

        // Verify the item is a pickupable item (SimpleObject or derived types)
        if (item is not SimpleObject simpleObject)
        {
            Logger.WriteLog(LogType.Error, $"HandleItemPickupPacket: Item {packet.ItemId.Coid} is not a SimpleObject");
            return;
        }

        // Check distance - player should be near the item to pick it up
        var distance = CurrentCharacter.CurrentVehicle.Position.DistSq(item.Position);
        const float maxPickupDistanceSq = 100.0f; // 10 units max distance
        if (distance > maxPickupDistanceSq)
        {
            Logger.WriteLog(LogType.Debug, $"HandleItemPickupPacket: Item {packet.ItemId.Coid} too far away (distance: {Math.Sqrt(distance):F2})");
            return;
        }

        Logger.WriteLog(LogType.Debug, $"HandleItemPickupPacket: Player {CurrentCharacter.Name} picking up item {packet.ItemId.Coid} (CBID: {simpleObject.CBID})");

        // TODO: Add item to player's inventory
        // For now, we'll just remove it from the world
        // The actual inventory system integration will need to be implemented separately

        // Save item info before removing from map
        var itemObjectId = item.ObjectId;
        var map = CurrentCharacter.Map;

        // Remove item from map (SetMap(null) handles calling LeaveMap internally)
        item.SetMap(null);

        // Broadcast destroy packet to all players in the map so they remove the item client-side
        var destroyPacket = new DestroyObjectPacket(itemObjectId);
        foreach (var character in map.Objects.Values.OfType<Character>().Where(c => c.OwningConnection != null))
        {
            character.OwningConnection.SendGamePacket(destroyPacket);
        }

        Logger.WriteLog(LogType.Debug, $"HandleItemPickupPacket: Item {packet.ItemId.Coid} removed from world");

        // TODO: Send InventoryAddItem packet to add the item to the player's inventory
    }
}
