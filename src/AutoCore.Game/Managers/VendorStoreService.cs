namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
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

    /// <summary>
    /// Per-character map of server-assigned store-slot COID → stock line.
    /// Client buy of catalog stock may use these COIDs when creates stick; live UI often keeps
    /// client-local slot COIDs for template stock.
    /// </summary>
    static readonly ConcurrentDictionary<long, Dictionary<long, StoreTemplate.ItemType>> StockSessionByCharacter = new();

    /// <summary>
    /// Items the player sold to the store this session, keyed by the item COID the client still
    /// holds for buyback (live: sell then buy same TFID e.g. 11131).
    /// </summary>
    static readonly ConcurrentDictionary<long, Dictionary<long, StoreBuybackListing>> BuybackByCharacter = new();

    /// <summary>
    /// Record the store the character just opened (from OpenStore GenericVar1).
    /// When <paramref name="conn"/> is set, materializes session stock creates so buy can map slot COIDs.
    /// </summary>
    public static void NoteOpened(Character character, long storeCoid, TNLConnection conn = null)
    {
        if (character == null)
            return;

        if (storeCoid <= 0)
        {
            OpenStoreByCharacter.TryRemove(character.ObjectId.Coid, out _);
            StockSessionByCharacter.TryRemove(character.ObjectId.Coid, out _);
            BuybackByCharacter.TryRemove(character.ObjectId.Coid, out _);
            return;
        }

        var charCoid = character.ObjectId.Coid;
        var alreadyOpen = OpenStoreByCharacter.TryGetValue(charCoid, out var prev) && prev == storeCoid
            && StockSessionByCharacter.TryGetValue(charCoid, out var existing) && existing.Count > 0;

        OpenStoreByCharacter[charCoid] = storeCoid;
        Logger.WriteLog(LogType.Debug,
            "VendorStore: session open charCoid={0} storeCoid={1}",
            charCoid,
            storeCoid);

        // Reaction.OpenStore may NoteOpened again without conn after TryOpen already materializes.
        // Do not re-assign slot COIDs or the client/session map diverges.
        // Buyback listings are kept for the open store session (sell → buyback same TFID).
        if (!alreadyOpen)
            MaterializeStockSession(character, storeCoid, conn);
    }

    /// <summary>Test/helper: clear open-store sessions.</summary>
    internal static void ResetSessionsForTests()
    {
        OpenStoreByCharacter.Clear();
        StockSessionByCharacter.Clear();
        BuybackByCharacter.Clear();
    }

    /// <summary>
    /// Test hook: when set, <see cref="ResolveSellPrice"/> uses this instead of clonebase.
    /// Clear in test cleanup.
    /// </summary>
    internal static Func<int, long> TestSellPriceResolver { get; set; }

    /// <summary>Test hook for buy unit price (clonebase-free).</summary>
    internal static Func<int, long> TestBuyPriceResolver { get; set; }

    /// <summary>Test hook for catalog lookup on buy (clonebase-free).</summary>
    internal static Func<int, InventoryCatalogEntry> TestBuyCatalogResolver { get; set; }

    /// <summary>Test hook for cargo create factory on buy.</summary>
    internal static IInventoryItemCreator TestItemCreator { get; set; }

    internal static long GetOpenStoreCoidForTests(long characterCoid)
        => OpenStoreByCharacter.TryGetValue(characterCoid, out var s) ? s : 0;

    internal static IReadOnlyDictionary<long, StoreTemplate.ItemType> GetStockSessionForTests(long characterCoid)
        => StockSessionByCharacter.TryGetValue(characterCoid, out var s) ? s : null;

    internal static IReadOnlyDictionary<long, StoreBuybackListing> GetBuybackForTests(long characterCoid)
        => BuybackByCharacter.TryGetValue(characterCoid, out var s) ? s : null;

    internal static StoreTemplate.ItemType MatchBuyLineForTests(
        List<StoreTemplate.ItemType> stock,
        long itemCoid,
        IReadOnlyDictionary<long, StoreTemplate.ItemType> session)
        => MatchBuyLine(stock, itemCoid, session);

    /// <summary>Server listing for an item the player sold and may buy back this session.</summary>
    internal sealed class StoreBuybackListing
    {
        public int Cbid { get; init; }
        public CloneBaseObjectType Type { get; init; }
        public string DisplayName { get; init; }
        public int Quantity { get; set; }
        /// <summary>Credits charged per unit to repurchase (defaults to sell payout unit).</summary>
        public long UnitPrice { get; init; }
        public bool IsMissionItem { get; init; }
    }

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
        // Materialize stock creates before OpenStore UI so client slot COIDs match session map.
        NoteOpened(character, storeCoid, conn);
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
        long responseItemCoid = packet.Item?.Coid ?? 0;
        long relatedA = 0;
        long relatedB = packet.Item?.Coid ?? 0;
        bool ok;
        if (packet.IsBuy)
        {
            ok = TryBuy(conn, character, packet, out var grantCoid, out relatedA, out relatedB);
            responseItemCoid = ok && grantCoid > 0 ? grantCoid : (packet.Item?.Coid ?? 0);
        }
        else
        {
            ok = TrySell(conn, character, packet, out postResponsePackets);
        }

        // Always ack with the full 0x30 layout (FUN_00810670).
        // Buy: +0x08 grant COID, +0x10 character, +0x18 store-slot TFID, +0x20 credits, +0x29 isBuy.
        // Sell: +0x08 sold item, +0x20 credits, +0x29=0.
        conn.SendGamePacket(new StoreTransactionResponsePacket
        {
            ItemCoid = responseItemCoid,
            RelatedCoidA = relatedA,
            RelatedCoidB = relatedB,
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

    internal static bool TryBuy(
        TNLConnection conn,
        Character character,
        StoreTransactionRequestPacket packet,
        out long grantedCoid,
        out long relatedCharacterCoid,
        out long storeSlotCoid)
    {
        grantedCoid = 0;
        relatedCharacterCoid = character?.ObjectId.Coid ?? 0;
        storeSlotCoid = packet?.Item?.Coid ?? 0;

        var storeCoid = packet?.StoreCoid > 0
            ? packet.StoreCoid
            : ResolveOpenStoreCoid(character);
        if (storeCoid <= 0)
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buy: no open store session for char={0}", character.ObjectId.Coid);
            return false;
        }

        // Keep session store in sync when client sends store COID on the wire.
        if (packet.StoreCoid > 0)
            OpenStoreByCharacter[character.ObjectId.Coid] = packet.StoreCoid;

        var itemCoid = packet.Item?.Coid ?? 0;
        var qty = Math.Max(1, packet.Quantity);

        // 1) Buyback of items this character sold this session (client reuses sold TFID).
        // Do not Create a new object — client still holds that TFID on the store UI.
        if (TryResolveBuyback(character.ObjectId.Coid, itemCoid, qty, out var buyback, out var buybackQty))
        {
            return CompleteBuyback(
                conn,
                character,
                buyback,
                itemCoid,
                buybackQty,
                out grantedCoid);
        }

        // 2) Catalog / session stock lines.
        var stock = ResolveStoreStock(character.Map, storeCoid);
        StockSessionByCharacter.TryGetValue(character.ObjectId.Coid, out var session);
        if ((session == null || session.Count == 0) && conn != null && stock != null)
        {
            MaterializeStockSession(character, storeCoid, conn);
            StockSessionByCharacter.TryGetValue(character.ObjectId.Coid, out session);
        }

        var line = MatchBuyLine(stock, itemCoid, session);
        if (line == null || line.CBID <= 0)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: no stock/buyback for itemCoid={0} store={1} sessionSlots={2} buybacks={3} lines={4}",
                itemCoid,
                storeCoid,
                session?.Count ?? 0,
                BuybackByCharacter.TryGetValue(character.ObjectId.Coid, out var bb) ? bb.Count : 0,
                stock == null
                    ? "(null)"
                    : string.Join(',', stock.Where(s => s.CBID > 0).Select(s => s.CBID).Take(12)));
            return false;
        }

        var entry = ResolveBuyCatalogEntry(line.CBID);
        if (entry == null)
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buy: CBID {0} not in catalog", line.CBID);
            return false;
        }

        return CompleteBuy(
            conn,
            character,
            cbid: line.CBID,
            type: entry.Type,
            displayName: entry.DisplayName,
            unitPrice: ResolveBuyPrice(line),
            qty: qty,
            storeSlotCoid: itemCoid,
            out grantedCoid,
            onSuccess: null,
            sourceTag: "catalog");
    }

    /// <summary>
    /// Buyback: restore the sold COID into cargo without CreateSimpleObject.
    /// Client still owns that TFID on the store UI; a second Create produces a spare invalid icon.
    /// Response grant COID is the original sold TFID so FUN_00810670 can resolve the live object.
    /// </summary>
    static bool CompleteBuyback(
        TNLConnection conn,
        Character character,
        StoreBuybackListing buyback,
        long soldItemCoid,
        int qty,
        out long grantedCoid)
    {
        grantedCoid = 0;
        if (buyback == null || soldItemCoid <= 0 || qty < 1 || character?.Inventory == null)
            return false;

        var total = buyback.UnitPrice * (long)qty;
        if (total < 0)
            total = 0;

        if (character.Credits < total)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: insufficient credits need={0} have={1} source=buyback",
                total,
                character.Credits);
            return false;
        }

        if (!InventoryItemTypePolicy.IsInventoryCapable(buyback.Type))
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buyback: CBID {0} not inventory-capable", buyback.Cbid);
            return false;
        }

        var restoreItem = new CharacterInventoryItem(
            buyback.Cbid,
            buyback.Type,
            buyback.DisplayName ?? $"CBID {buyback.Cbid}",
            soldItemCoid,
            0,
            0,
            qty,
            buyback.IsMissionItem);

        var result = character.Inventory.RestoreCargoWithoutCreate(restoreItem, character.ObjectId.Coid);
        if (result.AcceptedQuantity < 1 || result.Packets == null || result.Packets.Count == 0)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buyback: restore failed coid={0}: {1}",
                soldItemCoid,
                result.Message);
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

        ConsumeBuyback(character.ObjectId.Coid, soldItemCoid, qty);
        grantedCoid = soldItemCoid;

        Logger.WriteLog(LogType.Debug,
            "StoreTransaction buy OK char={0} cbid={1} grantCoid={2} slotCoid={3} qty={4} cost={5} source=buyback",
            character.ObjectId.Coid,
            buyback.Cbid,
            grantedCoid,
            soldItemCoid,
            qty,
            total);
        return true;
    }

    static bool CompleteBuy(
        TNLConnection conn,
        Character character,
        int cbid,
        CloneBaseObjectType type,
        string displayName,
        long unitPrice,
        int qty,
        long storeSlotCoid,
        out long grantedCoid,
        Action onSuccess,
        string sourceTag)
    {
        grantedCoid = 0;
        if (cbid <= 0 || qty < 1 || character?.Inventory == null)
            return false;

        var total = unitPrice * (long)qty;
        if (total < 0)
            total = 0;

        if (character.Credits < total)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: insufficient credits need={0} have={1} source={2}",
                total,
                character.Credits,
                sourceTag);
            return false;
        }

        var entry = ResolveBuyCatalogEntry(cbid)
            ?? new InventoryCatalogEntry(cbid, type, string.IsNullOrWhiteSpace(displayName) ? $"CBID {cbid}" : displayName);
        if (!InventoryItemTypePolicy.IsInventoryCapable(entry.Type))
        {
            Logger.WriteLog(LogType.Debug, "StoreTransaction buy: CBID {0} not inventory-capable", cbid);
            return false;
        }

        var runtime = new InventoryRuntime(character);
        if (!runtime.CanAllocateItem)
            return false;

        var coid = runtime.AllocateItemCoid();
        var creator = TestItemCreator ?? new InventoryItemCreator();
        var result = character.Inventory.AddItem(
            entry,
            creator,
            coid,
            character.ObjectId.Coid,
            qty,
            runtime.AllocateItemCoid);

        if (result.Packets == null || result.Packets.Count == 0)
        {
            Logger.WriteLog(LogType.Debug,
                "StoreTransaction buy: AddItem failed for CBID {0}: {1}",
                cbid,
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

        grantedCoid = result.AddedItem?.Coid ?? coid;
        onSuccess?.Invoke();

        Logger.WriteLog(LogType.Debug,
            "StoreTransaction buy OK char={0} cbid={1} grantCoid={2} slotCoid={3} qty={4} cost={5} source={6}",
            character.ObjectId.Coid,
            cbid,
            grantedCoid,
            storeSlotCoid,
            qty,
            total,
            sourceTag);
        return true;
    }

    static bool TryResolveBuyback(
        long characterCoid,
        long itemCoid,
        int requestedQty,
        out StoreBuybackListing listing,
        out int qty)
    {
        listing = null;
        qty = 0;
        if (itemCoid <= 0 || requestedQty < 1)
            return false;
        if (!BuybackByCharacter.TryGetValue(characterCoid, out var map) || map == null)
            return false;
        if (!map.TryGetValue(itemCoid, out listing) || listing == null || listing.Quantity < 1)
            return false;

        qty = Math.Min(requestedQty, listing.Quantity);
        return qty >= 1 && listing.Cbid > 0;
    }

    static void ConsumeBuyback(long characterCoid, long itemCoid, int qty)
    {
        if (!BuybackByCharacter.TryGetValue(characterCoid, out var map) || map == null)
            return;
        if (!map.TryGetValue(itemCoid, out var listing) || listing == null)
            return;

        listing.Quantity -= qty;
        if (listing.Quantity <= 0)
            map.Remove(itemCoid);

        if (map.Count == 0)
            BuybackByCharacter.TryRemove(characterCoid, out _);
    }

    static void RegisterBuyback(
        long characterCoid,
        long itemCoid,
        CharacterInventoryItem sold,
        int qty,
        long unitSellPrice)
    {
        if (characterCoid <= 0 || itemCoid <= 0 || sold == null || qty < 1 || sold.Cbid <= 0)
            return;

        var map = BuybackByCharacter.GetOrAdd(characterCoid, _ => new Dictionary<long, StoreBuybackListing>());
        if (map.TryGetValue(itemCoid, out var existing) && existing != null && existing.Cbid == sold.Cbid)
        {
            existing.Quantity += qty;
            return;
        }

        // Buyback costs what the store paid (player can rebuy at sell price this session).
        map[itemCoid] = new StoreBuybackListing
        {
            Cbid = sold.Cbid,
            Type = sold.Type,
            DisplayName = sold.DisplayName,
            Quantity = qty,
            UnitPrice = Math.Max(1, unitSellPrice),
            IsMissionItem = sold.IsMissionItem,
        };

        Logger.WriteLog(LogType.Debug,
            "VendorStore: buyback listed char={0} itemCoid={1} cbid={2} qty={3} unit={4}",
            characterCoid,
            itemCoid,
            sold.Cbid,
            qty,
            unitSellPrice);
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

        // Client keeps the sold TFID on the store UI for buyback; remember CBID/price by that COID.
        RegisterBuyback(character.ObjectId.Coid, itemCoid, invItem, qty, unit);

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

    static StoreTemplate.ItemType MatchBuyLine(
        List<StoreTemplate.ItemType> stock,
        long itemCoid,
        IReadOnlyDictionary<long, StoreTemplate.ItemType> session)
    {
        if (itemCoid <= 0)
            return null;

        // Preferred: server-assigned store-slot COID from MaterializeStockSession.
        if (session != null && session.TryGetValue(itemCoid, out var bySlot) && bySlot?.CBID > 0)
            return bySlot;

        if (stock == null)
            return null;

        // Catalog CBID as item identity (some UI paths / tests).
        if (itemCoid <= int.MaxValue)
        {
            var byCbid = stock.FirstOrDefault(s => s.CBID == (int)itemCoid && s.CBID > 0);
            if (byCbid != null)
                return byCbid;
        }

        return null;
    }

    /// <summary>
    /// Assign server COIDs for each stock CBID, remember them for buy matching, and send
    /// CreateSimpleObject (IsInInventory + CoidStore + IsInfinite) so the client can use those TFIDs.
    /// </summary>
    static void MaterializeStockSession(Character character, long storeCoid, TNLConnection conn)
    {
        if (character?.Map == null || storeCoid <= 0)
            return;

        var stock = ResolveStoreStock(character.Map, storeCoid);
        if (stock == null)
        {
            StockSessionByCharacter.TryRemove(character.ObjectId.Coid, out _);
            return;
        }

        var session = new Dictionary<long, StoreTemplate.ItemType>();
        byte slotX = 0;
        byte slotY = 0;
        foreach (var line in stock)
        {
            if (line == null || line.CBID <= 0)
                continue;

            var slotCoid = character.Map.LocalCoidCounter++;
            session[slotCoid] = line;

            if (conn == null)
                continue;

            var create = new CreateSimpleObjectPacket
            {
                CBID = line.CBID,
                CoidStore = storeCoid,
                ObjectId = new TFID(slotCoid, true),
                IsInInventory = true,
                IsInfinite = line.Unlimited,
                Quantity = Math.Max(1, line.Unlimited ? 1 : 1),
                InventoryPositionX = slotX,
                InventoryPositionY = slotY,
                Value = line.Value > 0 ? line.Value : (int)Math.Min(int.MaxValue, ResolveBuyPrice(line)),
                IsIdentified = true,
                Scale = 1f,
            };
            conn.SendGamePacket(create);

            slotX++;
            if (slotX >= 8)
            {
                slotX = 0;
                slotY++;
            }
        }

        StockSessionByCharacter[character.ObjectId.Coid] = session;
        Logger.WriteLog(LogType.Debug,
            "VendorStore: stock session char={0} store={1} slots={2}",
            character.ObjectId.Coid,
            storeCoid,
            session.Count);
    }

    static long ResolveBuyPrice(StoreTemplate.ItemType line)
    {
        if (line == null)
            return 0;
        if (TestBuyPriceResolver != null)
            return TestBuyPriceResolver(line.CBID);
        if (line.Value > 0)
            return line.Value;

        var cb = AssetManager.Instance.GetCloneBase(line.CBID);
        return cb?.CloneBaseSpecific.BaseValue ?? 0;
    }

    static InventoryCatalogEntry ResolveBuyCatalogEntry(int cbid)
    {
        if (TestBuyCatalogResolver != null)
            return TestBuyCatalogResolver(cbid);
        return InventoryCatalog.FromAssetManager().FindAny(cbid);
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
