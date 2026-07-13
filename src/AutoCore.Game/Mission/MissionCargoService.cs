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
/// Server-authoritative mission gear (Mission Inventory) grant/take for deliver objectives.
/// Items live in normal vehicle cargo with <see cref="CharacterInventoryItem.IsMissionItem"/>
/// and CreateSimpleObject <c>PossibleMissionItem</c> for client UI.
/// </summary>
public static class MissionCargoService
{
    public static int QuantityNeeded(int numToDeliver) => numToDeliver > 0 ? numToDeliver : 1;

    public static int QuantityMissing(int needed, int have) => Math.Max(0, needed - Math.Max(0, have));

    /// <summary>
    /// Deliver requirements on an objective that grant cargo at objective start.
    /// </summary>
    public static IReadOnlyList<(int Cbid, int Quantity)> GetGiveSpecs(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return Array.Empty<(int, int)>();

        var specs = new List<(int, int)>();
        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementDeliver deliver)
                continue;
            if (!deliver.GiveItemOnStart || deliver.ItemCBID <= 0)
                continue;

            specs.Add((deliver.ItemCBID, QuantityNeeded(deliver.NumToDeliver)));
        }

        return specs;
    }

    /// <summary>
    /// Deliver requirements that remove cargo on objective/mission complete.
    /// </summary>
    public static IReadOnlyList<(int Cbid, int Quantity)> GetTakeSpecs(MissionObjective objective)
    {
        if (objective?.Requirements == null || objective.Requirements.Count == 0)
            return Array.Empty<(int, int)>();

        var specs = new List<(int, int)>();
        foreach (var req in objective.Requirements)
        {
            if (req is not ObjectiveRequirementDeliver deliver)
                continue;
            if (!deliver.TakeItemAtEnd || deliver.ItemCBID <= 0)
                continue;

            specs.Add((deliver.ItemCBID, QuantityNeeded(deliver.NumToDeliver)));
        }

        return specs;
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

        var packets = new List<BasePacket>();
        foreach (var (cbid, needed) in GetGiveSpecs(objective))
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
                itemCreator);

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
            var result = character.Inventory.RemoveCargoByCbid(character.ObjectId.Coid, cbid, quantity);
            if (result.Packets != null && result.Packets.Count > 0)
                packets.AddRange(result.Packets);
        }

        return packets;
    }

    /// <summary>
    /// Ensure + send packets on an owning connection when present.
    /// </summary>
    public static void EnsureAndSend(Character character, CharacterQuest quest)
    {
        if (character == null || quest == null)
            return;

        var packets = EnsureActiveObjectiveItems(character, quest);
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
