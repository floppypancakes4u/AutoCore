# Static NPC spawn height

## Symptom

Interactive / quest humanoid NPCs (`bitIsNPC`) spawn about one-third into the ground on every map. Combat creatures stand correctly.

## Cause

AutoCore used to ghost map spawn-point XYZ verbatim. Retail client height path:

1. **`CVOGSpawnPoint_CreateCreature`** (`00564f60`) — for `IsNPC==1` or mobile creatures, raycasts terrain then elevates Y by `rlFlyingHeight` (when flag bit 4 is clear) and the runtime foot offset at creature `+0x120`.
2. **`CVOGCreature_FindTerrainHeight`** (`004c6100`) — AI/movement re-snaps Y using terrain + foot offset.
3. **`GhostCreature_UnpackUpdate`** (`005d2e40`) — applies server XYZ **as-is** (no height correction).

Combat mobs correct themselves via (2). Static IsNPCs never move, so they keep the raw map Y.

Clonebase dump: IsNPC `rlFlyingHeight` is almost always **0**. The missing lift is the physics foot offset for `creature` / `humanoid` bodies (~**1.1803** half-extent from `physics.glm` `creature.cache` / `humanoid.cache`), scaled by `rlPhysicsScale`.

## Server fix

`SpawnPoint.ApplyStaticNpcSpawnHeight` (IsNPC only):

```
Y' = spawnY + FlyingHeight + ResolvePhysicsFootOffset(PhysicsName) * PhysicsScale
```

- `PhysicsName` empty / `creature` / `humanoid` → foot offset `1.1803`
- Other physics names → foot offset `0`
- `IsNPC == 0` → unchanged (client AI handles height)

## Ghidra symbols (autoassault.exe)

| Address | Name | Notes |
|---|---|---|
| `00564f60` | `CVOGSpawnPoint_CreateCreature` | Spawn height: terrain + FlyingHeight + foot |
| `004c6100` | `CVOGCreature_FindTerrainHeight` | AI ground snap; returns terrainY + foot@`+0x120` |
| `004cfe60` | (terrain cast helper) | Used by CreateCreature / FindTerrainHeight |
| `005d2e40` | `GhostCreature_UnpackUpdate` | Packs PositionMask XYZ from server |
| `004c8b60` | `CVOGCreature_SetupGraphics` | Subtracts FlyingHeight when placing mesh |
| `00564700` | `CVOGSpawnPoint_SetObjectActiveState` | IsNPC physics/active path |

Tags: `npc-spawn`, `height`, `IsNPC`, `GhostCreature`. Bookmarks category `AutoCore-RE`.

## Key code

| Piece | Path |
|---|---|
| Spawn + height helper | `src/AutoCore.Game/Entities/SpawnPoint.cs` |
| Clonebase fields | `CreatureSpecific.IsNPC`, `FlyingHeight`, `PhysicsScale` |
| Ghost position | `GhostCreature.PackUpdate` (`PositionMask`) |
| Tests | `SpawnPointMapNpcTests` height cases |

## Live check

Static quest/interact NPCs stand on terrain; combat mobs still correct (not floating).
