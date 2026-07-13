using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Infrastructure;

using AutoCore.Game.Entities;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;

/// <summary>Shared observable-state assertions for mission contracts.</summary>
public static class MissionInvariantAssertions
{
    public static void AssertActiveMission(Character character, int missionId, byte? expectedSequence = null)
    {
        var quest = character.CurrentQuests.SingleOrDefault(q => q.MissionId == missionId);
        Assert.IsNotNull(quest, $"Expected active mission {missionId}");
        if (expectedSequence.HasValue)
            Assert.AreEqual(expectedSequence.Value, quest.ActiveObjectiveSequence);
        Assert.IsFalse(character.CompletedMissionIds.Contains(missionId),
            $"Mission {missionId} must not be both active and completed");
    }

    public static void AssertNotActive(Character character, int missionId)
    {
        Assert.IsFalse(character.CurrentQuests.Any(q => q.MissionId == missionId),
            $"Mission {missionId} should not be active");
    }

    public static void AssertCompleted(Character character, int missionId)
    {
        AssertNotActive(character, missionId);
        Assert.IsTrue(character.CompletedMissionIds.Contains(missionId),
            $"Mission {missionId} should be completed");
    }

    public static void AssertProgressAtMost(CharacterQuest quest, byte seq, int maximum)
    {
        Assert.IsTrue(seq < quest.ObjectiveProgress.Length);
        Assert.IsTrue(quest.ObjectiveProgress[seq] <= maximum,
            $"Progress {quest.ObjectiveProgress[seq]} exceeds max {maximum} for seq {seq}");
        Assert.IsTrue(quest.ObjectiveProgress[seq] >= 0);
    }

    public static void AssertPlayerIsolation(Character playerA, Character playerB)
    {
        Assert.AreNotEqual(playerA.ObjectId.Coid, playerB.ObjectId.Coid);
        // Player B's completed set must not be a subset forced by A's mutations in isolation tests;
        // callers assert specific mission presence after an operation on A only.
    }

    /// <summary>
    /// After any grant/progress path, template CompleteCount and Requirements count must match
    /// the values captured before the operation (templates are shared singletons in AssetManager).
    /// </summary>
    public static void AssertTemplateUnchanged(Mission mission, int objectiveId, int reqCount, int completeCount)
    {
        Assert.IsTrue(mission.Objectives.Values.Any(o => o.ObjectiveId == objectiveId));
        var objective = mission.Objectives.Values.First(o => o.ObjectiveId == objectiveId);
        Assert.AreEqual(reqCount, objective.Requirements.Count, "Template requirements mutated");
        Assert.AreEqual(completeCount, objective.CompleteCount, "Template CompleteCount mutated");
    }
}
