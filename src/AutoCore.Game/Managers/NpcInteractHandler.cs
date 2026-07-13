namespace AutoCore.Game.Managers;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Utils;

/// <summary>
/// NPC/world-object interact (UseObject 0x2072) and mission dialog response (0x206E).
/// Data-driven from mission assets / deliver requirements — no hardcoded mission ids.
/// </summary>
public static class NpcInteractHandler
{
    // Client click range DAT_00aaa6fc = 25f (Client_InteractClickPickTarget @ 0x009247b0);
    // small latency slack for C2S lag.
    private const float MaxInteractDistance = 30f;

    // When XZ exceeds MaxInteractDistance but the NPC is a data-driven mission dialog partner
    // (deliver / giver / offerable), still allow within this grace. Client already range-gated
    // at 25f against its body; server pose can lag (motion authority, map-Y vs terrain-Y).
    private const float MaxMissionInteractGrace = 150f;

    /// <summary>
    /// Delay before journal + OnMissionStateChanged after dialog deliver turn-in.
    /// Gives the client time to finish local CompleteObjective / dialog teardown / MSXML UI
    /// loads (and interact FX for new core offers) before we push more mission packets
    /// (mitigates AV @ 0x007B6DB0). Set to 0 in unit tests for synchronous follow-up.
    /// </summary>
    internal static int DialogTurnInFollowupDelayMs { get; set; } = 250;

    /// <summary>
    /// Schedules delayed work. Default: sync when delay≤0, else cancelled Task.Delay.
    /// Tests may replace to capture/flush without sleeping.
    /// Signature: (action, delayMs, cancellationToken).
    /// </summary>
    internal static Action<Action, int, CancellationToken> ScheduleDelayedWork { get; set; }
        = DefaultScheduleDelayedWork;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, CancellationTokenSource>
        PendingDialogTurnInFollowups = new();

    private static Dictionary<int, List<int>> _missionsByNpc;
    private static HashSet<int> _missionGiverCbids;
    private static readonly object IndexLock = new();

    /// <summary>Called when test missions or WAD mission set changes.</summary>
    internal static void InvalidateMissionIndex()
    {
        lock (IndexLock)
        {
            _missionsByNpc = null;
            _missionGiverCbids = null;
        }
    }

    /// <summary>Reset delay/scheduler hooks and cancel pending follow-ups (unit tests).</summary>
    internal static void ResetDialogTurnInFollowupForTests()
    {
        DialogTurnInFollowupDelayMs = 250;
        ScheduleDelayedWork = DefaultScheduleDelayedWork;
        foreach (var kv in PendingDialogTurnInFollowups)
        {
            kv.Value.Cancel();
            kv.Value.Dispose();
        }

        PendingDialogTurnInFollowups.Clear();
        MissionClientSoftPedal.ResetForTests();
    }

    private static void DefaultScheduleDelayedWork(Action action, int delayMs, CancellationToken token)
    {
        if (action == null)
            return;

        if (delayMs <= 0)
        {
            if (!token.IsCancellationRequested)
                action();
            return;
        }

        _ = RunDelayedWorkAsync(action, delayMs, token);
    }

    private static async Task RunDelayedWorkAsync(Action action, int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
                action();
        }
        catch (OperationCanceledException)
        {
            // superseded or test cleanup
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "NpcInteract: delayed dialog turn-in follow-up failed: {0}",
                ex.Message);
        }
    }

    /// <summary>
    /// After dialog deliver: wait briefly, then journal resync + mission-state trigger re-eval.
    /// Server state (quest removed / completed) is already applied before this is scheduled.
    /// </summary>
    private static void ScheduleDialogTurnInFollowup(
        TNLConnection conn,
        Character character,
        int missionId,
        int objectiveId = 0,
        bool forceClientObjectiveComplete = false)
    {
        if (conn == null || character == null)
            return;

        var coid = character.ObjectId.Coid;
        var cts = new CancellationTokenSource();
        if (PendingDialogTurnInFollowups.TryRemove(coid, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        PendingDialogTurnInFollowups[coid] = cts;
        // When forcing 0x2070 (multi-req patrol+deliver), wait at least for GRC soft-pedal so
        // CompleteDynamicObjective does not stack with core-mission interact FX MSXML.
        var delayMs = Math.Max(0, DialogTurnInFollowupDelayMs);
        if (forceClientObjectiveComplete)
            delayMs = Math.Max(delayMs, Math.Max(0, MissionClientSoftPedal.GroupReactionSuppressMs));
        var token = cts.Token;
        var schedule = ScheduleDelayedWork ?? DefaultScheduleDelayedWork;

        schedule(
            () =>
            {
                try
                {
                    if (token.IsCancellationRequested)
                        return;

                    // Drop work if the connection no longer owns this character.
                    if (conn.CurrentCharacter != character)
                        return;

                    // Multi-req objectives (patrol + deliver, Final Exam class): dialog turn-in
                    // completes server-side without 0x2070, but the client can leave AutoPatrol
                    // waypoints active. Force-complete after soft-pedal so journal/waypoints clear.
                    if (forceClientObjectiveComplete && objectiveId > 0)
                    {
                        conn.SendGamePacket(new CompleteDynamicObjectivePacket
                        {
                            MissionId = missionId,
                            ObjectiveId = objectiveId,
                        });
                        Logger.WriteLog(LogType.Debug,
                            "MissionDialogResponse: delayed 0x2070 force-complete mission={0} objective={1}",
                            missionId,
                            objectiveId);
                    }

                    PushJournalMissionList(conn, character);
                    var phaseActivator = character.CurrentVehicle ?? (ClonedObjectBase)character;
                    TriggerManager.Instance.OnMissionStateChanged(phaseActivator);
                    // Keep pad form / suppress original giver after turn-in (personal presence).
                    character.Map?.ReplayMissionWorldSetup(phaseActivator);

                    Logger.WriteLog(LogType.Debug,
                        "MissionDialogResponse: delayed follow-up after deliver mission={0} coid={1} delayMs={2} forceComplete={3}",
                        missionId,
                        coid,
                        delayMs,
                        forceClientObjectiveComplete ? 1 : 0);
                }
                finally
                {
                    if (PendingDialogTurnInFollowups.TryRemove(coid, out var done) && ReferenceEquals(done, cts))
                        cts.Dispose();
                }
            },
            delayMs,
            token);
    }

    /// <summary>
    /// True when the objective has non-deliver requirements (e.g. AutoComplete patrol) that the
    /// client may still track after dialog turn-in, so a delayed 0x2070 is needed to clear UI.
    /// </summary>
    internal static bool ObjectiveNeedsForceClientCompleteAfterDeliver(MissionObjective objective)
        => MissionWorldPhaseRules.NeedsForceClientCompleteAfterDeliver(objective);

    /// <summary>
    /// True when an NPC CBID is involved in any mission as either the giver (<see cref="Mission.NPC"/>)
    /// or a deliver-objective turn-in target (<see cref="ObjectiveRequirementDeliver.NPCTargetCBID"/>).
    /// Data-driven from the mission set — no hardcoded ids. Used to flag interactive NPCs at spawn
    /// so interest management can grant them an extended scope radius.
    /// </summary>
    public static bool IsMissionGiverCbid(int cbid)
    {
        if (cbid <= 0)
            return false;

        EnsureMissionIndex();
        return _missionGiverCbids != null && _missionGiverCbids.Contains(cbid);
    }

    public static void HandleUseObject(TNLConnection conn, UseObjectPacket packet)
    {
        var character = conn?.CurrentCharacter;
        if (character?.Map == null || packet == null)
            return;

        var targetCoid = packet.Target?.Coid ?? -1;
        Logger.WriteLog(LogType.Debug,
            "UseObject: charCoid={0} target={1} objectiveId={2}",
            character.ObjectId.Coid,
            targetCoid,
            packet.ObjectiveId);

        if (targetCoid <= 0)
            return;

        // Personal mission presence: COIDs deleted for this character are not interactable.
        character.MapPresence.EnsureContinent(character.Map.ContinentId);
        if (character.MapPresence.IsSuppressed(targetCoid))
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: rejected suppressed coid={0} for char={1}",
                targetCoid,
                character.ObjectId.Coid);
            return;
        }

        var playerPos = GetPlayerInteractPosition(character);

        // World-object interact (use-item / use-object objectives) before NPC dialog.
        if (TryCompleteUseItemObjective(conn, character, targetCoid, packet.ObjectiveId))
            return;

        // Resolve NPC: live creature, spawn-marker → child, or nearby active deliver target.
        // Client 0x206C Create often uses map-local COIDs while server pad NPCs use global
        // MapNpcIdentity TFIDs — direct GetObjectByCoid then fails; fall back by CBID+range.
        var npc = FindNpcByCoid(character, character.Map, targetCoid)
            ?? TryResolveNearbyDeliverNpc(character, packet.ObjectiveId, playerPos, targetCoid);

        if (npc == null)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: no NPC resolved for target={0} objectiveId={1}",
                targetCoid,
                packet.ObjectiveId);
            return;
        }

        if (character.MapPresence.IsSuppressed(npc.ObjectId.Coid))
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: resolved NPC coid={0} is suppressed for char={1}",
                npc.ObjectId.Coid,
                character.ObjectId.Coid);
            return;
        }

        var npcCbid = npc.CBID;
        if (npcCbid <= 0)
            return;

        var targetPos = GetNpcInteractPosition(npc, character.Map);
        var distXZSq = DistXZSq(playerPos, targetPos);
        var maxSq = MaxInteractDistance * MaxInteractDistance;

        // Prefer building dialog list once: needed both for soft-allow and for send.
        var dialogMissions = BuildDialogMissions(character, npcCbid, packet.ObjectiveId);

        if (distXZSq > maxSq)
        {
            var graceSq = MaxMissionInteractGrace * MaxMissionInteractGrace;
            if (dialogMissions.Count == 0 || distXZSq > graceSq)
            {
                Logger.WriteLog(LogType.Debug,
                    "UseObject: rejected out of range charCoid={0} npc={1} distXZ={2:F1} player={3} target={4} partners={5}",
                    character.ObjectId.Coid,
                    npc.ObjectId.Coid,
                    MathF.Sqrt(distXZSq),
                    playerPos,
                    targetPos,
                    dialogMissions.Count);
                return;
            }

            Logger.WriteLog(LogType.Debug,
                "UseObject: mission partner soft-allow charCoid={0} npc={1} cbid={2} distXZ={3:F1}",
                character.ObjectId.Coid,
                npc.ObjectId.Coid,
                npcCbid,
                MathF.Sqrt(distXZSq));
        }

        if (dialogMissions.Count == 0)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: no dialog missions for NPC cbid={0} objectiveId={1} activeQuests=[{2}]",
                npcCbid,
                packet.ObjectiveId,
                string.Join(',', character.CurrentQuests.Select(q => q.MissionId)));
            return;
        }

        // Client often advances objectives (0x206C / local UI) before the server — e.g. patrol
        // done client-side while ActiveObjectiveSequence is still 0. Reconcile from objectiveId
        // so deliver turn-in and PrepareClientTurnInDialog see the correct active sequence.
        TryReconcileClientObjectiveHint(conn, character, packet.ObjectiveId, npcCbid);

        // Re-build after reconcile so deliver-active sequence is reflected if hint advanced it.
        dialogMissions = BuildDialogMissions(character, npcCbid, packet.ObjectiveId);
        if (dialogMissions.Count == 0)
            return;

        PrepareClientTurnInDialog(conn, character, npcCbid, dialogMissions);
        SendNpcMissionDialog(conn, npc.ObjectId, dialogMissions);
    }

    /// <summary>
    /// Town continents: character on foot. Field/highway: vehicle chassis.
    /// Matches <see cref="TriggerManager.ResolvePlayerTriggerActivator"/> / create-packet UsingVehicle.
    /// </summary>
    internal static Vector3 GetPlayerInteractPosition(Character character)
    {
        var body = TriggerManager.ResolvePlayerTriggerActivator(character);
        return body?.Position ?? character?.Position ?? default;
    }

    /// <summary>
    /// Seated mission NPCs: client range-checks the chassis (object +0x80). Driver.Position is
    /// stamped at spawn and does not track the vehicle — use the owned chassis when present.
    /// </summary>
    internal static Vector3 GetNpcInteractPosition(Creature npc, SectorMap map)
    {
        if (npc == null)
            return default;

        if (map != null)
        {
            foreach (var obj in map.Objects.Values)
            {
                if (obj is Vehicle vehicle && ReferenceEquals(vehicle.Owner, npc))
                    return vehicle.Position;
            }
        }

        return npc.Position;
    }

    internal static float DistXZSq(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// When the client clicks a map-local Create body that has no matching server COID, resolve
    /// an active deliver-turn-in NPC by CBID near the player (Final Exam pad Gunny class).
    /// </summary>
    internal static Creature TryResolveNearbyDeliverNpc(
        Character character,
        int objectiveId,
        Vector3 playerPos,
        long clickedCoid)
    {
        if (character?.Map == null)
            return null;

        // Nearby fallback uses hard interact cap only (not mission grace) so we do not bind
        // a distant same-CBID NPC when the click COID is simply missing.
        var rangeSq = MaxInteractDistance * MaxInteractDistance;
        Creature best = null;
        var bestDist = float.MaxValue;

        foreach (var quest in character.CurrentQuests)
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
                || objective?.Requirements == null)
            {
                continue;
            }

            // Prefer the objective the client named when it belongs to this mission.
            MissionObjective scanObjective = objective;
            if (objectiveId > 0 && objective.ObjectiveId != objectiveId)
            {
                var hinted = AssetManager.Instance.GetObjectiveById(objectiveId);
                if (hinted != null && hinted.QuestId == quest.MissionId)
                    scanObjective = hinted;
                // else keep active objective (client hint may be stale)
            }

            foreach (var deliver in scanObjective.Requirements.OfType<ObjectiveRequirementDeliver>())
            {
                if (!deliver.NPCTargetCompletes || deliver.NPCTargetCBID <= 0)
                    continue;

                foreach (var obj in character.Map.Objects.Values)
                {
                    if (obj is not Creature creature || creature is Character)
                        continue;
                    if (creature.CBID != deliver.NPCTargetCBID || !IsNpc(creature))
                        continue;
                    if (IsSuppressedFor(character, creature.ObjectId.Coid))
                        continue;

                    var dist = DistXZSq(playerPos, GetNpcInteractPosition(creature, character.Map));
                    if (dist > rangeSq || dist >= bestDist)
                        continue;

                    bestDist = dist;
                    best = creature;
                }
            }
        }

        if (best != null)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: resolved deliver NPC cbid={0} coid={1} from click coid={2} (client/server TFID mismatch)",
                best.CBID,
                best.ObjectId.Coid,
                clickedCoid);
        }

        return best;
    }

    /// <summary>
    /// Forward-sync <see cref="CharacterQuest.ActiveObjectiveSequence"/> when the client UseObject
    /// hint names a later objective on an owned mission related to this NPC.
    /// </summary>
    private static void TryReconcileClientObjectiveHint(
        TNLConnection conn,
        Character character,
        int objectiveId,
        int npcCbid)
    {
        if (objectiveId <= 0 || npcCbid <= 0 || character == null)
            return;

        var mission = AssetManager.Instance.GetMissionByObjectiveId(objectiveId);
        var objective = AssetManager.Instance.GetObjectiveById(objectiveId);
        if (mission == null || objective == null)
            return;

        if (!ObjectiveRelatedToNpc(mission, objective, npcCbid))
            return;

        var quest = character.CurrentQuests.FirstOrDefault(q => q.MissionId == mission.Id);
        if (quest == null)
            return;

        if (objective.Sequence <= quest.ActiveObjectiveSequence)
            return;

        var oldSeq = quest.ActiveObjectiveSequence;
        quest.ActiveObjectiveSequence = objective.Sequence;

        if (oldSeq < quest.ObjectiveProgress.Length && oldSeq < quest.ObjectiveMax.Length)
            quest.ObjectiveProgress[oldSeq] = quest.ObjectiveMax[oldSeq];

        Logger.WriteLog(LogType.Debug,
            "UseObjectHint: reconciled mission={0} seq {1} -> {2} objective={3} charCoid={4}",
            mission.Id,
            oldSeq,
            objective.Sequence,
            objectiveId,
            character.ObjectId.Coid);

        conn?.SendGamePacket(new ObjectiveStatePacket
        {
            ObjectiveBitmask = 0u,
            ObjectiveId = objective.ObjectiveId,
        });

        TriggerManager.Instance.OnMissionStateChanged(
            character.CurrentVehicle ?? (ClonedObjectBase)character);
    }

    /// <summary>
    /// Active UseItem requirement: match target by PrimaryCOID and/or PrimaryCBID (and optional
    /// packet objective id). Advances or completes the objective (same path as AutoPatrol).
    /// ProgressTime channeling is deferred — immediate complete for now.
    /// </summary>
    private static bool TryCompleteUseItemObjective(
        TNLConnection conn,
        Character character,
        long targetCoid,
        int packetObjectiveId)
    {
        foreach (var quest in character.CurrentQuests.ToList())
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective))
            {
                continue;
            }

            var useItem = objective.Requirements.OfType<ObjectiveRequirementUseItem>().FirstOrDefault();
            if (useItem == null)
                continue;

            // Optional client hint: UseObject carries matching objective id when known.
            if (packetObjectiveId > 0 && packetObjectiveId != objective.ObjectiveId)
                continue;

            if (!UseItemMatchesTarget(character.Map, useItem, targetCoid))
                continue;

            Logger.WriteLog(LogType.Debug,
                "UseObject: use-item mission={0} seq={1} objective={2} target={3}",
                quest.MissionId,
                quest.ActiveObjectiveSequence,
                objective.ObjectiveId,
                targetCoid);

            LogUseItemIncomplete(useItem, quest, objective, targetCoid);
            AdvanceOrCompleteObjective(conn, character, quest, mission, objective, source: "UseItem");
            return true;
        }

        return false;
    }

    private static void LogUseItemIncomplete(
        ObjectiveRequirementUseItem useItem,
        CharacterQuest quest,
        MissionObjective objective,
        long targetCoid)
    {
        var context =
            $"mission={quest.MissionId} seq={quest.ActiveObjectiveSequence} objective={objective.ObjectiveId} target={targetCoid}";

        if (useItem.ProgressTime > 0)
        {
            IncompleteHandlerLog.Warn(
                "UseItem",
                context,
                $"ProgressTime={useItem.ProgressTime} ignored — interact completes immediately (no channeling/interrupt)",
                "Implement server-side channel timer + ProgressInterruptable; send progress UI; complete only after ProgressTime.");
        }

        if (useItem.RepeatCount > 1)
        {
            IncompleteHandlerLog.Warn(
                "UseItem",
                context,
                $"RepeatCount={useItem.RepeatCount} ignored — single interact finishes the whole objective",
                "Track uses in ObjectiveProgress[FirstStateSlot]; complete only when count reaches RepeatCount; emit ObjectiveState slots.");
        }

        if (useItem.PrimaryDestroy || useItem.SecondaryDestroy)
        {
            IncompleteHandlerLog.Warn(
                "UseItem",
                context,
                "PrimaryDestroy/SecondaryDestroy not applied — no inventory/world item removal",
                "Remove or consume matching inventory/world items when the use succeeds.");
        }

        if (useItem.PrimaryGiveAtStart || useItem.SecondaryGiveAtStart
            || useItem.PrimaryCompletedItem > 0 || useItem.CompletedItem > 0)
        {
            IncompleteHandlerLog.Warn(
                "UseItem",
                context,
                "Give/completed item fields ignored — no item grant on use or objective complete",
                "Grant PrimaryCompletedItem/CompletedItem (and start items) via inventory service when use completes.");
        }

        if (useItem.CompletedMission > 0)
        {
            IncompleteHandlerLog.Warn(
                "UseItem",
                context,
                $"CompletedMission={useItem.CompletedMission} not auto-granted",
                "On use-item objective complete, grant/chain CompletedMission through MissionManager.");
        }
    }

    private static bool UseItemMatchesTarget(SectorMap map, ObjectiveRequirementUseItem useItem, long targetCoid)
    {
        if (useItem.PrimaryItem > 0 && useItem.PrimaryItem == targetCoid)
            return true;

        if (useItem.PrimaryCBID > 0 && MapObjectHasCbid(map, targetCoid, useItem.PrimaryCBID))
            return true;

        return false;
    }

    private static bool MapObjectHasCbid(SectorMap map, long coid, int cbid)
    {
        var live = map.GetObjectByCoid(coid);
        if (live != null)
            return live.CBID == cbid;

        if (map.MapData.Templates.TryGetValue(coid, out var template))
            return template.CBID == cbid;

        return false;
    }

    public static void HandleMissionDialogResponse(TNLConnection conn, MissionDialogResponsePacket packet)
    {
        var character = conn?.CurrentCharacter;
        if (character?.Map == null || packet == null)
            return;

        Logger.WriteLog(LogType.Debug,
            "MissionDialogResponse(0x206E): mission={0} accepted={1} npc={2}",
            packet.MissionId,
            packet.Accepted,
            packet.MissionGiver?.Coid ?? -1);

        var npc = FindNpcByCoid(character, character.Map, packet.MissionGiver?.Coid ?? -1);
        var npcCbid = npc?.CBID ?? 0;

        // Client may echo mission id OR an objective id (same pattern as deliver turn-in).
        var resolvedOfferMissionId = ResolveMissionIdForGrant(packet.MissionId);

        foreach (var missionId in ResolveDialogResponseMissions(character, packet.MissionId, npcCbid))
        {
            if (TryCompleteDeliverFromDialog(conn, character, missionId, npcCbid, npc?.ObjectId))
                return;
        }

        if (resolvedOfferMissionId <= 0)
            return;

        // Do not grant when the packet id is an objective id for an active deliver here.
        if (IsActiveObjectiveIdForDeliver(character, packet.MissionId, npcCbid)
            || IsActiveObjectiveIdForDeliver(character, resolvedOfferMissionId, npcCbid))
            return;

        // Already active: resync journal/objective so the client shows "accepted" even when
        // create-packet restore or a prior grant left UI empty, then re-eval map triggers.
        var activeQuest = character.CurrentQuests.FirstOrDefault(q => q.MissionId == resolvedOfferMissionId);
        if (activeQuest != null)
        {
            Logger.WriteLog(LogType.Debug,
                "MissionDialogResponse: mission {0} already active for charCoid={1} — resync client + re-eval triggers",
                resolvedOfferMissionId,
                character.ObjectId.Coid);
            ResyncActiveMissionToClient(conn, character, activeQuest);
            TriggerManager.Instance.OnMissionStateChanged(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
            return;
        }

        if (!CanOfferMission(character, resolvedOfferMissionId, npcCbid))
        {
            Logger.WriteLog(LogType.Debug,
                "MissionDialogResponse: mission {0} (packet={1}) not offerable for charCoid={2} npcCbid={3}",
                resolvedOfferMissionId,
                packet.MissionId,
                character.ObjectId.Coid,
                npcCbid);
            return;
        }

        GrantMission(conn, character, resolvedOfferMissionId);
    }

    /// <summary>
    /// Map a dialog packet id to a mission id. Retail clients sometimes echo the first
    /// objective id (e.g. 5422) instead of the mission id (3037) on accept.
    /// </summary>
    internal static int ResolveMissionIdForGrant(int packetMissionId)
    {
        if (packetMissionId <= 0)
            return packetMissionId;

        if (AssetManager.Instance.GetMission(packetMissionId) != null)
            return packetMissionId;

        var byObjective = AssetManager.Instance.GetMissionByObjectiveId(packetMissionId);
        return byObjective?.Id ?? packetMissionId;
    }

    /// <summary>Push journal + active objective state for a quest the server already tracks.</summary>
    internal static void ResyncActiveMissionToClient(TNLConnection conn, Character character, CharacterQuest quest)
    {
        if (conn == null || character == null || quest == null)
            return;

        var objective = GetActiveObjective(quest);
        if (objective != null)
        {
            conn.SendGamePacket(new ObjectiveStatePacket
            {
                ObjectiveBitmask = 0u,
                ObjectiveId = objective.ObjectiveId,
            });
        }

        PushJournalMissionList(conn, character);
    }

    private static List<int> ResolveDialogResponseMissions(Character character, int packetMissionId, int npcCbid)
    {
        if (packetMissionId > 0)
        {
            // Prefer direct mission id match.
            if (character.CurrentQuests.Any(q => q.MissionId == packetMissionId))
                return [packetMissionId];

            // Client often echoes active objective id on turn-in OK.
            foreach (var quest in character.CurrentQuests)
            {
                if (!HasDeliverTurnIn(quest, npcCbid))
                    continue;

                var objective = GetActiveObjective(quest);
                if (objective != null && objective.ObjectiveId == packetMissionId)
                    return [quest.MissionId];
            }

            return [packetMissionId];
        }

        var inferred = new List<int>();
        foreach (var quest in character.CurrentQuests)
        {
            if (HasDeliverTurnIn(quest, npcCbid) && !inferred.Contains(quest.MissionId))
                inferred.Add(quest.MissionId);
        }

        return inferred;
    }

    private static bool IsActiveObjectiveIdForDeliver(Character character, int id, int npcCbid)
    {
        foreach (var quest in character.CurrentQuests)
        {
            if (!HasDeliverTurnIn(quest, npcCbid))
                continue;

            var objective = GetActiveObjective(quest);
            if (objective != null && objective.ObjectiveId == id)
                return true;
        }

        return false;
    }

    private static List<int> BuildDialogMissions(Character character, int npcCbid, int objectiveId = -1)
    {
        var missions = new List<int>();

        // 1) Active deliver turn-ins to this NPC
        foreach (var quest in character.CurrentQuests)
        {
            if (HasDeliverTurnIn(quest, npcCbid) && !missions.Contains(quest.MissionId))
                missions.Add(quest.MissionId);
        }

        if (missions.Count > 0)
            return missions;

        // 2) Client objective-id hint (UseObject IDObjective) for an owned mission related to this NPC
        if (TryAddMissionFromObjectiveHint(character, npcCbid, objectiveId, missions) && missions.Count > 0)
            return missions;

        // 3) In-progress missions given by this NPC (status dialog)
        foreach (var quest in character.CurrentQuests)
        {
            if (IsMissionNpcGiver(quest.MissionId, npcCbid) && !missions.Contains(quest.MissionId))
                missions.Add(quest.MissionId);
        }

        if (missions.Count > 0)
            return missions;

        // 4) In-progress missions with a remaining deliver to this NPC (status on turn-in NPCs)
        foreach (var quest in character.CurrentQuests)
        {
            if (HasRemainingDeliverToNpc(quest, npcCbid) && !missions.Contains(quest.MissionId))
                missions.Add(quest.MissionId);
        }

        if (missions.Count > 0)
            return missions;

        // 5) New offers from this NPC (prereqs / level / not active)
        foreach (var missionId in GetOfferableMissions(character, npcCbid))
        {
            if (!missions.Contains(missionId))
                missions.Add(missionId);
        }

        return missions;
    }

    /// <summary>
    /// When the client sends a known objective id, open dialog for that mission if the player owns it
    /// and the objective (or mission giver) relates to this NPC.
    /// </summary>
    private static bool TryAddMissionFromObjectiveHint(
        Character character,
        int npcCbid,
        int objectiveId,
        List<int> missions)
    {
        if (objectiveId <= 0 || npcCbid <= 0)
            return false;

        var mission = AssetManager.Instance.GetMissionByObjectiveId(objectiveId);
        var objective = AssetManager.Instance.GetObjectiveById(objectiveId);
        if (mission == null || objective == null)
            return false;

        if (!character.CurrentQuests.Any(q => q.MissionId == mission.Id))
            return false;

        if (!ObjectiveRelatedToNpc(mission, objective, npcCbid))
            return false;

        if (!missions.Contains(mission.Id))
            missions.Add(mission.Id);

        return true;
    }

    private static bool ObjectiveRelatedToNpc(Mission mission, MissionObjective objective, int npcCbid)
    {
        if (mission.NPC == npcCbid)
            return true;

        return objective.Requirements
            .OfType<ObjectiveRequirementDeliver>()
            .Any(d => d.NPCTargetCompletes && d.NPCTargetCBID == npcCbid);
    }

    /// <summary>
    /// True if any objective at or after the active sequence delivers to this NPC.
    /// Enables status dialog on turn-in NPCs before the deliver sequence is active.
    /// </summary>
    private static bool HasRemainingDeliverToNpc(CharacterQuest quest, int npcCbid)
    {
        if (npcCbid <= 0)
            return false;

        var mission = AssetManager.Instance.GetMission(quest.MissionId);
        if (mission?.Objectives == null)
            return false;

        foreach (var objective in mission.Objectives.Values)
        {
            if (objective.Sequence < quest.ActiveObjectiveSequence)
                continue;

            if (objective.Requirements
                .OfType<ObjectiveRequirementDeliver>()
                .Any(d => d.NPCTargetCompletes && d.NPCTargetCBID == npcCbid))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMissionNpcGiver(int missionId, int npcCbid)
    {
        var mission = AssetManager.Instance.GetMission(missionId);
        return mission != null && mission.NPC == npcCbid;
    }

    private static List<int> GetOfferableMissions(Character character, int npcCbid)
    {
        EnsureMissionIndex();
        var result = new List<int>();

        if (npcCbid <= 0 || _missionsByNpc == null)
            return result;

        if (!_missionsByNpc.TryGetValue(npcCbid, out var missionIds))
            return result;

        foreach (var missionId in missionIds)
        {
            if (CanOfferMission(character, missionId, npcCbid))
                result.Add(missionId);
        }

        return result;
    }

    private static bool CanOfferMission(Character character, int missionId, int npcCbid)
    {
        var mission = AssetManager.Instance.GetMission(missionId);
        if (mission == null)
            return false;

        if (character.CurrentQuests.Any(q => q.MissionId == missionId))
            return false;

        if (character.CompletedMissionIds.Contains(missionId) && mission.IsRepeatable == 0)
            return false;

        // Giver NPC must match when mission declares one (npcCbid 0 = skip check for grant path edge cases).
        if (npcCbid > 0 && mission.NPC > 0 && mission.NPC != npcCbid)
            return false;

        if (mission.ReqLevelMin > 0 && character.Level < mission.ReqLevelMin)
            return false;

        if (mission.ReqLevelMax > 0 && character.Level > mission.ReqLevelMax)
            return false;

        if (mission.Continent > 0 && character.Map != null && character.Map.ContinentId != mission.Continent)
            return false;

        if (mission.ReqMissionId != null)
        {
            foreach (var reqId in mission.ReqMissionId)
            {
                if (reqId <= 0)
                    continue;

                if (!character.CompletedMissionIds.Contains(reqId))
                    return false;
            }
        }

        return true;
    }

    private static void EnsureMissionIndex()
    {
        if (_missionsByNpc != null)
            return;

        lock (IndexLock)
        {
            if (_missionsByNpc != null)
                return;

            var index = new Dictionary<int, List<int>>();
            var giverCbids = new HashSet<int>();
            foreach (var mission in AssetManager.Instance.GetAllMissions())
            {
                if (mission == null)
                    continue;

                if (mission.NPC > 0)
                {
                    if (!index.TryGetValue(mission.NPC, out var list))
                    {
                        list = new List<int>();
                        index[mission.NPC] = list;
                    }

                    if (!list.Contains(mission.Id))
                        list.Add(mission.Id);

                    giverCbids.Add(mission.NPC);
                }

                // Deliver turn-in NPCs are also interactive mission givers for scope purposes.
                if (mission.Objectives == null)
                    continue;

                foreach (var objective in mission.Objectives.Values)
                {
                    if (objective?.Requirements == null)
                        continue;

                    foreach (var requirement in objective.Requirements.OfType<ObjectiveRequirementDeliver>())
                    {
                        if (requirement.NPCTargetCompletes && requirement.NPCTargetCBID > 0)
                            giverCbids.Add(requirement.NPCTargetCBID);
                    }
                }
            }

            _missionsByNpc = index;
            _missionGiverCbids = giverCbids;
            Logger.WriteLog(LogType.Debug,
                "NpcInteract: mission index built for {0} NPC keys ({1} mission-giver CBIDs)",
                index.Count,
                giverCbids.Count);
        }
    }

    internal static void GrantMission(TNLConnection conn, Character character, int missionId)
    {
        if (character.CurrentQuests.Any(q => q.MissionId == missionId))
        {
            // Duplicate grant path: still resync UI (journal may be empty after bad restore).
            var existing = character.CurrentQuests.First(q => q.MissionId == missionId);
            ResyncActiveMissionToClient(conn, character, existing);
            // Idempotent: top up missing GiveItemOnStart cargo without duplicating.
            MissionCargoService.EnsureAndSend(character, existing);
            TriggerManager.Instance.OnMissionStateChanged(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
            return;
        }

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        MissionPersistence.Instance.OnQuestChanged(character, quest);

        // Seed client objective state so journal can show the new objective.
        ResyncActiveMissionToClient(conn, character, quest);

        // Deliver GiveItemOnStart → mission cargo (server-authoritative; idempotent).
        MissionCargoService.EnsureAndSend(character, quest);

        Logger.WriteLog(LogType.Debug,
            "MissionDialogResponse: granted mission {0} to charCoid={1}",
            missionId,
            character.ObjectId.Coid);

        // Mission-computed logic vars (type 11/12) may unlock gates / remote triggers.
        TriggerManager.Instance.OnMissionStateChanged(character.CurrentVehicle ?? (ClonedObjectBase)character);
    }

    /// <summary>
    /// Force-complete an active mission: drop from CurrentQuests, record completed, persist,
    /// and push CompleteDynamicObjective + journal to the client. Used by /completeMission.
    /// </summary>
    internal static void ForceCompleteMission(TNLConnection conn, Character character, int missionId)
    {
        var quest = character.CurrentQuests.FirstOrDefault(q => q.MissionId == missionId);
        if (quest == null)
            return;

        var objective = GetActiveObjective(quest);
        var objectiveId = objective?.ObjectiveId ?? 0;

        character.CurrentQuests.Remove(quest);
        character.CompletedMissionIds.Add(missionId);
        MissionPersistence.Instance.OnMissionCompleted(character.ObjectId.Coid, missionId);

        var mission = AssetManager.Instance.GetMission(missionId);
        ApplyMissionCompleteRewards(character, mission, objective, source: "ForceCompleteMission");

        if (conn != null)
        {
            conn.SendGamePacket(new CompleteDynamicObjectivePacket
            {
                MissionId = missionId,
                ObjectiveId = objectiveId,
            });
            PushJournalMissionList(conn, character);
        }

        Logger.WriteLog(LogType.Debug,
            "ForceCompleteMission: completed mission {0} for charCoid={1}",
            missionId,
            character.ObjectId.Coid);

        TriggerManager.Instance.OnMissionStateChanged(
            character.CurrentVehicle ?? (ClonedObjectBase)character);
    }

    private static bool HasDeliverTurnIn(CharacterQuest quest, int npcCbid)
    {
        if (npcCbid <= 0)
            return false;

        var deliver = GetActiveDeliver(quest, npcCbid);
        return deliver != null;
    }

    private static ObjectiveRequirementDeliver GetActiveDeliver(CharacterQuest quest, int npcCbid)
    {
        var objective = GetActiveObjective(quest);
        if (objective == null)
            return null;

        return objective.Requirements
            .OfType<ObjectiveRequirementDeliver>()
            .FirstOrDefault(d => d.NPCTargetCompletes && d.NPCTargetCBID == npcCbid);
    }

    private static MissionObjective GetActiveObjective(CharacterQuest quest)
    {
        var mission = AssetManager.Instance.GetMission(quest.MissionId);
        if (mission == null)
            return null;

        if (!mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective))
            return null;

        return objective;
    }

    private static void PrepareClientTurnInDialog(
        TNLConnection conn,
        Character character,
        int npcCbid,
        IReadOnlyList<int> dialogMissions)
    {
        foreach (var missionId in dialogMissions)
        {
            var quest = character.CurrentQuests.FirstOrDefault(q => q.MissionId == missionId);
            if (quest == null)
                continue;

            var deliver = GetActiveDeliver(quest, npcCbid);
            if (deliver == null)
                continue;

            var objective = GetActiveObjective(quest);
            if (objective == null)
                continue;

            var slot = deliver.FirstStateSlot;
            if (slot >= ObjectiveStatePacket.SlotCount)
                continue;

            var packet = new ObjectiveStatePacket
            {
                ObjectiveBitmask = 0u,
                ObjectiveId = objective.ObjectiveId,
            };
            packet.SlotProgress[slot] = 1.0f;

            var seq = quest.ActiveObjectiveSequence;
            if (seq < quest.ObjectiveProgress.Length && seq < quest.ObjectiveMax.Length)
                quest.ObjectiveProgress[seq] = quest.ObjectiveMax[seq];

            conn.SendGamePacket(packet);
        }
    }

    private static void SendNpcMissionDialog(
        TNLConnection conn,
        TFID npcTfid,
        IReadOnlyList<int> missionIds)
    {
        var packet = new NpcMissionDialogPacket
        {
            NpcTfid = npcTfid ?? new TFID(),
        };

        foreach (var missionId in missionIds)
            packet.MissionIds.Add(missionId);

        conn.SendGamePacket(packet);

        Logger.WriteLog(LogType.Debug,
            "NpcInteract: NpcMissionDialog(0x206D) npc={0} missions=[{1}]",
            npcTfid?.Coid ?? -1,
            string.Join(',', missionIds));
    }

    private static bool TryCompleteDeliverFromDialog(
        TNLConnection conn,
        Character character,
        int missionId,
        int npcCbid,
        TFID npcTfid)
    {
        var quest = character.CurrentQuests.FirstOrDefault(q => q.MissionId == missionId);
        if (quest == null || !HasDeliverTurnIn(quest, npcCbid))
            return false;

        var objective = GetActiveObjective(quest);
        var objectiveId = objective?.ObjectiveId ?? 0;

        var mission = AssetManager.Instance.GetMission(missionId);
        var hasLaterObjectives = mission?.Objectives.Values.Any(o =>
            objective != null && o.Sequence > quest.ActiveObjectiveSequence) == true;

        if (hasLaterObjectives)
        {
            IncompleteHandlerLog.Warn(
                "DeliverTurnIn",
                $"mission={missionId} objective={objectiveId} npcCbid={npcCbid} seq={quest.ActiveObjectiveSequence}",
                "Deliver turn-in removes the entire quest even though later objective sequences exist",
                "Use AdvanceOrCompleteObjective (shared path): advance sequence when not final; only remove quest + CompletedMissionIds on last objective; apply rewards once.");
        }
        else
        {
            IncompleteHandlerLog.Warn(
                "DeliverTurnIn",
                $"mission={missionId} objective={objectiveId} npcCbid={npcCbid}",
                "Deliver complete bypasses AdvanceOrCompleteObjective — no shared reward/persist/ObjectiveState path",
                "Route deliver completion through MissionManager/AdvanceOrCompleteObjective for one generic complete pipeline.");
        }

        // TakeItemAtEnd before completing so cargo state matches turn-in.
        MissionCargoService.TakeAndSend(character, quest, objective);

        character.CurrentQuests.Remove(quest);
        character.CompletedMissionIds.Add(missionId);
        MissionPersistence.Instance.OnMissionCompleted(character.ObjectId.Coid, missionId);

        // Client already applied local CompleteObjective XP VFX; server must still persist rewards
        // without re-sending GiveXP delta (would double-count). If the grant levels the character,
        // GiveXp still sends CharacterLevel so mid-session level updates without relog.
        // No 0x2070 — soft-pedal below.
        ApplyMissionCompleteRewards(
            character,
            mission,
            objective,
            source: "DeliverTurnIn",
            notifyClient: false);

        // Do NOT send CompleteDynamicObjective (0x2070) on dialog turn-in.
        // Client already ran CVOGReaction_CompleteObjective; that also unlocks next CoreMission
        // offers and loads interact FX (interact_npc_available_new_mission_core → NDSpecialFX XML).
        // Stacking 0x2070 / journal / GroupReactionCall (0x206C) during that window → AV @ 0x007B6DB0.
        // Soft-pedal: no 0x2070, suppress 0x206C briefly, defer journal + OnMissionStateChanged.
        // Server-driven completes (kill/patrol/useitem) still use AdvanceOrCompleteObjective → 0x2070.
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);

        var forceClientComplete = ObjectiveNeedsForceClientCompleteAfterDeliver(objective);
        Logger.WriteLog(LogType.Debug,
            "MissionDialogResponse: completed deliver mission={0} objective={1} npcCbid={2} (immediate 0x2070={3}; follow-up forceComplete={4}; GRC suppress {5}ms)",
            missionId,
            objectiveId,
            npcCbid,
            0,
            forceClientComplete ? 1 : 0,
            MissionClientSoftPedal.GroupReactionSuppressMs);

        ScheduleDialogTurnInFollowup(
            conn,
            character,
            missionId,
            objectiveId,
            forceClientObjectiveComplete: forceClientComplete);

        // Do not auto-open follow-up offer dialog — player re-interacts (UseObject).
        if (npcCbid > 0)
        {
            var followUps = GetOfferableMissions(character, npcCbid);
            if (followUps.Count > 0)
            {
                Logger.WriteLog(LogType.Debug,
                    "MissionDialogResponse: follow-up offers [{0}] unlocked after {1} — available on next NPC interact",
                    string.Join(',', followUps),
                    missionId);
            }
        }

        return true;
    }

    /// <summary>
    /// C2S AutoPatrol (0x20B3): client is within auto-complete range of a patrol waypoint.
    /// Match packet target to the active AutoComplete patrol requirement, verify range, then
    /// advance the objective sequence or complete the mission.
    /// </summary>
    public static void HandleAutoPatrol(TNLConnection conn, AutoPatrolPacket packet)
    {
        if (conn == null || packet == null)
            return;

        var character = conn.CurrentCharacter;
        if (character?.Map == null)
            return;

        // Town: character on foot; field/highway: vehicle chassis (same as UseObject / triggers).
        var playerPos = GetPlayerInteractPosition(character);
        var activator = TriggerManager.ResolvePlayerTriggerActivator(character) ?? (ClonedObjectBase)character;

        var targetCoid = packet.Target?.Coid ?? -1;
        if (targetCoid <= 0)
            return;

        foreach (var quest in character.CurrentQuests.ToList())
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective))
            {
                continue;
            }

            var patrol = objective.Requirements.OfType<ObjectiveRequirementPatrol>().FirstOrDefault();
            if (patrol == null || !patrol.AutoComplete)
                continue;

            if (!PatrolListsTarget(patrol, targetCoid))
                continue;

            if (!TryGetWorldPosition(character.Map, targetCoid, out var targetPos))
                continue;

            var radius = patrol.AutoCompleteDistance > 0f ? patrol.AutoCompleteDistance : 25f;
            if (playerPos.DistSq(targetPos) > radius * radius)
                continue;

            // Patrol + deliver on same objective (Final Exam class): reaching the pad waypoint
            // must not finish the mission — NPC deliver still required. Client sends AutoPatrol
            // every tick while in volume; EnsureDeliverTurnInNpc is idempotent after first setup.
            if (ObjectiveHasBlockingSiblingRequirements(objective, RequirementType.Patrol))
            {
                foreach (var deliver in objective.Requirements.OfType<ObjectiveRequirementDeliver>())
                {
                    if (!deliver.NPCTargetCompletes || deliver.NPCTargetCBID <= 0)
                        continue;

                    // Skip all work (and logging) once pad NPC is live and client was notified.
                    if (character.Map != null
                        && character.MapPresence.IsDeliverTurnInReady(deliver.NPCTargetCBID)
                        && character.Map.MapHasPresentEntityWithCbidForTests(
                            character, deliver.NPCTargetCBID))
                    {
                        continue;
                    }

                    Logger.WriteLog(LogType.Debug,
                        "AutoPatrol: patrol target={0} mission={1} seq={2} — sibling deliver; ensuring pad NPC cbid={3} once",
                        targetCoid,
                        quest.MissionId,
                        quest.ActiveObjectiveSequence,
                        deliver.NPCTargetCBID);

                    character.Map?.EnsureDeliverTurnInNpc(activator, deliver.NPCTargetCBID);
                }

                return;
            }

            LogPatrolIncomplete(patrol, quest, objective, targetCoid);
            AdvanceOrCompleteObjective(conn, character, quest, mission, objective, source: "AutoPatrol");
            return;
        }
    }

    /// <summary>
    /// True when the objective has other requirements that still need a separate server event
    /// before the objective may advance/complete (e.g. deliver turn-in alongside patrol).
    /// </summary>
    internal static bool ObjectiveHasBlockingSiblingRequirements(
        MissionObjective objective,
        RequirementType satisfiedType)
        => MissionWorldPhaseRules.HasBlockingDeliverSibling(objective, satisfiedType);

    private static int CountPatrolTargets(ObjectiveRequirementPatrol patrol)
    {
        var count = Math.Max(patrol.TargetCount, 0);
        if (count == 0)
        {
            for (var i = 0; i < patrol.GenericTargets.Length; i++)
            {
                if (patrol.GenericTargets[i] > 0)
                    count++;
            }
        }

        return count;
    }

    private static void LogPatrolIncomplete(
        ObjectiveRequirementPatrol patrol,
        CharacterQuest quest,
        MissionObjective objective,
        long targetCoid)
    {
        var listed = CountPatrolTargets(patrol);
        var context =
            $"mission={quest.MissionId} seq={quest.ActiveObjectiveSequence} objective={objective.ObjectiveId} target={targetCoid} listedTargets={listed}";

        if (listed > 1)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                "Multi-waypoint patrol: any listed target currently completes the entire objective (no per-point progress)",
                "Track visited GenericTargets (Sequential order if Sequential=true); only AdvanceOrComplete when all points (or current lap) are done; update ObjectiveState slots/bitmask per FirstStateSlot.");
        }

        if (patrol.Laps > 1)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                $"Laps={patrol.Laps} ignored — single pass completes the objective",
                "Maintain lap counter on CharacterQuest progress; require Laps full circuits before complete.");
        }

        if (patrol.Sequential && listed > 1)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                "Sequential=true ignored — targets may be hit in any order",
                "Enforce GenericTargets order; reject or ignore out-of-order AutoPatrol until previous COID cleared.");
        }

        if (patrol.AutoFail)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                $"AutoFail=true (distance={patrol.AutoFailDistance}) not monitored",
                "On movement/tick, fail mission or objective when player leaves AutoFailDistance from active route.");
        }

        if (patrol.ContinentId > 0)
        {
            IncompleteHandlerLog.Warn(
                "AutoPatrol",
                context,
                $"ContinentCBID={patrol.ContinentId} not validated against current map",
                "Reject AutoPatrol progress when character map continent does not match requirement ContinentId.");
        }
    }

    private static bool PatrolListsTarget(ObjectiveRequirementPatrol patrol, long targetCoid)
    {
        var count = Math.Max(patrol.TargetCount, 0);
        if (count == 0)
            count = patrol.GenericTargets.Length;

        for (var i = 0; i < count && i < patrol.GenericTargets.Length; i++)
        {
            if (patrol.GenericTargets[i] == targetCoid)
                return true;
        }

        return false;
    }

    private static bool TryGetWorldPosition(SectorMap map, long coid, out Vector3 position)
    {
        var live = map.GetObjectByCoid(coid);
        if (live != null)
        {
            position = live.Position;
            return true;
        }

        if (map.MapData.Templates.TryGetValue(coid, out var template)
            && template is EntityTemplates.GraphicsObjectTemplate graphics)
        {
            position = graphics.Location.ToVector3();
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// Shared objective advance/complete path for <b>server-driven</b> finishes
    /// (UseItem, AutoPatrol, Kill, …). Sends CompleteDynamicObjective (0x2070) so the client
    /// force-completes and retargets UI. Dialog deliver turn-in must <b>not</b> use this packet
    /// (see <see cref="TryCompleteDeliverFromDialog"/>) — the client already completed locally.
    /// </summary>
    internal static void AdvanceOrCompleteObjective(
        TNLConnection conn,
        Character character,
        CharacterQuest quest,
        Mission mission,
        MissionObjective objective,
        string source = "Objective")
    {
        // Stale references after complete/abandon must not re-advance, re-persist, or re-reward.
        // List membership is reference-based; callers hold the live CharacterQuest instance.
        if (character == null || quest == null || mission == null || objective == null)
            return;
        if (!character.CurrentQuests.Contains(quest))
        {
            Logger.WriteLog(LogType.Debug,
                "AdvanceOrCompleteObjective: ignoring stale quest mission={0} source={1} coid={2}",
                quest.MissionId,
                source,
                character.ObjectId.Coid);
            return;
        }

        var seq = quest.ActiveObjectiveSequence;
        var hasNext = mission.Objectives.Values.Any(o => o.Sequence > seq);
        var context =
            $"source={source} mission={quest.MissionId} seq={seq} objective={objective.ObjectiveId} xp={objective.XP} credits={objective.Credits}";

        // Multi-requirement / multi-count objectives: we finish the whole objective in one call.
        if (objective.Requirements.Count > 1)
        {
            IncompleteHandlerLog.Warn(
                "AdvanceOrCompleteObjective",
                context,
                $"Requirements.Count={objective.Requirements.Count} — all requirements treated as satisfied without evaluation",
                "Evaluate each requirement type to completion; only advance when every requirement (or CompleteCount) is met.");
        }

        if (objective.CompleteCount > 1)
        {
            IncompleteHandlerLog.Warn(
                "AdvanceOrCompleteObjective",
                context,
                $"CompleteCount={objective.CompleteCount} ignored — single event finishes the objective",
                "Increment ObjectiveProgress[seq] per event; emit ObjectiveState; complete only at CompleteCount.");
        }

        // Server state transitions must succeed without a connection (disconnect mid-complete).
        conn?.SendGamePacket(new CompleteDynamicObjectivePacket
        {
            MissionId = quest.MissionId,
            ObjectiveId = objective.ObjectiveId,
        });

        // Take mission cargo for the finishing objective before advance/complete.
        MissionCargoService.TakeAndSend(character, quest, objective);

        if (hasNext)
        {
            var nextSeq = mission.Objectives.Values
                .Where(o => o.Sequence > seq)
                .Min(o => o.Sequence);
            quest.ActiveObjectiveSequence = nextSeq;
            if (seq < quest.ObjectiveProgress.Length)
                quest.ObjectiveProgress[seq] = quest.ObjectiveMax[seq];
            MissionPersistence.Instance.OnQuestChanged(character, quest);

            Logger.WriteLog(LogType.Debug,
                "{0}: advanced mission={1} seq {2} -> {3} objective={4}",
                source,
                quest.MissionId,
                seq,
                nextSeq,
                objective.ObjectiveId);

            var nextObjective = GetActiveObjective(quest);
            if (nextObjective != null)
            {
                IncompleteHandlerLog.Warn(
                    "AdvanceOrCompleteObjective",
                    context + $" nextObjective={nextObjective.ObjectiveId}",
                    "ObjectiveState sent with Bitmask=0 and SlotProgress all 0 — client partial progress/UI may be wrong",
                    "Build bitmask + FirstStateSlot floats from active requirement progress; map ObjectiveId correctly for client hash lookup.");

                conn?.SendGamePacket(new ObjectiveStatePacket
                {
                    ObjectiveBitmask = 0u,
                    ObjectiveId = nextObjective.ObjectiveId,
                });
            }

            // New active objective may GiveItemOnStart (deliver cargo).
            MissionCargoService.EnsureAndSend(character, quest);

            if (objective.XP != 0 || objective.Credits != 0 || objective.SkillPoints != 0 || objective.AttribPoints != 0)
            {
                IncompleteHandlerLog.Warn(
                    "AdvanceOrCompleteObjective",
                    context,
                    "Per-objective XP/credits/skill/attrib rewards not applied on advance",
                    "Apply MissionObjective reward fields (and mission-level rewards on final complete) via character economy APIs.");
            }

            if (conn != null)
                PushJournalMissionList(conn, character);
            var phaseActivator = character.CurrentVehicle ?? (ClonedObjectBase)character;
            TriggerManager.Instance.OnMissionStateChanged(phaseActivator);
            // Kill→deliver (or any advance): re-apply Create for new active deliver/kill targets
            // even if spawn TriggerEvents bookkeeping was lost.
            character.Map?.ReplayMissionWorldSetup(phaseActivator);
            return;
        }

        character.CurrentQuests.Remove(quest);
        character.CompletedMissionIds.Add(quest.MissionId);
        MissionPersistence.Instance.OnMissionCompleted(character.ObjectId.Coid, quest.MissionId);

        Logger.WriteLog(LogType.Debug,
            "{0}: completed mission={1} objective={2}",
            source,
            quest.MissionId,
            objective.ObjectiveId);

        ApplyMissionCompleteRewards(character, mission, objective, source);

        if (conn != null)
            PushJournalMissionList(conn, character);
        var completeActivator = character.CurrentVehicle ?? (ClonedObjectBase)character;
        TriggerManager.Instance.OnMissionStateChanged(completeActivator);
        // Post-complete pad form / giver suppress (personal presence).
        character.Map?.ReplayMissionWorldSetup(completeActivator);
    }

    /// <summary>
    /// Server-authoritative mission complete rewards (XP + credits + skill/attrib pools).
    /// Shared by dialog deliver turn-in (no 0x2070), <see cref="AdvanceOrCompleteObjective"/>,
    /// and <see cref="ForceCompleteMission"/>.
    /// </summary>
    /// <param name="notifyClient">
    /// False for dialog deliver turn-in (client already applied local CompleteObjective XP/credits).
    /// True for server-driven completes that need GiveXP / GiveCredits packets.
    /// </param>
    internal static void ApplyMissionCompleteRewards(
        Character character,
        Mission mission,
        MissionObjective objective,
        string source = "MissionComplete",
        bool notifyClient = true)
    {
        if (character == null)
            return;

        // Prefer the finishing objective; if null, use last sequence on the mission template.
        if (objective == null && mission?.Objectives != null && mission.Objectives.Count > 0)
        {
            var maxSeq = mission.Objectives.Values.Max(o => o.Sequence);
            mission.Objectives.TryGetValue(maxSeq, out objective);
        }

        if (mission == null || objective == null)
        {
            Logger.WriteLog(LogType.Error,
                "ApplyMissionCompleteRewards: missing mission/objective source={0} char={1}",
                source,
                character.ObjectId.Coid);
            return;
        }

        try
        {
            var xpAmount = Experience.ExperienceService.Instance.ComputeMissionXp(mission, objective);
            Experience.GiveXpResult xpResult = null;

            if (xpAmount != 0)
            {
                xpResult = Experience.ExperienceService.Instance.GiveXp(
                    character,
                    xpAmount,
                    Experience.XpSource.Mission,
                    levelHint: -1,
                    notifyClient: notifyClient);

                if (xpResult == null || !xpResult.Success)
                {
                    Logger.WriteLog(LogType.Error,
                        "Mission reward XP failed source={0} mission={1} obj={2} amount={3} msg={4}",
                        source,
                        mission.Id,
                        objective.ObjectiveId,
                        xpAmount,
                        xpResult?.Message ?? "null");
                }
                else
                {
                    Logger.WriteLog(LogType.Network,
                        "Mission reward: source={0} coid={1} mission={2} obj={3} xp={4} total={5} level={6} notify={7}",
                        source,
                        character.ObjectId.Coid,
                        mission.Id,
                        objective.ObjectiveId,
                        xpResult.AppliedAmount,
                        xpResult.TotalExperience,
                        xpResult.Level,
                        notifyClient);
                }
            }
            else
            {
                Logger.WriteLog(LogType.Debug,
                    "Mission reward: source={0} mission={1} obj={2} xpAmount=0 (index={3} targetLevel={4})",
                    source,
                    mission.Id,
                    objective.ObjectiveId,
                    objective.XPIndex,
                    mission.TargetLevel);
            }

            // Credits (client FUN_0059DF20 on final complete). Always persist on server.
            // Do NOT send GiveCredits (0x205E) here: dialog CompleteObjective and S2C 0x2070
            // already add the delta client-side; a second GiveCredits would double-count.
            // Always push absolute CharacterLevel (0x2017) so the money HUD matches the
            // server balance (CompleteObjective does not always refresh credit UI).
            var creditAmount = Experience.ExperienceService.Instance.ComputeMissionCredits(mission, objective);
            if (creditAmount != 0)
            {
                try
                {
                    var creditResult = character.Inventory.AddCredits(character, creditAmount);
                    SyncMissionCreditsToClient(character, creditResult.NewBalance);

                    Logger.WriteLog(LogType.Network,
                        "Mission reward credits: source={0} coid={1} mission={2} obj={3} amount={4} balance={5} notify={6}",
                        source,
                        character.ObjectId.Coid,
                        mission.Id,
                        objective.ObjectiveId,
                        creditResult.AppliedDelta,
                        creditResult.NewBalance,
                        notifyClient);
                }
                catch (Exception creditEx)
                {
                    Logger.WriteLog(LogType.Error,
                        "Mission reward credits failed source={0} mission={1} obj={2} amount={3}: {4}",
                        source,
                        mission.Id,
                        objective.ObjectiveId,
                        creditAmount,
                        creditEx.Message);
                }
            }

            var poolsChanged = false;
            if (objective.SkillPoints != 0)
            {
                character.SetSkillPoints((short)Math.Min(short.MaxValue, character.SkillPoints + objective.SkillPoints));
                poolsChanged = true;
            }

            if (objective.AttribPoints != 0)
            {
                character.SetAttributePoints((short)Math.Min(short.MaxValue, character.AttributePoints + objective.AttribPoints));
                poolsChanged = true;
            }

            // Persist pools when XP was zero (GiveXp already persists when amount != 0).
            // Use ExperienceService.Persistence so unit tests can inject RecordingProgressPersistence.
            if (poolsChanged && xpAmount == 0 && character.ObjectId.Coid > 0)
            {
                var xpSvc = Experience.ExperienceService.Instance;
                if (xpSvc.PersistOnGrant)
                {
                    xpSvc.Persistence.SaveProgress(
                        character.ObjectId.Coid,
                        character.ToProgressSnapshot());
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "ApplyMissionCompleteRewards failed source={0} mission={1}: {2}",
                source,
                mission.Id,
                ex.Message);
        }
    }

    /// <summary>
    /// Absolute money UI sync after mission credit grant (CharacterLevel Currency field).
    /// Extracted for tests; no-op without an owning connection.
    /// </summary>
    internal static void SyncMissionCreditsToClient(Character character, long absoluteCredits)
    {
        if (character?.OwningConnection == null)
            return;

        var packet = CurrencySync.CreateAbsoluteCurrencyPacket(character, absoluteCredits);
        character.OwningConnection.SendGamePacket(packet);
    }

    internal static void PushJournalMissionList(TNLConnection conn, Character character)
    {
        conn.SendGamePacket(new ConvoyMissionsResponsePacket
        {
            CurrentQuests = character.CurrentQuests.ToList()
        });
    }

    private static Creature FindNpcByCoid(Character character, SectorMap map, long coid)
    {
        if (map == null || coid <= 0)
            return null;

        if (character != null)
        {
            character.MapPresence.EnsureContinent(map.ContinentId);
            if (character.MapPresence.IsSuppressed(coid))
                return null;
        }

        var obj = map.GetObjectByCoid(coid);
        if (obj is Creature creature && IsNpc(creature)
            && !IsSuppressedFor(character, creature.ObjectId.Coid))
            return creature;

        // Dialog may send vehicle TFID for a seated NPC.
        if (obj is Vehicle vehicle)
        {
            var driver = vehicle.Owner as Creature ?? vehicle.GetSuperCharacter(false);
            if (driver != null && driver is not Character && IsNpc(driver)
                && !IsSuppressedFor(character, driver.ObjectId.Coid)
                && !IsSuppressedFor(character, vehicle.ObjectId.Coid))
                return driver;
        }

        // Or spawn-point COID (map template id) while the live child has a different global COID.
        if (obj is SpawnPoint spawn && spawn.HasLiveSpawn())
        {
            if (IsSuppressedFor(character, spawn.LastSpawnedCoid))
                return null;

            var child = map.GetObjectByCoid(spawn.LastSpawnedCoid);
            if (child is Creature childNpc && IsNpc(childNpc)
                && !IsSuppressedFor(character, childNpc.ObjectId.Coid))
                return childNpc;
            if (child is Vehicle childVehicle)
            {
                var driver = childVehicle.Owner as Creature ?? childVehicle.GetSuperCharacter(false);
                if (driver != null && driver is not Character && IsNpc(driver)
                    && !IsSuppressedFor(character, driver.ObjectId.Coid))
                    return driver;
            }
        }

        // Fallback: creature that still lists this coid as spawn owner (spawn marker deleted).
        foreach (var kvp in map.Objects)
        {
            if (kvp.Value is Creature owned
                && owned.SpawnOwner == coid
                && IsNpc(owned)
                && !IsSuppressedFor(character, owned.ObjectId.Coid))
            {
                return owned;
            }
        }

        return null;
    }

    static bool IsSuppressedFor(Character character, long coid)
        => character != null && coid > 0 && character.MapPresence.IsSuppressed(coid);

    private static bool IsNpc(Creature creature)
    {
        if (creature == null)
            return false;

        if (creature.CloneBaseObject is CloneBaseCreature cb)
            return cb.CreatureSpecific.IsNPC != 0;

        // Unit tests / objects without loaded clonebase: CBID override implies interactable NPC.
        return creature.CBID > 0;
    }

}
