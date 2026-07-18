namespace AutoCore.Game.Mission;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative mission gear (Mission Inventory) grant/take for deliver and useitem
/// objectives. Items live in normal vehicle cargo with
/// <see cref="CharacterInventoryItem.IsMissionItem"/> and CreateSimpleObject
/// <c>PossibleMissionItem</c> for client UI.
/// </summary>
public static class MissionCargoService
{
    /// <summary>
    /// Sentinel quantity for Collect <c>TakeAllItems</c>: remove every matching stack at take time.
    /// </summary>
    public const int TakeAllQuantity = int.MaxValue;

    public static int QuantityNeeded(int numToDeliver) => numToDeliver > 0 ? numToDeliver : 1;

    public static int QuantityMissing(int needed, int have) => Math.Max(0, needed - Math.Max(0, have));

    /// <summary>
    /// Client InitActive qty: MultipleUse → 1; otherwise max(1, RepeatCount).
    /// </summary>
    public static int QuantityForUseItemGive(bool multipleUse, int repeatCount)
        => multipleUse ? 1 : Math.Max(1, repeatCount);

    /// <summary>
    /// Requirements on an objective that grant cargo at objective start (deliver + useitem).
    /// </summary>
    public static IReadOnlyList<(int Cbid, int Quantity)> GetGiveSpecs(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return Array.Empty<(int, int)>();

        var specs = new List<(int, int)>();
        foreach (var req in objective.Requirements)
        {
            if (req is ObjectiveRequirementDeliver deliver)
            {
                if (!deliver.GiveItemOnStart || deliver.ItemCBID <= 0)
                    continue;

                specs.Add((deliver.ItemCBID, QuantityNeeded(deliver.NumToDeliver)));
                continue;
            }

            if (req is not ObjectiveRequirementUseItem useItem)
                continue;

            if (useItem.PrimaryGiveAtStart && useItem.PrimaryCBID > 0)
            {
                specs.Add((
                    useItem.PrimaryCBID,
                    QuantityForUseItemGive(useItem.PrimaryMultipleUse, useItem.RepeatCount)));
            }

            if (useItem.SecondaryGiveAtStart && useItem.SecondaryCBID > 0)
            {
                specs.Add((
                    useItem.SecondaryCBID,
                    QuantityForUseItemGive(useItem.SecondaryMultipleUse, useItem.RepeatCount)));
            }
        }

        return specs;
    }

    /// <summary>
    /// Deliver TakeItemAtEnd and Collect turn-in cargo removals.
    /// </summary>
    public static IReadOnlyList<(int Cbid, int Quantity)> GetTakeSpecs(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return Array.Empty<(int, int)>();

        var specs = new List<(int, int)>();
        foreach (var req in objective.Requirements)
        {
            if (req is ObjectiveRequirementDeliver deliver)
            {
                if (!deliver.TakeItemAtEnd || deliver.ItemCBID <= 0)
                    continue;

                specs.Add((deliver.ItemCBID, QuantityNeeded(deliver.NumToDeliver)));
                continue;
            }

            if (req is not ObjectiveRequirementCollect collect)
                continue;
            if (collect.ItemCBID <= 0)
                continue;

            // Collect always removes gathered items on objective/mission complete.
            var qty = collect.TakeItems
                ? TakeAllQuantity
                : QuantityNeeded(collect.NumToCollect);
            specs.Add((collect.ItemCBID, qty));
        }

        return specs;
    }

    /// <summary>
    /// Cargo to reclaim on fail/abandon: all GiveAtStart (deliver + useitem) and deliver
    /// TakeItemAtEnd across every objective (not only the active sequence).
    /// </summary>
    public static IReadOnlyList<(int Cbid, int Quantity)> GetAbandonTakeSpecs(Mission mission)
    {
        if (mission?.Objectives == null || mission.Objectives.Count == 0)
            return Array.Empty<(int, int)>();

        var byCbid = new Dictionary<int, int>();
        foreach (var objective in mission.Objectives.Values)
        {
            foreach (var (cbid, quantity) in GetGiveSpecs(objective))
                MergeQty(byCbid, cbid, quantity);
            foreach (var (cbid, quantity) in GetTakeSpecs(objective))
                MergeQty(byCbid, cbid, quantity);
        }

        if (byCbid.Count == 0)
            return Array.Empty<(int, int)>();

        var list = new List<(int, int)>(byCbid.Count);
        foreach (var kv in byCbid)
            list.Add((kv.Key, kv.Value));
        return list;
    }

    private static void MergeQty(Dictionary<int, int> byCbid, int cbid, int quantity)
    {
        if (cbid <= 0 || quantity <= 0)
            return;
        if (byCbid.TryGetValue(cbid, out var existing))
            byCbid[cbid] = Math.Max(existing, quantity);
        else
            byCbid[cbid] = quantity;
    }

    /// <summary>
    /// Inventory items to remove after a successful UseItem interact (not world props).
    /// World PrimaryDestroy is handled via map presence suppress, not cargo.
    /// </summary>
    public static IReadOnlyList<(int Cbid, int Quantity)> GetUseSuccessTakeSpecs(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return Array.Empty<(int, int)>();

        var specs = new List<(int, int)>();
        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementUseItem useItem)
                continue;

            if (useItem.PrimaryDestroy && !useItem.PrimaryInWorld && useItem.PrimaryCBID > 0)
                specs.Add((useItem.PrimaryCBID, 1));

            if (useItem.SecondaryDestroy && useItem.SecondaryCBID > 0)
                specs.Add((useItem.SecondaryCBID, 1));
        }

        return specs;
    }

    /// <summary>
    /// Remove inventory items destroyed by a successful UseItem use; send packets when connected.
    /// </summary>
    public static IReadOnlyList<BasePacket> TakeUseSuccessItems(
        Character character,
        MissionObjective objective)
    {
        if (character?.Inventory == null || objective == null)
            return Array.Empty<BasePacket>();

        var packets = new List<BasePacket>();
        foreach (var (cbid, quantity) in GetUseSuccessTakeSpecs(objective))
        {
            var result = character.Inventory.RemoveCargoByCbid(character.ObjectId.Coid, cbid, quantity);
            if (result.Packets != null && result.Packets.Count > 0)
                packets.AddRange(result.Packets);
        }

        return packets;
    }

    public static void TakeUseSuccessAndSend(Character character, MissionObjective objective)
    {
        if (character == null || objective == null)
            return;

        var packets = TakeUseSuccessItems(character, objective);
        SendPackets(character, packets);
    }

    /// <summary>
    /// Idempotently ensure active-objective GiveItemOnStart cargo is present and replicate to client.
    /// </summary>
    public static IReadOnlyList<BasePacket> EnsureActiveObjectiveItems(
        Character character,
        CharacterQuest quest,
        Func<long> allocateCoid = null,
        IInventoryItemCreator itemCreator = null)
    {
        if (character?.Inventory == null || quest == null)
            return Array.Empty<BasePacket>();

        var objective = ResolveActiveObjective(quest);
        if (objective == null)
            return Array.Empty<BasePacket>();

        var specs = GetGiveSpecs(objective);
        if (specs.Count > 0)
        {
            Logger.WriteLog(LogType.Debug,
                "MissionCargo Ensure mission={0} seq={1} give=[{2}] char={3}",
                quest.MissionId,
                quest.ActiveObjectiveSequence,
                string.Join(',', specs.Select(s => $"{s.Cbid}x{s.Quantity}")),
                character.ObjectId.Coid);
        }

        var packets = new List<BasePacket>();
        foreach (var (cbid, needed) in specs)
        {
            var have = character.Inventory.CountByCbid(cbid);
            var missing = QuantityMissing(needed, have);
            if (missing <= 0)
                continue;

            var coid = allocateCoid != null
                ? allocateCoid()
                : AllocateInventoryCoid(character);

            if (coid <= 0)
            {
                Logger.WriteLog(LogType.Error,
                    "MissionCargoService: cannot allocate COID for mission CBID {0} char={1}",
                    cbid,
                    character.ObjectId.Coid);
                continue;
            }

            var type = ResolveItemType(cbid);
            var displayName = ResolveDisplayName(cbid);
            var result = character.Inventory.GrantMissionCargoItem(
                cbid,
                type,
                displayName,
                coid,
                character.ObjectId.Coid,
                missing,
                itemCreator,
                allocateCoid);

            // Bare packet fallback when clonebase/factory cannot build typed QuestObject creates.
            if ((result.Packets == null || result.Packets.Count == 0) && itemCreator != null)
            {
                result = character.Inventory.GrantMissionCargoItem(
                    cbid,
                    type,
                    displayName,
                    coid,
                    character.ObjectId.Coid,
                    missing,
                    itemCreator: null,
                    allocateAdditionalCoid: allocateCoid);
            }

            if (result.Packets != null && result.Packets.Count > 0)
                packets.AddRange(result.Packets);
            else if (!string.IsNullOrEmpty(result.Message))
            {
                Logger.WriteLog(LogType.Error,
                    "MissionCargoService: grant failed CBID={0} qty={1} char={2}: {3}",
                    cbid,
                    missing,
                    character.ObjectId.Coid,
                    result.Message);
            }
        }

        return packets;
    }

    /// <summary>
    /// Remove TakeItemAtEnd cargo for the given objective (or active objective when null).
    /// </summary>
    public static IReadOnlyList<BasePacket> TakeObjectiveItems(
        Character character,
        CharacterQuest quest,
        MissionObjective objective = null)
    {
        if (character?.Inventory == null || quest == null)
            return Array.Empty<BasePacket>();

        objective ??= ResolveActiveObjective(quest);
        if (objective == null)
            return Array.Empty<BasePacket>();

        var packets = new List<BasePacket>();
        foreach (var (cbid, quantity) in GetTakeSpecs(objective))
        {
            var takeQty = quantity == TakeAllQuantity
                ? Math.Max(1, character.Inventory.CountByCbid(cbid))
                : quantity;
            if (takeQty < 1)
                continue;

            var result = character.Inventory.RemoveCargoByCbid(character.ObjectId.Coid, cbid, takeQty);
            if (result.Packets != null && result.Packets.Count > 0)
                packets.AddRange(result.Packets);
        }

        return packets;
    }

    /// <summary>
    /// Ensure + send packets on an owning connection when present.
    /// Prefers <see cref="InventoryItemCreator"/> for QuestObject/MissionObject create payloads
    /// (mission inventory UI); falls back to bare CreateSimpleObject if factory cannot allocate.
    /// </summary>
    public static void EnsureAndSend(Character character, CharacterQuest quest)
    {
        if (character == null || quest == null)
            return;

        var packets = EnsureActiveObjectiveItems(
            character,
            quest,
            itemCreator: new InventoryItemCreator());
        SendPackets(character, packets);
    }

    /// <summary>
    /// Take + send packets on an owning connection when present.
    /// </summary>
    public static void TakeAndSend(Character character, CharacterQuest quest, MissionObjective objective = null)
    {
        if (character == null || quest == null)
            return;

        var packets = TakeObjectiveItems(character, quest, objective);
        SendPackets(character, packets);
    }

    /// <summary>
    /// Remove all mission-granted cargo for abandon/fail (GiveAtStart + deliver TakeItemAtEnd).
    /// </summary>
    public static IReadOnlyList<BasePacket> TakeOnAbandonItems(Character character, CharacterQuest quest)
    {
        if (character?.Inventory == null || quest == null)
            return Array.Empty<BasePacket>();

        var mission = AssetManager.Instance.GetMission(quest.MissionId);
        if (mission == null)
            return Array.Empty<BasePacket>();

        var packets = new List<BasePacket>();
        foreach (var (cbid, quantity) in GetAbandonTakeSpecs(mission))
        {
            var result = character.Inventory.RemoveCargoByCbid(character.ObjectId.Coid, cbid, quantity);
            if (result.Packets != null && result.Packets.Count > 0)
                packets.AddRange(result.Packets);
        }

        return packets;
    }

    public static void TakeOnAbandonAndSend(Character character, CharacterQuest quest)
    {
        if (character == null || quest == null)
            return;

        var packets = TakeOnAbandonItems(character, quest);
        SendPackets(character, packets);
    }

    private static void SendPackets(Character character, IReadOnlyList<BasePacket> packets)
    {
        var conn = character.OwningConnection;
        if (conn == null || packets == null || packets.Count == 0)
            return;

        foreach (var packet in packets)
            conn.SendGamePacket(packet);
    }

    private static MissionObjective ResolveActiveObjective(CharacterQuest quest)
    {
        var mission = AssetManager.Instance.GetMission(quest.MissionId);
        if (mission == null)
            return null;

        return mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
            ? objective
            : null;
    }

    private static long AllocateInventoryCoid(Character character)
    {
        if (character.Map != null)
            return character.Map.LocalCoidCounter++;

        // Offline / unit tests without a map: use a high ephemeral range.
        return character.ObjectId.Coid > 0
            ? character.ObjectId.Coid + 1_000_000 + character.Inventory.Items.Count + 1
            : Environment.TickCount64 & 0x7FFFFFFF;
    }

    private static CloneBaseObjectType ResolveItemType(int cbid)
    {
        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase?.CloneBaseSpecific != null)
        {
            var type = (CloneBaseObjectType)cloneBase.CloneBaseSpecific.Type;
            if (InventoryItemTypePolicy.IsInventoryCapable(type))
                return type;
        }

        return CloneBaseObjectType.Item;
    }

    private static string ResolveDisplayName(int cbid)
    {
        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        var name = cloneBase?.CloneBaseSpecific.UniqueName;
        return string.IsNullOrWhiteSpace(name) ? $"CBID {cbid}" : name;
    }
}
