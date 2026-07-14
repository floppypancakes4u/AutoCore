namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Utils;

/// <summary>
/// Vendor store open (OpenStore 0x206C) and buy/sell (<see cref="GameOpcode.StoreTransactionRequest"/>).
/// Map OpenStore reactions use GenericVar1 = store object COID; stock from <see cref="StoreTemplate"/>.
/// </summary>
public static class VendorStoreService
{
    /// <summary>
    /// Player-to-store XZ range for opening. Town kiosks can sit farther from stock objects
    /// than combat interact (25–30); 80f covers dispensary layouts on backrange/Ascent.
    /// </summary>
    internal const float MaxOpenDistance = 80f;

    /// <summary>Sell payout as a fraction of clonebase BaseValue (retail-ish placeholder).</summary>
    internal const float SellValueFraction = 0.25f;

    static readonly ConcurrentDictionary<long, long> OpenStoreByCharacter = new();

    /// <summary>Record the store the character just opened (from OpenStore GenericVar1).</summary>
    public static void NoteOpened(Character character, long storeCoid)
    {
        if (character == null)
            return;

        if (storeCoid <= 0)
        {
            OpenStoreByCharacter.TryRemove(character.ObjectId.Coid, out _);
            return;
        }

        OpenStoreByCharacter[character.ObjectId.Coid] = storeCoid;
        Logger.WriteLog(LogType.Debug,
            "VendorStore: session open charCoid={0} storeCoid={1}",
            character.ObjectId.Coid,
            storeCoid);
    }

    /// <summary>Test/helper: clear open-store sessions.</summary>
    internal static void ResetSessionsForTests() => OpenStoreByCharacter.Clear();

    /// <summary>
    /// Test hook: when set, <see cref="ResolveSellPrice"/> uses this instead of clonebase.
    /// Clear in test cleanup.
    /// </summary>
    internal static Func<int, long> TestSellPriceResolver { get; set; }

    internal static long GetOpenStoreCoidForTests(long characterCoid)
        => OpenStoreByCharacter.TryGetValue(characterCoid, out var s) ? s : 0;

    /// <summary>
    /// If an OpenStore reaction is in range of the player (or targets the clicked COID), fire it.
    /// Returns true when a reaction was triggered.
    /// </summary>
    public static bool TryOpen(TNLConnection conn, Character character, long targetCoid)
    {
        if (character?.Map == null || targetCoid <= 0)
            return false;

        var playerPos = NpcInteractHandler.GetPlayerInteractPosition(character);
        EnsureOpenStoreReactionsMaterialized(character.Map);

        var reaction = FindBestOpenStoreReaction(character.Map, playerPos, targetCoid);
        if (reaction == null)
        {
            LogMiss(character.Map, playerPos, targetCoid);
            return false;
        }

        var storeCoid = reaction.Template.GenericVar1;
        NoteOpened(character, storeCoid);
        Logger.WriteLog(LogType.Debug,
            "UseObject: OpenStore reaction={0} storeCoid={1} target={2} charCoid={3}",
            reaction.ObjectId.Coid,
            storeCoid,
            targetCoid,
            character.ObjectId.Coid);

        // Pure-client open via GroupReactionCall (0x206C).
        character.Map.TriggerReactions(
            character.CurrentVehicle ?? (ClonedObjectBase)character,
            new List<long> { reaction.ObjectId.Coid });

        return true;
    }

    /// <summary>
    /// Handle C2S StoreTransactionRequest (buy/sell). Dumps wire payload, applies economy when possible.
    /// </summary>
    public static void HandleTransaction(TNLConnection conn, StoreTransactionRequestPacket packet)
    {
        var character = conn?.CurrentCharacter;
        if (character?.Map == null || packet == null)
            return;

        Logger.WriteLog(LogType.Debug,
            "StoreTransaction: charCoid={0} isBuy={1} qty={2} item=({3},{4}) rawLen={5} hex={6}",
            character.ObjectId.Coid,
            packet.IsBuy,
            packet.Quantity,
            packet.Item?.Coid ?? -1,
            packet.Item?.Global ?? false,
            packet.RawBytes?.Length ?? 0,
            packet.RawBytes != null ? Convert.ToHexString(packet.RawBytes) : "");

        // Buy mutates inventory then response; sell defers CargoSendAll until after 0x2028 so
        // the client can resolve the held TFID and clear the cursor (FUN_007fc150).
        List<BasePacket> postResponsePackets = null;
        var ok = packet.IsBuy
            ? TryBuy(conn, character, packet)
            : TrySell(conn, character, packet, out postResponsePackets);

        // Always ack with the full 0x30 layout (FUN_00810670). Sell success uses item@+0x08
        // + credits@+0x20 + isBuy@+0x29; wrong body leaves the cursor held.
        conn.SendGamePacket(new StoreTransactionResponsePacket
        {
            ItemCoid = packet.Item?.Coid ?? 0,
            Credits = character.Credits,
            WasSuccessful = ok,
            IsBuy = packet.IsBuy,
            Quantity = Math.Max(1, packet.Quantity),
        });

        if (postResponsePackets != null)
        {
            foreach (var p in postResponsePackets)
                conn.SendGamePacket(p);
        }
    }

    internal static bool TryBuy(TNLConnection conn, Character character, StoreTransactionRequestPacket packet)
    {
        var storeCoid = ResolveOpenStoreCoid(character);
        if (storeCoid <= 0)
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buy: no open store session for char={0}", character.ObjectId.Coid);
            return false;
        }

        var stock = ResolveStoreStock(character.Map, storeCoid);
        if (stock == null || stock.Count == 0)
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buy: no stock for storeCoid={0}", storeCoid);
            return false;
        }

        var itemCoid = packet.Item?.Coid ?? 0;
        var qty = Math.Max(1, packet.Quantity);
        var line = MatchBuyLine(stock, itemCoid);
        if (line == null || line.CBID <= 0)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: no stock line for itemCoid={0} store={1} lines={2}",
                itemCoid,
                storeCoid,
                string.Join(',', stock.Where(s => s.CBID > 0).Select(s => s.CBID).Take(12)));
            return false;
        }

        var unitPrice = ResolveBuyPrice(line);
        var total = unitPrice * (long)qty;
        if (total < 0)
            total = 0;

        if (character.Credits < total)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: insufficient credits need={0} have={1}",
                total,
                character.Credits);
            return false;
        }

        var catalog = InventoryCatalog.FromAssetManager();
        var entry = catalog.FindAny(line.CBID);
        if (entry == null || !InventoryItemTypePolicy.IsInventoryCapable(entry.Type))
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buy: CBID {0} not inventory-capable", line.CBID);
            return false;
        }

        var runtime = new InventoryRuntime(character);
        if (!runtime.CanAllocateItem)
            return false;

        var coid = runtime.AllocateItemCoid();
        var result = character.Inventory.AddItem(
            entry,
            new InventoryItemCreator(),
            coid,
            character.ObjectId.Coid,
            qty,
            runtime.AllocateItemCoid);

        if (result.Packets == null || result.Packets.Count == 0)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: AddItem failed for CBID {0}: {1}",
                line.CBID,
                result.Message ?? result.ToString());
            return false;
        }

        if (total > 0)
        {
            var creditResult = character.Inventory.AddCredits(character, -total);
            if (creditResult.DeltaPacket != null)
                conn.SendGamePacket(creditResult.DeltaPacket);
        }

        foreach (var p in result.Packets)
            conn.SendGamePacket(p);

        Logger.WriteLog(LogType.Debug,
            "StoreTransaction buy OK char={0} cbid={1} qty={2} cost={3}",
            character.ObjectId.Coid,
            line.CBID,
            qty,
            total);
        return true;
    }

    internal static bool TrySell(
        TNLConnection conn,
        Character character,
        StoreTransactionRequestPacket packet,
        out List<BasePacket> postResponsePackets)
    {
        postResponsePackets = null;
        var itemCoid = packet.Item?.Coid ?? 0;
        if (itemCoid <= 0 || character.Inventory == null)
            return false;

        var invItem = character.Inventory.FindByCoid(itemCoid);
        if (invItem == null)
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction sell: item coid={0} not in cargo", itemCoid);
            return false;
        }

        var qty = Math.Max(1, Math.Min(packet.Quantity, invItem.Quantity));
        var unit = ResolveSellPrice(invItem.Cbid);
        if (unit <= 0)
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction sell: CBID {0} not sellable", invItem.Cbid);
            return false;
        }

        var total = unit * qty;
        // Server cargo only — do not DestroyObject before 0x2028. Client FUN_00810670 must
        // still resolve the held TFID; then it destroys cargo and/or FUN_007fc150 clears the hand
        // (same cursor-clear family as equip PutInHand / drop success).
        var itemGlobal = packet.Item?.Global ?? true;
        var remove = character.Inventory.RemoveCargoByCoid(
            character.ObjectId.Coid,
            itemCoid,
            itemGlobal,
            emitClientDestroy: false);
        if (remove.AcceptedQuantity < 1)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction sell: RemoveCargoByCoid failed coid={0}: {1}",
                itemCoid,
                remove.Message);
            return false;
        }

        if (total > 0)
        {
            var creditResult = character.Inventory.AddCredits(character, total);
            if (creditResult.DeltaPacket != null)
                conn.SendGamePacket(creditResult.DeltaPacket);
        }

        // Defer CargoSendAll until after StoreTransactionResponse.
        postResponsePackets = remove.Packets?.ToList() ?? new List<BasePacket>();

        Logger.WriteLog(LogType.Debug,
            "StoreTransaction sell OK char={0} coid={1} cbid={2} qty={3} payout={4}",
            character.ObjectId.Coid,
            itemCoid,
            invItem.Cbid,
            qty,
            total);
        return true;
    }

    static long ResolveOpenStoreCoid(Character character)
    {
        if (character == null)
            return 0;
        if (OpenStoreByCharacter.TryGetValue(character.ObjectId.Coid, out var store) && store > 0)
            return store;

        // Fallback: nearest OpenStore stock to player (session lost after relog mid-UI).
        if (character.Map == null)
            return 0;
        EnsureOpenStoreReactionsMaterialized(character.Map);
        var reaction = FindBestOpenStoreReaction(
            character.Map,
            NpcInteractHandler.GetPlayerInteractPosition(character),
            targetCoid: -1);
        return reaction?.Template?.GenericVar1 ?? 0;
    }

    static List<StoreTemplate.ItemType> ResolveStoreStock(SectorMap map, long storeCoid)
    {
        if (map?.MapData?.Templates == null)
            return null;
        if (!map.MapData.Templates.TryGetValue(storeCoid, out var tpl) || tpl is not StoreTemplate store)
            return null;
        return store.Items;
    }

    static StoreTemplate.ItemType MatchBuyLine(List<StoreTemplate.ItemType> stock, long itemCoid)
    {
        if (stock == null || itemCoid <= 0)
            return null;

        // Client often uses CBID as the item identity for catalog store lines.
        if (itemCoid <= int.MaxValue)
        {
            var byCbid = stock.FirstOrDefault(s => s.CBID == (int)itemCoid && s.CBID > 0);
            if (byCbid != null)
                return byCbid;
        }

        return null;
    }

    static long ResolveBuyPrice(StoreTemplate.ItemType line)
    {
        if (line == null)
            return 0;
        if (line.Value > 0)
            return line.Value;

        var cb = AssetManager.Instance.GetCloneBase(line.CBID);
        return cb?.CloneBaseSpecific.BaseValue ?? 0;
    }

    static long ResolveSellPrice(int cbid)
    {
        if (TestSellPriceResolver != null)
            return TestSellPriceResolver(cbid);

        var cb = AssetManager.Instance.GetCloneBase(cbid);
        if (cb == null)
            return 0;
        if (!cb.CloneBaseSpecific.IsSellable)
            return 0;
        var baseVal = cb.CloneBaseSpecific.BaseValue;
        if (baseVal <= 0)
            return 0;
        return Math.Max(1, (long)(baseVal * SellValueFraction));
    }

    /// <summary>
    /// Materialize any OpenStore reaction templates that are in MapData but not live yet
    /// (same pattern as mission Create reaction placement).
    /// </summary>
    internal static void EnsureOpenStoreReactionsMaterialized(SectorMap map)
    {
        if (map?.MapData?.Templates == null)
            return;

        foreach (var kvp in map.MapData.Templates)
        {
            if (kvp.Value is not ReactionTemplate rt
                || rt.ReactionType != ReactionType.OpenStore
                || rt.GenericVar1 <= 0)
            {
                continue;
            }

            var coid = kvp.Key;
            if (map.GetObjectByCoid(coid) is Reaction)
                continue;

            var placed = rt.Create() as Reaction;
            if (placed == null)
                continue;

            placed.SetCoid(coid, false);
            // Pose at store stock if known so any pose-based fallback is sane.
            var storePos = ResolveObjectPosition(map, rt.GenericVar1);
            if (storePos.HasValue)
                placed.Position = storePos.Value;

            placed.SetMap(map);
            Logger.WriteLog(LogType.Debug,
                "VendorStore: materialized OpenStore reaction coid={0} storeCoid={1}",
                coid,
                rt.GenericVar1);
        }
    }

    /// <summary>
    /// Pick the best OpenStore reaction: exact store/reaction click, else nearest store to player
    /// within <see cref="MaxOpenDistance"/> (optionally preferring stores near the click target).
    /// </summary>
    internal static Reaction FindBestOpenStoreReaction(SectorMap map, Vector3 playerPos, long targetCoid)
    {
        if (map == null)
            return null;

        var maxSq = MaxOpenDistance * MaxOpenDistance;
        Reaction best = null;
        var bestScore = float.MaxValue; // lower is better

        foreach (var reaction in EnumerateOpenStoreReactions(map))
        {
            var storeCoid = reaction.Template.GenericVar1;
            if (storeCoid <= 0)
                continue;

            var distPlayerToStore = DistToStoreOrReaction(map, playerPos, storeCoid, reaction);

            // Direct click on the store object or OpenStore reaction COID.
            if (storeCoid == targetCoid || reaction.ObjectId.Coid == targetCoid)
            {
                if (distPlayerToStore <= maxSq)
                    return reaction;
                continue;
            }

            if (distPlayerToStore > maxSq)
                continue;

            // Score: player distance, with a bonus when the click target is also near the store
            // (kiosk NPCs). Do not *require* target near store — MapNpcIdentity kiosks can
            // sit outside the stock object's tight radius while the player is still in front.
            var score = distPlayerToStore;
            var targetPos = ResolveObjectPosition(map, targetCoid);
            if (targetPos.HasValue)
            {
                var distTargetToStore = DistToStoreOrReaction(map, targetPos.Value, storeCoid, reaction);
                if (distTargetToStore <= maxSq)
                    score *= 0.25f; // strong preference for kiosk-linked stores
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = reaction;
            }
        }

        return best;
    }

    private static IEnumerable<Reaction> EnumerateOpenStoreReactions(SectorMap map)
    {
        // Prefer dedicated Reactions dictionary (always populated for reaction entities).
        if (map.Reactions != null)
        {
            foreach (var reaction in map.Reactions.Values)
            {
                if (reaction?.Template?.ReactionType == ReactionType.OpenStore)
                    yield return reaction;
            }

            yield break;
        }

        foreach (var kvp in map.Objects)
        {
            if (kvp.Value is Reaction reaction
                && reaction.Template?.ReactionType == ReactionType.OpenStore)
            {
                yield return reaction;
            }
        }
    }

    private static void LogMiss(SectorMap map, Vector3 playerPos, long targetCoid)
    {
        var openCount = 0;
        float nearest = float.MaxValue;
        int nearestStore = 0;
        foreach (var reaction in EnumerateOpenStoreReactions(map))
        {
            openCount++;
            var storeCoid = reaction.Template?.GenericVar1 ?? 0;
            if (storeCoid <= 0)
                continue;
            var d = DistToStoreOrReaction(map, playerPos, storeCoid, reaction);
            if (d < nearest)
            {
                nearest = d;
                nearestStore = storeCoid;
            }
        }

        var targetPos = ResolveObjectPosition(map, targetCoid);
        Logger.WriteLog(LogType.Debug,
            "UseObject: OpenStore miss target={0} openStoreReactions={1} nearestStore={2} distXZ={3:F1} player={4} targetPos={5}",
            targetCoid,
            openCount,
            nearestStore,
            nearest < float.MaxValue ? MathF.Sqrt(nearest) : -1f,
            playerPos,
            targetPos.HasValue ? targetPos.Value.ToString() : "null");
    }

    private static float DistToStoreOrReaction(
        SectorMap map,
        Vector3 from,
        int storeCoid,
        Reaction reaction)
    {
        var storePos = ResolveObjectPosition(map, storeCoid);
        if (storePos.HasValue)
            return NpcInteractHandler.DistXZSq(from, storePos.Value);

        // Fall back to reaction entity pose if store graphics object missing.
        return NpcInteractHandler.DistXZSq(from, reaction.Position);
    }

    private static Vector3? ResolveObjectPosition(SectorMap map, long coid)
    {
        if (map == null || coid <= 0)
            return null;

        var obj = map.GetObjectByCoid(coid);
        if (obj != null)
            return obj.Position;

        // Map template not materialized as live object — use template location when present.
        if (map.MapData?.Templates != null
            && map.MapData.Templates.TryGetValue(coid, out var template)
            && template is GraphicsObjectTemplate graphics)
        {
            return graphics.Location.ToVector3();
        }

        return null;
    }
}
