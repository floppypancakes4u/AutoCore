namespace AutoCore.Game.Managers;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Entities;
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
    // Client click range ~25f (DAT_00aaa6fc); allow small latency slack.
    private const float MaxInteractDistance = 30f;

    private static Dictionary<int, List<int>> _missionsByNpc;
    private static readonly object IndexLock = new();

    /// <summary>Called when test missions or WAD mission set changes.</summary>
    internal static void InvalidateMissionIndex()
    {
        lock (IndexLock)
            _missionsByNpc = null;
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

        // Prefer live object; fall back to map template pose for client-only world objects.
        var targetObj = character.Map.GetObjectByCoid(targetCoid);
        if (!TryGetWorldPosition(character.Map, targetCoid, out var targetPos))
        {
            Logger.WriteLog(LogType.Debug, "UseObject: no object/template for coid {0}", targetCoid);
            return;
        }

        var playerPos = character.CurrentVehicle?.Position ?? character.Position;
        if (playerPos.DistSq(targetPos) > MaxInteractDistance * MaxInteractDistance)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: rejected out of range charCoid={0} target={1}",
                character.ObjectId.Coid,
                targetCoid);
            return;
        }

        // World-object interact (use-item / use-object objectives) before NPC dialog.
        if (TryCompleteUseItemObjective(conn, character, targetCoid, packet.ObjectiveId))
            return;

        if (targetObj is not Creature npc || !IsNpc(npc))
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: target {0} is not an NPC and no matching use-item objective",
                targetCoid);
            return;
        }

        var npcCbid = npc.CBID;
        if (npcCbid <= 0)
            return;

        var dialogMissions = BuildDialogMissions(character, npcCbid);
        if (dialogMissions.Count == 0)
        {
            Logger.WriteLog(LogType.Debug, "UseObject: no dialog missions for NPC cbid={0}", npcCbid);
            return;
        }

        PrepareClientTurnInDialog(conn, character, npcCbid, dialogMissions);
        SendNpcMissionDialog(conn, npc.ObjectId, dialogMissions);
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

        var npc = FindNpcByCoid(character.Map, packet.MissionGiver?.Coid ?? -1);
        var npcCbid = npc?.CBID ?? 0;

        foreach (var missionId in ResolveDialogResponseMissions(character, packet.MissionId, npcCbid))
        {
            if (TryCompleteDeliverFromDialog(conn, character, missionId, npcCbid, npc?.ObjectId))
                return;
        }

        if (packet.MissionId <= 0)
            return;

        // Do not grant when the packet id is an objective id for an active deliver here.
        if (IsActiveObjectiveIdForDeliver(character, packet.MissionId, npcCbid))
            return;

        if (!CanOfferMission(character, packet.MissionId, npcCbid))
        {
            Logger.WriteLog(LogType.Debug,
                "MissionDialogResponse: mission {0} not offerable for charCoid={1} npcCbid={2}",
                packet.MissionId,
                character.ObjectId.Coid,
                npcCbid);
            return;
        }

        GrantMission(conn, character, packet.MissionId);
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

    private static List<int> BuildDialogMissions(Character character, int npcCbid)
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

        // 2) In-progress missions given by this NPC (status dialog)
        foreach (var quest in character.CurrentQuests)
        {
            if (IsMissionNpcGiver(quest.MissionId, npcCbid) && !missions.Contains(quest.MissionId))
                missions.Add(quest.MissionId);
        }

        if (missions.Count > 0)
            return missions;

        // 3) New offers from this NPC (prereqs / level / not active)
        foreach (var missionId in GetOfferableMissions(character, npcCbid))
        {
            if (!missions.Contains(missionId))
                missions.Add(missionId);
        }

        return missions;
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
            foreach (var mission in AssetManager.Instance.GetAllMissions())
            {
                if (mission == null || mission.NPC <= 0)
                    continue;

                if (!index.TryGetValue(mission.NPC, out var list))
                {
                    list = new List<int>();
                    index[mission.NPC] = list;
                }

                if (!list.Contains(mission.Id))
                    list.Add(mission.Id);
            }

            _missionsByNpc = index;
            Logger.WriteLog(LogType.Debug,
                "NpcInteract: mission index built for {0} NPC keys",
                index.Count);
        }
    }

    private static void GrantMission(TNLConnection conn, Character character, int missionId)
    {
        if (character.CurrentQuests.Any(q => q.MissionId == missionId))
            return;

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        // Seed client objective state so journal can show the new objective.
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

        Logger.WriteLog(LogType.Debug,
            "MissionDialogResponse: granted mission {0} to charCoid={1}",
            missionId,
            character.ObjectId.Coid);

        // Mission-computed logic vars (type 11/12) may unlock gates / remote triggers.
        TriggerManager.Instance.OnMissionStateChanged(character.CurrentVehicle ?? (ClonedObjectBase)character);
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

        character.CurrentQuests.Remove(quest);
        character.CompletedMissionIds.Add(missionId);

        conn.SendGamePacket(new CompleteDynamicObjectivePacket
        {
            MissionId = missionId,
            ObjectiveId = objectiveId,
        });

        PushJournalMissionList(conn, character);

        Logger.WriteLog(LogType.Debug,
            "MissionDialogResponse: completed deliver mission={0} objective={1} npcCbid={2}",
            missionId,
            objectiveId,
            npcCbid);

        // Type-9 "has completed mission" vars and type-11 active-mission vars both change.
        TriggerManager.Instance.OnMissionStateChanged(character.CurrentVehicle ?? (ClonedObjectBase)character);

        // Open follow-up offers unlocked by completing this mission (ReqMissionId prereqs).
        if (npcTfid != null && npcCbid > 0)
        {
            var followUps = GetOfferableMissions(character, npcCbid);
            if (followUps.Count > 0)
            {
                Logger.WriteLog(LogType.Debug,
                    "MissionDialogResponse: opening follow-up offers [{0}] after completing {1}",
                    string.Join(',', followUps),
                    missionId);
                SendNpcMissionDialog(conn, npcTfid, followUps);
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
        var vehicle = character?.CurrentVehicle;
        if (character?.Map == null || vehicle == null)
            return;

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
            if (vehicle.Position.DistSq(targetPos) > radius * radius)
                continue;

            LogPatrolIncomplete(patrol, quest, objective, targetCoid);
            AdvanceOrCompleteObjective(conn, character, quest, mission, objective, source: "AutoPatrol");
            return;
        }
    }

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
    /// Finish the current objective: advance to the next sequence, or complete the mission.
    /// Sends CompleteDynamicObjective so the client retargets UI / waypoints.
    /// </summary>
    /// <summary>Shared objective advance/complete path (UseItem, AutoPatrol, Kill, …).</summary>
    internal static void AdvanceOrCompleteObjective(
        TNLConnection conn,
        Character character,
        CharacterQuest quest,
        Mission mission,
        MissionObjective objective,
        string source = "Objective")
    {
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

        conn.SendGamePacket(new CompleteDynamicObjectivePacket
        {
            MissionId = quest.MissionId,
            ObjectiveId = objective.ObjectiveId,
        });

        if (hasNext)
        {
            var nextSeq = mission.Objectives.Values
                .Where(o => o.Sequence > seq)
                .Min(o => o.Sequence);
            quest.ActiveObjectiveSequence = nextSeq;
            if (seq < quest.ObjectiveProgress.Length)
                quest.ObjectiveProgress[seq] = quest.ObjectiveMax[seq];

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

                conn.SendGamePacket(new ObjectiveStatePacket
                {
                    ObjectiveBitmask = 0u,
                    ObjectiveId = nextObjective.ObjectiveId,
                });
            }

            if (objective.XP != 0 || objective.Credits != 0 || objective.SkillPoints != 0 || objective.AttribPoints != 0)
            {
                IncompleteHandlerLog.Warn(
                    "AdvanceOrCompleteObjective",
                    context,
                    "Per-objective XP/credits/skill/attrib rewards not applied on advance",
                    "Apply MissionObjective reward fields (and mission-level rewards on final complete) via character economy APIs.");
            }

            PushJournalMissionList(conn, character);
            TriggerManager.Instance.OnMissionStateChanged(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
            return;
        }

        character.CurrentQuests.Remove(quest);
        character.CompletedMissionIds.Add(quest.MissionId);

        Logger.WriteLog(LogType.Debug,
            "{0}: completed mission={1} objective={2}",
            source,
            quest.MissionId,
            objective.ObjectiveId);

        IncompleteHandlerLog.Warn(
            "AdvanceOrCompleteObjective",
            context,
            "Mission complete: no XP/credits/items/medals applied; quest state not persisted to DB",
            "On final objective: grant mission rewards (client CompleteObjective path), inventory rewards, persist CurrentQuests/CompletedMissionIds, send FailMission/complete UI packets as needed.");

        PushJournalMissionList(conn, character);
        TriggerManager.Instance.OnMissionStateChanged(
            character.CurrentVehicle ?? (ClonedObjectBase)character);
    }

    internal static void PushJournalMissionList(TNLConnection conn, Character character)
    {
        conn.SendGamePacket(new ConvoyMissionsResponsePacket
        {
            CurrentQuests = character.CurrentQuests.ToList()
        });
    }

    private static Creature FindNpcByCoid(SectorMap map, long coid)
    {
        if (map == null || coid <= 0)
            return null;

        if (map.GetObjectByCoid(coid) is Creature creature && IsNpc(creature))
            return creature;

        return null;
    }

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
