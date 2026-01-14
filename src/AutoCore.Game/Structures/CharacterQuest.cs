namespace AutoCore.Game.Structures;

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

    // Progress for each objective (up to 8)
    // Each objective: 4 bytes current progress, 4 bytes max progress
    public int[] ObjectiveProgress { get; set; } = new int[MaxObjectives];
    public int[] ObjectiveMax { get; set; } = new int[MaxObjectives];

    public CharacterQuest(int missionId, byte activeObjectiveSequence = 0)
    {
        MissionId = missionId;
        ActiveObjectiveSequence = activeObjectiveSequence;
        State = 0; // Active

        // Initialize all objectives with 0 progress
        for (int i = 0; i < MaxObjectives; i++)
        {
            ObjectiveProgress[i] = 0;
            ObjectiveMax[i] = 1; // Default max of 1
        }
    }

    /// <summary>
    /// Write the quest state to the packet (72 bytes total).
    /// </summary>
    public void Write(BinaryWriter writer)
    {
        // Mission ID (4 bytes)
        writer.Write(MissionId);

        // Active objective sequence (1 byte)
        writer.Write(ActiveObjectiveSequence);

        // State/Flags (1 byte)
        writer.Write(State);

        // Padding (2 bytes)
        writer.Write((short)0);

        // Objective progress data (64 bytes = 8 objectives x 8 bytes)
        for (int i = 0; i < MaxObjectives; i++)
        {
            writer.Write(ObjectiveProgress[i]);  // 4 bytes
            writer.Write(ObjectiveMax[i]);       // 4 bytes
        }

        // Total: 4 + 1 + 1 + 2 + 64 = 72 bytes
    }
}
