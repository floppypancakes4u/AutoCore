namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative kill progress for mission kill / kill_aggregate requirements.
/// Generic matching by CBID, map template COID, or faction — no hardcoded mission ids.
/// </summary>
public static class MissionKillProgress
{
    /// <summary>
    /// Credit the murderer's character for kill requirements on active quests.
    /// Called from <see cref="ClonedObjectBase.OnDeath"/> after Murderer is set.
    /// </summary>
    public static void NotifyObjectKilled(ClonedObjectBase victim)
    {
        if (victim == null)
            return;

        var killer = ResolveKillerCharacter(victim);
        if (killer?.OwningConnection == null)
            return;

        var conn = killer.OwningConnection;
        var victimCbid = victim.CBID;
        var victimCoid = victim.ObjectId.Coid;
        var victimFaction = victim.Faction;
        // Final Exam style: kill req CBID is tVehicleTemplate id when TargetIsTemplateVehicle=1.
        var victimTemplateId = victim is Vehicle vehicle ? vehicle.TemplateId : -1;
        var continentId = killer.Map?.ContinentId ?? -1;

        foreach (var quest in killer.CurrentQuests.ToList())
        {
            if (killer.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective))
            {
                continue;
            }

            if (!TryMatchKillRequirement(
                    objective,
                    victimCbid,
                    victimCoid,
                    victimFaction,
                    continentId,
                    victimTemplateId,
                    out var numToKill))
                continue;

            var seq = quest.ActiveObjectiveSequence;
            EnsureProgressCapacity(quest, seq);

            quest.ObjectiveProgress[seq] = Math.Min(
                quest.ObjectiveProgress[seq] + 1,
                Math.Max(numToKill, quest.ObjectiveMax[seq]));

            var needed = Math.Max(1, numToKill);
            if (quest.ObjectiveMax[seq] > needed)
                needed = quest.ObjectiveMax[seq];

            Logger.WriteLog(LogType.Debug,
                "Kill progress: mission={0} seq={1} objective={2} progress={3}/{4} victim coid={5} cbid={6} templateId={7}",
                quest.MissionId,
                seq,
                objective.ObjectiveId,
                quest.ObjectiveProgress[seq],
                needed,
                victimCoid,
                victimCbid,
                victimTemplateId);

            // Always publish absolute kill progress to the client (0x2071).
            var statePacket = ObjectiveStateBuilder.Build(
                objective,
                quest.ObjectiveProgress[seq],
                needed);
            if (statePacket != null)
                conn.SendGamePacket(statePacket);
            MissionPersistence.Instance.OnQuestChanged(killer, quest);
            NpcInteractHandler.PushJournalMissionList(conn, killer);
            TriggerManager.Instance.OnMissionStateChanged(
                killer.CurrentVehicle ?? (ClonedObjectBase)killer);

            if (quest.ObjectiveProgress[seq] < needed)
                return;

            // Kill count satisfied.
            // Mid-chain: auto-advance to the next objective (kill → deliver, kill → kill, …).
            var hasNext = mission.Objectives.Values.Any(o => o.Sequence > seq);
            if (hasNext)
            {
                NpcInteractHandler.AdvanceOrCompleteObjective(
                    conn, killer, quest, mission, objective, source: "Kill");
                return;
            }

            // Final objective that is kill/kill_aggregate only: do NOT complete here.
            // Client marks interact state 8 (ready for turn-in) when Kill_Eval passes while
            // the quest is still active; CompleteText / NotCompleteText are shown at the
            // mission giver (FUN_0052b420). Completing requires dialog with mission.NPC.
            if (IsKillOnlyObjective(objective))
            {
                Logger.WriteLog(LogType.Debug,
                    "Kill progress: mission={0} objective={1} ready for giver turn-in ({2}/{3})",
                    quest.MissionId,
                    objective.ObjectiveId,
                    quest.ObjectiveProgress[seq],
                    needed);
                return;
            }

            // Multi-req final (e.g. kill+deliver on same objective): kill alone does not finish.
            // Deliver turn-in / other paths complete when all reqs are satisfied.
            if (HasNonKillRequirement(objective))
                return;

            NpcInteractHandler.AdvanceOrCompleteObjective(
                conn, killer, quest, mission, objective, source: "Kill");
            return;
        }
    }

    /// <summary>
    /// True when every authored requirement is kill or kill_aggregate (final bounty-style
    /// objectives that complete at the mission giver, not on the last kill).
    /// </summary>
    internal static bool IsKillOnlyObjective(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementKill and not ObjectiveRequirementKillAggregate)
                return false;
        }

        return true;
    }

    internal static bool HasNonKillRequirement(MissionObjective objective)
    {
        if (objective?.Requirements == null)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementKill and not ObjectiveRequirementKillAggregate)
                return true;
        }

        return false;
    }

    internal static Character ResolveKillerCharacter(ClonedObjectBase victim)
    {
        if (victim is null)
            return null;

        var murderer = victim.Murderer;
        if (murderer is null || murderer.Coid <= 0)
            return null;

        ClonedObjectBase murdererObj = null;
        if (victim.Map != null)
        {
            murdererObj = victim.Map.GetObjectByCoid(murderer.Coid)
                ?? victim.Map.GetObject(murderer.Coid);

            // Player vehicles are global TFID; also match by CurrentVehicle coid.
            if (murdererObj is null)
            {
                foreach (var player in victim.Map.Players)
                {
                    if (player?.CurrentVehicle != null
                        && player.CurrentVehicle.ObjectId.Coid == murderer.Coid)
                    {
                        return player;
                    }

                    if (player != null && player.ObjectId.Coid == murderer.Coid)
                        return player;
                }
            }
        }

        if (murdererObj is null)
        {
            try
            {
                murdererObj = ObjectManager.Instance?.GetObject(murderer);
            }
            catch
            {
                // ObjectManager may be uninitialized in unit tests.
                murdererObj = null;
            }
        }

        return murdererObj?.GetAsCharacter()
            ?? murdererObj?.GetSuperCharacter(false);
    }

    internal static bool TryMatchKillRequirement(
        MissionObjective objective,
        int victimCbid,
        long victimCoid,
        int victimFaction,
        int continentId,
        out int numToKill)
        => TryMatchKillRequirement(
            objective, victimCbid, victimCoid, victimFaction, continentId, victimTemplateId: -1, out numToKill);

    internal static bool TryMatchKillRequirement(
        MissionObjective objective,
        int victimCbid,
        long victimCoid,
        int victimFaction,
        int continentId,
        int victimTemplateId,
        out int numToKill)
    {
        numToKill = 1;
        if (objective?.Requirements == null)
            return false;

        foreach (var req in objective.Requirements)
        {
            switch (req)
            {
                case ObjectiveRequirementKill kill:
                    if (!KillMatches(kill, victimCbid, victimFaction, continentId, victimTemplateId))
                        continue;
                    numToKill = kill.NumToKill > 0 ? kill.NumToKill : 1;
                    return true;

                case ObjectiveRequirementKillAggregate agg:
                    if (!KillAggregateMatches(agg, victimCbid, victimCoid, victimFaction, continentId))
                        continue;
                    numToKill = agg.NumToKill > 0 ? agg.NumToKill : 1;
                    return true;
            }
        }

        return false;
    }

    internal static bool KillMatches(
        ObjectiveRequirementKill kill,
        int victimCbid,
        int victimFaction,
        int continentId)
        => KillMatches(kill, victimCbid, victimFaction, continentId, victimTemplateId: -1);

    internal static bool KillMatches(
        ObjectiveRequirementKill kill,
        int victimCbid,
        int victimFaction,
        int continentId,
        int victimTemplateId)
    {
        if (kill == null)
            return false;

        if (kill.NegativeKill)
            return false;

        if (kill.ContinentId > 0 && continentId > 0 && kill.ContinentId != continentId)
            return false;

        if (kill.TargetIsPlayer)
            return false;

        if (kill.TargetIsFaction)
            return kill.TargetCBID >= 0 && victimFaction == kill.TargetCBID;

        // Final Exam / many tutorial kills: CBID field is tVehicleTemplate id, not clonebase CBID.
        if (kill.TargetIsTemplateVehicle)
        {
            if (kill.TargetCBID < 0)
                return false;

            if (victimTemplateId == kill.TargetCBID)
                return true;

            // Fallback: template row may be missing TemplateId on the vehicle but chassis CBID matches.
            var template = AssetManager.Instance.GetVehicleTemplate(kill.TargetCBID);
            return template != null && template.VehicleCbid > 0 && victimCbid == template.VehicleCbid;
        }

        if (kill.TargetCBID >= 0)
            return victimCbid == kill.TargetCBID;

        return false;
    }

    internal static bool KillAggregateMatches(
        ObjectiveRequirementKillAggregate agg,
        int victimCbid,
        long victimCoid,
        int victimFaction,
        int continentId)
    {
        if (agg == null)
            return false;

        if (agg.NegativeKill)
            return false;

        if (agg.ContinentId > 0 && continentId > 0 && agg.ContinentId != continentId)
            return false;

        if (agg.TemplateTargets.Count > 0
            && agg.TemplateTargets.Any(t => t == victimCoid || t == (int)victimCoid))
        {
            return true;
        }

        if (agg.Targets.Count > 0 && agg.Targets.Contains(victimCbid))
            return true;

        if (agg.TargetIsFaction && agg.Targets.Count > 0 && agg.Targets.Contains(victimFaction))
            return true;

        return false;
    }

    internal static void EnsureProgressCapacity(CharacterQuest quest, int seq)
    {
        if (quest == null || seq < quest.ObjectiveProgress.Length)
            return;

        var size = seq + 1;
        var progress = new int[size];
        var max = new int[size];
        Array.Copy(quest.ObjectiveProgress, progress, quest.ObjectiveProgress.Length);
        Array.Copy(quest.ObjectiveMax, max, quest.ObjectiveMax.Length);
        for (var i = quest.ObjectiveMax.Length; i < size; i++)
            max[i] = 1;
        quest.ObjectiveProgress = progress;
        quest.ObjectiveMax = max;
    }
}
