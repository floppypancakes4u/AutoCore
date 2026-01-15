# Loot System Investigation

## Problem Statement

When creatures die, loot is not spawning. Debug logs show:
```
LootManager.GenerateLoot: Creature 2712 (CBID: 19703) - LootTableId: 5, BaseLootChance: 10
LootManager.GenerateLoot: No items found in loot table 5 for creature 2712
```

The creature has a valid `LootTableId` (5) and `BaseLootChance` (10), but no items are being found for that loot table.

## Code Flow

1. `Creature.OnDeath()` calls `LootManager.Instance.GenerateLoot(this)`
2. `GenerateLoot()` reads `LootTableId` and `BaseLootChance` from `CreatureSpecific`
3. `GetLootTableItems(lootTableId)` searches for items where `InLootGenerator == lootTableId`
4. If items found, one is randomly selected and spawned via `SpawnLootItem()`

## Data Structures

### Creature Loot Config (CreatureSpecific.cs)
- `LootTableId` (int) - ID of the loot table for this creature
- `BaseLootChance` (byte, 0-255) - Chance of loot dropping

### Item Loot Config (CloneBaseSpecific.cs)
- `InLootGenerator` (uint) - Which loot table(s) this item belongs to
- `InStores` (uint) - Which stores sell this item

### SpawnPoint Loot Config (SpawnPointTemplate.cs)
- `Loot` (int) - Could be a CBID or loot table ID
- `LootPercent` (float)
- `LootChance` (float)

## Fixes Applied

### 1. Fixed CloneBase Iteration (AssetManager.cs)
Added `GetAllCloneBases()` method to properly enumerate all loaded CloneBases instead of iterating through arbitrary CBID ranges.

### 2. Fixed Type Filtering (LootManager.cs)
Original code only checked `CloneBaseObject` types, missing weapons (`CloneBaseWeapon`), armor (`CloneBaseArmor`), etc. Now checks all `CloneBase` types since `InLootGenerator` is on `CloneBaseSpecific`.

### 3. Added Bitmask Matching (LootManager.cs)
`InLootGenerator` might be a bitmask rather than a simple ID. Added fallback to check `(InLootGenerator & (1 << lootTableId)) != 0` for lootTableId < 32.

### 4. Added SpawnPoint Loot Fallback (LootManager.cs)
If creature's `LootTableId` yields no items, now checks the SpawnPoint's `Loot` field as an alternative source.

### 5. Added Diagnostics (LootManager.cs)
`LogLootTableStatistics()` is called at startup and logs:
- All unique `InLootGenerator` values found in CloneBase data
- Sample items for each loot table
- Creature statistics (total, with LootTableId, with BaseLootChance)
- Whether creature LootTableIds match any InLootGenerator values

## Files Modified

- `AutoCore.Game/Managers/AssetManager.cs` - Added `GetAllCloneBases()`
- `AutoCore.Game/Managers/LootManager.cs` - Complete overhaul of lookup logic
- `AutoCore.Sector/Program.cs` - Added `LogLootTableStatistics()` call
- `AutoCore.Launcher/Program.cs` - Added `LogLootTableStatistics()` call

## Diagnostic Output to Look For

After restarting the server, check logs for:

```
LootManager: Found X unique InLootGenerator values with Y total items
LootManager: Creature loot statistics:
  Total creatures: XXX
  With LootTableId > 0: XXX
  With BaseLootChance > 0: XXX
  Unique LootTableIds used: 1, 2, 3, 5, ...
  LootTableIds with matching InLootGenerator items: X/Y
```

## Possible Issues & Next Steps

### If "matching InLootGenerator items: 0/Y"
The `InLootGenerator` field is NOT how loot tables work. Possible alternatives:

1. **Loot tables stored externally** - Check for XML files, database tables, or other config files that define loot table contents.

2. **Reaction-based loot** - `ReactionType.RollFromLootTable` (81) exists but isn't implemented. Loot might be triggered via the reaction system instead.

3. **LootTableId is a CBID** - The `LootTableId` might point to a CloneBase object that contains the loot table definition internally.

4. **Different field mapping** - The binary parsing might be reading the wrong field. Check if the offset for `LootTableId` in `CreatureSpecific.ReadNew()` is correct.

5. **SpawnPoint is the source** - Loot might be defined entirely on SpawnPoints, not creatures. Check `SpawnPointTemplate.Loot` values.

### If items ARE found but not spawning
Check `SpawnLootItem()`:
- Verify the item entity is created correctly
- Verify the packet is being sent
- Check client-side item rendering

### Testing Notes
The loot roll check is currently disabled for testing:
```csharp
//if (roll > effectiveLootChance)
if (true == false) // for testing
```
Re-enable this after loot spawning works.

## Key Code Locations

| File | Line | Description |
|------|------|-------------|
| `LootManager.cs` | `GenerateLoot()` | Main loot generation entry point |
| `LootManager.cs` | `GetLootTableItems()` | Loot table item lookup |
| `LootManager.cs` | `LogLootTableStatistics()` | Startup diagnostics |
| `LootManager.cs` | `SpawnLootItem()` | Item spawning and packet broadcast |
| `Creature.cs` | `OnDeath()` | Triggers loot generation |
| `CreatureSpecific.cs` | Line 26, 63, 75 | LootTableId and BaseLootChance fields |
| `CloneBaseSpecific.cs` | Line 12, 38 | InLootGenerator field |
| `SpawnPointTemplate.cs` | Line 21, 22, 27 | Loot, LootPercent, LootChance fields |

## Questions to Answer

1. What values does `InLootGenerator` actually contain in the loaded data?
2. Do any creatures have matching loot tables?
3. Is `LootTableId` supposed to reference a CBID or an abstract table ID?
4. Are there external loot table definitions (XML, DB)?
5. Does the original game use reaction-based loot distribution?
