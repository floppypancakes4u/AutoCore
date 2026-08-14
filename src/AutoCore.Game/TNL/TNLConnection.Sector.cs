namespace AutoCore.Game.TNL;

using System.Diagnostics.CodeAnalysis;
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
                Diagnostics.PlayerActionTrace.SkillCast(
                    CurrentCharacter, packet.SkillId, 0, success: false,
                    response: "NotLearned", targetCoid: packet.Target?.Coid ?? 0);
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
            Diagnostics.PlayerActionTrace.SkillCast(
                CurrentCharacter, packet.SkillId, rank, success: false,
                response: response.ToString(), targetCoid: packet.Target?.Coid ?? 0);
            return;
        }

        Diagnostics.PlayerActionTrace.SkillCast(
            CurrentCharacter, packet.SkillId, rank, success: true,
            response: "Ok", targetCoid: packet.Target?.Coid ?? 0);
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

    /// <summary>
    /// Stage-1 sector transfer: live CharContext load + map fill. Soft-fail Disconnect on
    /// missing character. Stage2 soft-fails covered by handler unit tests with TestPacketSink.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Live CharContext EF I/O for GetOrLoadCharacter; Stage2 soft-fail unit-tested.")]
    private void HandleTransferFromGlobalPacket(BinaryReader reader)
    {
        var packet = new TransferFromGlobalPacket();
        packet.Read(reader);

        AutoCore.Utils.Logging.GameLog.Info("SectorHandshakeStarted",
            ("SessionId", SessionId),
            ("CharacterId", packet.CharacterCoid));

        // SS-29 (accepted risk, log-only): sector transfer security key is not enforced yet.
        // Emit a structured warning when a non-zero key arrives so playtest logs surface the gap
        // without disconnecting clients. Full validation is deferred.
        if (packet.SecurityKey != 0)
        {
            AutoCore.Utils.Logging.GameLog.Warn("SecurityKeyMismatch", "SEC-002",
                ("SessionId", SessionId),
                ("CharacterId", packet.CharacterCoid),
                ("SecurityKeyPresent", true),
                ("Note", "Transfer key not validated (SS-29 accepted risk)"));
        }

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

        // Instanced starting areas resolve to a fresh private copy per login (retail behavior);
        // shared continents return the one shared map, identical to GetMap.
        var map = MapManager.Instance.GetMapForCharacter(CurrentCharacter.LastTownId, CurrentCharacter);

        CurrentCharacter.SetOwningConnection(this);
        CurrentCharacter.GMLevel = Account.Level;

        // SS-51: login lands the character on its map here, but the create stream only goes out
        // in Stage3 (SendLocalPlayerCreatePackets). Close the world-entry gate so EnterMap's
        // mission-phase replay is deferred instead of firing at a client that has nothing yet.
        CurrentCharacter.BeginWorldEntry();

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
            if (TransferPhase != SectorTransferPhase.None)
            {
                LogStaleTransferStage("UnknownCoidStage2", packet.CharacterCoid);
                return;
            }

            Disconnect("Invalid character");
            return;
        }

        if (TransferPhase == SectorTransferPhase.WaitingForStage2
            && IsCurrentTransferHandshake(packet.CharacterCoid))
        {
            SendTransferStage3(character, packet.SecurityKey);
            TransferPhase = SectorTransferPhase.WaitingForStage3Ack;
            AutoCore.Utils.Logging.GameLog.Info("MapTransferStage2Received",
                TransferHandshakeLogProps());
            AutoCore.Utils.Logging.GameLog.Info("MapTransferStage3Sent",
                TransferHandshakeLogProps());
            return;
        }

        if (TransferPhase == SectorTransferPhase.WaitingForStage3Ack
            && packet.CharacterCoid == TransferHandshakeCharacterCoid)
        {
            LogStaleTransferStage("DuplicateStage2", packet.CharacterCoid);
            return;
        }

        if (TransferPhase != SectorTransferPhase.None)
        {
            LogStaleTransferStage("WrongCoidStage2", packet.CharacterCoid);
            return;
        }

        _loginStage3Offered = true;
        SendTransferStage3(character, packet.SecurityKey);
    }

    /// <summary>
    /// Stage-3 ghost activation + local create packets. Soft-fail missing character is
    /// trivial Disconnect; success path needs live ghosting/create clonebases.
    /// Stage2 soft-fail covered by unit tests.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Live sector ghosting + create-packet path; Stage2 soft-fail unit-tested.")]
    private void HandleTransferFromGlobalStage3Packet(BinaryReader reader)
    {
        var packet = new TransferFromGlobalStage3Packet();
        packet.Read(reader);

        var character = ObjectManager.Instance.GetCharacter(packet.CharacterCoid);
        if (character == null)
        {
            if (TransferPhase != SectorTransferPhase.None)
            {
                LogStaleTransferStage("UnknownCoidStage3", packet.CharacterCoid);
                return;
            }

            Disconnect("Invalid character");
            return;
        }

        if (TransferPhase == SectorTransferPhase.WaitingForStage3Ack
            && IsCurrentTransferHandshake(packet.CharacterCoid))
        {
            AutoCore.Utils.Logging.GameLog.Info("MapTransferStage3AckReceived",
                TransferHandshakeLogProps());
            TransferPhase = SectorTransferPhase.None;
            CompletePendingMapTransferWorldEntry(character);
            return;
        }

        if (TransferPhase == SectorTransferPhase.WaitingForStage2)
        {
            LogStaleTransferStage("Stage3BeforeStage2", packet.CharacterCoid);
            return;
        }

        if (TransferPhase != SectorTransferPhase.None)
        {
            LogStaleTransferStage("WrongCoidStage3", packet.CharacterCoid);
            return;
        }

        if (character.WorldEntryComplete)
        {
            LogStaleTransferStage("DuplicateStage3Ack", packet.CharacterCoid);
            AutoCore.Utils.Logging.GameLog.Info("SectorGhostingDuplicateActivationPrevented",
                ("SessionId", SessionId),
                ("CharacterId", packet.CharacterCoid));
            return;
        }

        if (!_loginStage3Offered)
        {
            LogStaleTransferStage("Stage3BeforeStage2", packet.CharacterCoid);
            return;
        }

        CompleteInitialWorldEntry(character);

        AutoCore.Utils.Logging.GameLog.Info("CharacterSpawned",
            ("SessionId", SessionId),
            ("CharacterId", character.ObjectId.Coid),
            ("CharacterName", character.Name),
            ("AccountId", Account?.Id));
    }

    /// <summary>
    /// Login Stage3 finalizer: local Creates, then the first ghost lifecycle.
    /// Client FUN_008078B0 applies foreign ghosts immediately and is Stage-unaware;
    /// Creates must land before rpcStartGhosting.
    /// </summary>
    private void CompleteInitialWorldEntry(Character character)
    {
        var skipCreates = SuppressCreatePacketsForTests
            || MapManager.Instance.SuppressCreatePacketsForTests;
        if (!skipCreates)
        {
            SendLocalPlayerCreatePackets(character);
        }
        else
        {
            WorldEntryOpsForTests.Add("CreateVehicleExtended");
            WorldEntryOpsForTests.Add("CreateCharacterExtended");
            character.CompleteWorldEntry();
        }

        character.CreateGhost();
        character.CurrentVehicle.CreateGhost();

        var seqBefore = GetGhostingSequence();
        EnsureSectorGhostingStarted();
        ScopeLocalPlayerGhosts(character);

        if (GetGhostingSequence() > seqBefore)
        {
            AutoCore.Utils.Logging.GameLog.Info("SectorGhostingActivatedAfterWorldEntry",
                ("SessionId", SessionId),
                ("CharacterId", character.ObjectId.Coid));
        }
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
        if (character == null)
            throw new ArgumentNullException(nameof(character));
        if (character.CurrentVehicle == null)
            throw new InvalidOperationException("Cannot re-establish ghosting without a current vehicle.");

        // Create local ghosts and forget old-map global-vehicle tracking, but do not
        // ActivateGhosting yet. rpcStartGhosting would let nearby giver CreateCreature
        // land before CreateCharacterExtended restores the client's completed-mission hash
        // (interact icon states 6/7). Login Stage3 uses the same Creates-then-activate order.
        ClearGlobalVehicleCreateTracking();
        character.CreateGhost();
        character.CurrentVehicle.CreateGhost();

        var ghostingAlreadyStarted = Scoping;
        if (ghostingAlreadyStarted)
            ScopeLocalPlayerGhosts(character);

        if (sendCreatePackets && !SuppressCreatePacketsForTests)
            SendLocalPlayerCreatePackets(character);

        EnsureSectorGhostingStarted();
        ScopeLocalPlayerGhosts(character);

        LocalCreateSentBeforeActivateGhostingForTests = sendCreatePackets && !ghostingAlreadyStarted;
    }

    private void ScopeLocalPlayerGhosts(Character character)
    {
        SetScopeObject(character.Ghost);
        ObjectLocalScopeAlways(character.Ghost);
        if (character.CurrentVehicle?.Ghost != null)
            ObjectLocalScopeAlways(character.CurrentVehicle.Ghost);
    }

    /// <summary>
    /// True when the last <see cref="ReestablishGhostingAfterMapTransfer"/> sequenced
    /// local create (or its suppressed stand-in) before <c>ActivateGhosting</c>.
    /// </summary>
    public bool LocalCreateSentBeforeActivateGhostingForTests { get; private set; }

    /// <summary>
    /// Ordered world-entry operations for login/transfer tests. Appended at the
    /// exact send/activate sites; not a wire capture.
    /// </summary>
    internal List<string> WorldEntryOpsForTests { get; } = new();

    /// <summary>
    /// Set when login Stage2 offers S2C Stage3. Login Stage3 ack is ignored until then
    /// so a hostile Stage3-before-Stage2 cannot start Creates or ghosting.
    /// </summary>
    private bool _loginStage3Offered;

    /// <summary>
    /// Same-map owner teleport: tear down client ghosts and re-send local create packets so the
    /// client re-materializes the vehicle/character at the server's current <see cref="ClonedObjectBase.Position"/>.
    /// </summary>
    /// <remarks>
    /// Retail GM <c>/teleport</c> / <c>/waypoint</c> snap pose client-side via
    /// <c>CVOGReaction_TeleportTarget</c> (no S2C). Owner vehicles ignore ghost
    /// <see cref="GhostObject.PositionMask"/> as motion authority. SpecialEvent Respawn (0x20A9 type 0)
    /// is the death INC airlift (<c>cptest.geo</c>) and cancels when setup fails — not a living GM snap.
    /// Map-transfer style ResetGhosting + CreateVehicle/Character is the server-driven path that
    /// actually places the local owner at create-packet pose.
    /// </remarks>
    public void ResyncLocalPlayerAtCurrentPose(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));
        if (character.CurrentVehicle == null)
            throw new InvalidOperationException("Cannot resync local player without a current vehicle.");

        ResyncLocalPlayerAtCurrentPoseCallCountForTests++;

        // Tell the client to drop local ghosts (same as map leave), then re-scope self/vehicle.
        ResetGhosting();
        EnsureGhostsAndScopeAfterMapTransfer(character);

        if (SuppressCreatePacketsForTests)
            return;

        // Mid-session pose snap only — do not re-run full login restore (missions, XP, cargo).
        SendLocalPlayerCreatePosePackets(character);
    }

    /// <summary>When true, <see cref="ResyncLocalPlayerAtCurrentPose"/> skips create packet I/O (unit tests).</summary>
    public bool SuppressCreatePacketsForTests { get; set; }

    /// <summary>How many times <see cref="ResyncLocalPlayerAtCurrentPose"/> ran (tests / diagnostics).</summary>
    public int ResyncLocalPlayerAtCurrentPoseCallCountForTests { get; private set; }

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
        ScopeLocalPlayerGhosts(character);
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
        WorldEntryOpsForTests.Add("ActivateGhosting");
    }

    internal void BeginPendingMapTransferHandshake(
        Character character,
        int destinationContinentId,
        int sourceContinentId = 0)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        if (TransferPhase != SectorTransferPhase.None)
        {
            AutoCore.Utils.Logging.GameLog.Info("MapTransferHandshakeAborted",
                TransferHandshakeLogProps(("Reason", "superseded")));
        }

        TransferHandshakeGeneration++;
        TransferPhase = SectorTransferPhase.WaitingForStage2;
        TransferHandshakeCharacterCoid = character.ObjectId.Coid;
        TransferHandshakeDestinationContinentId = destinationContinentId;
        TransferHandshakeSourceContinentId = sourceContinentId;

        AutoCore.Utils.Logging.GameLog.Info("MapTransferHandshakeWaiting",
            TransferHandshakeLogProps());
    }

    internal void AbortPendingMapTransferHandshake(string reason)
    {
        if (TransferPhase == SectorTransferPhase.None)
            return;

        AutoCore.Utils.Logging.GameLog.Info("MapTransferHandshakeAborted",
            TransferHandshakeLogProps(("Reason", reason)));
        TransferPhase = SectorTransferPhase.None;
        TransferHandshakeCharacterCoid = 0;
        TransferHandshakeDestinationContinentId = 0;
        TransferHandshakeSourceContinentId = 0;
    }

    internal void CompletePendingMapTransferWorldEntry(Character character)
    {
        if (character == null)
            throw new ArgumentNullException(nameof(character));

        var skipCreates = SuppressCreatePacketsForTests
            || MapManager.Instance.SuppressCreatePacketsForTests;
        if (!skipCreates)
            SendLocalPlayerCreatePackets(character);
        else
            character.CompleteWorldEntry();

        ReestablishGhostingAfterMapTransfer(character, sendCreatePackets: false);

        AutoCore.Utils.Logging.GameLog.Info("MapTransferCreatesReleased",
            TransferHandshakeLogProps());
        AutoCore.Utils.Logging.GameLog.Info("MapTransferGhostingActivated",
            TransferHandshakeLogProps());
    }

    private void SendTransferStage3(Character character, uint securityKey)
    {
        SendGamePacket(new TransferFromGlobalStage3Packet
        {
            SecurityKey = securityKey,
            CharacterCoid = character.ObjectId.Coid,
            PositionX = character.Position.X,
            PositionY = character.Position.Y,
            PositionZ = character.Position.Z
        });
    }

    private bool IsCurrentTransferHandshake(long characterCoid)
    {
        return CurrentCharacter != null
            && characterCoid == TransferHandshakeCharacterCoid
            && characterCoid == CurrentCharacter.ObjectId.Coid;
    }

    private void LogStaleTransferStage(string reason, long packetCharacterCoid)
    {
        AutoCore.Utils.Logging.GameLog.Info("MapTransferStaleStagePacket",
            TransferHandshakeLogProps(
                ("Reason", reason),
                ("PacketCharacterId", packetCharacterCoid)));
    }

    private (string, object)[] TransferHandshakeLogProps(params (string, object)[] extra)
    {
        var props = new List<(string, object)>(8 + extra.Length)
        {
            ("SessionId", SessionId),
            ("CharacterId", TransferHandshakeCharacterCoid),
            ("ToMapId", TransferHandshakeDestinationContinentId),
            ("TransferPhase", TransferPhase.ToString()),
            ("TransferGeneration", TransferHandshakeGeneration),
        };
        if (TransferHandshakeSourceContinentId != 0)
            props.Add(("FromMapId", TransferHandshakeSourceContinentId));
        props.AddRange(extra);
        return props.ToArray();
    }

    /// <summary>
    /// Vehicle + character create only, using current entity pose. Used for same-map owner resync
    /// after GM teleport — avoids re-firing login mission/XP/cargo side effects.
    /// </summary>
    private void SendLocalPlayerCreatePosePackets(Character character)
    {
        var charPacket = new CreateCharacterExtendedPacket();
        var vehiclePacket = new CreateVehicleExtendedPacket();

        character.WriteToPacket(charPacket);
        character.CurrentVehicle.WriteToPacket(vehiclePacket);

        SendGamePacket(vehiclePacket);
        SendGamePacket(charPacket);
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

        SendInventoryLoginObjectPackets(character);
        WorldEntryOpsForTests.Add("CreateVehicleExtended");
        SendGamePacket(vehiclePacket);
        WorldEntryOpsForTests.Add("CreateCharacterExtended");
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

        // SS-51: the client now has its own objects. Open the world-entry gate and flush the
        // single coalesced mission re-eval that was deferred during entry.
        character.CompleteWorldEntry();
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

    internal void HandleCreatureMovedPacket(BinaryReader reader)
    {
        // SS-32: movement can arrive before the character is bound (zone-in race) — drop, don't
        // NRE into the dispatch catch as a NET-002 "malformed packet".
        var character = CurrentCharacter;
        if (character == null)
            return;

        var packet = new CreatureMovedPacket();
        packet.Read(reader);

        character.HandleMovement(packet);
    }

    internal void HandleVehicleMovedPacket(BinaryReader reader)
    {
        // SS-32: same zone-in race as CreatureMoved — an NRE here used to eat the entire
        // move+fire+target update for the packet.
        var vehicle = CurrentCharacter?.CurrentVehicle;
        if (vehicle == null)
            return;

        var packet = new VehicleMovedPacket();
        packet.Read(reader);

        vehicle.HandleMovement(packet);
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
            Logger.WriteException(LogType.Warning, "HandleUseObjectPacket: parse", ex);
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
            Logger.WriteException(LogType.Warning, "HandleStoreTransactionRequestPacket: parse", ex);
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
            Logger.WriteException(LogType.Warning, "HandleAutoPatrolPacket: parse", ex);
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
            Logger.WriteException(LogType.Warning, "HandleFailMissionPacket: parse", ex);
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
            Logger.WriteException(LogType.Warning, "HandleMissionDialogResponse: parse", ex);
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
            Logger.WriteException(LogType.Warning, "HandleRequestObject: parse", ex);
            return;
        }

        foreach (var tfid in request.Objects)
            ResendObjectCreate(tfid);
    }

    private void ResendObjectCreate(TFID tfid)
    {
        if (tfid == null || tfid.Coid <= 0 || CurrentCharacter?.Map == null)
            return;

        // Full TFID is in hand — exact lookup avoids serving the wrong same-COID entity (SS-31).
        var obj = Combat.CombatTargetResolver.Resolve(CurrentCharacter.Map, tfid);
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
                case Character character:
                {
                    // Character : Creature. Must precede the Creature case — client
                    // FUN_008078B0 / Client_RecvCreateCharacter expects 0x2015 (0x1A8),
                    // not CreateCreature 0x2013 (0x130).
                    var packet = new CreateCharacterPacket();
                    character.WriteToPacket(packet);
                    SendGamePacket(packet);
                    Logger.WriteLog(LogType.Debug,
                        "RequestObject: resent CreateCharacter coid={0} cbid={1}",
                        character.ObjectId.Coid,
                        character.CBID);
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
                    // TFID-exact — COID-only resolution latches the wrong entity on collision (SS-31).
                    var targetObj = Combat.CombatTargetResolver.Resolve(vehicle.Map, target);
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
            Logger.WriteException(LogType.Warning, "HandleFiringPacket", ex);
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

        if (!inventory.CanAcceptAnyOfCbid(cbid))
        {
            Logger.WriteLog(LogType.Debug,
                $"HandleItemPickupPacket: claim failed for world coid={worldObjectId.Coid}: no free slot or mergeable stack (SS-31 leak guard)");
            return;
        }

        var inventoryCoid = runtime.AllocateItemCoid();

        var claim = inventory.PickupWorldItem(
            cbid,
            type,
            displayName,
            inventoryCoid,
            new InventoryItemCreator(),
            CurrentCharacter.ObjectId.Coid,
            quantity: 1,
            isMissionItem: simpleObject.PossibleMissionItem);

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

        try
        {
            Managers.MissionCollectProgress.SyncProgressFromInventory(CurrentCharacter, cbid);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "MissionCollectProgress.SyncProgressFromInventory failed cbid={0}: {1}",
                cbid,
                ex.Message);
        }
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
        => LogInventoryOperationOutcome(result);

    /// <summary>
    /// Phase 3: FAILED inventory operations always emit <c>InventoryRequestRejected</c>
    /// (INV-001) so rejections are visible without the debug gate; successful-operation
    /// debug logging stays gated by <see cref="Diagnostics.ServerConfig.InventoryDebugPackets"/>.
    /// </summary>
    internal static void LogInventoryOperationOutcome(InventoryOperationResult result)
    {
        if (result == null)
            return;

        if (IsInventoryOperationRejected(result))
        {
            AutoCore.Utils.Logging.GameLog.Warn(
                "InventoryRequestRejected",
                "INV-001",
                ("Reason", result.LogMessage));
        }

        if (!Diagnostics.ServerConfig.InventoryDebugPackets)
            return;

        if (!string.IsNullOrWhiteSpace(result.LogMessage))
            Logger.WriteLog(LogType.Debug, result.LogMessage);
    }

    /// <summary>True when the result carries a client response packet marked unsuccessful.</summary>
    internal static bool IsInventoryOperationRejected(InventoryOperationResult result)
    {
        if (result?.Packets == null)
            return false;

        foreach (var packet in result.Packets)
        {
            switch (packet)
            {
                case InventoryGrabResponsePacket { WasSuccessful: false }:
                case InventoryDropResponsePacket { WasSuccessful: false }:
                case ItemDropResponsePacket { WasSuccessful: false }:
                case InventoryAddItemResponsePacket { WasSuccessful: false }:
                    return true;
            }
        }

        return false;
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
