# GhostObject diag (AV `0x005B0EFF`)

Correlates **server plain-`GhostObject` lifecycle** with **client waiting-bind apply**
(`FUN_005b0ed0` / crash IP `0x005B0EFF` after `"Assigned a ghost to waiting"`).

**World loot** no longer creates plain `GhostObject`s (`LootManager.TrySpawnLootItem` —
shared by `/loot` and creature/vehicle death). Remaining risk is **map-prop combat
ghosts** (`MakeNotInvincible` / `TakeDamage` → `EnsureCombatGhost` →
`ObjectLocalScopeAlways`) and any other `new GhostObject()` path with a local TFID.

### Rate limiting

High-frequency events log **once per (name, coid, player)** only:

- `InterestSelect`, `ObjectInScope`, `ScopeAlways`, `PackDelta`, `EnsureCombatGhost`

Always logged (lifecycle / first signal):

- `CreateGhost`, `PackInitial`, `BecameDamagable`, `SendCreate`

## Server

### Enable

Any one of:

```text
# env
set AUTOCORE_GHOST_OBJECT_DIAG=1

# or wire lever JSON / console
GhostObjectDiag = true
# env equivalent:
set AUTOCORE_WIRE_GHOST_OBJECT_DIAG=1
```

Restart Sector/Launcher after changing env. Console lever: same board as other `wire` levers.

### Log lines

Prefix: **`[GhostObjectDiag]`** (also retained in `GhostObjectDiag.Snapshot()` when enabled).

| `name` | Meaning |
|--------|---------|
| `CreateGhost` | Plain ghost allocated (`GraphicsObject` or `SimpleObject`) |
| `BecameDamagable` | `SetInvincible(false)` → combat path |
| `EnsureCombatGhost` | Combat ghost ensure (created=0/1) |
| `ScopeAlways` | `ObjectLocalScopeAlways` for a map prop |
| `InterestSelect` | Entity selected by interest query with plain ghost |
| `ObjectInScope` | About to `ObjectInScope` plain ghost |
| `PackInitial` / `PackDelta` | TNL pack for plain `GhostObject` only |
| `SendCreate` | Outgoing CreateSimple/Armor/Weapon/… game packet |

Each line carries `type`, `cbid`, `coid`, `global`, `pos`, `hp`, `inv`, `ghost` class, and `conn=` player coid when known.

### What to collect

1. Reproduce the backrange spot crash (or near-miss).
2. Grep sector log for `[GhostObjectDiag]` in the **~5s before** client AV / client `GhostApply_CRASH_IMMINENT`.
3. Note every plain-ghost `coid`/`cbid`/`pos` for that window — especially `ScopeAlways` / `PackInitial` / `BecameDamagable`.

## Client (PathAHook)

### Build + arm

```powershell
powershell -ExecutionPolicy Bypass -File tools\PathAHook\build.ps1
scripts\path-a-debug.cmd arm
```

Hit log: `%TEMP%\AutoCorePathA\hits.jsonl`

### New events

| `ev` | VA | Meaning |
|------|-----|---------|
| `GhostOnAdd` | `0x005B0D70` | GhostObject OnGhostAdd |
| `GhostApply_enter` | `0x005B0ED0` | FUN_005b0ed0 entry: ghost, bound, buf, tfid, global, buf_opcode/cbid, obj_coid |
| `GhostApply_iface` | same | `object->vtbl+0x1C8()` returned non-null |
| `GhostApply_CRASH_IMMINENT` | same | iface null/invalid — **this is the pre-AV state** |
| `GhostApply_SKIPPED_NULL_IFACE` | same | hook **skipped** original so the client stays up |
| `GhostApply_exit` | same | original returned cleanly |

Fields to correlate with server:

- `tfid` / `obj_coid_lo` ↔ server `coid`
- `buf_opcode` (`0x2012` = simple create shell from local initial)
- `buf_cbid` (`-1` on auto stub from `FUN_005b0e30`)
- `global` (0 = local map identity path)

### Safety

On `iface_ok=0` the hook **does not call** retail `FUN_005b0ed0`, so you should get **CRASH_IMMINENT without process death**. That is intentional for multi-repro; it is not a permanent game fix.

## Correlation recipe

1. Enable server `GhostObjectDiag` + rebuild/restart.
2. Rebuild PathAHook, arm, login, drive to the spot.
3. On client `GhostApply_CRASH_IMMINENT`, copy `tfid` / `obj_coid_lo` / `t` (GetTickCount).
4. On server, find the same `coid` in `[GhostObjectDiag]` near that wall-clock time.
5. The event stack (`BecameDamagable` → `CreateGhost` → `ScopeAlways` → `PackInitial`) identifies the **entity class and reason** for scoping.

## Related RE

- Client crash: `FUN_005b0ed0` @ `0x005B0ED0`, caller waiting-bind in `FUN_008078b0` @ `0x008079D7`
- Sibling historical bug: GhostCreature apply `0x005D262A` (fixed server-side via `MapNpcIdentity`)
