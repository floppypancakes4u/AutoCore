namespace AutoCore.Game.Entities;

using System.Linq;
using AutoCore.Utils;

/// <summary>
/// Focused, non-tick mission world-state failure diagnostics (Pass 20).
/// Includes mission id, objective/phase, map, target COID, and action.
/// </summary>
internal static class MissionWorldStateLog
{
    public static void WarnMissingTarget(
        Reaction reaction,
        ClonedObjectBase activator,
        long targetCoid,
        string action)
    {
        var character = activator?.GetAsCharacter() ?? activator?.GetSuperCharacter(false);
        var mapId = activator?.Map?.ContinentId ?? reaction?.Map?.ContinentId ?? -1;
        WarnMissingTarget(character, mapId, targetCoid, action,
            $"reaction={reaction?.Template?.COID ?? 0} type={reaction?.Template?.ReactionType}");
    }

    public static void WarnMissingTarget(
        Character character,
        int mapId,
        long targetCoid,
        string action,
        string detail)
    {
        Logger.WriteLog(LogType.Warning,
            "MissionWorldState {0} target missing map={1} targetCoid={2} action={3} {4} {5}",
            action,
            mapId,
            targetCoid,
            action,
            DescribeQuest(character),
            detail);
    }

    public static void WarnCreateChildFailed(
        Reaction reaction,
        ClonedObjectBase activator,
        long targetCoid,
        SpawnPoint spawn)
    {
        var character = activator?.GetAsCharacter() ?? activator?.GetSuperCharacter(false);
        var mapId = activator?.Map?.ContinentId ?? reaction?.Map?.ContinentId ?? -1;
        WarnCreateChildFailed(character, mapId, targetCoid, spawn?.LastFailureDiagnostic);
    }

    public static void WarnCreateChildFailed(
        Character character,
        int mapId,
        long targetCoid,
        string diagnostic)
    {
        Logger.WriteLog(LogType.Warning,
            "MissionWorldState Create child failed map={0} targetCoid={1} action=Create {2} diag={3}",
            mapId,
            targetCoid,
            DescribeQuest(character),
            diagnostic ?? "(none)");
    }

    public static void WarnUnsupportedAction(
        Reaction reaction,
        ClonedObjectBase activator,
        string reason)
    {
        var character = activator?.GetAsCharacter() ?? activator?.GetSuperCharacter(false);
        Logger.WriteLog(LogType.Warning,
            "MissionWorldState unsupported action map={0} reaction={1} type={2} {3} {4}",
            activator?.Map?.ContinentId ?? reaction?.Map?.ContinentId ?? -1,
            reaction?.Template?.COID ?? 0,
            reaction?.Template?.ReactionType,
            DescribeQuest(character),
            reason);
    }

    private static string DescribeQuest(Character character)
    {
        if (character == null)
            return "mission=- phase=-";

        var quest = character.CurrentQuests.FirstOrDefault();
        if (quest == null)
        {
            return character.CompletedMissionIds.Count > 0
                ? $"mission=completed:{string.Join(',', character.CompletedMissionIds.Take(4))} phase=-"
                : "mission=- phase=-";
        }

        return $"mission={quest.MissionId} phase={quest.ActiveObjectiveSequence}";
    }
}
