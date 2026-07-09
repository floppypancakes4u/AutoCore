namespace AutoCore.Game.Structures;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;

/// <summary>
/// Represents a player's current quest/mission state.
/// The client expects 72 bytes per quest in CreateCharacterExtendedPacket.
///
/// Structure (72 bytes total) - Partially reverse-engineered:
/// - Offset 0:  Mission ID (4 bytes, int)
/// - Offset 4:  Active Objective Sequence (1 byte)
/// - Offset 5:  State/Flags (1 byte)
/// - Offset 6:  Padding (2 bytes)
/// - Offset 8:  Objective progress data (64 bytes - 8 objectives x 8 bytes each)
/// </summary>
public class CharacterQuest
{
    public const int StructureSize = 72;
    public const int MaxObjectives = 8;

    public int MissionId { get; set; }
    public byte ActiveObjectiveSequence { get; set; }
    public byte State { get; set; } // 0 = active, 1 = completed, etc.

    // Progress for each objective (up to 8 on the wire; runtime may be larger)
    public int[] ObjectiveProgress { get; set; } = new int[MaxObjectives];
    public int[] ObjectiveMax { get; set; } = new int[MaxObjectives];

    public CharacterQuest(int missionId, byte activeObjectiveSequence = 0)
    {
        MissionId = missionId;
        ActiveObjectiveSequence = activeObjectiveSequence;
        State = 0; // Active

        for (int i = 0; i < MaxObjectives; i++)
        {
            ObjectiveProgress[i] = 0;
            ObjectiveMax[i] = 1;
        }
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(MissionId);
        writer.Write(ActiveObjectiveSequence);
        writer.Write(State);
        writer.Write((short)0);

        for (int i = 0; i < MaxObjectives; i++)
        {
            var progress = i < ObjectiveProgress.Length ? ObjectiveProgress[i] : 0;
            var max = i < ObjectiveMax.Length ? ObjectiveMax[i] : 1;
            writer.Write(progress);
            writer.Write(max);
        }
    }

    /// <summary>Size progress arrays from mission template assets.</summary>
    public void PopulateFromAssets()
    {
        PopulateFromMission(AssetManager.Instance.GetMission(MissionId));
    }

    public void PopulateFromMission(Mission mission)
    {
        if (mission?.Objectives is null || mission.Objectives.Count == 0)
            return;

        var maxSeq = 0;
        foreach (var objective in mission.Objectives.Values)
        {
            if (objective.Sequence > maxSeq)
                maxSeq = objective.Sequence;
        }

        var capacity = Math.Max(MaxObjectives, maxSeq + 1);
        if (ObjectiveProgress.Length < capacity)
        {
            ObjectiveProgress = new int[capacity];
            ObjectiveMax = new int[capacity];
        }

        for (var i = 0; i < ObjectiveMax.Length; i++)
            ObjectiveMax[i] = 1;

        foreach (var objective in mission.Objectives.Values)
            ObjectiveMax[objective.Sequence] = objective.CompleteCount > 0 ? objective.CompleteCount : 1;
    }
}
