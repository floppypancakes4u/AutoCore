namespace AutoCore.Game.TNL;

using System.Text.Json;
using System.Linq;
using AutoCore.Database.Char;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
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

        // ActivateGhosting already set Scoping=true, but Ghosting stays false until the
        // client answers rpcReadyForNormalGhosts. ObjectLocalScopeAlways still works while
        // Scoping is true; call it again after ghost create so vehicle dirty masks have a
        // GhostInfo connection ref (required for SetMaskBits → CollapseDirtyList delivery).
        ObjectLocalScopeAlways(character.Ghost);
        ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);

        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        InventoryCoidCounter.SyncFromCargo(character);
        SendInventoryLoginObjectPackets(character);
        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
        SendGamePacket(InventoryPacketFactory.CreateCargoSendAll(character.Inventory));
    }

    private void SendInventoryLoginObjectPackets(Character character)
    {
        if (character.Inventory.Items.Count == 0)
            return;

        var catalog = InventoryCatalog.FromAssetManager();
        var itemCreator = new InventoryItemCreator();
        foreach (var itemPacket in character.Inventory.CreateItemObjectPackets(catalog, itemCreator))
            SendGamePacket(itemPacket);
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
        var packet = new UpdateFirstTimeFlagsRequestPacket();
        packet.Read(reader);

        if (Account == null)
        {
            Logger.WriteLog(LogType.Error, "HandleUpdateFirstTimeFlagsRequest: Account is null");
            return;
        }

        using var context = new CharContext();
        var account = context.Accounts.FirstOrDefault(a => a.Id == Account.Id);
        
        if (account == null)
        {
            Logger.WriteLog(LogType.Error, $"HandleUpdateFirstTimeFlagsRequest: Account {Account.Id} not found in database");
            return;
        }

        account.FirstFlags1 = packet.FirstFlags1;
        account.FirstFlags2 = packet.FirstFlags2;
        account.FirstFlags3 = packet.FirstFlags3;
        account.FirstFlags4 = packet.FirstFlags4;

        try
        {
            context.SaveChanges();

            // Update the in-memory Account object
            Account.FirstFlags1 = account.FirstFlags1;
            Account.FirstFlags2 = account.FirstFlags2;
            Account.FirstFlags3 = account.FirstFlags3;
            Account.FirstFlags4 = account.FirstFlags4;

            Logger.WriteLog(LogType.Network, $"HandleUpdateFirstTimeFlagsRequest: Successfully updated FirstTimeFlags for account {Account.Id}");
        }
        catch (Exception ex)
        {
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

    private void HandleInventoryGrabPacket(BinaryReader reader)
    {
        var packet = new InventoryGrabPacket();
        packet.Read(reader);

        Logger.WriteLog(
            LogType.Debug,
            $"HandleInventoryGrabPacket: raw={Convert.ToHexString(packet.RawBytes)} parsedCoid={packet.ItemCoid} quantity={packet.Quantity} invType={packet.InventoryType}");

        var result = CurrentCharacter?.Inventory.Grab(packet, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateGrabFailure(packet),
                "HandleInventoryGrabPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
        DestroyInventoryWorldObject(result.WorldObjectToDestroy);
    }

    private void HandleInventoryDropPacket(BinaryReader reader)
    {
        var packet = new InventoryDropPacket();
        packet.Read(reader);

        Logger.WriteLog(
            LogType.Debug,
            $"HandleInventoryDropPacket: raw={Convert.ToHexString(packet.RawBytes)} coid={packet.ItemCoid} global={packet.ItemGlobal} invType={packet.InventoryType} slot={packet.InventoryPositionX},{packet.InventoryPositionY}");

        var result = CurrentCharacter?.Inventory.Drop(packet, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateDropFailure(packet),
                "HandleInventoryDropPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
    }

    private void LogInventoryOperationResult(InventoryOperationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result?.LogMessage))
            Logger.WriteLog(LogType.Debug, result.LogMessage);
    }

    private void SendInventoryOperationPackets(InventoryOperationResult result)
    {
        foreach (var response in result.Packets)
            SendGamePacket(response);
    }

    private void DestroyInventoryWorldObject(ClonedObjectBase worldObject)
    {
        if (worldObject == null)
            return;

        var map = worldObject.Map;
        var objectId = worldObject.ObjectId;
        worldObject.SetMap(null);

        if (map == null)
            return;

        var destroyPacket = new DestroyObjectPacket(objectId);
        foreach (var character in map.Objects.Values.OfType<Character>().Where(c => c.OwningConnection != null))
            character.OwningConnection.SendGamePacket(destroyPacket);
    }
}
