namespace AutoCore.Game.Mission;

using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Builds S2C <see cref="ObjectiveStatePacket"/> (0x2071) for client progress UI.
/// Client <c>Client_RecvObjectiveState</c> always copies slot floats, then only invokes
/// requirement callbacks when <c>lChangeBitmask</c> bit <em>i</em> is set for requirement index
/// <em>i</em> (not <see cref="Requirements.ObjectiveRequirement.FirstStateSlot"/>).
/// </summary>
public static class ObjectiveStateBuilder
{
    /// <summary>
    /// Build an ObjectiveState packet from objective-level progress/max.
    /// Progress is written into each requirement's authored <c>FirstStateSlot</c> float as a 0..1 ratio.
    /// Bitmask bits mark every requirement index that should receive a client callback.
    /// </summary>
    public static ObjectiveStatePacket Build(MissionObjective objective, int progress, int maximum)
    {
        if (objective == null)
            return null;

        var packet = new ObjectiveStatePacket
        {
            ObjectiveId = objective.ObjectiveId,
        };

        var max = maximum > 0 ? maximum : 1;
        var ratio = Math.Clamp((float)progress / max, 0.0f, 1.0f);

        var requirements = objective.Requirements;
        if (requirements == null || requirements.Count == 0)
        {
            // No authored requirements: still publish slot 0 so progress is not silent.
            packet.ObjectiveBitmask = 1u;
            packet.SlotProgress[0] = ratio;
            return packet;
        }

        uint bitmask = 0u;
        for (var i = 0; i < requirements.Count; i++)
        {
            if (i >= 32)
                break;

            bitmask |= 1u << i;
            var slot = requirements[i].FirstStateSlot;
            if (slot < ObjectiveStatePacket.SlotCount)
                packet.SlotProgress[slot] = ratio;
        }

        packet.ObjectiveBitmask = bitmask;
        return packet;
    }

    /// <summary>
    /// Build from a live quest's progress arrays for the given objective sequence.
    /// </summary>
    public static ObjectiveStatePacket Build(MissionObjective objective, CharacterQuest quest)
    {
        if (objective == null)
            return null;

        var seq = objective.Sequence;
        var progress = quest != null && seq < quest.ObjectiveProgress.Length
            ? quest.ObjectiveProgress[seq]
            : 0;
        var maximum = quest != null && seq < quest.ObjectiveMax.Length
            ? quest.ObjectiveMax[seq]
            : Math.Max(1, objective.CompleteCount);

        return Build(objective, progress, maximum);
    }

    /// <summary>
    /// Turn-in prep: mark the active deliver (or whole objective) complete for client dialog.
    /// </summary>
    public static ObjectiveStatePacket BuildTurnInReady(MissionObjective objective)
    {
        if (objective == null)
            return null;

        var maximum = Math.Max(1, objective.CompleteCount);
        return Build(objective, progress: maximum, maximum: maximum);
    }

    /// <summary>
    /// Multi-pad patrol mid-route sync only. Client
    /// <c>CVOGObjectiveRequirement_Patrol_GetTarget/Eval</c> treats the patrol slot float as an
    /// <b>absolute pad count</b> (0,1,2…), not a 0..1 ratio. Do not use for kill/deliver or
    /// create-packet restore.
    /// </summary>
    public static ObjectiveStatePacket BuildPatrolPadCount(
        MissionObjective objective,
        ObjectiveRequirementPatrol patrol,
        int padsCompleted)
    {
        if (objective == null || patrol == null)
            return null;

        var packet = new ObjectiveStatePacket
        {
            ObjectiveId = objective.ObjectiveId,
        };

        var reqIndex = objective.Requirements?.IndexOf(patrol) ?? 0;
        if (reqIndex < 0)
            reqIndex = 0;
        if (reqIndex < 32)
            packet.ObjectiveBitmask = 1u << reqIndex;
        else
            packet.ObjectiveBitmask = 1u;

        var slot = patrol.FirstStateSlot;
        if (slot < ObjectiveStatePacket.SlotCount)
            packet.SlotProgress[slot] = Math.Max(0, padsCompleted);

        return packet;
    }
}
