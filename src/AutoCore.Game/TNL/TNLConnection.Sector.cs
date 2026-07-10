namespace AutoCore.Game.TNL;

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

        SendLocalPlayerCreatePackets(character);
    }

    /// <summary>
    /// Restarts TNL ghosting after a map change and re-scopes the local character/vehicle.
    /// <see cref="ResetGhosting"/> tears down all ghosts and leaves Ghosting/Scoping off;
    /// without this follow-up the client never receives object ghosts on the new map, and
    /// in-flight ghost teardown can leave half-initialized creature ghosts that crash the
    /// client (GhostCreature apply at 0x005D262A).
    /// </summary>
    public void ReestablishGhostingAfterMapTransfer(Character character, bool sendCreatePackets = true)
    {
        EnsureGhostsAndScopeAfterMapTransfer(character);

        // Client deleted local ghosts on rpcEndGhosting; global entities need create packets
        // again after MapInfo so ghost assignment can find the player/vehicle.
        if (sendCreatePackets)
            SendLocalPlayerCreatePackets(character);
    }

    /// <summary>
    /// Creates/reuses character+vehicle ghosts, restarts ghosting, and re-scopes them.
    /// Separated from create-packet send so ghosting restart can be regression-tested
    /// without full clonebase-backed WriteToPacket data.
    /// </summary>
    public void EnsureGhostsAndScopeAfterMapTransfer(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (character.CurrentVehicle == null)
            throw new InvalidOperationException("Cannot re-establish ghosting without a current vehicle.");

        // The preceding ResetGhosting told the client to delete its local ghosts (rpcEndGhosting),
        // discarding every foreign global-vehicle object we created on the old map. Forget the sent
        // set so the new map's scope queries re-send creates instead of suppressing them as dupes.
        ClearGlobalVehicleCreateTracking();

        // Ensure NetObjects exist (no-op if already created before the transfer).
        character.CreateGhost();
        character.CurrentVehicle.CreateGhost();

        // ResetGhosting always clears Ghosting and Scoping; restart the full sequence.
        ActivateGhosting();

        SetScopeObject(character.Ghost);

        ObjectLocalScopeAlways(character.Ghost);
        ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);
    }

    private void SendLocalPlayerCreatePackets(Character character)
    {
        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        InventoryCoidCounter.SyncFromCargo(character);
        SendInventoryLoginObjectPackets(character);
        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
        SendGamePacket(InventoryPacketFactory.CreateCargoSendAll(character.Inventory));

        // CreateCharacterExtended hash-inserts continents without per-bit UI notify.
        // UnlockRegion (sent twice) forces client apply + map fog refresh.
        ExplorationManager.Instance.SyncExplorationAfterLogin(character);

        // Fire map PerPlayerLoad trigger (if findable) with CHARACTER activator after create
        // packets so 0x206C GiveMission can seed client mission state.
        character.Map?.FireOnLoadPlayerMissions(character);

        // CreateCharacterExtended.Credits stay 0 (login-safe). Restore absolute money the same
        // way /currency does (CharacterLevel 0x2017), after create so the client object exists.
        // Reload from DB so restore always uses the ledger /currency persists.
        var currencyRestore = CurrencySync.TryCreateLoginRestorePacket(
            character,
            InventoryPersistence.Instance);
        if (currencyRestore != null)
        {
            Logger.WriteLog(
                LogType.Network,
                $"Login currency restore: character={character.ObjectId.Coid} credits={currencyRestore.Currency}");
            SendGamePacket(currencyRestore);
        }
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

    private void HandleUseObjectPacket(BinaryReader reader)
    {
        var packet = new UseObjectPacket();
        try
        {
            packet.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleUseObjectPacket: parse failed: {0}", ex.Message);
            return;
        }

        NpcInteractHandler.HandleUseObject(this, packet);
    }

    private void HandleAutoPatrolPacket(BinaryReader reader)
    {
        // Client may send this every tick while near a waypoint — quiet parse + progress once.
        var packet = new AutoPatrolPacket();
        try
        {
            packet.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleAutoPatrolPacket: parse failed: {0}", ex.Message);
            return;
        }

        NpcInteractHandler.HandleAutoPatrol(this, packet);
    }

    private void HandleMissionDialogResponse(BinaryReader reader)
    {
        // Ghidra: S2C dialog open is 0x206D (NpcMissionDialogPacket);
        // C2S OK/Accept is 0x206E (MissionDialogResponsePacket) via dialog+0x650.
        var packet = new MissionDialogResponsePacket();

        try
        {
            packet.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleMissionDialogResponse: Failed to parse packet: {0}", ex.Message);
            return;
        }

        NpcInteractHandler.HandleMissionDialogResponse(this, packet);
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

    private void HandleItemDropPacket(BinaryReader reader)
    {
        var packet = new ItemDropPacket();
        packet.Read(reader);

        Logger.WriteLog(
            LogType.Network,
            $"HandleItemDropPacket: raw={Convert.ToHexString(packet.RawBytes)} source={packet.SourceObjectId} coid={packet.ItemCoid} pos={packet.DropPosition}" +
            (packet.RawBytes.Length >= ItemDropPacket.MinimumLength ? $" tail={packet.TailValue}" : string.Empty));

        var result = CurrentCharacter?.Inventory.TossToWorld(packet, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateItemDropFailure(packet),
                "HandleItemDropPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
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

        LogInventoryTossPacket("HandleInventoryDropPacket", packet.RawBytes, packet.ItemCoid, packet.ItemGlobal,
            packet.InventoryType, packet.InventoryPositionX, packet.InventoryPositionY, packet.TailBytes,
            packet.EnumerateInt64Candidates(), packet.EnumerateInt32Candidates());

        var result = CurrentCharacter?.Inventory.Drop(packet, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateDropFailure(packet),
                "HandleInventoryDropPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
    }

    private void HandleInventoryDropMMPacket(BinaryReader reader)
    {
        var packet = new InventoryDropMMPacket();
        packet.Read(reader);

        LogInventoryTossPacket("HandleInventoryDropMMPacket", packet.RawBytes, packet.ItemCoid, packet.ItemGlobal,
            packet.InventoryType, packet.InventoryPositionX, packet.InventoryPositionY, packet.TailBytes,
            packet.EnumerateInt64Candidates(), packet.EnumerateInt32Candidates());

        Logger.WriteLog(
            LogType.Network,
            "HandleInventoryDropMMPacket: log-only stub — world toss via InventoryDropMM is not implemented yet");
    }

    private void HandleInventoryDestroyItemPacket(BinaryReader reader)
    {
        var packet = new InventoryDestroyItemPacket();
        packet.Read(reader);

        Logger.WriteLog(
            LogType.Network,
            $"HandleInventoryDestroyItemPacket: raw={Convert.ToHexString(packet.RawBytes)} coid={packet.ItemCoid} global={packet.ItemGlobal}" +
            (packet.TailBytes.Length > 0 ? $" tail={Convert.ToHexString(packet.TailBytes)}" : string.Empty) +
            $" i32=[{string.Join(",", packet.EnumerateInt32Candidates())}] i64=[{string.Join(",", packet.EnumerateInt64Candidates())}]");

        Logger.WriteLog(
            LogType.Network,
            "HandleInventoryDestroyItemPacket: log-only stub — inventory destroy/toss is not implemented yet");
    }

    private static void LogInventoryTossPacket(
        string handler,
        byte[] rawBytes,
        long itemCoid,
        bool itemGlobal,
        byte inventoryType,
        byte inventoryPositionX,
        byte inventoryPositionY,
        ReadOnlySpan<byte> tailBytes,
        IEnumerable<long> int64Candidates,
        IEnumerable<int> int32Candidates)
    {
        var message =
            $"{handler}: raw={Convert.ToHexString(rawBytes)} coid={itemCoid} global={itemGlobal} invType={inventoryType} slot={inventoryPositionX},{inventoryPositionY}";

        if (tailBytes.Length > 0)
            message += $" tail={Convert.ToHexString(tailBytes)}";

        if (inventoryType is not 1 and not 2)
        {
            message += $" i32=[{string.Join(",", int32Candidates)}] i64=[{string.Join(",", int64Candidates)}]";
        }

        Logger.WriteLog(LogType.Network, message);
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
