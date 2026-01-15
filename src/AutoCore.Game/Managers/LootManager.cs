namespace AutoCore.Game.Managers;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
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

    // Item index: (ItemType, Rarity) -> List of (CBID, RequiredLevel)
    private Dictionary<(CloneBaseObjectType type, short rarity), List<GeneratableItem>> _itemIndex;
    private bool _initialized;

    private class GeneratableItem
    {
        public int CBID { get; set; }
        public short RequiredLevel { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Initializes the loot manager by building the item index.
    /// Call this after AssetManager.LoadAllData() completes.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        BuildItemIndex();
        LogLootStatistics();
        _initialized = true;
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

            if (cloneBase is CloneBaseObject cloneBaseObject)
            {
                rarity = cloneBaseObject.SimpleObjectSpecific.ItemRarity;
                requiredLevel = cloneBaseObject.SimpleObjectSpecific.RequiredLevel;
            }

            var key = (type, rarity);
            if (!_itemIndex.ContainsKey(key))
                _itemIndex[key] = new List<GeneratableItem>();

            _itemIndex[key].Add(new GeneratableItem
            {
                CBID = cbid,
                RequiredLevel = requiredLevel,
                Name = cloneBase.CloneBaseSpecific.UniqueName
            });
        }

        Logger.WriteLog(LogType.Initialize, $"LootManager: Built item index with {_itemIndex.Count} type/rarity combinations");
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
    /// Determines if an item type requires auto-loot (direct to inventory) vs ground spawn.
    /// Equipment types (armor, weapons, etc.) cannot be picked up from the ground by the client,
    /// so they must be auto-looted directly to the player's inventory.
    /// </summary>
    public bool RequiresAutoLoot(int cbid)
    {
        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase == null)
            return false;

        return cloneBase.Type switch
        {
            // Equipment types require auto-loot - client doesn't allow ground pickup
            CloneBaseObjectType.Weapon => true,
            CloneBaseObjectType.Armor => true,
            CloneBaseObjectType.PowerPlant => true,
            CloneBaseObjectType.WheelSet => true,
            CloneBaseObjectType.Gadget => true,
            CloneBaseObjectType.TinkeringKit => true,
            CloneBaseObjectType.Accessory => true,
            CloneBaseObjectType.RaceItem => true,
            CloneBaseObjectType.Ornament => true,
            CloneBaseObjectType.Vehicle => true,
            // Item types can be picked up from ground
            CloneBaseObjectType.Item => false,
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

        // Check if anything drops at all (BaseLootChance from creature AND DropChance from table)
        // BaseLootChance is 0-255, convert to 0-1 range
        var creatureLootChance = baseLootChance / 255.0f;
        var effectiveDropChance = lootTable.DropChance * creatureLootChance;

        // if (_random.NextDouble() > effectiveDropChance)
        // {
        //     Logger.WriteLog(LogType.Debug, $"LootManager.GenerateLoot: Creature {creature.ObjectId.Coid} failed drop chance roll ({effectiveDropChance:P2})");
        //     return lootItems;
        // }

        // Generate items based on LootRolls
        for (int roll = 0; roll < lootTable.LootRolls; roll++)
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

    private int? GenerateSingleItem(LootTable lootTable, byte creatureLevel)
    {
        // Step 1: Roll item type
        var itemType = RollItemType(lootTable);
        if (itemType == null)
            return null;

        // Step 2: Roll rarity
        var rarity = RollRarity(lootTable);

        // Step 3: Calculate target level range
        var (minLevel, maxLevel) = CalculateLevelRange(lootTable, creatureLevel);

        // Step 4: Find matching item
        var item = FindGeneratableItem(itemType.Value, rarity, minLevel, maxLevel);
        if (item == null)
        {
            // Try with lower rarity if no item found
            for (short fallbackRarity = (short)(rarity - 1); fallbackRarity >= 0; fallbackRarity--)
            {
                item = FindGeneratableItem(itemType.Value, fallbackRarity, minLevel, maxLevel);
                if (item != null)
                    break;
            }
        }

        if (item == null)
        {
            Logger.WriteLog(LogType.Debug, $"LootManager: No item found for type={itemType}, rarity={rarity}, level={minLevel}-{maxLevel}");
            return null;
        }

        Logger.WriteLog(LogType.Debug, $"LootManager: Generated item CBID {item.CBID} ({item.Name}), type={itemType}, rarity={rarity}, level={item.RequiredLevel}");
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

    private GeneratableItem FindGeneratableItem(CloneBaseObjectType type, short rarity, short minLevel, short maxLevel)
    {
        var key = (type, rarity);
        if (!_itemIndex.TryGetValue(key, out var items) || items.Count == 0)
            return null;

        // Filter by level range
        var eligibleItems = items.Where(i => i.RequiredLevel >= minLevel && i.RequiredLevel <= maxLevel).ToList();

        // If no items in level range, expand search to all items of this type/rarity
        if (eligibleItems.Count == 0)
            eligibleItems = items;

        if (eligibleItems.Count == 0)
            return null;

        // Random selection
        return eligibleItems[_random.Next(eligibleItems.Count)];
    }

    /// <summary>
    /// Auto-loots an item directly to a player's inventory.
    /// Used for equipment types (armor, weapons, etc.) that the client doesn't allow ground pickup for.
    /// </summary>
    public void AutoLootItem(int cbid, Character character)
    {
        if (character?.OwningConnection == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.AutoLootItem: Cannot auto-loot item {cbid} - character or connection is null");
            return;
        }

        var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
        if (cloneBase == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.AutoLootItem: CloneBase not found for CBID {cbid}");
            return;
        }

        // Create the appropriate create packet based on item type
        CreateSimpleObjectPacket createPacket = cloneBase.Type switch
        {
            CloneBaseObjectType.Armor => new CreateArmorPacket(),
            CloneBaseObjectType.Weapon => new CreateWeaponPacket(),
            CloneBaseObjectType.PowerPlant => new CreatePowerPlantPacket(),
            CloneBaseObjectType.WheelSet => new CreateWheelSetPacket(),
            _ => new CreateSimpleObjectPacket()
        };

        // Create a temporary item object to populate the packet
        var item = ClonedObjectBase.AllocateNewObjectFromCBID(cbid);
        if (item == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.AutoLootItem: Unable to create item {cbid}");
            return;
        }

        // Set up the item with a local COID for the character's inventory
        var map = character.Map;
        long assignedCoid = -1;
        if (map != null)
        {
            assignedCoid = map.LocalCoidCounter++;
        }
        item.SetCoid(assignedCoid, false);
        item.LoadCloneBase(cbid);

        // Write item data to packet
        if (item is SimpleObject simpleObject)
        {
            simpleObject.WriteToPacket(createPacket);
        }

        // Mark as inventory item
        createPacket.IsInInventory = true;
        createPacket.IsBound = false;
        createPacket.IsIdentified = true;

        var itemName = cloneBase.CloneBaseSpecific.UniqueName;
        Logger.WriteLog(LogType.Debug, $"LootManager.AutoLootItem: Auto-looted {itemName} (CBID {cbid}). Inventory system WIP");
    }

    /// <summary>
    /// Spawns a loot item at the specified position and broadcasts it to all players in the map.
    /// </summary>
    public void SpawnLootItem(int cbid, Vector3 position, Quaternion rotation, SectorMap map)
    {
        if (map == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.SpawnLootItem: Cannot spawn loot item {cbid} - map is null");
            return;
        }

        // Create the item using AllocateNewObjectFromCBID
        var item = ClonedObjectBase.AllocateNewObjectFromCBID(cbid);
        if (item == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.SpawnLootItem: Unable to create item {cbid}");
            return;
        }

        // Set up the item
        var assignedCoid = map.LocalCoidCounter++;
        item.SetCoid(assignedCoid, false);
        item.LoadCloneBase(cbid);
        item.Position = position;
        item.Rotation = rotation;
        item.Faction = -1; // Loot items are neutral
        item.SetMap(map);
        item.CreateGhost();
        
        // Verify the item was added to the map
        var verifyItem = map.GetObjectByCoid(assignedCoid);
        if (verifyItem == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.SpawnLootItem: Item with COID {assignedCoid} was not found in map after SetMap! This is a critical error.");
            return;
        }

        // For loot items, always use CreateSimpleObjectPacket so they can be picked up
        // The client only recognizes CreateSimpleObject opcode items as pickable from the ground
        // Even though armor/weapons have their own packet types, loot items must use CreateSimpleObject
        CreateSimpleObjectPacket createPacket = null;

        if (item is SimpleObject simpleObject)
        {
            createPacket = new CreateSimpleObjectPacket();
            simpleObject.WriteToPacket(createPacket);
        }
        else
        {
            Logger.WriteLog(LogType.Error, $"LootManager.SpawnLootItem: Item {item.GetType().Name} for CBID {cbid} is not a SimpleObject");
            item.SetMap(null);
            return;
        }

        if (createPacket == null)
        {
            Logger.WriteLog(LogType.Error, $"LootManager.SpawnLootItem: Failed to create packet for item {cbid}");
            item.SetMap(null);
            return;
        }

        // Loot items should be pickupable (not bound)
        createPacket.IsBound = false;

        // Broadcast to all players in the map
        BroadcastPacketToMap(map, createPacket);

        var itemName = AssetManager.Instance.GetCloneBase(cbid)?.CloneBaseSpecific.UniqueName ?? "Unknown";
        Logger.WriteLog(LogType.Debug, $"LootManager.SpawnLootItem: Spawned {itemName} (CBID {cbid}, COID {assignedCoid}) at {position}");
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
