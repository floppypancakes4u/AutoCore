namespace AutoCore.Game.Managers;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative UseItem progress for mission objectives.
/// Generic matching by PrimaryCOID / PrimaryCBID and optional SecondaryCBID cargo —
/// no hardcoded mission ids.
/// </summary>
public static class MissionUseItemProgress
{
    /// <summary>
    /// Handle C2S UseObject against an active UseItem requirement.
    /// Returns true when the packet was consumed as a useitem interact (success or soft reject
    /// that should not fall through to NPC dialog).
    /// </summary>
    public static bool TryHandleUseObject(
        TNLConnection conn,
        Character character,
        long targetCoid,
        int packetObjectiveId)
    {
        if (character?.Map == null || targetCoid <= 0)
            return false;

        if (character.MapPresence.IsSuppressed(targetCoid))
        {
            Logger.WriteLog(LogType.Debug,
                "UseItem: rejected suppressed coid={0} char={1}",
                targetCoid,
                character.ObjectId.Coid);
            return true;
        }

        foreach (var quest in character.CurrentQuests.ToList())
        {
            if (character.CompletedMissionIds.Contains(quest.MissionId))
                continue;

            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null
                || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
                || objective?.Requirements == null)
            {
                continue;
            }

            var useItem = objective.Requirements.OfType<ObjectiveRequirementUseItem>().FirstOrDefault();
            if (useItem == null)
                continue;

            if (packetObjectiveId > 0 && packetObjectiveId != objective.ObjectiveId)
                continue;

            if (useItem.ContinentID > 0
                && character.Map.ContinentId > 0
                && useItem.ContinentID != character.Map.ContinentId)
            {
                continue;
            }

            if (!MatchesTarget(character.Map, useItem, targetCoid))
                continue;

            // Client MatchTarget requires SecondaryCBID in inventory when set. Top up GiveAtStart
            // cargo if accept-time grant was missed (full cargo, order race, failed persist).
            if (useItem.SecondaryCBID > 0
                && (character.Inventory == null
                    || character.Inventory.CountByCbid(useItem.SecondaryCBID) < 1))
            {
                if (useItem.SecondaryGiveAtStart)
                    MissionCargoService.EnsureAndSend(character, quest);

                if (character.Inventory == null
                    || character.Inventory.CountByCbid(useItem.SecondaryCBID) < 1)
                {
                    Logger.WriteLog(LogType.Debug,
                        "UseItem: missing secondary CBID={0} mission={1} char={2} (GiveAtStart={3})",
                        useItem.SecondaryCBID,
                        quest.MissionId,
                        character.ObjectId.Coid,
                        useItem.SecondaryGiveAtStart);
                    return true;
                }
            }

            var seq = quest.ActiveObjectiveSequence;
            EnsureProgressCapacity(quest, seq);

            var needed = Math.Max(1, useItem.RepeatCount);
            if (seq < quest.ObjectiveMax.Length && quest.ObjectiveMax[seq] > needed)
                needed = quest.ObjectiveMax[seq];

            quest.ObjectiveProgress[seq] = Math.Min(quest.ObjectiveProgress[seq] + 1, needed);

            Logger.WriteLog(LogType.Debug,
                "UseItem progress: mission={0} seq={1} objective={2} progress={3}/{4} target={5}",
                quest.MissionId,
                seq,
                objective.ObjectiveId,
                quest.ObjectiveProgress[seq],
                needed,
                targetCoid);

            // Inventory destroy applies on each successful use (client action path).
            MissionCargoService.TakeUseSuccessAndSend(character, objective);

            ApplyWorldPrimaryDestroy(conn, character, useItem, targetCoid);

            MissionPersistence.Instance.OnQuestChanged(character, quest);

            if (quest.ObjectiveProgress[seq] < needed)
            {
                var partial = ObjectiveStateBuilder.BuildUseItemCount(
                    objective,
                    useItem,
                    quest.ObjectiveProgress[seq]);
                if (partial != null)
                    conn?.SendGamePacket(partial);

                NpcInteractHandler.PushJournalMissionList(conn, character);
                TriggerManager.Instance.OnMissionStateChanged(
                    character.CurrentVehicle ?? (ClonedObjectBase)character);
                return true;
            }

            GrantCompletedItems(character, useItem);

            NpcInteractHandler.AdvanceOrCompleteObjective(
                conn,
                character,
                quest,
                mission,
                objective,
                source: "UseItem");

            if (useItem.CompletedMission > 0)
            {
                NpcInteractHandler.GrantMission(conn, character, useItem.CompletedMission);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// World PrimaryDestroy: personal suppress + InitCreateObject remove.
    /// DoDeath follows PrimaryExplode so prop NFX (break/explode) plays via client RemoveObject.
    /// Do not use DestroyObject for map props — that path skips death FX.
    /// </summary>
    internal static void ApplyWorldPrimaryDestroy(
        TNLConnection conn,
        Character character,
        ObjectiveRequirementUseItem useItem,
        long targetCoid)
    {
        if (character?.Map == null || useItem == null || targetCoid <= 0)
            return;
        if (!useItem.PrimaryDestroy || !useItem.PrimaryInWorld)
            return;

        character.MapPresence.EnsureContinent(character.Map.ContinentId);
        character.MapPresence.Suppress(targetCoid);

        // Personal mission-world remove (same character who used the prop).
        conn?.SendGamePacket(new InitCreateObjectPacket(
            targetCoid,
            create: false,
            doDeath: useItem.PrimaryExplode));
    }

    internal static bool MatchesTarget(SectorMap map, ObjectiveRequirementUseItem useItem, long targetCoid)
    {
        if (useItem == null || targetCoid <= 0)
            return false;

        if (useItem.PrimaryItem > 0 && useItem.PrimaryItem == targetCoid)
            return true;

        if (useItem.PrimaryCBID > 0 && MapObjectHasCbid(map, targetCoid, useItem.PrimaryCBID))
            return true;

        return false;
    }

    private static bool MapObjectHasCbid(SectorMap map, long coid, int cbid)
    {
        if (map == null)
            return false;

        var live = map.GetObjectByCoid(coid);
        if (live != null)
            return live.CBID == cbid;

        if (map.MapData?.Templates != null
            && map.MapData.Templates.TryGetValue(coid, out var template))
        {
            return template.CBID == cbid;
        }

        return false;
    }

    private static void GrantCompletedItems(Character character, ObjectiveRequirementUseItem useItem)
    {
        if (character?.Inventory == null || useItem == null)
            return;

        if (useItem.PrimaryCompletedItem > 0)
            GrantOne(character, useItem.PrimaryCompletedItem);

        if (useItem.CompletedItem > 0)
            GrantOne(character, useItem.CompletedItem);
    }

    private static void GrantOne(Character character, int cbid)
    {
        if (character.Inventory.CountByCbid(cbid) > 0)
            return;

        var coid = character.Map != null
            ? character.Map.LocalCoidCounter++
            : character.ObjectId.Coid + 1_000_000 + character.Inventory.Items.Count + 1;

        var type = CloneBaseObjectType.Item;
        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase?.CloneBaseSpecific != null)
        {
            var t = (CloneBaseObjectType)cloneBase.CloneBaseSpecific.Type;
            if (InventoryItemTypePolicy.IsInventoryCapable(t))
                type = t;
        }

        var name = cloneBase?.CloneBaseSpecific.UniqueName;
        if (string.IsNullOrWhiteSpace(name))
            name = $"CBID {cbid}";

        var result = character.Inventory.GrantMissionCargoItem(
            cbid,
            type,
            name,
            coid,
            character.ObjectId.Coid,
            1);

        if (result.Packets == null || result.Packets.Count == 0)
            return;

        var conn = character.OwningConnection;
        if (conn == null)
            return;

        foreach (var packet in result.Packets)
            conn.SendGamePacket(packet);
    }

    private static void EnsureProgressCapacity(CharacterQuest quest, int seq)
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
