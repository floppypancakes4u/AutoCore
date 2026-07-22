namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative collect kill-to-loot: roll <see cref="ObjectiveRequirementCollect.OptionalDropPercent"/>
/// against optional target CBIDs on death, spawn mission-flagged ground loot, and sync progress from inventory.
/// </summary>
public static class MissionCollectProgress
{
    private static readonly Random DefaultRng = new();

    /// <summary>
    /// Test seam: returns a unit interval [0,1) used as <c>roll * 100</c> vs OptionalDropPercent.
    /// </summary>
    internal static Func<double> NextDropRoll01 { get; set; } = DefaultNextDropRoll01;

    internal static void ResetDropRollForTests()
        => NextDropRoll01 = DefaultNextDropRoll01;

    private static double DefaultNextDropRoll01()
    {
        lock (DefaultRng)
            return DefaultRng.NextDouble();
    }

    /// <summary>
    /// On death: for each matching active Collect req, roll drop % and spawn mission loot near the victim.
    /// </summary>
    public static void NotifyObjectKilled(ClonedObjectBase victim)
    {
        if (victim == null)
            return;

        var killer = MissionKillProgress.ResolveKillerCharacter(victim);
        if (killer?.OwningConnection == null || killer.Inventory == null)
            return;

        var map = victim.Map;
        if (map == null)
            return;

        var victimCbid = victim.CBID;
        var victimTemplateId = victim is Vehicle vehicle ? vehicle.TemplateId : -1;
        var victimIsPlayer = victim is Character;
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

            if (!TryGetMatchingCollect(
                    objective,
                    victimCbid,
                    continentId,
                    victimTemplateId,
                    victimIsPlayer,
                    out var collect))
            {
                continue;
            }

            var needed = NumNeeded(collect);
            var have = killer.Inventory.CountByCbid(collect.ItemCBID);
            if (have >= needed)
                continue;

            if (!ShouldDrop(collect))
                continue;

            if (!TrySpawnCollectLoot(victim, collect.ItemCBID, out _))
                continue;

            Logger.WriteLog(LogType.Debug,
                "Collect drop: mission={0} item={1} victim cbid={2} pct={3} have={4}/{5}",
                quest.MissionId,
                collect.ItemCBID,
                victimCbid,
                collect.OptionalDropPercent,
                have,
                needed);

            // First matching quest only (same product limit as kill credit).
            return;
        }
    }

    /// <summary>
    /// After a cargo change for <paramref name="itemCbid"/>, recount Collect progress for active quests.
    /// Continent filters apply to drops only — progress follows cargo wherever the player is.
    /// </summary>
    public static void SyncProgressFromInventory(Character character, int itemCbid)
    {
        if (character?.OwningConnection == null || character.Inventory == null || itemCbid <= 0)
            return;

        var conn = character.OwningConnection;

        foreach (var quest in character.CurrentQuests.ToList())
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective))
            {
                continue;
            }

            var collect = objective.Requirements?
                .OfType<ObjectiveRequirementCollect>()
                .FirstOrDefault(c => c.ItemCBID == itemCbid);
            if (collect == null)
                continue;

            ApplyInventoryProgress(character, conn, quest, mission, objective, collect);
            return;
        }
    }

    /// <summary>
    /// Recount Collect progress for one quest from cargo (no continent gate).
    /// Used at giver turn-in so stale ObjectiveProgress cannot block a cargo-complete collect.
    /// </summary>
    public static void SyncQuestProgressFromInventory(Character character, CharacterQuest quest)
    {
        if (character?.Inventory == null || quest == null)
            return;

        var mission = AssetManager.Instance.GetMission(quest.MissionId);
        if (mission == null
            || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
            || objective?.Requirements == null)
        {
            return;
        }

        var conn = character.OwningConnection;
        foreach (var collect in objective.Requirements.OfType<ObjectiveRequirementCollect>())
        {
            if (collect.ItemCBID <= 0)
                continue;

            ApplyInventoryProgress(character, conn, quest, mission, objective, collect, advanceOnFull: false);
        }
    }

    private static void ApplyInventoryProgress(
        Character character,
        AutoCore.Game.TNL.TNLConnection conn,
        CharacterQuest quest,
        Mission mission,
        MissionObjective objective,
        ObjectiveRequirementCollect collect,
        bool advanceOnFull = true)
    {
        var itemCbid = collect.ItemCBID;
        var needed = NumNeeded(collect);
        var have = character.Inventory.CountByCbid(itemCbid);
        var progress = Math.Min(have, needed);

        var seq = quest.ActiveObjectiveSequence;
        MissionKillProgress.EnsureProgressCapacity(quest, seq);
        quest.ObjectiveProgress[seq] = progress;
        if (quest.ObjectiveMax[seq] < needed)
            quest.ObjectiveMax[seq] = needed;

        Logger.WriteLog(LogType.Debug,
            "Collect progress: mission={0} seq={1} objective={2} progress={3}/{4} item={5}",
            quest.MissionId,
            seq,
            objective.ObjectiveId,
            progress,
            needed,
            itemCbid);

        if (conn != null)
        {
            var statePacket = ObjectiveStateBuilder.Build(objective, progress, needed);
            if (statePacket != null)
                conn.SendGamePacket(statePacket);

            MissionPersistence.Instance.OnQuestChanged(character, quest);
            NpcInteractHandler.PushJournalMissionList(conn, character);
            TriggerManager.Instance.OnMissionStateChanged(
                character.CurrentVehicle ?? (ClonedObjectBase)character);
        }
        else
        {
            MissionPersistence.Instance.OnQuestChanged(character, quest);
        }

        if (!advanceOnFull || progress < needed)
            return;

        var hasNext = mission.Objectives.Values.Any(o => o.Sequence > seq);
        if (hasNext)
        {
            NpcInteractHandler.AdvanceOrCompleteObjective(
                conn, character, quest, mission, objective, source: "Collect");
            return;
        }

        if (IsCollectOnlyObjective(objective))
        {
            Logger.WriteLog(LogType.Debug,
                "Collect progress: mission={0} objective={1} ready for giver turn-in ({2}/{3})",
                quest.MissionId,
                objective.ObjectiveId,
                progress,
                needed);
            return;
        }

        if (HasNonCollectRequirement(objective))
            return;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn, character, quest, mission, objective, source: "Collect");
    }

    internal static bool ShouldDrop(ObjectiveRequirementCollect collect)
    {
        if (collect == null || collect.OptionalDropPercent <= 0f || collect.ItemCBID <= 0)
            return false;

        var roll = NextDropRoll01() * 100.0;
        return roll < collect.OptionalDropPercent;
    }

    internal static bool CollectMatches(
        ObjectiveRequirementCollect collect,
        int victimCbid,
        int continentId,
        int victimTemplateId,
        bool victimIsPlayer)
    {
        if (collect == null)
            return false;

        if (collect.ItemCBID <= 0 || collect.OptionalDropPercent <= 0f)
            return false;

        if (collect.ContinentId > 0 && continentId > 0 && collect.ContinentId != continentId)
            return false;

        if (collect.TargetIsPlayer)
            return victimIsPlayer;

        if (collect.TargetIsTemplateVehicle)
        {
            // Optional targets hold template ids when TargetIsTemplateVehicle is set.
            return HasOptionalTarget(collect, victimTemplateId)
                || HasOptionalTarget(collect, victimCbid);
        }

        if (collect.TargetCount > 0 && HasAnyPositiveOptionalTarget(collect))
            return HasOptionalTarget(collect, victimCbid);

        // No positive OptionalTargetCBID list: do not invent open-world drops.
        return false;
    }

    internal static bool IsCollectOnlyObjective(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementCollect)
                return false;
        }

        return true;
    }

    internal static bool HasNonCollectRequirement(MissionObjective objective)
    {
        if (objective?.Requirements == null)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementCollect)
                return true;
        }

        return false;
    }

    internal static int NumNeeded(ObjectiveRequirementCollect collect)
        => collect != null && collect.NumToCollect > 0 ? collect.NumToCollect : 1;

    private static bool TryGetMatchingCollect(
        MissionObjective objective,
        int victimCbid,
        int continentId,
        int victimTemplateId,
        bool victimIsPlayer,
        out ObjectiveRequirementCollect collect)
    {
        collect = null;
        if (objective?.Requirements == null)
            return false;

        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementCollect candidate)
                continue;

            if (!CollectMatches(candidate, victimCbid, continentId, victimTemplateId, victimIsPlayer))
                continue;

            collect = candidate;
            return true;
        }

        return false;
    }

    private static bool HasAnyPositiveOptionalTarget(ObjectiveRequirementCollect collect)
    {
        var n = Math.Clamp(collect.TargetCount, 0, collect.OptinonalTargets?.Length ?? 0);
        for (var i = 0; i < n; i++)
        {
            if (collect.OptinonalTargets[i] > 0)
                return true;
        }

        return false;
    }

    private static bool HasOptionalTarget(ObjectiveRequirementCollect collect, int id)
    {
        if (id <= 0)
            return false;

        var n = Math.Clamp(collect.TargetCount, 0, collect.OptinonalTargets?.Length ?? 0);
        for (var i = 0; i < n; i++)
        {
            if (collect.OptinonalTargets[i] == id)
                return true;
        }

        return false;
    }

    private static bool TrySpawnCollectLoot(ClonedObjectBase victim, int itemCbid, out long spawnedCoid)
    {
        spawnedCoid = -1;
        var map = victim.Map;
        if (map == null || itemCbid <= 0)
            return false;

        return LootManager.Instance.TrySpawnLootItem(
            itemCbid,
            victim.Position,
            victim.Rotation,
            map,
            out spawnedCoid,
            possibleMissionItem: true);
    }
}
