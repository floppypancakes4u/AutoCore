namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Managers;
using AutoCore.Game.Structures;

public partial class Character
{
    /// <summary>
    /// Load persisted quest + completed-mission rows into <see cref="CurrentQuests"/> /
    /// <see cref="CompletedMissionIds"/>. Objective max counts are re-derived from mission templates;
    /// only stored progress is overlaid.
    /// </summary>
    internal void LoadMissions(CharContext context)
    {
        CurrentQuests.Clear();
        CompletedMissionIds.Clear();

        if (context == null)
            return;

        var coid = ObjectId.Coid;

        var questRows = context.CharacterQuests
            .Where(q => q.CharacterCoid == coid)
            .ToList();
        var completedRows = context.CharacterCompletedMissions
            .Where(c => c.CharacterCoid == coid)
            .ToList();

        ApplyMissionRows(questRows, completedRows);

        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Debug,
            $"Character.LoadMissions: coid={coid} loaded {CurrentQuests.Count} active "
            + $"[{string.Join(",", CurrentQuests.Select(q => q.MissionId))}], "
            + $"{CompletedMissionIds.Count} completed [{string.Join(",", CompletedMissionIds)}]");
    }

    /// <summary>Test hook to seed mission state without a DB.</summary>
    internal void SetMissionsForTests(
        IEnumerable<CharacterQuestData> questRows,
        IEnumerable<CharacterCompletedMissionData> completedRows)
    {
        CurrentQuests.Clear();
        CompletedMissionIds.Clear();
        ApplyMissionRows(
            questRows?.ToList() ?? new List<CharacterQuestData>(),
            completedRows?.ToList() ?? new List<CharacterCompletedMissionData>());
    }

    private void ApplyMissionRows(
        List<CharacterQuestData> questRows,
        List<CharacterCompletedMissionData> completedRows)
    {
        foreach (var row in completedRows)
            CompletedMissionIds.Add(row.MissionId);

        foreach (var row in questRows)
        {
            var quest = new CharacterQuest(row.MissionId, row.ActiveObjectiveSequence)
            {
                State = row.State,
            };

            // Re-derive ObjectiveMax from the mission template (CompleteCount), then overlay progress.
            quest.PopulateFromAssets();

            var stored = MissionPersistence.UnpackProgress(row.ObjectiveProgress);
            if (stored.Length > 0)
            {
                if (quest.ObjectiveProgress.Length < stored.Length)
                {
                    var grown = new int[stored.Length];
                    Array.Copy(quest.ObjectiveProgress, grown, quest.ObjectiveProgress.Length);
                    quest.ObjectiveProgress = grown;
                }

                Array.Copy(stored, quest.ObjectiveProgress, stored.Length);
            }

            CurrentQuests.Add(quest);
        }
    }
}
