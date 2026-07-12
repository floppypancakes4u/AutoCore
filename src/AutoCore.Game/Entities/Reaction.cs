namespace AutoCore.Game.Entities;

using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

public enum ReactionType : byte
{
    Activate = 0,
    Deactivate = 1,
    Create = 2,
    Delete = 3,
    MakeFriend = 4,
    MakeEnemy = 5,
    MakeInvincible = 6,
    MakeNotInvincbile = 7,
    Death = 8,
    TakeArena = 9,
    TransferMap = 10,
    ClientText = 11,
    SkillCast = 12,
    VariableSet = 13,
    VariableAdd = 14,
    VariableSub = 15,
    Enable = 16,
    Disable = 17,
    Text = 18,
    ResetTrigger = 19,
    SetFaction = 20,
    ResetFaction = 21,
    SetFactionFromVar = 22,
    SetHP = 23,
    Boost = 24,
    RemoveFromInv = 25,
    AdjustCredits = 26,
    AddSkillPoints = 27,
    AddXP = 28,
    MarkRepairStation = 29,
    GiveMission = 30,
    CompleteObjective = 31,
    UnlockContObj = 32,
    AddMissionString = 33,
    DelMissionString = 34,
    AddWaypoint = 35,
    DelWaypoint = 36,
    GiveMissionDialog = 37,
    GiveItemNumCBID = 38,
    GiveItemNumCBIDGen = 39,
    OpenBodyShop = 40,
    OpenRefinery = 41,
    OpenGarage = 42,
    SetPath = 43,
    SetPatrolDistance = 44,
    Teleport = 45,
    TimerStart = 46,
    TimerStop = 47,
    VariableSetRandom = 48,
    VariableMul = 49,
    VariableDiv = 50,
    GiveMedal = 51,
    OpenStore = 52,
    SetTeamFaction = 53,
    ResetTeamFaction = 54,
    SetTeamFactionFromVar = 55,
    AddPoints = 56,
    ResetPoints = 57,
    OpenArena = 58,
    OpenClanManager = 59,
    SetActiveObjective = 60,
    ProgressBar = 61,
    SetMapWaypoint = 62,
    SetMapDynamicWaypoint = 63,
    SetStatusText = 64,
    SetProgressBar = 65,
    RemoveProgressBar = 66,
    RemoveText = 67,
    RemoveMapWaypoint = 68,
    RemoveMapDynamicWaypoint = 69,
    RelockContObj = 70,
    OpenSkillTrainer = 71,
    FailMission = 72,
    SpawnCollide = 73,
    FirstTimeEvent = 74,
    OpenDialog = 75,
    PlayMusic = 76,
    Path = 77,
    CaptureOutpost = 78,
    SetLevel = 79,
    GiveRespec = 80,
    RollFromLootTable = 81,
    TimerPause = 82,
    TaxiStops = 83,
    RaceWaypointReached = 84,
    StartRaceTimer = 85,
    OpenMailBox = 86,
    OpenAuctionHouse = 87
}

public enum ReactionWaypointType
{
    Kill = 0,
    Protect = 1,
    Interact = 2
}

public enum ReactionTextType
{
    ChoiceDialog = 0,
    OKDialog = 1,
    ScreenTop = 2,
    ChatQueue = 3
}

public enum ReactionTextParamType
{
    Variable = 0,
    PlayerName = 1,
    PlayerClass = 2,
    PlayerRace = 3,
    FactionName = 4,
    ObjectName = 5,
    ObjectClass = 6
}

public enum ReactionTextTargetType
{
    Client = 0,
    Convoy = 1,
    Global = 2
}

public class Reaction : ClonedObjectBase
{
    public ReactionTemplate Template { get; }

    public Reaction(ReactionTemplate template)
    {
        Template = template;
    }

    public bool CanTrigger(ClonedObjectBase activator)
    {
        if (activator is null || activator.Map is null)
            return false;

        if (Template.Conditions.Count > 0)
        {
            foreach (var condition in Template.Conditions)
            {
                var conditionSatisfied = condition.Check(activator);
                if (conditionSatisfied && !Template.AllConditionsNeeded)
                    break;

                if (!conditionSatisfied && Template.AllConditionsNeeded)
                    return false;
            }
        }

        return true;
    }

    public bool TriggerIfPossible(ClonedObjectBase activator)
    {
        if (!CanTrigger(activator))
            return false;

        var triggered = TriggerCore(activator);
        if (triggered)
            LogPlayerReaction(activator);

        return triggered;
    }

    private bool TriggerCore(ClonedObjectBase activator)
    {
        switch (Template.ReactionType)
        {
            case ReactionType.Activate:
                HandleActivateCascade(activator);
                return true;

            case ReactionType.Deactivate:
            case ReactionType.Enable:
            case ReactionType.Disable:
                // Primarily client-side via 0x206C reaction COID lookup.
                return true;

            case ReactionType.Delete:
                return HandleDelete(activator);

            case ReactionType.MakeInvincible:
                return ReactionObjectStateEffects.ApplyInvincible(Template, activator, invincible: true);

            case ReactionType.MakeNotInvincbile:
                return ReactionObjectStateEffects.ApplyInvincible(Template, activator, invincible: false);

            case ReactionType.SetFactionFromVar:
                return ReactionObjectStateEffects.ApplyFactionFromVar(Template, activator);

            case ReactionType.VariableSet:
            case ReactionType.VariableSetRandom:
                return HandleVariableSet(activator);

            case ReactionType.VariableAdd:
            case ReactionType.VariableSub:
            case ReactionType.VariableMul:
            case ReactionType.VariableDiv:
                return HandleVariableArithmetic(activator);

            case ReactionType.UnlockContObj:
                return HandleUnlockContObj(activator);

            case ReactionType.RelockContObj:
                return HandleRelockContObj(activator);

            case ReactionType.SetActiveObjective:
                return HandleSetActiveObjective(activator);

            case ReactionType.GiveMission:
                return HandleGiveMission(activator);

            case ReactionType.CompleteObjective:
                LogMissionReactionStub(
                    "Reaction.CompleteObjective",
                    "No server quest advance — client may finish via 0x206C only",
                    "Call shared MissionManager/AdvanceOrCompleteObjective using GenericVar1 (objective id) or active quest; send CompleteDynamicObjective + ObjectiveState + rewards; OnMissionStateChanged.");
                return true;

            case ReactionType.FailMission:
                LogMissionReactionStub(
                    "Reaction.FailMission",
                    "Fail not applied to CurrentQuests/CompletedMissionIds",
                    "Remove or mark quest failed, send FailMission (0x20B2) if required, clear objective UI, OnMissionStateChanged; honor GenericVar1 mission id if set.");
                return true;

            case ReactionType.AddMissionString:
            case ReactionType.DelMissionString:
                LogMissionReactionStub(
                    $"Reaction.{Template.ReactionType}",
                    "Mission string list not tracked server-side",
                    "Track mission strings on character if anything gates on them; otherwise document pure-client via 0x206C and stop treating as server mission progress.");
                return true;

            case ReactionType.GiveMissionDialog:
                return HandleGiveMissionDialog(activator);

            case ReactionType.AddWaypoint:
            case ReactionType.DelWaypoint:
            case ReactionType.SetMapWaypoint:
            case ReactionType.SetMapDynamicWaypoint:
            case ReactionType.RemoveMapWaypoint:
            case ReactionType.RemoveMapDynamicWaypoint:
                // Waypoint reactions are client-side via 0x206C (high volume — no per-fire spam).
                return true;

            case ReactionType.Text:
            case ReactionType.ClientText:
            case ReactionType.SetStatusText:
            case ReactionType.RemoveText:
                // Text/UI — client via 0x206C; no per-fire spam (high volume).
                return true;

            case ReactionType.SetProgressBar:
            case ReactionType.RemoveProgressBar:
            case ReactionType.ProgressBar:
                // Progress bar — client via 0x206C.
                return true;

            case ReactionType.Boost:
                // Client applies boost presentation/effect from the reaction COID via 0x206C.
                return true;

            case ReactionType.Death:
                return HandleDeath(activator);

            case ReactionType.Create:
                return HandleCreate(activator);

            case ReactionType.TransferMap:
                return HandleTransferMap(activator);

            case ReactionType.MarkRepairStation:
                return HandleMarkRepairStation(activator);

            case ReactionType.SkillCast:
                return Skills.SkillService.TryCastReaction(activator, Template.GenericVar1, Template.GenericVar3);

            case ReactionType.ResetTrigger:
                return HandleResetTrigger(activator);

            case ReactionType.SetPath:
                return HandleSetPath(activator);

            case ReactionType.SetPatrolDistance:
                return HandleSetPatrolDistance(activator);

            default:
                IncompleteHandlerLog.Warn(
                    "Reaction.Unhandled",
                    $"coid={Template.COID} name='{Template.Name}' type={Template.ReactionType} ({(byte)Template.ReactionType}) g1={Template.GenericVar1} g2={Template.GenericVar2} g3={Template.GenericVar3} objs=[{string.Join(',', Template.Objects)}]",
                    "No server handler for this ReactionType",
                    "Implement TriggerIfPossible case or confirm pure-client via 0x206C; document in reaction topic extraction.");
                return true;
        }
    }

    private void LogPlayerReaction(ClonedObjectBase activator)
    {
        var character = GetCharacterFromActivator(activator);
        if (character == null)
            return;

        Logger.WriteLog(LogType.Debug,
            "Player reaction occurred: playerCoid={0} activatorCoid={1} reaction={2} type={3} name='{4}'",
            character.ObjectId.Coid,
            activator.ObjectId.Coid,
            ObjectId.Coid,
            Template.ReactionType,
            Template.Name ?? string.Empty);
    }

    private void LogMissionReactionStub(string handler, string gap, string todo)
    {
        IncompleteHandlerLog.Warn(
            handler,
            $"coid={Template.COID} name='{Template.Name}' g1={Template.GenericVar1} g2={Template.GenericVar2} g3={Template.GenericVar3} objs=[{string.Join(',', Template.Objects)}]",
            gap,
            todo);
    }

    private bool HandleGiveMissionDialog(ClonedObjectBase activator)
    {
        // GiveMissionDialog is handled via the GroupReactionCall packet (opcode 0x206C).
        // The client receives the reaction coid via GroupReactionCallPacket, looks up the 
        // ReactionTemplate in clonebase, and displays the mission dialog based on that data.
        //
        // NOTE: A separate MissionDialogPacket (opcode 0x206D) was previously sent here, but:
        // 1. 0x206D is the MissionDialog_Response opcode (client→server), NOT server→client
        // 2. The client's dispatcher ignores packets with opcode > 0x206C (except 0x804D)
        // 3. The GroupReactionCallPacket already sends the reaction coid which the client uses
        //
        // See MISSION_DIALOG_CLIENT_ANALYSIS.md for details on the packet structure analysis.
        
        var character = GetCharacterFromActivator(activator);
        if (character == null)
        {
            Logger.WriteLog(LogType.Debug, $"GiveMissionDialog reaction {Template.COID}: Could not get character from activator");
            return true;
        }

        // Log mission info for debugging
        if (Template.Missions.Count > 0)
        {
            Logger.WriteLog(LogType.Debug, $"GiveMissionDialog reaction {Template.COID}: Triggering dialog with {Template.Missions.Count} missions for character {character.Name}");
            foreach (var missionId in Template.Missions)
            {
                var mission = Managers.AssetManager.Instance.GetMission(missionId);
                if (mission != null)
                {
                    Logger.WriteLog(LogType.Debug, $"  - Mission {missionId}: '{mission.Name}' (Title: {mission.Title})");
                }
                else
                {
                    Logger.WriteLog(LogType.Debug, $"  - Mission {missionId}: (not found in AssetManager)");
                }
            }
        }
        else
        {
            Logger.WriteLog(LogType.Debug, $"GiveMissionDialog reaction {Template.COID}: No missions in template - client will use clonebase data");
        }

        // Return true to indicate the reaction triggered successfully.
        // The GroupReactionCallPacket with this reaction's coid will be sent by TriggerReactionsInternal.
        return true;
    }

    /// <summary>
    /// Activate targeting a Trigger COID fires that trigger's reactions server-side
    /// (client 0x206C alone does not run nested map trigger graphs).
    /// Activate targeting a SpawnPoint materializes children if missing (client
    /// CVOGSpawnPoint_SetObjectActiveState).
    /// </summary>
    private void HandleActivateCascade(ClonedObjectBase activator)
    {
        var map = activator?.Map;
        if (map == null)
            return;

        foreach (var objectCoid in Template.Objects)
        {
            if (map.Triggers.TryGetValue(new TFID(objectCoid, false), out var trigger))
            {
                if (trigger.Template.Reactions.Count == 0)
                    continue;

                Logger.WriteLog(LogType.Debug,
                    "Activate reaction {0}: cascade trigger {1} reactions=[{2}]",
                    Template.COID,
                    objectCoid,
                    string.Join(',', trigger.Template.Reactions));
                TriggerManager.Instance.FireTriggerReactions(activator, trigger);
                continue;
            }

            // SpawnPoint activate: ensure children exist (Create may have only placed the marker).
            if (map.GetObjectByCoid(objectCoid) is SpawnPoint spawnPoint)
            {
                // Do not mutate shared MapData.IsActive (see Create path).
                if (!spawnPoint.HasLiveSpawn())
                {
                    // Activate is reaction-driven — fire TriggerEvents (combat → pad setup).
                    spawnPoint.Spawn(fireTriggerEvents: true, triggerActivator: activator);
                    Logger.WriteLog(LogType.Debug,
                        "Activate reaction {0}: SpawnPoint coid={1} spawned children",
                        Template.COID,
                        objectCoid);
                }
            }
        }
    }

    private bool HandleVariableSet(ClonedObjectBase activator)
    {
        var character = GetCharacterFromActivator(activator);
        var store = character?.EnsureLogicVariables();
        if (store == null)
            return true;

        // VariableSet: var[GenericVar1] = var[GenericVar3] (GhidraMCP-verified).
        var value = store.Get(Template.GenericVar3);
        store.Set(Template.GenericVar1, value);
        Logger.WriteLog(LogType.Debug,
            "VariableSet reaction {0}: var[{1}] = var[{2}] = {3}",
            Template.COID,
            Template.GenericVar1,
            Template.GenericVar3,
            value);

        TriggerManager.Instance.OnVariableChanged(activator, Template.GenericVar1);
        return true;
    }

    private bool HandleVariableArithmetic(ClonedObjectBase activator)
    {
        var character = GetCharacterFromActivator(activator);
        var store = character?.EnsureLogicVariables();
        if (store == null)
            return true;

        var left = store.Get(Template.GenericVar1);
        var right = store.Get(Template.GenericVar3);
        var result = Template.ReactionType switch
        {
            ReactionType.VariableAdd => left + right,
            ReactionType.VariableSub => left - right,
            ReactionType.VariableMul => left * right,
            ReactionType.VariableDiv => Math.Abs(right) < 1e-6f ? left : left / right,
            _ => left,
        };

        store.Set(Template.GenericVar1, result);
        TriggerManager.Instance.OnVariableChanged(activator, Template.GenericVar1);
        return true;
    }

    private bool HandleDelete(ClonedObjectBase activator)
    {
        return ApplyReactionMapRemove(activator, "Delete");
    }

    /// <summary>
    /// Create (type 2): spawn map template COIDs that are not yet live (client CVOGReaction_SpawnObject).
    /// Nested child reactions are fired by <see cref="SectorMap"/> after this returns.
    /// Only listed COIDs are created — never parents/siblings of a collision blocker.
    /// </summary>
    private bool HandleCreate(ClonedObjectBase activator)
    {
        var map = activator?.Map;
        if (map == null)
        {
            Logger.WriteLog(LogType.Debug, $"Create reaction {Template.COID}: Activator has no map");
            return true;
        }

        foreach (var objectCoid in Template.Objects)
        {
            var existing = map.GetObjectByCoid(objectCoid);
            if (existing != null)
            {
                // Inactive spawn points may already sit on the map without a live child
                // (or after a prior despawn). Create must re-Spawn, not skip.
                if (existing is SpawnPoint existingSpawn && !existingSpawn.HasLiveSpawn())
                {
                    // Do not mutate shared MapData template.IsActive — that is process-global
                    // and leaves combat spawns permanently "active" after one Create.
                    // fireTriggerEvents: Create is reaction-driven (pad Gunny TE, etc.).
                    existingSpawn.Spawn(fireTriggerEvents: true, triggerActivator: activator);
                    Logger.WriteLog(LogType.Debug,
                        "Create reaction {0}: re-spawned children for existing SpawnPoint coid={1}",
                        Template.COID,
                        objectCoid);
                }
                else
                {
                    Logger.WriteLog(LogType.Debug,
                        "Create reaction {0}: object {1} already on map — skip",
                        Template.COID,
                        objectCoid);
                }

                continue;
            }

            if (!map.MapData.Templates.TryGetValue(objectCoid, out var template) || template == null)
            {
                Logger.WriteLog(LogType.Debug,
                    "Create reaction {0}: no MapData template for coid={1} (client-side only)",
                    Template.COID,
                    objectCoid);
                continue;
            }

            ClonedObjectBase obj;
            try
            {
                obj = template.Create();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error,
                    "Create reaction {0}: template.Create failed for coid={1}: {2}",
                    Template.COID,
                    objectCoid,
                    ex.Message);
                continue;
            }

            if (obj == null)
            {
                Logger.WriteLog(LogType.Debug,
                    "Create reaction {0}: template.Create returned null for coid={1}",
                    Template.COID,
                    objectCoid);
                continue;
            }

            // Fresh template.Create() leaves TFID at default Coid=-1; assign map template COID.
            if (obj.ObjectId.Coid <= 0)
                obj.SetCoid(template.COID != 0 ? template.COID : objectCoid, false);

            // Never set shared template.IsActive — map load uses OriginalIsActive only.

            obj.SetMap(map);

            if (obj is SpawnPoint spawnPoint)
                spawnPoint.Spawn(fireTriggerEvents: true, triggerActivator: activator);

            Logger.WriteLog(LogType.Debug,
                "Create reaction {0}: spawned coid={1} type={2} on map {3}",
                Template.COID,
                objectCoid,
                obj.GetType().Name,
                map.ContinentId);
        }

        return true;
    }

    /// <summary>
    /// Death (type 8): server-side leave-map for listed COIDs only.
    /// Client death FX / collision / mesh removal is owned by 0x206C (CVOGReaction_RemoveObject).
    /// Do <b>not</b> send DestroyObject here — that double-frees client objects that 0x206C already
    /// removes and can wipe whole gate meshes when only a blocker COID was intended.
    /// </summary>
    private bool HandleDeath(ClonedObjectBase activator)
    {
        return ApplyReactionMapRemove(activator, "Death");
    }

    /// <summary>
    /// Shared Delete/Death server authority: drop exactly the listed map COIDs (or activator prop)
    /// from <see cref="SectorMap"/> without client destroy packets. Never touch player vehicles.
    /// </summary>
    private bool ApplyReactionMapRemove(ClonedObjectBase activator, string label)
    {
        var map = activator?.Map;
        if (map == null)
        {
            Logger.WriteLog(LogType.Debug, $"{label} reaction {Template.COID}: Activator has no map");
            return true;
        }

        if (Template.ActOnActivator)
        {
            if (!CanReactionRemoveFromMap(activator, label, isActivator: true))
                return true;

            Logger.WriteLog(LogType.Debug,
                "{0} reaction {1}: removing activator coid={2} from map (no DestroyObject; client 0x206C)",
                label,
                Template.COID,
                activator.ObjectId.Coid);
            activator.SetMap(null);
            return true;
        }

        foreach (var objectCoid in Template.Objects)
        {
            var obj = map.GetObjectByCoid(objectCoid);
            if (obj == null)
            {
                // Often expected: collision blockers / FX are client-only until Create, or never server-owned.
                Logger.WriteLog(LogType.Debug,
                    "{0} reaction {1}: object {2} not found on server (client-side only)",
                    label,
                    Template.COID,
                    objectCoid);
                continue;
            }

            if (!CanReactionRemoveFromMap(obj, label, isActivator: false))
                continue;

            // SpawnPoints own spawned creatures/vehicles (SpawnOwner / SpawnOwnerCoid).
            // Client RemoveObject tears down the whole spawn presence; mirror that here.
            if (obj is SpawnPoint spawnPoint)
                spawnPoint.DespawnOwnedEntities();

            Logger.WriteLog(LogType.Debug,
                "{0} reaction {1}: removing object coid={2} type={3} from map (no DestroyObject; client 0x206C)",
                label,
                Template.COID,
                objectCoid,
                obj.GetType().Name);
            obj.SetMap(null);
        }

        return true;
    }

    /// <summary>
    /// Guards against deleting player vehicles/characters or reaction/trigger definitions via ActOnActivator.
    /// Listed Objects COIDs are trusted as map-authored targets (gate blocker, prop, etc.).
    /// </summary>
    private bool CanReactionRemoveFromMap(ClonedObjectBase obj, string label, bool isActivator)
    {
        if (obj == null)
            return false;

        if (obj is Vehicle or Character)
        {
            Logger.WriteLog(LogType.Debug,
                "{0} reaction {1}: refusing to remove {2} coid={3}{4}",
                label,
                Template.COID,
                obj.GetType().Name,
                obj.ObjectId.Coid,
                isActivator ? " (ActOnActivator)" : "");
            return false;
        }

        // Never tear down the reaction entity itself mid-batch via ActOnActivator.
        if (isActivator && obj is Reaction or Trigger)
        {
            Logger.WriteLog(LogType.Debug,
                "{0} reaction {1}: refusing to remove {2} activator coid={3}",
                label,
                Template.COID,
                obj.GetType().Name,
                obj.ObjectId.Coid);
            return false;
        }

        return true;
    }

    private Character GetCharacterFromActivator(ClonedObjectBase activator)
    {
        // In Auto Assault, players are usually in vehicles, so we need to get the character from the vehicle
        return activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
    }

    private bool HandleUnlockContObj(ClonedObjectBase activator)
    {
        // UnlockContObj unlocks mission objectives/containers for the player
        // GenericVar1 may contain the objective ID to unlock
        // The client handles the visual unlocking via LogicStateChangePacket
        var character = GetCharacterFromActivator(activator);
        var objectiveId = Template.GenericVar1;

        // Look up which mission this objective belongs to
        var mission = Managers.AssetManager.Instance.GetMissionByObjectiveId(objectiveId);
        if (mission != null)
        {
            Logger.WriteLog(LogType.Debug, $"UnlockContObj reaction {Template.COID}: ObjectiveID={objectiveId} belongs to Mission {mission.Id} '{mission.Name}' (Title: {mission.Title})");
        }

        Logger.WriteLog(LogType.Debug, $"UnlockContObj reaction {Template.COID}: GenericVar1={objectiveId}, GenericVar3={Template.GenericVar3}, Objects={Template.Objects.Count}, ActOnActivator={Template.ActOnActivator}");

        IncompleteHandlerLog.Warn(
            "Reaction.UnlockContObj",
            $"coid={Template.COID} objectiveIdOrG1={objectiveId} g3={Template.GenericVar3} objs=[{string.Join(',', Template.Objects)}] char={character?.Name ?? "(null)"}",
            "No per-character unlock set — only logs; map/mission gates using unlock state will not open server-side",
            "Track unlocked objective/container ids on character; gate triggers/conditions; sync client if needed beyond 0x206C.");

        if (character != null)
        {
            Logger.WriteLog(LogType.Debug, $"UnlockContObj reaction {Template.COID}: Unlocking for character {character.Name}");
        }

        if (Template.Objects.Count > 0)
        {
            var map = activator.Map;
            foreach (var objectCoid in Template.Objects)
            {
                var obj = map?.GetObjectByCoid(objectCoid);
                if (obj != null)
                {
                    Logger.WriteLog(LogType.Debug, $"UnlockContObj reaction {Template.COID}: Unlocking object {objectCoid}");
                }
                else
                {
                    Logger.WriteLog(LogType.Debug, $"UnlockContObj reaction {Template.COID}: Object {objectCoid} is client-side only");
                }
            }
        }
        return true;
    }

    private bool HandleRelockContObj(ClonedObjectBase activator)
    {
        // RelockContObj re-locks previously unlocked objectives/containers
        var character = GetCharacterFromActivator(activator);

        Logger.WriteLog(LogType.Debug, $"RelockContObj reaction {Template.COID}: GenericVar1={Template.GenericVar1}, GenericVar3={Template.GenericVar3}");

        if (character != null)
        {
            Logger.WriteLog(LogType.Debug, $"RelockContObj reaction {Template.COID}: Relocking for character {character.Name}");
        }

        if (Template.Objects.Count > 0)
        {
            foreach (var objectCoid in Template.Objects)
            {
                Logger.WriteLog(LogType.Debug, $"RelockContObj reaction {Template.COID}: Relocking object {objectCoid}");
            }
        }
        return true;
    }

    /// <summary>
    /// GiveMission (type 30). GenericVar1 = mission id. Server tracks CurrentQuests;
    /// client applies via 0x206C reaction COID.
    /// </summary>
    private bool HandleGiveMission(ClonedObjectBase activator)
    {
        var missionId = Template.GenericVar1;
        Logger.WriteLog(LogType.Debug,
            "Mission reaction GiveMission triggered for reaction {0} mission={1}",
            Template.COID,
            missionId);

        if (missionId <= 0)
            return true;

        var character = GetCharacterFromActivator(activator);
        if (character == null)
            return true;

        // Return false on decline so SectorMap.TriggerReactions does NOT broadcast a GiveMission
        // 0x206C to the client. On relog the map PerPlayerLoad trigger re-fires this reaction; if we
        // returned true the client would re-add an already-owned/completed mission as active even
        // though the server keeps it completed-only (create-packet state), desyncing the journal.
        if (character.CurrentQuests.Any(q => q.MissionId == missionId))
        {
            Logger.WriteLog(LogType.Debug,
                "GiveMission: mission {0} already tracked for coid={1}; not re-sending",
                missionId,
                character.ObjectId.Coid);
            return false;
        }

        // Do not re-grant a completed non-repeatable mission (mirrors NpcInteractHandler.CanOfferMission).
        if (character.CompletedMissionIds.Contains(missionId)
            && AssetManager.Instance.GetMission(missionId)?.IsRepeatable == 0)
        {
            Logger.WriteLog(LogType.Debug,
                "GiveMission: mission {0} already completed (non-repeatable) for coid={1}; skipping re-grant",
                missionId,
                character.ObjectId.Coid);
            return false;
        }

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        MissionPersistence.Instance.OnQuestChanged(character, quest);
        Logger.WriteLog(LogType.Debug,
            "GiveMission: tracked mission {0} on server for coid={1}",
            missionId,
            character.ObjectId.Coid);

        // 0x206C still tells the client to apply GiveMission; also push journal/objective
        // state so sector UI matches server authority even if the reaction batch is dropped.
        var conn = character.OwningConnection;
        if (conn != null)
            NpcInteractHandler.ResyncActiveMissionToClient(conn, character, quest);

        TriggerManager.Instance.OnMissionStateChanged(character.CurrentVehicle ?? (ClonedObjectBase)character);
        return true;
    }

    /// <summary>
    /// SetActiveObjective (type 60). GenericVar1 = objective id.
    /// Client UI is driven by 0x206C apply (case 0x3C); server tracks sequence.
    /// </summary>
    private bool HandleSetActiveObjective(ClonedObjectBase activator)
    {
        var character = GetCharacterFromActivator(activator);
        var objectiveId = Template.GenericVar1;

        var mission = AssetManager.Instance.GetMissionByObjectiveId(objectiveId);
        if (mission != null)
        {
            Logger.WriteLog(LogType.Debug,
                $"SetActiveObjective reaction {Template.COID}: ObjectiveID={objectiveId} belongs to Mission {mission.Id} '{mission.Name}'");
        }
        else
        {
            Logger.WriteLog(LogType.Debug,
                $"SetActiveObjective reaction {Template.COID}: ObjectiveID={objectiveId} - no mission found");
        }

        if (character != null && mission != null)
        {
            var objective = AssetManager.Instance.GetObjectiveById(objectiveId);
            if (objective != null)
            {
                var quest = character.CurrentQuests.FirstOrDefault(q => q.MissionId == mission.Id);
                if (quest != null)
                {
                    quest.ActiveObjectiveSequence = objective.Sequence;
                    MissionPersistence.Instance.OnQuestChanged(character, quest);
                    TriggerManager.Instance.OnMissionStateChanged(character.CurrentVehicle ?? (ClonedObjectBase)character);

                    IncompleteHandlerLog.Warn(
                        "Reaction.SetActiveObjective",
                        $"coid={Template.COID} mission={mission.Id} objective={objectiveId} seq={objective.Sequence} charCoid={character.ObjectId.Coid}",
                        "Server updated ActiveObjectiveSequence but did not send ObjectiveState / CompleteDynamicObjective / ConvoyMissionsResponse",
                        "After sequence change: send ObjectiveState (slots/bitmask), refresh journal packet, ensure login persistence of active sequence.");
                }
                else
                {
                    IncompleteHandlerLog.Warn(
                        "Reaction.SetActiveObjective",
                        $"coid={Template.COID} mission={mission.Id} objective={objectiveId}",
                        "Mission not in CurrentQuests — sequence not tracked; relies on client 0x206C only",
                        "Grant mission first or set active only when quest is present; optional auto-grant if design requires.");
                }
            }
        }
        else if (character == null)
        {
            IncompleteHandlerLog.Warn(
                "Reaction.SetActiveObjective",
                $"coid={Template.COID} objective={objectiveId}",
                "No character from activator — cannot update quest state",
                "Resolve vehicle→character like GetSuperCharacter before applying.");
        }

        return true;
    }

    private bool HandleMarkRepairStation(ClonedObjectBase activator)
    {
        // GenericVar1 is a station key (often small), not a map object COID.
        // Pad pose: linked Objects → nearby graphics → trigger (Y-safe) → activator.
        var character = GetCharacterFromActivator(activator);
        if (character == null)
        {
            Logger.WriteLog(LogType.Debug, $"MarkRepairStation reaction {Template.COID}: Could not get character from activator");
            return true;
        }

        var map = activator.Map ?? character.Map;
        var mapId = map?.ContinentId ?? character.LastTownId;
        var stationId = Template.GenericVar1 != 0 ? Template.GenericVar1 : Template.COID;

        // Activator is always non-null from TriggerIfPossible; pose resolve always succeeds.
        ResolveRepairStationPose(map, activator, out var posePos, out var poseRot, out var poseSource);
        character.SetLastRepairStation(stationId, mapId, posePos, poseRot);

        Logger.WriteLog(LogType.Network,
            $"MarkRepairStation reaction {Template.COID}: character {character.ObjectId.Coid} stationId={stationId} mapId={mapId} objects={Template.Objects.Count} pose={posePos} via {poseSource}");

        return true;
    }

    private void ResolveRepairStationPose(
        SectorMap map,
        ClonedObjectBase activator,
        out Vector3 posePos,
        out Quaternion poseRot,
        out string poseSource)
    {
        var reactionCoid = ObjectId.Coid != 0 ? ObjectId.Coid : Template.COID;
        var groundY = activator.Position.Y;

        if (map != null && TryResolveLinkedPadPose(map, out posePos, out poseRot, out var linkedCoid))
        {
            posePos = ApplyGroundYSafety(posePos, groundY);
            poseSource = $"linked-pad:{linkedCoid}";
            return;
        }

        var firingTrigger = map != null ? FindTriggerForReaction(map, reactionCoid, activator) : null;

        if (map != null && firingTrigger != null &&
            TryFindNearbyPadGraphics(map, firingTrigger, out posePos, out poseRot, out var nearbyCoid))
        {
            posePos = ApplyGroundYSafety(posePos, groundY);
            poseSource = $"nearby-pad:{nearbyCoid}";
            return;
        }

        if (firingTrigger != null)
        {
            posePos = ApplyGroundYSafety(firingTrigger.Position, groundY);
            poseRot = firingTrigger.Rotation;
            poseSource = $"trigger:{firingTrigger.ObjectId.Coid}";
            return;
        }

        if (Position.X != 0f || Position.Y != 0f || Position.Z != 0f)
        {
            posePos = ApplyGroundYSafety(Position, groundY);
            poseRot = Rotation;
            poseSource = "reaction";
            return;
        }

        posePos = activator.Position;
        poseRot = activator.Rotation;
        poseSource = "activator";
    }

    private bool TryResolveLinkedPadPose(
        SectorMap map,
        out Vector3 posePos,
        out Quaternion poseRot,
        out long objectCoid)
    {
        posePos = default;
        poseRot = Quaternion.Default;
        objectCoid = 0;

        foreach (var coid in Template.Objects)
        {
            var linked = map.GetObjectByCoid(coid);
            if (linked != null && linked is not Trigger && linked is not Reaction)
            {
                posePos = linked.Position;
                poseRot = linked.Rotation;
                objectCoid = coid;
                return true;
            }

            if (map.MapData?.Templates != null &&
                map.MapData.Templates.TryGetValue(coid, out var template) &&
                template is GraphicsObjectTemplate graphics &&
                template is not TriggerTemplate &&
                template is not SpawnPointTemplate)
            {
                posePos = graphics.Location.ToVector3();
                poseRot = graphics.Rotation;
                objectCoid = coid;
                return true;
            }
        }

        return false;
    }

    private Trigger FindTriggerForReaction(SectorMap map, long reactionCoid, ClonedObjectBase activator)
    {
        Trigger bestInRange = null;
        Trigger bestAny = null;

        foreach (var trigger in map.Triggers.Values)
        {
            if (trigger.Template?.Reactions == null)
                continue;

            var listsThis = false;
            foreach (var listed in trigger.Template.Reactions)
            {
                if (listed == reactionCoid || listed == Template.COID)
                {
                    listsThis = true;
                    break;
                }
            }

            if (!listsThis)
                continue;

            bestAny ??= trigger;

            var range = trigger.Scale > 0f ? trigger.Scale : 1f;
            if (activator.Position.DistSq(trigger.Position) <= range * range)
            {
                bestInRange = trigger;
                break;
            }
        }

        return bestInRange ?? bestAny;
    }

    private static bool TryFindNearbyPadGraphics(
        SectorMap map,
        Trigger trigger,
        out Vector3 posePos,
        out Quaternion poseRot,
        out long objectCoid)
    {
        posePos = default;
        poseRot = Quaternion.Default;
        objectCoid = 0;

        var searchRadius = Math.Max(trigger.Scale * 2f, 25f);
        var searchRadiusSq = searchRadius * searchRadius;
        var origin = trigger.Position;

        ClonedObjectBase best = null;
        var bestDistSq = float.MaxValue;

        foreach (var obj in map.Objects.Values)
        {
            if (obj is null or Trigger or Reaction or SpawnPoint or Vehicle or Character or Creature)
                continue;

            var distSq = obj.Position.DistSq(origin);
            if (distSq > searchRadiusSq)
                continue;

            var score = distSq;
            if (obj.Position.Y + 0.5f < origin.Y)
                score += 1000f;

            if (score < bestDistSq)
            {
                bestDistSq = score;
                best = obj;
            }
        }

        if (best == null && map.MapData?.Templates != null)
        {
            GraphicsObjectTemplate bestTmpl = null;
            var bestTmplDistSq = float.MaxValue;
            long bestTmplCoid = 0;

            foreach (var kvp in map.MapData.Templates)
            {
                if (kvp.Value is not GraphicsObjectTemplate graphics)
                    continue;
                if (kvp.Value is TriggerTemplate or SpawnPointTemplate)
                    continue;

                var loc = graphics.Location.ToVector3();
                var distSq = loc.DistSq(origin);
                if (distSq > searchRadiusSq)
                    continue;

                var score = distSq;
                if (loc.Y + 0.5f < origin.Y)
                    score += 1000f;

                if (score < bestTmplDistSq)
                {
                    bestTmplDistSq = score;
                    bestTmpl = graphics;
                    bestTmplCoid = kvp.Key;
                }
            }

            if (bestTmpl != null)
            {
                posePos = bestTmpl.Location.ToVector3();
                poseRot = bestTmpl.Rotation;
                objectCoid = bestTmplCoid;
                return true;
            }
        }

        if (best == null)
            return false;

        posePos = best.Position;
        poseRot = best.Rotation;
        objectCoid = best.ObjectId.Coid;
        return true;
    }

    private static Vector3 ApplyGroundYSafety(Vector3 pose, float groundY)
    {
        if (pose.Y < groundY - 2f)
            return new Vector3(pose.X, groundY, pose.Z);

        return pose;
    }

    private bool HandleTransferMap(ClonedObjectBase activator)
    {
        var character = GetCharacterFromActivator(activator);
        if (character == null)
        {
            Logger.WriteLog(LogType.Debug, $"TransferMap reaction {Template.COID}: Could not get character from activator");
            return true;
        }

        var mapTransferType = Template.MapTransfer;
        var mapTransferData = Template.MapTransferData;

        Logger.WriteLog(LogType.Debug, $"TransferMap reaction {Template.COID}: Type={mapTransferType}, Data={mapTransferData} for character {character.Name}");

        switch (mapTransferType)
        {
            case Constants.MapTransferType.ContinentObject:
                // MapTransferData is the continent/map ID to transfer to
                MapManager.Instance.TransferCharacterToMap(character, mapTransferData);
                break;

            case Constants.MapTransferType.Highway:
            case Constants.MapTransferType.Random:
            case Constants.MapTransferType.Mission:
            case Constants.MapTransferType.GMTest:
            case Constants.MapTransferType.RepairStation:
            case Constants.MapTransferType.Death:
            case Constants.MapTransferType.Warp:
            case Constants.MapTransferType.Arean:
                Logger.WriteLog(LogType.Debug, $"TransferMap reaction {Template.COID}: MapTransferType {mapTransferType} not yet implemented, attempting ContinentObject transfer to map {mapTransferData}");
                MapManager.Instance.TransferCharacterToMap(character, mapTransferData);
                break;

            default:
                // Unknown type - treat as ContinentObject transfer (type value may be garbage/uninitialized in map data)
                Logger.WriteLog(LogType.Debug, $"TransferMap reaction {Template.COID}: Unknown MapTransferType {(byte)mapTransferType}, treating as ContinentObject transfer to map {mapTransferData}");
                MapManager.Instance.TransferCharacterToMap(character, mapTransferData);
                break;
        }

        return true;
    }

    private bool HandleResetTrigger(ClonedObjectBase activator)
    {
        // ResetTrigger allows triggers to fire again for the activator
        // The Objects list contains trigger coids to reset
        // If ActOnActivator is true or Objects is empty, we reset based on GenericVar1 which may contain a trigger coid
        
        var objectCoid = activator.ObjectId.Coid;

        if (Template.Objects.Count > 0)
        {
            // Reset specific triggers listed in Objects
            foreach (var triggerCoid in Template.Objects)
            {
                Logger.WriteLog(LogType.Debug, $"ResetTrigger reaction {Template.COID}: Resetting trigger {triggerCoid} for object {objectCoid}");
                TriggerManager.Instance.ResetTriggerFor(objectCoid, triggerCoid);
            }
        }
        else if (Template.GenericVar1 != 0)
        {
            // Reset the trigger specified by GenericVar1
            Logger.WriteLog(LogType.Debug, $"ResetTrigger reaction {Template.COID}: Resetting trigger {Template.GenericVar1} for object {objectCoid}");
            TriggerManager.Instance.ResetTriggerFor(objectCoid, Template.GenericVar1);
        }
        else
        {
            Logger.WriteLog(LogType.Debug, $"ResetTrigger reaction {Template.COID}: No triggers specified to reset");
        }

        return true;
    }

    /// <summary>
    /// SetPath (type 43): pathCoid = GenericVar1. &lt;= 0 clears the path (CoidCurrentPath = -1);
    /// otherwise assigns the path and reads PathReversing from the resolved MapPathTemplate
    /// (false when the path coid is unknown to this map), and resets the NpcAi follower
    /// index/direction so the NPC restarts the new path from its beginning. PatrolDistance is
    /// left untouched either way. Target resolution matches HandleCreate: ActOnActivator ->
    /// activator, else Objects COIDs -> map.GetObjectByCoid; a Character target resolves to its
    /// CurrentVehicle.
    /// </summary>
    private bool HandleSetPath(ClonedObjectBase activator)
    {
        var map = activator?.Map;
        if (map == null)
            return true;

        var pathCoid = Template.GenericVar1;
        var hasPath = pathCoid > 0;
        var reversing = hasPath && map.TryGetMapPath(pathCoid, out var mapPath) && mapPath.ReverseDirection;

        ForEachSetPathTarget(activator, map, target => ApplySetPath(target, pathCoid, hasPath, reversing));
        return true;
    }

    /// <summary>
    /// SetPatrolDistance (type 44): reads logic var[GenericVar1] via the character's
    /// EnsureLogicVariables store (same pattern as HandleVariableSet) and writes it to the
    /// resolved target's PatrolDistance. CoidCurrentPath is left untouched.
    /// </summary>
    private bool HandleSetPatrolDistance(ClonedObjectBase activator)
    {
        var map = activator?.Map;
        if (map == null)
            return true;

        var character = GetCharacterFromActivator(activator);
        var store = character?.EnsureLogicVariables();
        if (store == null)
            return true;

        var value = store.Get(Template.GenericVar1);

        ForEachSetPathTarget(activator, map, target => ApplySetPatrolDistance(target, value));
        return true;
    }

    /// <summary>
    /// Shared SetPath/SetPatrolDistance target enumeration: ActOnActivator -> activator, else
    /// Objects COIDs -> map.GetObjectByCoid. A Character target/activator resolves to its
    /// CurrentVehicle (client dispatch §2.5).
    /// </summary>
    private void ForEachSetPathTarget(ClonedObjectBase activator, SectorMap map, Action<ClonedObjectBase> apply)
    {
        if (Template.ActOnActivator)
        {
            apply(ResolveNpcPathTarget(activator));
            return;
        }

        foreach (var objectCoid in Template.Objects)
        {
            var obj = map.GetObjectByCoid(objectCoid);
            if (obj == null)
                continue;

            apply(ResolveNpcPathTarget(obj));
        }
    }

    private static ClonedObjectBase ResolveNpcPathTarget(ClonedObjectBase obj)
    {
        return obj?.GetAsCharacter() is Character character ? character.CurrentVehicle : obj;
    }

    private static void ApplySetPath(ClonedObjectBase target, int pathCoid, bool hasPath, bool reversing)
    {
        switch (target)
        {
            case Vehicle vehicle:
                vehicle.CoidCurrentPath = hasPath ? pathCoid : -1;
                if (hasPath)
                {
                    vehicle.PathReversing = reversing;
                    ResetNpcPathFollower(vehicle.NpcAi);
                }
                break;

            case Creature creature:
                creature.CoidCurrentPath = hasPath ? pathCoid : -1;
                if (hasPath)
                {
                    creature.PathReversing = reversing;
                    ResetNpcPathFollower(creature.NpcAi);
                }
                break;
        }
    }

    private static void ResetNpcPathFollower(NpcAiState npcAi)
    {
        if (npcAi == null)
            return;

        npcAi.PathIndex = -1;
        npcAi.PathDirection = 1;
    }

    private static void ApplySetPatrolDistance(ClonedObjectBase target, float value)
    {
        switch (target)
        {
            case Vehicle vehicle:
                vehicle.PatrolDistance = value;
                break;

            case Creature creature:
                creature.PatrolDistance = value;
                break;
        }
    }

    public override int GetCurrentHP() => 1;
    public override int GetMaximumHP() => 1;
    public override int GetBareTeamFaction() => Faction;
}
