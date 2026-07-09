# Map Reveal / Exploration

Server-authoritative fog-of-war for the world / continent map. Driving into a new area ORs a bit into the character’s explored mask, persists it, and notifies the client.

Ghidra target: `autoassault.exe`.

## Authority model

| Concern | Owner |
|---------|--------|
| Persist explored bits | Server (`character_exploration`) |
| Login restore | Server → client `CreateCharacterExtended` (`0x2016`) continent block |
| Runtime unlock | Server → client `UnlockRegion` (`0x205B`) |
| Position → area id | Terrain TGA G channel high bits (client **and** server sample the same formula) |
| Client local discovery | Exists (`FUN_005d6c60`) for UI responsiveness; not the source of truth for AutoCore |

`0x205B` is **not** a client→server request. It is handled only in `Client_PacketDispatch` as a receive path (`FUN_00809550`).

## Opcodes

| Direction | Opcode | Name |
|-----------|--------|------|
| Server → client | `0x205B` | `EMSG_Sector_UnlockRegion` |
| Server → client | `0x2016` | `EMSG_Sector_CreateCharacterExtended` (continent slots) |
| Server → client | `0x205F` | `EMSG_Sector_GiveXP` (first-visit XP; optional / future) |

## `UnlockRegion` payload (`0x205B`)

Client handler: `FUN_00809550` @ `0x00809550`. Size **0x10** including opcode.

| Offset | Size | Field |
|--------|------|--------|
| `0x00` | 4 | Opcode `0x205B` |
| `0x04` | 4 | `ContinentId` |
| `0x08` | 1 | `UnlockFlag` (`0` = relock, non-zero = unlock/update) |
| `0x09` | 3 | padding |
| `0x0C` | 4 | `ExploredBits` (full mask after update) |

Client apply:

- Flag `0` → `CVOGReaction_RelockContinentObject`
- No local entry → `CVOGReaction_UnlockContinentObject`
- Existing entry with different bits → per-bit `FUN_005326b0` for area ids `1..32`

## Login continent slots (`CreateCharacterExtended`)

Absolute packet offset **`0x1B8`**, **50 × 12** bytes:

| Field | Size |
|-------|------|
| `ContinentId` | `int32` |
| unlocked flag | `byte` (server writes `1`) |
| pad | 3 bytes |
| `ExploredBits` | `uint32` |

Client walks until `ContinentId == 0` (`CVOGCharacter_ApplyCreateFromPacket` @ `0x00534bd0`).

## Bit semantics

| Meaning | Encoding |
|---------|----------|
| Area id `N` (`1..32`) | Bit `(N - 1)` of `ExploredBits` |
| Area `0` | Empty / out of bounds — no bit |
| First visit | Bit was clear → set; award XP later via `ContinentArea.XPLevel` if desired |

Helpers: `ContinentAreaMask.AreaBit`, `ContinentAreaMask.TryAddArea`.

## Position → area (terrain TGA)

Client `FUN_004a8b90` samples the map height/tile TGA loaded by `CVOGTerrain_LoadMapImage`:

- File: `{MapFileName}.tga` (G channel)
- Low 3 bits of G = tile layer; **high 5 bits (`>> 3`) = explored area id**
- Grid constants: `0.5f` origin half-cell, scale `1 / GridSize` (`.fam` `GridSize`)
- After TGA load, **image y=0 is always the map bottom** (bottom-origin file order kept; top-origin 32bpp is flipped in `FUN_004332e0`)

```text
cellX = (int)((posX - GridSize * 0.5f) / GridSize)
cellZ = (int)((posZ - GridSize * 0.5f) / GridSize)
areaId = tileBuffer[height * cellX + cellZ] >> 3   // 0 if OOB
```

Unlock happens on the **first terrain cell** painted with that area id — same as retail. Region art on the world map can look slightly larger/smaller than the TGA paint, so boundaries may feel a few cells early/late relative to the painted label.

Metadata (name / XP level) lives in `tContinentExploredAreas` → `ContinentArea` — **no geometry**.

## Server persistence

Table **`autocore_char.character_exploration`**:

| Column | Type |
|--------|------|
| `CharacterCoid` | `long` (PK part) |
| `ContinentId` | `int` (PK part) |
| `ExploredBits` | `uint` |

Lifecycle:

1. Sector login → `Character.LoadFromDB` → `LoadExplorations`
2. Local player create → `WriteExploration` → `CreateCharacterExtended.ContinentUnlocked`
3. Immediately after create packets → `SyncExplorationAfterLogin` sends **two** `UnlockRegion` packets per known continent (client creates empty entry on first packet if missing, then applies bits on the second) and samples the current cell
4. `VehicleMoved` → `ExplorationManager.OnVehicleMoved` → sample TGA cell → new bit → DB upsert + double `UnlockRegion`

### Client quirk (`FUN_00809550`)

If the local character has **no** continent entry yet, UnlockRegion only calls `UnlockContinentObject` (creates entry with **bits=0**) and **ignores** the packet’s `ExploredBits`. A second UnlockRegion with the same payload then takes the per-bit update path. Create-packet hash insert does not fire the LogicUI bit notify path, so post-login UnlockRegion is required for fog refresh.

## Key client functions (Ghidra)

Renamed in the Auto Assault Ghidra project during this work:

| Address | Name | Role |
|---------|------|------|
| `0x00815710` | `Client_PacketDispatch` | case `0x205B` → `Client_RecvUnlockRegion` |
| `0x00809550` | `Client_RecvUnlockRegion` | S2C UnlockRegion apply (double-send bootstrap) |
| `0x00534bd0` | `CVOGCharacter_ApplyCreateFromPacket` | Continent hash load @ packet `0x1B8` |
| `0x0052f650` | `CVOGCharacter_SerializeCreatePacket` | Writes continent hash slots |
| `0x004a8b90` | `CVOGTerrain_SampleExploredAreaId` | World pos → area id (`G>>3`) |
| `0x004aba80` | `CVOGTerrain_LoadMapImage` | `{map}.tga` → tile buffer |
| `0x004347d0` | `NDAssetImage_LoadTGA` | TGA load; y=0 ends as image bottom |
| `0x004332e0` | `NDAssetImage_FlipVertical` | Top-origin 32bpp normalize |
| `0x005326b0` | `CVOGCharacter_SetAreaExploredBit` | Set/clear bit + LogicUI |
| `0x0052b310` | `CVOGCharacter_IsAreaExplored` | Test explored bit |
| `0x00531c80` | `CVOGReaction_UnlockContinentObject` | Create empty continent entry |
| `0x005b0920` | `CNDHash_LookupByKey` | Hash lookup (continent unlocked table) |
| `0x0053c560` | `CNDHash_Insert` | Hash insert (create-packet continent slots) |
| `0x005d6c60` | `Client_LocalDiscoveryTick` | Client-only discovery while driving |

Structs: `USContinentUnlocked` (12B), `SMSG_Sector_UnlockRegion` (16B).

## Live verification

1. Seed `character_exploration` with known bits → relog → world map shows those areas.
2. Drive into a new TGA area → server log “revealed … area N” → client map updates → DB bit set.
3. Drive same area again → no repeated packet spam.
4. Missing TGA / town → no crash; discovery no-ops.
