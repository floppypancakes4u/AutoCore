namespace AutoCore.Game.Managers;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.AgentDebug;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

/// <summary>
/// Manages procedural loot generation based on loot tables from wad.xml.
/// </summary>
public class LootManager : Singleton<LootManager>
{
    private readonly Random _random = new();

    // Item index: (ItemType, Rarity) -> List of (CBID, RequiredLevel, RequiredClass)
    private Dictionary<(CloneBaseObjectType type, short rarity), List<GeneratableItem>> _itemIndex;
    private List<CommodityDropEntry> _commodityIndex;
    private bool _initialized;

    private class GeneratableItem
    {
        public int CBID { get; set; }
        public short RequiredLevel { get; set; }
        public string Name { get; set; }
        /// <summary>Clonebase <c>RequiredClass</c>; negative means unrestricted.</summary>
        public int RequiredClass { get; set; } = -1;
    }

    private class CommodityDropEntry
    {
        public int CBID { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public float DropChance { get; set; }
    }

    /// <summary>Inputs for multi-track retail death loot (gear, junk, consumable, credits, commodity).</summary>
    public sealed class DeathLootRequest
    {
        public SectorMap Map { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public Character Killer { get; set; }
        public int VictimCbid { get; set; }
        public byte Level { get; set; }
        public int LootTableId { get; set; }
        /// <summary>Vehicle <c>tinLootChance</c> 0–255. Creature path uses <see cref="UseCreatureDropFormula"/>.</summary>
        public byte TemplateLootChance { get; set; }
        public byte GearRolls { get; set; }
        public bool UseCreatureDropFormula { get; set; }
        public byte CreatureBaseLootChance { get; set; }
        /// <summary>Ground scatter radius around <see cref="Position"/> (default ~1–2 for NPC deaths).</summary>
        public float LootScatterRadius { get; set; } = 1.0f;
        /// <summary>
        /// Pure map-prop destruction (ram/scenery). Retail: only <c>tLootWeights</c> fixed junk
        /// for the destroyed CBID — no gear table, no commodity pool (fence/rubble stay empty).
        /// Commodities / gear / credits stay on creature and vehicle death tracks.
        /// </summary>
        public bool MapPropSalvage { get; set; }
    }

    /// <summary>
    /// Initializes the loot manager by building the item index.
    /// Call this after AssetManager.LoadAllData() completes.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        // LootRate comes from loot.tuning.json (LootTuning.ApplyFromConfigFiles) — already applied at launch.
        Logger.WriteLog(LogType.Initialize,
            $"LootManager: LootRate={LootTuning.LootRate:0.###} (1.0=retail, higher=more drops)");

        BuildItemIndex();
        BuildCommodityIndex();
        LogLootStatistics();
        _initialized = true;
    }

    /// <summary>
    /// Test seam: registers a single generatable item into the index and marks the manager
    /// initialized, so loot generation can be exercised without loading the WAD item catalog.
    /// </summary>
    /// <param name="requiredClass">Race/class restriction; negative = any race.</param>
    internal void SeedGeneratableItemForTests(
        CloneBaseObjectType type,
        short rarity,
        int cbid,
        short requiredLevel,
        int requiredClass = -1)
    {
        _itemIndex ??= new Dictionary<(CloneBaseObjectType type, short rarity), List<GeneratableItem>>();
        var key = (type, rarity);
        if (!_itemIndex.TryGetValue(key, out var list))
        {
            list = new List<GeneratableItem>();
            _itemIndex[key] = list;
        }

        list.Add(new GeneratableItem
        {
            CBID = cbid,
            RequiredLevel = requiredLevel,
            Name = "test",
            RequiredClass = requiredClass,
        });
        _initialized = true;
    }

    /// <summary>Test seam: seed a commodity drop candidate.</summary>
    internal void SeedCommodityForTests(int cbid, int minLevel, int maxLevel, float dropChance)
    {
        _commodityIndex ??= new List<CommodityDropEntry>();
        _commodityIndex.Add(new CommodityDropEntry
        {
            CBID = cbid,
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            DropChance = dropChance,
        });
        _initialized = true;
    }

    /// <summary>Test seam: clears the item index / initialized flag so tests don't leak state.</summary>
    internal void ResetForTests()
    {
        _itemIndex = null;
        _commodityIndex = null;
        _initialized = false;
        LootTuning.ResetToDefaults();
    }

    private void BuildItemIndex()
    {
        _itemIndex = new Dictionary<(CloneBaseObjectType type, short rarity), List<GeneratableItem>>();

        var allCloneBases = AssetManager.Instance.GetAllCloneBases();

        foreach (var kvp in allCloneBases)
        {
            var cbid = kvp.Key;
            var cloneBase = kvp.Value;

            // Only include items that are generatable
            if (cloneBase.CloneBaseSpecific.IsGeneratable != 1)
                continue;

            // Only include item types that can drop as loot
            var type = cloneBase.Type;
            if (!IsLootableType(type))
                continue;

            // Get rarity and level from the appropriate specific data
            short rarity = 0;
            short requiredLevel = 0;
            var requiredClass = -1;

            if (cloneBase is CloneBaseObject cloneBaseObject)
            {
                rarity = cloneBaseObject.SimpleObjectSpecific.ItemRarity;
                requiredLevel = cloneBaseObject.SimpleObjectSpecific.RequiredLevel;
                requiredClass = cloneBaseObject.SimpleObjectSpecific.RequiredClass;
            }

            var key = (type, rarity);
            if (!_itemIndex.ContainsKey(key))
                _itemIndex[key] = new List<GeneratableItem>();

            _itemIndex[key].Add(new GeneratableItem
            {
                CBID = cbid,
                RequiredLevel = requiredLevel,
                Name = cloneBase.CloneBaseSpecific.UniqueName,
                RequiredClass = requiredClass,
            });
        }

        Logger.WriteLog(LogType.Initialize, $"LootManager: Built item index with {_itemIndex.Count} type/rarity combinations");
    }

    private void BuildCommodityIndex()
    {
        _commodityIndex = new List<CommodityDropEntry>();
        var allCloneBases = AssetManager.Instance.GetAllCloneBases();

        foreach (var kvp in allCloneBases)
        {
            if (kvp.Value is not CloneBaseCommodity commodity)
                continue;

            var dropChance = commodity.CommoditySpecific.DropChance;
            if (dropChance <= 0f)
                continue;

            _commodityIndex.Add(new CommodityDropEntry
            {
                CBID = kvp.Key,
                MinLevel = commodity.CommoditySpecific.MinLevel,
                MaxLevel = commodity.CommoditySpecific.MaxLevel > 0
                    ? commodity.CommoditySpecific.MaxLevel
                    : 100,
                DropChance = dropChance,
            });
        }

        Logger.WriteLog(LogType.Initialize, $"LootManager: Indexed {_commodityIndex.Count} droppable commodities");
    }

    private bool IsLootableType(CloneBaseObjectType type)
    {
        return type switch
        {
            CloneBaseObjectType.Weapon => true,
            CloneBaseObjectType.Armor => true,
            CloneBaseObjectType.PowerPlant => true,
            CloneBaseObjectType.WheelSet => true,
            CloneBaseObjectType.Vehicle => true,
            CloneBaseObjectType.Gadget => true,
            CloneBaseObjectType.TinkeringKit => true,
            CloneBaseObjectType.Accessory => true,
            CloneBaseObjectType.RaceItem => true,
            CloneBaseObjectType.Ornament => true,
            CloneBaseObjectType.Item => true,
            _ => false
        };
    }

    /// <summary>
    /// True when the retail client will <b>not</b> offer world ground pickup for this CBID
    /// (<c>FUN_005130e0</c> @ 0x005130E0 returns 0). Those types may use cargo auto-loot instead.
    /// Weapon/Armor/PowerPlant/WheelSet/Item/Money/etc. return 1 on the client and must ground-drop.
    /// </summary>
    public bool RequiresAutoLoot(int cbid)
    {
        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase == null)
            return false;

        // Client FUN_005130e0 pickable cases: 4,6,8,10,0xC,0x10,0x1A,0x1C,0x32,0x34,0x42.
        // Types not in that set (and not special vehicle-flag) are not world-pickable.
        return cloneBase.Type switch
        {
            CloneBaseObjectType.Ornament => true,
            CloneBaseObjectType.RaceItem => true,
            // Full vehicle chassis as a drop is not a normal ground pickup (type 0xE special-cased).
            CloneBaseObjectType.Vehicle => true,
            _ => false
        };
    }

    private void LogLootStatistics()
    {
        var lootTables = AssetManager.Instance.GetAllLootTables().ToList();
        Logger.WriteLog(LogType.Initialize, $"LootManager: Loaded {lootTables.Count} loot tables");

        // Log item index statistics
        var totalItems = _itemIndex.Values.Sum(list => list.Count);
        Logger.WriteLog(LogType.Initialize, $"LootManager: {totalItems} generatable items indexed");

        // Log items by type
        var byType = _itemIndex.GroupBy(kvp => kvp.Key.type)
            .Select(g => new { Type = g.Key, Count = g.Sum(kvp => kvp.Value.Count) })
            .OrderByDescending(x => x.Count);

        foreach (var entry in byType)
        {
            Logger.WriteLog(LogType.Initialize, $"  {entry.Type}: {entry.Count} items");
        }

        // Log sample loot tables
        foreach (var table in lootTables.Take(5))
        {
            Logger.WriteLog(LogType.Initialize, $"  Loot Table {table.Id} ({table.Name}): DropChance={table.DropChance:P1}, Rolls={table.LootRolls}");
        }
    }

    /// <summary>
    /// Generates loot for a creature when it dies using procedural generation.
    /// Drop chances are scaled by <see cref="LootTuning.LootRate"/>.
    /// </summary>
    public List<int> GenerateLoot(Creature creature)
    {
        var lootItems = new List<int>();

        if (!_initialized)
        {
            Logger.WriteLog(LogType.Error, "LootManager.GenerateLoot: LootManager not initialized!");
            return lootItems;
        }

        if (creature.CloneBaseObject is not CloneBaseCreature creatureCloneBase)
        {
            return lootItems;
        }

        var creatureSpecific = creatureCloneBase.CreatureSpecific;
        var lootTableId = creatureSpecific.LootTableId;
        var baseLootChance = creatureSpecific.BaseLootChance;
        var creatureLevel = creature.Level;

        // Get the loot table
        var lootTable = AssetManager.Instance.GetLootTable(lootTableId);
        if (lootTable == null)
        {
            Logger.WriteLog(LogType.Debug, $"LootManager.GenerateLoot: No loot table {lootTableId} found for creature {creature.ObjectId.Coid}");
            return lootItems;
        }

        // BaseLootChance is 0-255; DropChance is 0-1 from wad.xml; both scaled by LootRate.
        var creatureLootChance = baseLootChance / 255.0;
        var effectiveDropChance = lootTable.DropChance * creatureLootChance;
        if (!LootTuning.Passes(effectiveDropChance, _random))
        {
            Logger.WriteLog(LogType.Debug,
                $"LootManager.GenerateLoot: Creature {creature.ObjectId.Coid} failed drop chance (base={effectiveDropChance:P2}, rate={LootTuning.LootRate:0.###})");
            return lootItems;
        }

        var rolls = lootTable.LootRolls > 0 ? lootTable.LootRolls : 1;
        for (var roll = 0; roll < rolls; roll++)
        {
            var item = GenerateSingleItem(lootTable, creatureLevel);
            if (item.HasValue)
            {
                lootItems.Add(item.Value);
                Logger.WriteLog(LogType.Debug, $"LootManager.GenerateLoot: Generated item CBID {item.Value} for creature {creature.ObjectId.Coid}");
            }
        }

        if (lootItems.Count > 0)
        {
            Logger.WriteLog(LogType.Debug, $"LootManager.GenerateLoot: Generated {lootItems.Count} items for creature {creature.ObjectId.Coid} from loot table {lootTableId} ({lootTable.Name})");
        }

        return lootItems;
    }

    /// <summary>
    /// Generates loot for a killed NPC vehicle from its <c>tVehicleTemplate</c> loot columns
    /// (LootTableId / LootChance / LootRolls) rather than a creature clonebase. LootChance 0 drops
    /// nothing; otherwise <paramref name="lootRolls"/> items are rolled from the table.
    /// Chances are scaled by <see cref="LootTuning.LootRate"/>.
    /// </summary>
    public List<int> GenerateLoot(int lootTableId, byte lootChance, byte lootRolls, byte level)
    {
        var lootItems = new List<int>();

        if (!_initialized)
        {
            Logger.WriteLog(LogType.Error, "LootManager.GenerateLoot(template): LootManager not initialized!");
            return lootItems;
        }

        if (lootChance == 0 || lootRolls == 0 || lootTableId <= 0)
            return lootItems;

        // tinLootChance 0-255, scaled by LootRate.
        if (!LootTuning.Passes(lootChance / 255.0, _random))
            return lootItems;

        var lootTable = AssetManager.Instance.GetLootTable(lootTableId);
        if (lootTable == null)
        {
            Logger.WriteLog(LogType.Debug, $"LootManager.GenerateLoot(template): No loot table {lootTableId} found");
            return lootItems;
        }

        for (var roll = 0; roll < lootRolls; roll++)
        {
            var item = GenerateSingleItem(lootTable, level);
            if (item.HasValue)
                lootItems.Add(item.Value);
        }

        return lootItems;
    }

    /// <summary>
    /// Multi-track retail death loot: gear + consumable + credits (master chance) + fixed junk + commodities.
    /// Map-prop path (<see cref="DeathLootRequest.MapPropSalvage"/>) is junk-only via <c>tLootWeights</c>.
    /// Chance tracks use <see cref="LootTuning.LootRate"/>.
    /// </summary>
    public void ProcessDeathLoot(DeathLootRequest request)
    {
        if (!_initialized || request?.Map == null)
            return;

        var lootCbids = new List<int>();
        var killerRace = ResolveKillerRace(request.Killer);
        var junkWeightCount = AssetManager.Instance.GetLootWeightsForDestroyed(request.VictimCbid)?.Count ?? 0;
        var dropCommodities = request.Map.ContinentObject?.DropCommodities == true;
        var commodityPool = _commodityIndex?.Count ?? 0;

        // Track D: fixed junk (independent of master chance) — only track for pure map props.
        var junkCount = 0;
        if (TryRollFixedJunk(request.VictimCbid, out var junkCbid))
        {
            lootCbids.Add(junkCbid);
            junkCount = 1;
        }

        var gearCount = 0;
        var consumableCount = 0;
        var creditsAwarded = 0;
        var commodityCount = 0;
        var masterPass = false;

        if (request.MapPropSalvage)
        {
            // Retail scenery: only the handful of CBIDs in tLootWeights drop anything.
            LogFilters.WriteIf(
                LogFilters.Loot,
                LogType.Debug,
                "LootManager.ProcessDeathLoot: mapProp junk={0} victimCbid={1} junkWeights={2}",
                junkCount, request.VictimCbid, junkWeightCount);

            DeliverDeathLoot(
                lootCbids,
                request.Position,
                request.Rotation,
                request.Map,
                request.Killer,
                request.LootScatterRadius);
            return;
        }

        var table = request.LootTableId > 0
            ? AssetManager.Instance.GetLootTable(request.LootTableId)
            : null;

        masterPass = PassMasterDropChance(request, table);

        if (masterPass && table != null)
        {
            var rolls = ResolveGearRollCount(request, table);
            for (var roll = 0; roll < rolls; roll++)
            {
                var item = GenerateSingleItem(table, request.Level, killerRace);
                if (!item.HasValue)
                    continue;
                lootCbids.Add(item.Value);
                gearCount++;
            }

            if (TryRollConsumable(table, request.Level, out var consumableCbid))
            {
                lootCbids.Add(consumableCbid);
                consumableCount = 1;
            }

            creditsAwarded = RollCreditsAmount(table);
        }

        // Track E: commodities (map gate + per-commodity chance × LootRate) — combatants only.
        if (TryRollCommodity(request, out var commodityCbid))
        {
            lootCbids.Add(commodityCbid);
            commodityCount = 1;
        }

        if (creditsAwarded > 0 && request.Killer != null)
            TryGiveCredits(request.Killer, creditsAwarded);

        LogFilters.WriteIf(
            LogFilters.Loot,
            LogType.Debug,
            "LootManager.ProcessDeathLoot: gear={0} junk={1} cons={2} credits={3} commodity={4} table={5} race={6} victimCbid={7} dropCommodities={8} junkWeights={9} commodityPool={10} ignoreGate={11}",
            gearCount, junkCount, consumableCount, creditsAwarded, commodityCount,
            request.LootTableId, killerRace?.ToString() ?? "any", request.VictimCbid,
            dropCommodities, junkWeightCount, commodityPool, LootTuning.IgnoreDropCommoditiesGate);

        if (lootCbids.Count == 0)
        {
            LogFilters.WriteIf(
                LogFilters.Loot,
                LogType.Debug,
                "LootManager.ProcessDeathLoot: empty — reasons: junkWeights={0}, dropCommodities={1}, ignoreGate={2}, commodityPool={3}, masterPass={4}",
                junkWeightCount,
                dropCommodities,
                LootTuning.IgnoreDropCommoditiesGate,
                commodityPool,
                masterPass);
        }

        DeliverDeathLoot(
            lootCbids,
            request.Position,
            request.Rotation,
            request.Map,
            request.Killer,
            request.LootScatterRadius);
    }

    private bool PassMasterDropChance(DeathLootRequest request, LootTable table)
    {
        if (request.UseCreatureDropFormula)
        {
            if (table == null)
                return false;
            var effective = table.DropChance * (request.CreatureBaseLootChance / 255.0);
            return LootTuning.Passes(effective, _random);
        }

        if (request.TemplateLootChance == 0 || request.GearRolls == 0 || request.LootTableId <= 0)
            return false;
        if (table == null)
            return false;
        return LootTuning.Passes(request.TemplateLootChance / 255.0, _random);
    }

    private static int ResolveGearRollCount(DeathLootRequest request, LootTable table)
    {
        if (request.UseCreatureDropFormula)
        {
            var rolls = table.LootRolls > 0 ? table.LootRolls : 1;
            return rolls;
        }

        return Math.Max(0, (int)request.GearRolls);
    }

    /// <summary>Weighted pick from <c>tLootWeights</c> for the destroyed CBID.</summary>
    public bool TryRollFixedJunk(int destroyedCbid, out int lootCbid)
    {
        lootCbid = 0;
        if (destroyedCbid <= 0)
            return false;

        var weights = AssetManager.Instance.GetLootWeightsForDestroyed(destroyedCbid);
        if (weights == null || weights.Count == 0)
            return false;

        var total = 0;
        foreach (var w in weights)
            total += Math.Max(0, (int)w.Weight);
        if (total <= 0)
            return false;

        var roll = _random.Next(total);
        var cumulative = 0;
        foreach (var w in weights)
        {
            cumulative += Math.Max(0, (int)w.Weight);
            if (roll < cumulative)
            {
                lootCbid = w.LootCbid;
                return lootCbid > 0;
            }
        }

        lootCbid = weights[^1].LootCbid;
        return lootCbid > 0;
    }

    /// <summary>Independent consumable roll using table chance + <c>tConsumables</c> (× LootRate).</summary>
    public bool TryRollConsumable(LootTable table, byte level, out int cbid)
    {
        cbid = 0;
        if (table == null)
            return false;

        if (!LootTuning.Passes(table.ConsumableDropChance, _random))
            return false;

        var entries = AssetManager.Instance.GetConsumables();
        if (entries == null || entries.Count == 0)
            return false;

        var eligible = new List<ConsumableLootEntry>();
        var totalWeight = 0;
        foreach (var e in entries)
        {
            if (level < e.LevelMin || level > e.LevelMax)
                continue;
            var w = e.Offset > 0 ? e.Offset : 1;
            eligible.Add(e);
            totalWeight += w;
        }

        if (eligible.Count == 0 || totalWeight <= 0)
            return false;

        var roll = _random.Next(totalWeight);
        var cumulative = 0;
        foreach (var e in eligible)
        {
            cumulative += e.Offset > 0 ? e.Offset : 1;
            if (roll < cumulative)
            {
                cbid = e.Cbid;
                return cbid > 0;
            }
        }

        cbid = eligible[^1].Cbid;
        return cbid > 0;
    }

    /// <summary>Credits amount from table chance (× LootRate), or 0.</summary>
    public int RollCreditsAmount(LootTable table)
    {
        if (table == null)
            return 0;

        if (!LootTuning.Passes(table.DropCreditsChance, _random))
            return 0;

        var min = Math.Max(0, table.MinCreditsDrop);
        var max = Math.Max(min, table.MaxCreditsDrop);
        if (max <= 0)
            return 0;
        return min == max ? min : _random.Next(min, max + 1);
    }

    public bool TryRollCommodity(DeathLootRequest request, out int cbid)
    {
        cbid = 0;
        if (request?.Map?.ContinentObject == null)
            return false;

        // Continent bitDropCommodities (combatant deaths only). Map props never reach here.
        // Testing: LootTuning.IgnoreDropCommoditiesGate bypasses for creature/vehicle.
        if (!request.Map.ContinentObject.DropCommodities && !LootTuning.IgnoreDropCommoditiesGate)
            return false;

        var pool = _commodityIndex;
        if (pool == null || pool.Count == 0)
            return false;

        var level = request.Level;
        var eligible = new List<CommodityDropEntry>();
        foreach (var e in pool)
        {
            if (level < e.MinLevel || level > e.MaxLevel)
                continue;
            if (e.DropChance <= 0f)
                continue;
            eligible.Add(e);
        }

        if (eligible.Count == 0)
            return false;

        // One roll: pick random eligible, then apply DropChance × LootRate.
        var pick = eligible[_random.Next(eligible.Count)];
        if (!LootTuning.Passes(pick.DropChance, _random))
            return false;

        cbid = pick.CBID;
        return cbid > 0;
    }

    private static int? ResolveKillerRace(Character killer)
    {
        if (killer?.CloneBaseObject is CloneBaseCharacter charCb)
            return charCb.CharacterSpecific.Race;
        return null;
    }

    private static void TryGiveCredits(Character killer, int amount)
    {
        if (killer == null || amount <= 0)
            return;

        try
        {
            var result = CurrencySync.AddCredits(persistence: null, killer, amount);
            if (result.DeltaPacket != null && killer.OwningConnection != null)
                killer.OwningConnection.SendGamePacket(result.DeltaPacket);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"LootManager: GiveCredits failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Delivers rolled loot CBIDs as world ground objects (CreateSimpleObject, no ghost).
    /// Client <c>FUN_005130e0</c> allows pickup for Weapon/Armor/etc.; player uses ItemPickup (0x2055).
    /// Only non-pickable types (<see cref="RequiresAutoLoot"/>) try cargo auto-loot when a killer is known.
    /// Credits use GiveCredits (0x205E) via <see cref="ProcessDeathLoot"/> — not handled here.
    /// </summary>
    public void DeliverDeathLoot(
        IReadOnlyList<int> cbids,
        Vector3 deathPosition,
        Quaternion deathRotation,
        SectorMap map,
        Character killerCharacter,
        float scatterRadius = 1.0f)
    {
        if (cbids == null || cbids.Count == 0 || map == null)
            return;

        if (scatterRadius < 0f)
            scatterRadius = 0f;

        var random = new Random();
        foreach (var cbid in cbids)
        {
            if (cbid <= 0)
                continue;

            // Rare: ornament/race-item not ground-pickable on retail client.
            if (RequiresAutoLoot(cbid) && killerCharacter != null)
            {
                if (AutoLootItem(cbid, killerCharacter))
                    continue;

                Logger.WriteLog(LogType.Debug,
                    $"LootManager.DeliverDeathLoot: auto-loot failed for CBID {cbid}; falling back to ground");
            }

            Vector3 lootPosition;
            if (scatterRadius <= 0.001f)
            {
                lootPosition = deathPosition;
            }
            else
            {
                var angle = (float)(random.NextDouble() * 2.0 * Math.PI);
                var distance = scatterRadius * (0.25f + (float)random.NextDouble() * 0.75f);
                lootPosition = new Vector3(
                    deathPosition.X + (float)(Math.Cos(angle) * distance),
                    deathPosition.Y,
                    deathPosition.Z + (float)(Math.Sin(angle) * distance));
            }

            SpawnLootItem(cbid, lootPosition, deathRotation, map);
        }
    }

    private int? GenerateSingleItem(LootTable lootTable, byte creatureLevel, int? killerRace = null)
    {
        // Step 1: Roll item type
        var itemType = RollItemType(lootTable);
        if (itemType == null)
            return null;

        // Step 2: Roll rarity
        var rarity = RollRarity(lootTable);

        // Step 3: Calculate target level range
        var (minLevel, maxLevel) = CalculateLevelRange(lootTable, creatureLevel);

        // Step 4: Find matching item (race-filtered when killer race known)
        var item = FindGeneratableItem(itemType.Value, rarity, minLevel, maxLevel, killerRace);
        if (item == null)
        {
            // Try with lower rarity if no item found
            for (short fallbackRarity = (short)(rarity - 1); fallbackRarity >= 0; fallbackRarity--)
            {
                item = FindGeneratableItem(itemType.Value, fallbackRarity, minLevel, maxLevel, killerRace);
                if (item != null)
                    break;
            }
        }

        if (item == null)
        {
            Logger.WriteLog(LogType.Debug,
                $"LootManager: No item found for type={itemType}, rarity={rarity}, level={minLevel}-{maxLevel}, race={killerRace?.ToString() ?? "any"}");
            return null;
        }

        LogFilters.WriteIf(
            LogFilters.Loot,
            LogType.Debug,
            "LootManager: Generated item CBID {0} ({1}), type={2}, rarity={3}, level={4}",
            item.CBID, item.Name, itemType, rarity, item.RequiredLevel);
        return item.CBID;
    }

    private CloneBaseObjectType? RollItemType(LootTable lootTable)
    {
        var totalWeight = lootTable.GetTotalItemTypeWeight();
        if (totalWeight <= 0)
            return null;

        var roll = _random.Next(totalWeight);
        var cumulative = 0;

        if ((cumulative += lootTable.ChanceWeapon) > roll) return CloneBaseObjectType.Weapon;
        if ((cumulative += lootTable.ChanceArmor) > roll) return CloneBaseObjectType.Armor;
        if ((cumulative += lootTable.ChancePowerPlant) > roll) return CloneBaseObjectType.PowerPlant;
        if ((cumulative += lootTable.ChanceWheelSet) > roll) return CloneBaseObjectType.WheelSet;
        if ((cumulative += lootTable.ChanceVehicle) > roll) return CloneBaseObjectType.Vehicle;
        if ((cumulative += lootTable.ChanceGadget) > roll) return CloneBaseObjectType.Gadget;
        if ((cumulative += lootTable.ChanceTinkeringKit) > roll) return CloneBaseObjectType.TinkeringKit;
        if ((cumulative += lootTable.ChanceAccessory) > roll) return CloneBaseObjectType.Accessory;
        if ((cumulative += lootTable.ChanceRaceItem) > roll) return CloneBaseObjectType.RaceItem;
        if ((cumulative += lootTable.ChanceOrnament) > roll) return CloneBaseObjectType.Ornament;
        if ((cumulative += lootTable.ChanceOther) > roll) return CloneBaseObjectType.Item;

        return CloneBaseObjectType.Item; // Fallback
    }

    private short RollRarity(LootTable lootTable)
    {
        var totalWeight = lootTable.GetTotalRarityWeight();
        if (totalWeight <= 0)
            return 0;

        var roll = _random.Next(totalWeight);
        var cumulative = 0;

        for (short rarity = 0; rarity <= 8; rarity++)
        {
            cumulative += lootTable.GetRarityChance(rarity);
            if (cumulative > roll)
                return rarity;
        }

        return 0; // Fallback to common
    }

    private (short minLevel, short maxLevel) CalculateLevelRange(LootTable lootTable, byte creatureLevel)
    {
        // Calculate level offset based on loot table parameters
        var levelOffset = (short)(creatureLevel * lootTable.DropLevelOffset);
        levelOffset = Math.Min(levelOffset, lootTable.MaxLevelOffset);

        var minLevel = (short)Math.Max(1, creatureLevel - levelOffset);
        var maxLevel = (short)(creatureLevel + levelOffset);

        return (minLevel, maxLevel);
    }

    private GeneratableItem FindGeneratableItem(
        CloneBaseObjectType type,
        short rarity,
        short minLevel,
        short maxLevel,
        int? killerRace = null)
    {
        var key = (type, rarity);
        if (!_itemIndex.TryGetValue(key, out var items) || items.Count == 0)
            return null;

        // Filter by level range
        var eligibleItems = items.Where(i => i.RequiredLevel >= minLevel && i.RequiredLevel <= maxLevel).ToList();

        // If no items in level range, expand search to all items of this type/rarity
        if (eligibleItems.Count == 0)
            eligibleItems = items.ToList();

        if (killerRace.HasValue)
        {
            var raceFiltered = eligibleItems.Where(i => IsRaceCompatible(i.RequiredClass, killerRace.Value)).ToList();
            // Only apply race filter when it yields candidates; empty race pool means no drop for this roll.
            eligibleItems = raceFiltered;
        }

        if (eligibleItems.Count == 0)
            return null;

        // Random selection
        return eligibleItems[_random.Next(eligibleItems.Count)];
    }

    /// <summary>
    /// Client <c>FUN_005e07d0</c>: keep item if unrestricted, race matches, or races 2↔3 mutual.
    /// <paramref name="requiredClass"/> negative = any race.
    /// </summary>
    internal static bool IsRaceCompatible(int requiredClass, int killerRace)
    {
        if (requiredClass < 0)
            return true;
        if (requiredClass == killerRace)
            return true;
        // Client special-case: race 2 and 3 are mutually compatible.
        if ((requiredClass == 2 && killerRace == 3) || (requiredClass == 3 && killerRace == 2))
            return true;
        return false;
    }

    /// <summary>
    /// Auto-loots an item into the killer's cargo with the same client wire as /addItem and world pickup:
    /// Create (IsInInventory) → InventoryAddItemResponse → CargoSendAll.
    /// </summary>
    /// <returns>True when the item was added and outbound packets were sent.</returns>
    public bool AutoLootItem(int cbid, Character character)
    {
        if (character?.OwningConnection == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.AutoLootItem: Cannot auto-loot item {cbid} - character or connection is null");
            return false;
        }

        if (character.Inventory == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.AutoLootItem: character coid={character.ObjectId?.Coid} has no inventory");
            return false;
        }

        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.AutoLootItem: CloneBase not found for CBID {cbid}");
            return false;
        }

        var runtime = new InventoryRuntime(character);
        if (!runtime.CanAllocateItem)
        {
            Logger.WriteLog(LogType.Debug, $"LootManager.AutoLootItem: cannot allocate coid for character coid={character.ObjectId?.Coid}");
            return false;
        }

        var type = cloneBase.Type;
        if (!InventoryItemTypePolicy.IsInventoryCapable(type))
            type = CloneBaseObjectType.Item;

        var displayName = cloneBase.CloneBaseSpecific.UniqueName;
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = $"CBID {cbid}";

        var inventoryCoid = runtime.AllocateItemCoid();
        var claim = character.Inventory.PickupWorldItem(
            cbid,
            type,
            displayName,
            inventoryCoid,
            new InventoryItemCreator(),
            character.ObjectId.Coid);

        if (claim.AddedItem == null)
        {
            Logger.WriteLog(LogType.Debug,
                $"LootManager.AutoLootItem: claim failed for CBID {cbid}: {claim.Message}");
            return false;
        }

        foreach (var packet in claim.Packets)
            character.OwningConnection.SendGamePacket(packet);

        Logger.WriteLog(LogType.Debug,
            $"LootManager.AutoLootItem: {displayName} (CBID {cbid}) → cargo coid={inventoryCoid} for character coid={character.ObjectId?.Coid}");
        return true;
    }

    /// <summary>
    /// Spawns a loot item at the specified position and broadcasts it to all players in the map.
    /// </summary>
    public void SpawnLootItem(int cbid, Vector3 position, Quaternion rotation, SectorMap map)
    {
        if (!TrySpawnLootItem(cbid, position, rotation, map, out _))
            return;
    }

    /// <summary>
    /// Picks a random ground-pickable generatable Item CBID (used by <c>/loot</c>).
    /// </summary>
    public bool TryPickRandomGroundLootCbid(out int cbid)
    {
        cbid = 0;
        if (!_initialized || _itemIndex == null || _itemIndex.Count == 0)
            return false;

        var ground = new List<int>();
        foreach (var kvp in _itemIndex)
        {
            if (kvp.Key.type != CloneBaseObjectType.Item)
                continue;

            foreach (var entry in kvp.Value)
            {
                if (entry.CBID > 0 && !RequiresAutoLoot(entry.CBID))
                    ground.Add(entry.CBID);
            }
        }

        if (ground.Count == 0)
            return false;

        cbid = ground[_random.Next(ground.Count)];
        return true;
    }

    /// <summary>
    /// Any ground-pickable generatable CBID (Weapon/Armor/Item/etc. per FUN_005130e0).
    /// Used by guaranteed-drop testing fallback when loot tables are empty/"No Loot".
    /// Optional <paramref name="killerRace"/> keeps retail race filtering.
    /// </summary>
    public bool TryPickAnyGroundLootCbid(out int cbid, int? killerRace = null)
    {
        cbid = 0;
        if (!_initialized || _itemIndex == null || _itemIndex.Count == 0)
            return false;

        var ground = new List<int>();
        foreach (var kvp in _itemIndex)
        {
            foreach (var entry in kvp.Value)
            {
                if (entry.CBID <= 0 || RequiresAutoLoot(entry.CBID))
                    continue;
                if (killerRace.HasValue && !IsRaceCompatible(entry.RequiredClass, killerRace.Value))
                    continue;
                ground.Add(entry.CBID);
            }
        }

        if (ground.Count == 0)
            return false;

        cbid = ground[_random.Next(ground.Count)];
        return true;
    }

    /// <summary>
    /// Spawns a world ground item: map registration + CreateSimpleObject broadcast.
    /// Used by <c>/loot</c>, creature/vehicle death drops, and reaction loot — one contract for all.
    /// Does <b>not</b> create a GhostObject (plain ghost + local TFID AV at client 0x005B0EFF).
    /// Always uses CreateSimpleObject so the client treats the object as ground-pickable.
    /// </summary>
    public bool TrySpawnLootItem(
        int cbid,
        Vector3 position,
        Quaternion rotation,
        SectorMap map,
        out long spawnedCoid,
        bool possibleMissionItem = false)
    {
        spawnedCoid = -1;

        if (map == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.TrySpawnLootItem: Cannot spawn item {cbid} - map is null");
            return false;
        }

        var item = ClonedObjectBase.AllocateNewObjectFromCBID(cbid);
        if (item == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.TrySpawnLootItem: Unable to create item {cbid}");
            return false;
        }

        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        var cloneType = cloneBase?.Type ?? CloneBaseObjectType.Object;

        // #region agent log
        TossDebugLogger.Log(
            "H1",
            "LootManager.TrySpawnLootItem:allocate",
            "spawn item allocated",
            new { cbid, cloneType = cloneType.ToString(), runtimeType = item.GetType().Name });
        // #endregion

        spawnedCoid = map.LocalCoidCounter++;
        item.SetCoid(spawnedCoid, false);
        item.LoadCloneBase(cbid);
        item.Position = position;
        item.Rotation = rotation;
        item.Faction = -1;
        item.SetMap(map);
        // No CreateGhost: world loot is static ground pickup resolved by COID.
        // GhostObject + PackInitial for local items caused client AV 0x005B0EFF.

        if (map.GetObjectByCoid(spawnedCoid) == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.TrySpawnLootItem: Item with COID {spawnedCoid} was not found in map after SetMap");
            item.SetMap(null);
            spawnedCoid = -1;
            return false;
        }

        if (item is not SimpleObject simpleObject)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.TrySpawnLootItem: Item {item.GetType().Name} for CBID {cbid} is not a SimpleObject");
            item.SetMap(null);
            spawnedCoid = -1;
            return false;
        }

        if (possibleMissionItem)
            simpleObject.PossibleMissionItem = true;

        var createPacket = BuildGroundLootCreatePacket(simpleObject);

        // #region agent log
        TossDebugLogger.Log(
            "H1",
            "LootManager.TrySpawnLootItem:packet",
            "broadcast create packet",
            new
            {
                cbid,
                cloneType = cloneType.ToString(),
                packetOpcode = createPacket.Opcode.ToString(),
                packetRuntimeType = createPacket.GetType().Name,
                position = new { position.X, position.Y, position.Z },
                spawnedCoid
            },
            "post-fix");
        // #endregion

        BroadcastPacketToMap(map, createPacket);

        var itemName = AssetManager.Instance.GetCloneBase(cbid)?.CloneBaseSpecific.UniqueName ?? "Unknown";
        LogFilters.WriteIf(
            LogFilters.Loot,
            LogType.Debug,
            "LootManager.TrySpawnLootItem: Spawned {0} (CBID {1}, COID {2}) at {3}",
            itemName, cbid, spawnedCoid, position);
        return true;
    }

    /// <summary>
    /// Ground-loot wire shape: always <see cref="CreateSimpleObjectPacket"/> (not typed CreateArmor/Weapon).
    /// </summary>
    internal static CreateSimpleObjectPacket BuildGroundLootCreatePacket(SimpleObject simpleObject)
    {
        var createPacket = new CreateSimpleObjectPacket();
        simpleObject.WriteToPacket(createPacket);
        createPacket.IsBound = false;
        createPacket.IsInInventory = false;
        createPacket.IsIdentified = true;
        return createPacket;
    }

    private void BroadcastPacketToMap(SectorMap map, BasePacket packet)
    {
        if (map == null || packet == null)
            return;

        var charactersInMap = map.Objects.Values
            .OfType<Character>()
            .Where(c => c.OwningConnection != null)
            .ToList();

        foreach (var character in charactersInMap)
        {
            try
            {
                character.OwningConnection.SendGamePacket(packet);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"LootManager.BroadcastPacketToMap: Failed to send packet to character {character.Name}: {ex.Message}");
            }
        }
    }
}
