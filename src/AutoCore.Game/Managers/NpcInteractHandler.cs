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

        var targetObj = character.Map.GetObjectByCoid(targetCoid);
        if (targetObj == null)
        {
            Logger.WriteLog(LogType.Debug, "UseObject: no object for coid {0}", targetCoid);
            return;
        }

        var playerPos = character.CurrentVehicle?.Position ?? character.Position;
        if (playerPos.DistSq(targetObj.Position) > MaxInteractDistance * MaxInteractDistance)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: rejected out of range charCoid={0} target={1}",
                character.ObjectId.Coid,
                targetCoid);
            return;
        }

        if (targetObj is not Creature npc || !IsNpc(npc))
        {
            Logger.WriteLog(LogType.Debug, "UseObject: target {0} is not an NPC", targetCoid);
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

            AdvanceOrCompleteObjective(conn, character, quest, mission, objective);
            return;
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
    private static void AdvanceOrCompleteObjective(
        TNLConnection conn,
        Character character,
        CharacterQuest quest,
        Mission mission,
        MissionObjective objective)
    {
        var seq = quest.ActiveObjectiveSequence;
        var hasNext = mission.Objectives.Values.Any(o => o.Sequence > seq);

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
                "AutoPatrol: advanced mission={0} seq {1} -> {2} objective={3}",
                quest.MissionId,
                seq,
                nextSeq,
                objective.ObjectiveId);

            var nextObjective = GetActiveObjective(quest);
            if (nextObjective != null)
            {
                conn.SendGamePacket(new ObjectiveStatePacket
                {
                    ObjectiveBitmask = 0u,
                    ObjectiveId = nextObjective.ObjectiveId,
                });
            }

            PushJournalMissionList(conn, character);
            TriggerManager.Instance.OnMissionStateChanged(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
            return;
        }

        character.CurrentQuests.Remove(quest);
        character.CompletedMissionIds.Add(quest.MissionId);

        Logger.WriteLog(LogType.Debug,
            "AutoPatrol: completed mission={0} objective={1}",
            quest.MissionId,
            objective.ObjectiveId);

        PushJournalMissionList(conn, character);
        TriggerManager.Instance.OnMissionStateChanged(
            character.CurrentVehicle ?? (ClonedObjectBase)character);
    }

    private static void PushJournalMissionList(TNLConnection conn, Character character)
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
