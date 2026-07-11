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

            if (!TryMatchKillRequirement(objective, victimCbid, victimCoid, victimFaction, continentId, out var numToKill))
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
                "Kill progress: mission={0} seq={1} objective={2} progress={3}/{4} victim coid={5} cbid={6}",
                quest.MissionId,
                seq,
                objective.ObjectiveId,
                quest.ObjectiveProgress[seq],
                needed,
                victimCoid,
                victimCbid);

            if (quest.ObjectiveProgress[seq] < needed)
            {
                IncompleteHandlerLog.Warn(
                    "KillProgress",
                    $"mission={quest.MissionId} seq={seq} objective={objective.ObjectiveId} progress={quest.ObjectiveProgress[seq]}/{needed}",
                    "Partial kill progress — ObjectiveState bitmask/slots not fully populated",
                    "Send ObjectiveState with FirstStateSlot floats / bitmask reflecting kill count.");

                conn.SendGamePacket(new ObjectiveStatePacket
                {
                    ObjectiveBitmask = 0u,
                    ObjectiveId = objective.ObjectiveId,
                });
                MissionPersistence.Instance.OnQuestChanged(killer, quest);
                NpcInteractHandler.PushJournalMissionList(conn, killer);
                TriggerManager.Instance.OnMissionStateChanged(
                    killer.CurrentVehicle ?? (ClonedObjectBase)killer);
                return;
            }

            NpcInteractHandler.AdvanceOrCompleteObjective(conn, killer, quest, mission, objective, source: "Kill");
            return;
        }
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
    {
        numToKill = 1;
        if (objective?.Requirements == null)
            return false;

        foreach (var req in objective.Requirements)
        {
            switch (req)
            {
                case ObjectiveRequirementKill kill:
                    if (!KillMatches(kill, victimCbid, victimFaction, continentId))
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
