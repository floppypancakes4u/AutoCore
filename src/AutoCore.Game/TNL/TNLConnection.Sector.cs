namespace AutoCore.Game.TNL;

using System.Linq;
using AutoCore.Database.Char;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Skills;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public partial class TNLConnection
{
    private void HandleSkillIncrementPacket(BinaryReader reader)
    {
        var packet = new SkillIncrementPacket();
        packet.Read(reader);
        if (!packet.SkillId.HasValue)
        {
            Logger.WriteLog(LogType.Network, "SkillIncrement capture: bodyLength={0} body={1}", packet.RawBody.Length, Convert.ToHexString(packet.RawBody));
            return;
        }
        if (!CharacterSkillService.Instance.TryIncrement(CurrentCharacter, packet.SkillId.Value, out var error))
            Logger.WriteLog(LogType.Debug, "Rejected skill increment {0}: {1}", packet.SkillId, error);
        else
            SendGamePacket(CharacterLevelManager.Instance.BuildPacket(CurrentCharacter));
    }

    private void HandleAttributeIncrementPacket(BinaryReader reader)
    {
        var packet = new AttributeIncrementPacket();
        packet.Read(reader);
        if (CurrentCharacter == null)
        {
            Logger.WriteLog(LogType.Debug, "Rejected attribute increment: no character");
            return;
        }

        if (!CharacterAttributeService.Instance.TryIncrement(CurrentCharacter, packet.AttributeMask, out var error))
        {
            Logger.WriteLog(LogType.Debug, "Rejected attribute increment mask=0x{0:X8}: {1}", packet.AttributeMask, error);
            return;
        }

        // Client already applied optimistically; push absolute CharacterLevel (attrs + HP).
        SendGamePacket(CharacterLevelManager.Instance.BuildPacket(CurrentCharacter));
    }

    private void HandleRequestCastSkillPacket(BinaryReader reader)
    {
        var packet = new RequestCastSkillPacket();
        packet.Read(reader);
        if (CurrentCharacter == null || !CurrentCharacter.LearnedSkills.TryGetValue(packet.SkillId, out var rank))
        {
            if (CurrentCharacter != null)
            {
                SendGamePacket(new SkillStatusEffectPacket
                {
                    SkillId = packet.SkillId,
                    SkillLevel = 0,
                    ApplyPower = 0,
                    Status = (byte)SkillResponse.ServerChecksFailed,
                    Caster = CurrentCharacter.ObjectId,
                    PosX = packet.TargetPosition.X,
                    PosY = packet.TargetPosition.Y,
                    PosZ = packet.TargetPosition.Z,
                    Flag = 0,
                });
                // Client already spent optimistically; push server current so the HUD can restore.
                CharacterLevelManager.Instance.SyncCurrentPowerGhost(CurrentCharacter);
            }
            Logger.WriteLog(LogType.Debug, "Rejected RequestCastSkill skill={0}: skill is not learned", packet.SkillId);
            return;
        }

        if (!SkillService.TryCastPlayer(
                CurrentCharacter,
                packet.SkillId,
                rank,
                packet.Target,
                packet.TargetPosition,
                out var response))
        {
            SendGamePacket(new SkillStatusEffectPacket
            {
                SkillId = packet.SkillId,
                SkillLevel = rank,
                ApplyPower = 0,
                Status = (byte)response,
                Caster = CurrentCharacter.ObjectId,
                PosX = packet.TargetPosition.X,
                PosY = packet.TargetPosition.Y,
                PosZ = packet.TargetPosition.Z,
                Flag = 0,
            });
            // Client spent optimistically; server often did not (CD/range/power). Resync current.
            CharacterLevelManager.Instance.SyncCurrentPowerGhost(CurrentCharacter);
            Logger.WriteLog(LogType.Debug,
                "RequestCastSkill failed: skill={0} rank={1} response={2} target={3} pos={4}",
                packet.SkillId, rank, response, packet.Target, packet.TargetPosition);
        }
    }

    private void HandleQuickBarUpdatePacket(BinaryReader reader)
    {
        var packet = new QuickBarUpdatePacket();
        packet.Read(reader);
        if (!packet.IsValid)
        {
            Logger.WriteLog(LogType.Network, "QuickBarUpdate short body: bodyLength={0} body={1}",
                packet.RawBody.Length, Convert.ToHexString(packet.RawBody));
            return;
        }

        if (CurrentCharacter == null)
            return;

        // Mutual exclusivity: skill place clears item; item place/clear clears skill.
        if (!CharacterSkillService.Instance.TryUpdateQuickBar(
                CurrentCharacter, packet.Slot, packet.ItemCoid, packet.SkillId, out var error))
            Logger.WriteLog(LogType.Debug, "Rejected QuickBarUpdate slot={0} isItem={1} value={2}: {3}",
                packet.Slot, packet.IsItem, packet.Value, error);
        else
            Logger.WriteLog(LogType.Network, "QuickBarUpdate applied: slot={0} isItem={1} skill={2} item={3}",
                packet.Slot, packet.IsItem, packet.SkillId, packet.ItemCoid);
    }

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

        // OnConnectionEstablished already ActivateGhosting when DoGhosting. That leaves
        // Scoping=true and Ghosting=false until rpcReadyForNormalGhosts. Calling ActivateGhosting
        // again here (old `if (!Ghosting)`) bumps GhostingSequence and orphans the client's ready
        // reply → Ghosting stuck false → zero GhostPacks / CreateVehicle thrash without owner.
        EnsureSectorGhostingStarted();

        character.CreateGhost();
        character.CurrentVehicle.CreateGhost();

        SetScopeObject(character.Ghost);

        // Local character always in scope.
        ObjectLocalScopeAlways(character.Ghost);

        SendLocalPlayerCreatePackets(character);

        // Scope the local vehicle after CreateVehicleExtended so combat ghost masks
        // (Heat/Shield/HP/Power) can reach the owner. GhostVehicle.PackUpdate uses an
        // owner-combat initial profile (no equipment/pose) to avoid clearing +0x258.
        if (character.CurrentVehicle?.Ghost != null)
            ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);
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

        // ResetGhosting clears Ghosting and Scoping; restart only if not already scoping.
        EnsureSectorGhostingStarted();

        SetScopeObject(character.Ghost);

        ObjectLocalScopeAlways(character.Ghost);
        if (character.CurrentVehicle?.Ghost != null)
            ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);
    }

    /// <summary>
    /// Starts ghost scoping if inactive. Safe when already waiting for
    /// <c>rpcReadyForNormalGhosts</c> (does not re-sequence).
    /// </summary>
    internal void EnsureSectorGhostingStarted()
    {
        // Scoping is set by ActivateGhosting and cleared by ResetGhosting. Ghosting flips true only
        // after the client ready RPC matches GhostingSequence — do not ActivateGhosting when
        // Scoping is already true (would orphan the client's ready for the prior sequence).
        if (Scoping)
            return;

        ActivateGhosting();
    }

    private void SendLocalPlayerCreatePackets(Character character)
    {
        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        // Load XP / unspent pools / spent attributes before create packets so Tech feeds
        // HP+heat recalcs and CreateCharacterExtended attribute fields.
        var xpSvc = AutoCore.Game.Experience.ExperienceService.Instance;
        xpSvc.TryCreateLoginRestorePacket(
            character,
            AutoCore.Game.Experience.CharacterProgressPersistence.Instance);

        // CreateCharacterExtended.Credits stay 0 (login-safe). Reload money from DB now so
        // later CharacterLevel restore has the live balance.
        var currencyRestore = CurrencySync.TryCreateLoginRestorePacket(
            character,
            InventoryPersistence.Instance);
        if (currencyRestore != null)
        {
            Logger.WriteLog(
                LogType.Network,
                $"Login currency reloaded: character={character.ObjectId.Coid} credits={character.Credits}");
        }

        // Re-seed heat/power from equipped PP now that Owner is the Character (LoadFromDB
        // may have run before ownership was attached). Uses loaded Tech for heat max.
        // Fill full first so maxes are correct, then overwrite currents from DB if saved.
        character.CurrentVehicle.ApplyPowerPlantCapacities(startPowerAtFull: true, clearHeat: true);
        character.CurrentVehicle.ApplyRaceItemShieldFromEquipped(startAtFull: true);
        // Clonebase MaxHitPoint is stub 1 — recompute retail max before create + CharacterLevel.
        character.CurrentVehicle.RecalculateMaximumHitPoints(refillCurrent: true, triggerGhostUpdate: false);
        character.CurrentVehicle.RestoreCombatStateFromDb(character);
        // Ensure cargo matches chassis InventorySlots (retail 6×13×pages) before create packets.
        character.ApplyCargoCapacityFromCurrentVehicle(persist: false);

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        InventoryCoidCounter.SyncFromCargo(character);
        SendInventoryLoginObjectPackets(character);
        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
        SendGamePacket(InventoryPacketFactory.CreateCargoSendAll(character.Inventory));

        // Top up GiveItemOnStart mission gear if cargo rows were missing (failed persist / old
        // session). Idempotent by CBID quantity. Packets go out before PerPlayerLoad GiveMission
        // so client has objects before journal/dialog flows.
        RestoreMissionCargoAfterLogin(character);

        // CreateCharacterExtended hash-inserts continents without per-bit UI notify.
        // UnlockRegion (sent twice) forces client apply + map fog refresh.
        ExplorationManager.Instance.SyncExplorationAfterLogin(character);

        // Fire map PerPlayerLoad trigger (if findable) with CHARACTER activator after create
        // packets so 0x206C GiveMission can seed client mission state.
        character.Map?.FireOnLoadPlayerMissions(character);

        // Reconstruct mid-mission reaction NPCs (pad turn-in, etc.) and re-eval type 9/11/12
        // gates now that quests are loaded and both character + vehicle are on the map.
        character.Map?.ApplyMissionPhaseWorldState(
            character.CurrentVehicle ?? (ClonedObjectBase)character);

        // Always push Level/XP/currency/points after create (client XP starts at 0).
        xpSvc.SendLoginProgressToClient(character);
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

    /// <summary>
    /// After cargo load + create packets: re-ensure deliver GiveItemOnStart items for active quests.
    /// Covers failed mid-session persists and older DBs without mission cargo rows.
    /// </summary>
    private void RestoreMissionCargoAfterLogin(Character character)
    {
        if (character?.CurrentQuests == null || character.CurrentQuests.Count == 0)
            return;

        foreach (var quest in character.CurrentQuests.ToList())
        {
            try
            {
                MissionCargoService.EnsureAndSend(character, quest);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error,
                    "RestoreMissionCargoAfterLogin: mission={0} char={1}: {2}",
                    quest.MissionId,
                    character.ObjectId.Coid,
                    ex.Message);
            }
        }
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

        ObjectUseManager.Handle(this, packet);
    }

    private void HandleStoreTransactionRequestPacket(BinaryReader reader)
    {
        var packet = new StoreTransactionRequestPacket();
        try
        {
            packet.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleStoreTransactionRequestPacket: parse failed: {0}", ex.Message);
            return;
        }

        VendorStoreService.HandleTransaction(this, packet);
    }

    private void HandleStoreClosePacket(BinaryReader reader)
    {
        // Optional body ignored for now; session clear is enough for buy/sell.
        var character = CurrentCharacter;
        if (character != null)
            VendorStoreService.NoteOpened(character, 0);
        Logger.WriteLog(LogType.Debug,
            "StoreClose: charCoid={0}",
            character?.ObjectId.Coid ?? -1);
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

    private void HandleFailMissionPacket(BinaryReader reader)
    {
        // C2S journal abandon confirm (0x20B2). Client does not apply fail locally —
        // server must echo S2C FailMission after removing active quest state.
        var packet = new FailMissionPacket();
        try
        {
            packet.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleFailMissionPacket: parse failed: {0}", ex.Message);
            return;
        }

        NpcInteractHandler.HandleFailMission(this, packet);
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

    /// <summary>
    /// C2S RequestObject (0x2011): client wants full create payload for TFIDs it is missing
    /// (common after destroy/respawn races or ghost-only scope). Resend CreateVehicle/Creature/SimpleObject.
    /// Layout after opcode: u8 count + 3 pad + count × TFID16 (Ghidra FUN_0091da70).
    /// </summary>
    private void HandleRequestObjectPacket(BinaryReader reader)
    {
        if (CurrentCharacter?.Map == null)
            return;

        RequestObjectPacket request;
        try
        {
            request = new RequestObjectPacket();
            request.Read(reader);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleRequestObject: parse failed: {0}", ex.Message);
            return;
        }

        foreach (var tfid in request.Objects)
            ResendObjectCreate(tfid);
    }

    private void ResendObjectCreate(TFID tfid)
    {
        if (tfid == null || tfid.Coid <= 0 || CurrentCharacter?.Map == null)
            return;

        var obj = CurrentCharacter.Map.GetObjectByCoid(tfid.Coid);
        if (obj == null)
        {
            Logger.WriteLog(LogType.Debug,
                "RequestObject: coid={0} global={1} not on map for char={2}",
                tfid.Coid,
                tfid.Global ? 1 : 0,
                CurrentCharacter.ObjectId.Coid);
            return;
        }

        try
        {
            switch (obj)
            {
                case Vehicle vehicle:
                {
                    vehicle.EnsureDefaultWheelSetForWire();
                    var packet = new CreateVehiclePacket();
                    vehicle.WriteToPacket(packet);
                    SendGamePacket(packet);
                    ForeignNpcDriverWire.TrySendDriverCreate(this, vehicle);
                    Logger.WriteLog(LogType.Debug,
                        "RequestObject: resent CreateVehicle coid={0} cbid={1} templateId={2}",
                        vehicle.ObjectId.Coid,
                        vehicle.CBID,
                        vehicle.TemplateId);
                    break;
                }
                case Creature creature:
                {
                    var packet = new CreateCreaturePacket();
                    creature.WriteToPacket(packet);
                    SendGamePacket(packet);
                    Logger.WriteLog(LogType.Debug,
                        "RequestObject: resent CreateCreature coid={0} cbid={1}",
                        creature.ObjectId.Coid,
                        creature.CBID);
                    break;
                }
                case GraphicsObject graphics:
                {
                    var packet = new CreateSimpleObjectPacket();
                    graphics.WriteToPacket(packet);
                    SendGamePacket(packet);
                    Logger.WriteLog(LogType.Debug,
                        "RequestObject: resent CreateSimpleObject coid={0} cbid={1}",
                        graphics.ObjectId.Coid,
                        graphics.CBID);
                    break;
                }
                default:
                    Logger.WriteLog(LogType.Debug,
                        "RequestObject: unsupported type {0} coid={1}",
                        obj.GetType().Name,
                        tfid.Coid);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "RequestObject: failed to re-create coid={0}: {1}",
                tfid.Coid,
                ex.Message);
        }
    }

    /// <summary>
    /// C2S Firing (0x2022): fire state without a full VehicleMoved. Layout mirrors the VehicleMoved
    /// fire/target tail: u8 firing, u16 reserved, TFID target (best-effort; extra trailing bytes ignored).
    /// </summary>
    private void HandleFiringPacket(BinaryReader reader)
    {
        var vehicle = CurrentCharacter?.CurrentVehicle;
        if (vehicle == null || vehicle.Map == null)
            return;

        try
        {
            var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remaining < 1)
                return;

            vehicle.Firing = reader.ReadByte();
            if (remaining >= 1 + 2 + 16)
            {
                _ = reader.ReadUInt16();
                var target = reader.ReadTFID();
                if (target.Coid > 0)
                {
                    var targetObj = vehicle.Map.GetObjectByCoid(target.Coid)
                        ?? vehicle.Map.GetObject(target.Coid)
                        ?? ObjectManager.Instance?.GetObject(target);
                    vehicle.SetTargetObject(targetObj);
                }
                else
                {
                    vehicle.SetTargetObject(null);
                }
            }

            vehicle.ProcessCombatIfFiring();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "HandleFiringPacket: {0}", ex.Message);
        }
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

        var inventory = CurrentCharacter.Inventory;
        if (inventory == null)
        {
            Logger.WriteLog(LogType.Error, "HandleItemPickupPacket: Character inventory is null");
            return;
        }

        // Same path as /addItem: allocate inventory coid, Create(IsInInventory) + 0x2047 + CargoSendAll.
        // Do not reuse the world local TFID or only send 0x2047 — client cargo will not bind.
        var runtime = new InventoryRuntime(CurrentCharacter);
        if (!runtime.CanAllocateItem)
        {
            Logger.WriteLog(LogType.Error, "HandleItemPickupPacket: cannot allocate inventory coid (no map)");
            return;
        }

        var worldObjectId = item.ObjectId;
        var cbid = simpleObject.CBID;
        var type = simpleObject.Type;
        var displayName = simpleObject.CloneBaseObject?.CloneBaseSpecific.UniqueName ?? $"CBID {cbid}";
        var inventoryCoid = runtime.AllocateItemCoid();

        var claim = inventory.PickupWorldItem(
            cbid,
            type,
            displayName,
            inventoryCoid,
            new InventoryItemCreator(),
            CurrentCharacter.ObjectId.Coid);

        if (claim.AddedItem == null)
        {
            Logger.WriteLog(LogType.Debug,
                $"HandleItemPickupPacket: claim failed for world coid={worldObjectId.Coid}: {claim.Message}");
            return;
        }

        foreach (var outbound in claim.Packets)
            SendGamePacket(outbound);

        var map = CurrentCharacter.Map;
        item.SetMap(null);

        var destroyPacket = new DestroyObjectPacket(worldObjectId);
        foreach (var character in map.Objects.Values.OfType<Character>().Where(c => c.OwningConnection != null))
            character.OwningConnection.SendGamePacket(destroyPacket);

        Logger.WriteLog(LogType.Debug,
            $"HandleItemPickupPacket: world coid={worldObjectId.Coid} cbid={cbid} → cargo coid={inventoryCoid} slot ({claim.AddedItem.InventoryPositionX},{claim.AddedItem.InventoryPositionY})");
    }

    private void HandleItemDropPacket(BinaryReader reader)
    {
        var packet = new ItemDropPacket();
        packet.Read(reader);

        LogInventoryDebugPacket(
            "HandleItemDropPacket",
            packet.RawBytes,
            $"source={packet.SourceObjectId} coid={packet.ItemCoid} pos={packet.DropPosition}" +
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

        LogInventoryDebugPacket(
            "HandleInventoryGrabPacket",
            packet.RawBytes,
            $"coid={packet.ItemCoid} quantity={packet.Quantity} invType={packet.InventoryType}");

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

        LogInventoryDebugPacket(
            "HandleInventoryDropPacket",
            packet.RawBytes,
            $"coid={packet.ItemCoid} global={packet.ItemGlobal} invType={packet.InventoryType} slot={packet.InventoryPositionX},{packet.InventoryPositionY}");

        var result = CurrentCharacter?.Inventory.Drop(packet, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateDropFailure(packet),
                "HandleInventoryDropPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
    }

    private void HandleInventoryGrabMMPacket(BinaryReader reader)
    {
        var packet = new InventoryGrabMMPacket();
        packet.Read(reader);

        LogInventoryDebugPacket(
            "HandleInventoryGrabMMPacket",
            packet.RawBytes,
            $"coid={packet.ItemCoid} quantity={packet.Quantity} invType={packet.InventoryType}");

        // Mass-move grab is the same as a normal grid grab (client sends one GrabMM per item).
        // Respond with InventoryGrabResponse (0x2035): client early-outs on GrabMMResponse 0x2039.
        var grab = packet.ToGrabPacket();
        var result = CurrentCharacter?.Inventory.Grab(grab, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateGrabFailure(grab),
                "HandleInventoryGrabMMPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
        DestroyInventoryWorldObject(result.WorldObjectToDestroy);
    }

    private void HandleInventoryDropMMPacket(BinaryReader reader)
    {
        var packet = new InventoryDropMMPacket();
        packet.Read(reader);

        LogInventoryDebugPacket(
            "HandleInventoryDropMMPacket",
            packet.RawBytes,
            $"coid={packet.ItemCoid} global={packet.ItemGlobal} invType={packet.InventoryType} slot={packet.InventoryPositionX},{packet.InventoryPositionY}");

        // Mass-move drop: same as InventoryDrop (cargo/locker rearrange or transfer).
        // Respond with InventoryDropResponse (0x2037): client early-outs on DropMMResponse 0x203B.
        var drop = packet.ToDropPacket();
        var result = CurrentCharacter?.Inventory.Drop(drop, CurrentCharacter)
            ?? InventoryOperationResult.SinglePacket(
                InventoryManager.CreateDropFailure(drop),
                "HandleInventoryDropMMPacket: Character is null");

        LogInventoryOperationResult(result);
        SendInventoryOperationPackets(result);
    }

    private void HandleInventoryDestroyItemPacket(BinaryReader reader)
    {
        var packet = new InventoryDestroyItemPacket();
        packet.Read(reader);

        LogInventoryDebugPacket(
            "HandleInventoryDestroyItemPacket",
            packet.RawBytes,
            $"coid={packet.ItemCoid} global={packet.ItemGlobal}");

        Logger.WriteLog(
            LogType.Network,
            "HandleInventoryDestroyItemPacket: log-only stub — inventory destroy/toss is not implemented yet");
    }

    /// <summary>
    /// Packet detail logs (including raw hex) for inventory handlers.
    /// Gated by <see cref="Diagnostics.ServerConfig.InventoryDebugPackets"/> (<c>serverConfig.yaml</c> → inventory.debugPackets).
    /// </summary>
    private static void LogInventoryDebugPacket(string handler, byte[] rawBytes, string summary)
    {
        if (!Diagnostics.ServerConfig.InventoryDebugPackets)
            return;

        var message = $"{handler}: {summary}";
        if (rawBytes is { Length: > 0 })
            message += $" raw={Convert.ToHexString(rawBytes)}";

        Logger.WriteLog(LogType.Debug, message);
    }

    private void LogInventoryOperationResult(InventoryOperationResult result)
    {
        if (!Diagnostics.ServerConfig.InventoryDebugPackets)
            return;

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
