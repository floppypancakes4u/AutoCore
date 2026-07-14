namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Temporary high-signal diagnostics for live mission reaction/tracking failures.
/// Uses <see cref="LogType.Command"/> so lines are visible even when Debug is noisy/filtered.
/// Prefix: <c>[MISSION-DIAG]</c> — grep sector logs for this.
/// Remove or gate once root cause is confirmed.
/// </summary>
internal static class MissionFlowDiag
{
    public static void Log(string message)
    {
        Logger.WriteLog(LogType.Command, "[MISSION-DIAG] " + message);
    }

    public static void Log(string format, params object[] args)
    {
        Logger.WriteLog(LogType.Command, "[MISSION-DIAG] " + string.Format(format, args));
    }

    public static string QuestSummary(Character character)
    {
        if (character?.CurrentQuests == null || character.CurrentQuests.Count == 0)
            return "quests=[]";

        return "quests=[" + string.Join("; ", character.CurrentQuests.Select(q =>
        {
            var prog = q.ActiveObjectiveSequence < q.ObjectiveProgress.Length
                ? q.ObjectiveProgress[q.ActiveObjectiveSequence]
                : -1;
            var max = q.ActiveObjectiveSequence < q.ObjectiveMax.Length
                ? q.ObjectiveMax[q.ActiveObjectiveSequence]
                : -1;
            return $"m{q.MissionId}:seq{q.ActiveObjectiveSequence}:p{prog}/{max}";
        })) + "]";
    }
}
