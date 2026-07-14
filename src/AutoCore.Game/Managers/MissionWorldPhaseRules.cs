namespace AutoCore.Game.Managers;

using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>
/// Pure mission-phase rules used by <see cref="SectorMap"/> / <see cref="NpcInteractHandler"/>.
/// Kept free of map/entity dependencies so TDD can cover 95%+ of the decision surface.
/// </summary>
public static class MissionWorldPhaseRules
{
    /// <summary>
    /// True when deliver targets a CBID other than the mission giver (pad / alternate form).
    /// </summary>
    public static bool HasAlternateFormDeliver(MissionObjective objective, int giverNpcCbid)
    {
        if (objective?.Requirements == null || giverNpcCbid <= 0)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req is ObjectiveRequirementDeliver d
                && d.NPCTargetCompletes
                && d.NPCTargetCBID > 0
                && d.NPCTargetCBID != giverNpcCbid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deliver CBIDs that need pad Create / world reconstruction for this objective.
    /// Same-NPC return missions (deliver CBID == giver) are excluded.
    /// </summary>
    public static IEnumerable<int> CollectPadDeliverCbids(MissionObjective objective, int giverNpcCbid)
    {
        if (objective?.Requirements == null)
            yield break;

        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementDeliver d
                || !d.NPCTargetCompletes
                || d.NPCTargetCBID <= 0)
            {
                continue;
            }

            if (giverNpcCbid > 0 && d.NPCTargetCBID == giverNpcCbid)
                continue;

            yield return d.NPCTargetCBID;
        }
    }

    /// <summary>
    /// Completing deliver turn-in CBIDs on an objective (includes same-NPC returns).
    /// Used so active turn-in targets stay interactable even if they were previously
    /// phase-suppressed as completed alternate-form givers.
    /// </summary>
    public static IEnumerable<int> CollectActiveCompletingDeliverCbids(MissionObjective objective)
    {
        if (objective?.Requirements == null)
            yield break;

        foreach (var req in objective.Requirements)
        {
            if (req is ObjectiveRequirementDeliver d
                && d.NPCTargetCompletes
                && d.NPCTargetCBID > 0)
            {
                yield return d.NPCTargetCBID;
            }
        }
    }

    /// <summary>
    /// Active completing deliver CBIDs always win over completed-giver suppress.
    /// Removes any CBID in <paramref name="activeDeliverCbids"/> from
    /// <paramref name="giverCbidsToSuppress"/> in place.
    /// </summary>
    public static void ExcludeActiveDeliverFromGiverSuppress(
        HashSet<int> giverCbidsToSuppress,
        IEnumerable<int> activeDeliverCbids)
    {
        if (giverCbidsToSuppress == null || activeDeliverCbids == null)
            return;

        foreach (var cbid in activeDeliverCbids)
        {
            if (cbid > 0)
                giverCbidsToSuppress.Remove(cbid);
        }
    }

    /// <summary>
    /// Client <c>CVOGCharacter_CheckMissionRequirements</c>: <c>sinReqRace</c> /
    /// <c>sinReqClass</c> as short; <c>-1</c> (0xFFFF) means unrestricted.
    /// <paramref name="hasCharacterBody"/> false when race/class cannot be resolved.
    /// </summary>
    public static bool MeetsRaceClassRequirements(
        short reqRace,
        short reqClass,
        bool hasCharacterBody,
        int characterRace,
        int characterClass)
    {
        var needRace = reqRace != -1;
        var needClass = reqClass != -1;
        if (!needRace && !needClass)
            return true;

        if (!hasCharacterBody)
            return false;

        if (needRace && characterRace != reqRace)
            return false;

        if (needClass && characterClass != reqClass)
            return false;

        return true;
    }

    /// <summary>
    /// True when a completed non-repeatable mission with a positive giver NPC used an
    /// alternate-form deliver that is <b>not</b> pad-class (standing-NPC turn-in only).
    /// Used to clear sticky giver suppress without undoing Final Exam pad faces.
    /// </summary>
    public static bool IsCompletedNonPadAlternateFormGiver(
        bool missionValid,
        bool isRepeatable,
        int giverNpcCbid,
        bool hasPadClassAlternateDeliver,
        bool hasAnyAlternateFormDeliver)
    {
        if (!missionValid || isRepeatable || giverNpcCbid <= 0)
            return false;

        if (hasPadClassAlternateDeliver)
            return false;

        return hasAnyAlternateFormDeliver;
    }

    /// <summary>
    /// Kill spawn-list types from an active kill objective (template vehicle id or CBID).
    /// </summary>
    public static IEnumerable<int> CollectKillSpawnTypes(MissionObjective objective)
    {
        if (objective?.Requirements == null)
            yield break;

        var seen = new HashSet<int>();
        foreach (var req in objective.Requirements)
        {
            switch (req)
            {
                case ObjectiveRequirementKill kill when !kill.NegativeKill && !kill.TargetIsPlayer
                    && !kill.TargetIsFaction && kill.TargetCBID > 0:
                    if (seen.Add(kill.TargetCBID))
                        yield return kill.TargetCBID;
                    break;

                case ObjectiveRequirementKillAggregate agg when !agg.NegativeKill:
                    foreach (var t in agg.Targets)
                    {
                        if (t > 0 && seen.Add(t))
                            yield return t;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// True when AutoPatrol alone must not complete the objective (sibling deliver remains).
    /// </summary>
    public static bool HasBlockingDeliverSibling(MissionObjective objective, RequirementType satisfiedType)
    {
        if (objective?.Requirements == null || objective.Requirements.Count <= 1)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req.RequirementType == satisfiedType)
                continue;

            if (req is ObjectiveRequirementDeliver deliver
                && deliver.NPCTargetCompletes
                && deliver.NPCTargetCBID > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when dialog deliver turn-in should delayed-force 0x2070 so client clears patrol UI.
    /// </summary>
    public static bool NeedsForceClientCompleteAfterDeliver(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count <= 1)
            return false;

        var hasDeliver = false;
        var hasOther = false;
        foreach (var req in objective.Requirements)
        {
            if (req is ObjectiveRequirementDeliver d && d.NPCTargetCompletes)
                hasDeliver = true;
            else
                hasOther = true;
        }

        return hasDeliver && hasOther;
    }
}
