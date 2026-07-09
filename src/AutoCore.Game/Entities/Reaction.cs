namespace AutoCore.Game.Entities;

using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
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

        switch (Template.ReactionType)
        {
            case ReactionType.Activate:
            case ReactionType.Deactivate:
            case ReactionType.Enable:
            case ReactionType.Disable:
                // These are primarily handled client-side
                // The LogicStateChangePacket tells the client to activate/deactivate objects
                return true;

            case ReactionType.Delete:
                return HandleDelete(activator);

            case ReactionType.UnlockContObj:
                return HandleUnlockContObj(activator);

            case ReactionType.RelockContObj:
                return HandleRelockContObj(activator);

            case ReactionType.SetActiveObjective:
                return HandleSetActiveObjective(activator);

            case ReactionType.GiveMission:
                return HandleGiveMission(activator);

            case ReactionType.CompleteObjective:
            case ReactionType.FailMission:
            case ReactionType.AddMissionString:
            case ReactionType.DelMissionString:
                Logger.WriteLog(LogType.Debug, $"Mission reaction {Template.ReactionType} triggered for reaction {Template.COID}");
                return true;

            case ReactionType.GiveMissionDialog:
                return HandleGiveMissionDialog(activator);

            case ReactionType.AddWaypoint:
            case ReactionType.DelWaypoint:
            case ReactionType.SetMapWaypoint:
            case ReactionType.SetMapDynamicWaypoint:
            case ReactionType.RemoveMapWaypoint:
            case ReactionType.RemoveMapDynamicWaypoint:
                // Waypoint reactions are client-side
                return true;

            case ReactionType.Text:
            case ReactionType.ClientText:
            case ReactionType.SetStatusText:
            case ReactionType.RemoveText:
                // Text/UI reactions are client-side
                return true;

            case ReactionType.SetProgressBar:
            case ReactionType.RemoveProgressBar:
            case ReactionType.ProgressBar:
                // Progress bar reactions are client-side
                return true;

            case ReactionType.Create:
                // Object creation - needs server-side implementation
                Logger.WriteLog(LogType.Debug, $"Create reaction triggered for reaction {Template.COID} - not yet fully implemented");
                return true;

            case ReactionType.TransferMap:
                return HandleTransferMap(activator);

            case ReactionType.MarkRepairStation:
                return HandleMarkRepairStation(activator);

            case ReactionType.ResetTrigger:
                return HandleResetTrigger(activator);

            default:
                Logger.WriteLog(LogType.Error, $"Unhandled reaction type: {Template.ReactionType} for reaction {Template.COID}!");
                return true;
        }
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

    private bool HandleDelete(ClonedObjectBase activator)
    {
        var map = activator.Map;
        if (map == null)
        {
            Logger.WriteLog(LogType.Debug, $"Delete reaction {Template.COID}: Activator has no map");
            return true;
        }

        if (Template.ActOnActivator)
        {
            // Delete the activator itself
            Logger.WriteLog(LogType.Debug, $"Delete reaction {Template.COID}: Removing activator {activator.ObjectId.Coid} from map");
            activator.SetMap(null);
        }
        else
        {
            // Delete objects specified in the Objects list
            foreach (var objectCoid in Template.Objects)
            {
                var obj = map.GetObjectByCoid(objectCoid);
                if (obj != null)
                {
                    Logger.WriteLog(LogType.Debug, $"Delete reaction {Template.COID}: Removing object {objectCoid} from map");
                    obj.SetMap(null);
                }
                else
                {
                    // Object not on server - this is often expected as many objects are client-side only
                    Logger.WriteLog(LogType.Debug, $"Delete reaction {Template.COID}: Object {objectCoid} not found on server (client-side only)");
                }
            }
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

        if (character != null)
        {
            Logger.WriteLog(LogType.Debug, $"UnlockContObj reaction {Template.COID}: Unlocking for character {character.Name}");
            // TODO: Track unlocked objectives per character when mission system is implemented
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

        if (character.CurrentQuests.Any(q => q.MissionId == missionId))
        {
            Logger.WriteLog(LogType.Debug,
                "GiveMission: mission {0} already tracked for coid={1}",
                missionId,
                character.ObjectId.Coid);
            return true;
        }

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        Logger.WriteLog(LogType.Debug,
            "GiveMission: tracked mission {0} on server for coid={1}",
            missionId,
            character.ObjectId.Coid);

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
                    quest.ActiveObjectiveSequence = objective.Sequence;
                else
                {
                    Logger.WriteLog(LogType.Debug,
                        "SetActiveObjective reaction {0}: mission {1} not granted yet — 0x206C only",
                        Template.COID,
                        mission.Id);
                }
            }
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

    public override int GetCurrentHP() => 1;
    public override int GetMaximumHP() => 1;
    public override int GetBareTeamFaction() => Faction;
}
