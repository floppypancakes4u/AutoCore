namespace AutoCore.Game.Entities;

using AutoCore.Game.Constants;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
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
            case ReactionType.CompleteObjective:
            case ReactionType.FailMission:
            case ReactionType.AddMissionString:
            case ReactionType.DelMissionString:
            case ReactionType.GiveMissionDialog:
                // Mission-related reactions are primarily client-side
                // Server will need to track mission state in the future
                Logger.WriteLog(LogType.Debug, $"Mission reaction {Template.ReactionType} triggered for reaction {Template.COID}");
                return true;

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

            case ReactionType.ResetTrigger:
                // Reset trigger state - client handles the trigger reset
                return true;

            default:
                Logger.WriteLog(LogType.Error, $"Unhandled reaction type: {Template.ReactionType} for reaction {Template.COID}!");
                return true;
        }
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

    private bool HandleSetActiveObjective(ClonedObjectBase activator)
    {
        // SetActiveObjective sets the current active objective for the mission
        // GenericVar1 contains the objective index to set as active
        var character = GetCharacterFromActivator(activator);
        var objectiveId = Template.GenericVar1;

        // Look up which mission this objective belongs to
        var mission = Managers.AssetManager.Instance.GetMissionByObjectiveId(objectiveId);
        if (mission != null)
        {
            Logger.WriteLog(LogType.Debug, $"SetActiveObjective reaction {Template.COID}: ObjectiveID={objectiveId} belongs to Mission {mission.Id} '{mission.Name}' (Title: {mission.Title})");
        }
        else
        {
            Logger.WriteLog(LogType.Debug, $"SetActiveObjective reaction {Template.COID}: ObjectiveID={objectiveId} - no mission found containing this objective");
        }

        Logger.WriteLog(LogType.Debug, $"SetActiveObjective reaction {Template.COID}: ObjectiveID={objectiveId}, GenericVar3={Template.GenericVar3}, ObjectiveIDCheck={Template.ObjectiveIDCheck}");

        if (character != null)
        {
            Logger.WriteLog(LogType.Debug, $"SetActiveObjective reaction {Template.COID}: Setting active objective to {objectiveId} for character {character.Name}");
            // TODO: Store active objective on character when mission tracking is implemented
            // TODO: Give the player this mission if they don't have it yet
        }
        return true;
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
            case MapTransferType.ContinentObject:
                // MapTransferData is the continent/map ID to transfer to
                MapManager.Instance.TransferCharacterToMap(character, mapTransferData);
                break;

            case MapTransferType.Highway:
            case MapTransferType.Random:
            case MapTransferType.Mission:
            case MapTransferType.GMTest:
            case MapTransferType.RepairStation:
            case MapTransferType.Death:
            case MapTransferType.Warp:
            case MapTransferType.Arean:
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

    public override int GetCurrentHP() => 1; 
    public override int GetMaximumHP() => 1;
    public override int GetBareTeamFaction() => Faction;
}
