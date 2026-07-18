namespace AutoCore.Game.Structures;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>
/// Represents a player's current quest/mission state.
/// The client expects 72 bytes per quest in CreateCharacterExtendedPacket.
///
/// Structure (72 bytes total), verified against CVOGCharacter_ApplyCreateFromPacket:
/// - +0x00 mission id; +0x04 reserved
/// - +0x08 ten mission saved-state dwords
/// - +0x30 global active objective id
/// - +0x34 four objective saved-state floats; +0x44 reserved
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
        writer.Write(0); // +0x04 reserved/ignored by the client parser

        // CVOGCharacter_ApplyCreateFromPacket copies these ten dwords into the mission's
        // 0x30-byte saved-state object. A newly granted mission initializes them to -1.
        for (var i = 0; i < 10; ++i)
            writer.Write(-1);

        var mission = AssetManager.Instance.GetMission(MissionId);
        var objective = mission != null
            && mission.Objectives.TryGetValue(ActiveObjectiveSequence, out var activeObjective)
                ? activeObjective
                : null;

        writer.Write(objective?.ObjectiveId ?? -1); // +0x30

        var slots = new float[4];
        if (objective != null)
        {
            var progress = ActiveObjectiveSequence < ObjectiveProgress.Length
                ? ObjectiveProgress[ActiveObjectiveSequence]
                : 0;
            var maximum = ActiveObjectiveSequence < ObjectiveMax.Length
                ? Math.Max(1, ObjectiveMax[ActiveObjectiveSequence])
                : 1;

            // Multi-pad patrol + UseItem + kill: client Eval casts slot floats to absolute counts
            // (0,1,2…), not 0..1 ratios. Kill_Eval: (float)NumToKill <= slotFloat.
            var multiPad = objective.Requirements?
                .OfType<ObjectiveRequirementPatrol>()
                .FirstOrDefault(p => MissionPatrolProgress.CountListedTargets(p) > 1
                    || (p.Laps > 1 && MissionPatrolProgress.CountListedTargets(p) > 0));
            var useItem = objective.Requirements?
                .OfType<ObjectiveRequirementUseItem>()
                .FirstOrDefault();
            var killReq = objective.Requirements?
                .FirstOrDefault(r => r is ObjectiveRequirementKill or ObjectiveRequirementKillAggregate);
            var collectReq = objective.Requirements?
                .OfType<ObjectiveRequirementCollect>()
                .FirstOrDefault();

            if (multiPad != null)
            {
                var slot = multiPad.FirstStateSlot;
                if (slot < slots.Length)
                    slots[slot] = Math.Max(0, progress);
            }
            else if (useItem != null)
            {
                var slot = useItem.FirstStateSlot;
                if (slot < slots.Length)
                    slots[slot] = Math.Max(0, progress);
            }
            else if (killReq != null)
            {
                var slot = killReq.FirstStateSlot;
                if (slot < slots.Length)
                    slots[slot] = Math.Max(0, progress);
            }
            else if (collectReq != null)
            {
                var slot = collectReq.FirstStateSlot;
                if (slot < slots.Length)
                    slots[slot] = Math.Max(0, progress);
            }
            else
            {
                var normalized = Math.Clamp((float)progress / maximum, 0.0f, 1.0f);
                var authoredSlots = objective.Requirements
                    .Select(requirement => (int)requirement.FirstStateSlot)
                    .Where(slot => slot < slots.Length)
                    .Distinct()
                    .ToList();
                if (authoredSlots.Count == 0)
                    authoredSlots.Add(0);
                foreach (var slot in authoredSlots)
                    slots[slot] = normalized;
            }
        }

        foreach (var slot in slots)
            writer.Write(slot);
        writer.Write(0); // +0x44 reserved/ignored by the client parser
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
            ObjectiveMax[objective.Sequence] = ResolveObjectiveMax(objective);
    }

    /// <summary>
    /// CompleteCount when authored; otherwise max of UseItem RepeatCount / kill NumToKill; else 1.
    /// Grouchy Gun (and many kill quests) author CompleteCount=0 with NumToKill on the requirement.
    /// </summary>
    internal static int ResolveObjectiveMax(MissionObjective objective)
    {
        if (objective == null)
            return 1;

        if (objective.CompleteCount > 0)
            return objective.CompleteCount;

        var derived = 0;
        if (objective.Requirements != null)
        {
            foreach (var req in objective.Requirements)
            {
                switch (req)
                {
                    case ObjectiveRequirementUseItem useItem when useItem.RepeatCount > derived:
                        derived = useItem.RepeatCount;
                        break;
                    case ObjectiveRequirementKill kill when kill.NumToKill > derived:
                        derived = kill.NumToKill;
                        break;
                    case ObjectiveRequirementKillAggregate agg when agg.NumToKill > derived:
                        derived = agg.NumToKill;
                        break;
                    case ObjectiveRequirementCollect collect when collect.NumToCollect > derived:
                        derived = collect.NumToCollect;
                        break;
                }
            }
        }

        return derived > 0 ? derived : 1;
    }
}
