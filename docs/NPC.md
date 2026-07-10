# NPC / AI / Vehicle Driving — Research Inventory

**Status:** Research / RE / design inventory. **Phase A server code reimplemented** (2026-07-09). Phases B–D / combat AI remain unreimplemented after the full code revert.

**Binary:** `autoassault.exe` (Ghidra)  
**Related docs:** [TRIGGER_SYSTEM](../Documentation/TRIGGER_SYSTEM.md), [NPC_SPAWN_HEIGHT](../Documentation/NPC_SPAWN_HEIGHT.md), [auto-patrol](topic-extractions/auto-patrol.md)

### Implementation status (2026-07-09)

**Design rule:** handlers are **data-driven and mission-agnostic**. No mission, continent, or named NPC is special-cased.

| Phase | Piece | Status |
|------:|-------|--------|
| A | `tVehicleTemplate` load + template/CBID spawn + driver + path on create/ghost | **Restored** |
| B | `SetPath` / `SetPatrolDistance` reactions | Not restored (docs only) |
| C | Server path follower (pose + point reactions + wait) | Not restored (docs only) |
| C | Full combat AI (aggro/pursue/fire) | Not restored (docs only) |
| D | Ghidra renames + RE notes for HBAI/path | RE notes retained (§10) |
| — | val1–val20 profile semantics | Documented §10.7 |

**Phase A key code:** `VehicleTemplate.cs`, `WadXmlWorldDataLoader.LoadVehicleTemplates`, `AssetManager.GetVehicleTemplate`, `SpawnPoint.cs` (template/CBID + driver + equip + path), `Vehicle` path state + create fields, `GhostVehicle` path block when assigned.

---

## Executive summary

| Layer | NPC vehicle driving AI? | Scripted events / NPCs? |
|-------|-------------------------|-------------------------|
| **Retail client** | **Yes** — full **HBAI** stack, including `CVOGHBAIDriver` | Map paths, waypoints, SpecialEvent Path, reactions SetPath / SetPatrolDistance |
| **AutoCore server** | Path follower + combat AI (aggro/leash/fire); spawn path/driver; SetPath/SetPatrol | Triggers/reactions, interact NPCs, mission patrol notify, SpecialEvent respawn/teleport |

**Practical implication for AutoCore:** Driving behavior is designed to run **on the client** once a creature/vehicle is created with HBAI attached and path/patrol fields set. The private server already parses map path geometry and spawn path links but never feeds them into create/ghost, never spawns drivers, and never implements path reactions. A high-leverage first step is **plumbing** (path id + patrol distance + driver), not reimplementing DoLogic on the server.

---

## 1. Client: HBAI hierarchy

HBAI = client “heartbeat AI” (method names use `OnHeartBeat`, `DoLogic`). Recovered from RTTI and scoped assert strings; most functions are still unnamed in Ghidra except where AutoCore RE renamed them.

### 1.1 Classes (RTTI)

| RTTI name | Role |
|-----------|------|
| `CVOGHBAIBase` | Base: aggro list, find target |
| `CVOGHBAICreatureBase` | Walking/pursue/heartbeat for creatures |
| **`CVOGHBAIDriver`** | **NPC vehicle driver** (main interest) |
| `CVOGHBAIFollowVehicle` | Follow + weapon gating |
| `CVOGHBAICharacter` | Character AI |
| `CVOGHBAIBot` | Bot AI |
| `CVOGHBAIMine` | Mine AI |
| `CVOGHBAIWalkingCreatureTurreted` | Walking turreted creature |

### 1.2 Key functions (verified by string xref → decompile)

| Address | Identity | What it does |
|--------:|----------|--------------|
| `005d7750` | `CVOGHBAIDriver::DoLogic` | Main driver tick / combat-idle state machine |
| `005d6e80` | `CVOGHBAIDriver::ReturnToNormalLocation` | Leash / return-home toward spawn or waypoint |
| `005cfb60` | `CVOGHBAICreatureBase::DoVehiclePursue` | Steer/pursue a target vehicle |
| `005d0310` | `CVOGHBAICreatureBase::OnHeartBeat` | Periodic AI heartbeat for creatures |
| `005d0840` | `CVOGHBAICreatureBase::DecideHeading` | Turn toward desired heading |
| `005d7100` | `CVOGHBAIFollowVehicle::FireWeapons` | Front/turret/rear fire mask from geometry |
| `00639210` | `CVOGHBAIBase::FindTargetToAttack` | Spatial scan for valid attack targets |
| `00638ec0` | `CVOGHBAIBase::GetTargetFromAggro` | Pick from aggro set within range |
| `004c5c30` | Creature create-from-packet tail | Logs if **HBAI missing**: `"Creature/character %s created from packet without an HBAI created...!!!"` |
| `00564f60` | `CVOGSpawnPoint_CreateCreature` | Local/map spawn of creatures + waypoint init |
| `005d5580` | (Waypoint init helper) | Sets path COID, patrol distance, waypoint state |
| `005d6300` | `CVOGWaypoint::UpdateState` | Waypoint FSM states 0–3 |
| `005df950` | Map-path advance / steer helper | Walks path points; fires point `ReactionCoid` |
| `0057c500` | `CVOGReaction_Dispatch` | Reaction type switch including SetPath / SetPatrolDistance |
| `005f7720` | `VehicleNet_UnpackGhostVehicle` | Ghost unpack; **reads path block** when flag set |

### 1.3 `CVOGHBAIDriver::DoLogic` (`005d7750`) — deep dive

**Entry:** requires live vehicle/object at `param_1[0x2f]` (early-out if null).

**Loads AI behavior table** via nested pointer chain from the owner creature’s clonebase (vision/patrol-related floats used as thresholds at offsets like `+0x4dc` relative to AI table).

**Primary state** is a byte on the creature at **`owner + 0x26c`**:

| Value | Behavior in DoLogic |
|------:|---------------------|
| `0` | **Idle / patrol.** Calls path/skill helper `FUN_005d1280(0)`, check `FUN_005cced0`, optional vtable ops. If not “busy” (`owner+0x305`), runs movement scan (`FUN_005cedf0`), optional random wander (`FUN_005cc980`), else **`ReturnToNormalLocation`**. If that returns false, issues a move toward home/spawn via vtable `+0x4c`. |
| `1` | **Engage / hold.** Starts timer at `param_1[0x2d]`. Calls `FUN_005d1280(1)` (skills). After timeout or HP ratio vs AI table thresholds, transitions combat mode (`vtable +0x2c` with 0 or 2). Circles/offsets with `FUN_005ccbd0` when target present. |
| else (`2` path) | **Combat.** `FUN_005d1280(2)`, chance-based skill cast (`FUN_00638cd0`), chance to re-enter state 1. If target exists and return-home fails → **`DoVehiclePursue`**. |

**Always (end of tick):**

- Optional hard-point / secondary update if flag at nested `+0x7e`
- **`CVOGHBAIFollowVehicle::FireWeapons`** (`FUN_005d7100`) with “may fire” flag
- `FUN_005d6de0` — secondary steering/impulse helper (constants `0x40000000`, `0x41700000` ≈ 2.0f / 15.0f)

**Callees of interest:**

```
DoLogic
 ├─ FUN_005d1280(state)     // skill selection / cast from AI skill tables
 ├─ FUN_005cedf0            // movement / target scan
 ├─ FUN_005cc980            // random wander offset
 ├─ FUN_005ccbd0            // circle/offset relative to target
 ├─ ReturnToNormalLocation  // leash
 ├─ DoVehiclePursue         // chase
 ├─ FireWeapons             // weapon mask
 └─ FUN_005d6de0            // fine steering
```

**Reuse note:** DoLogic is pure client simulation (physics + local objects). AutoCore cannot “call” it; it can only **create the conditions** (spawn, owner/driver, path, patrol, faction, targets) so the client constructs HBAI and runs this.

### 1.4 `ReturnToNormalLocation` (`005d6e80`)

- Scoped string: `"CVOGHBAIDriver::ReturnToNormalLocation()"`.
- If creature has no explicit path target TFID (`+0x228/+0x22c == -1`), uses **waypoint** via vehicle/creature `+0xf8` → `CVOGWaypoint::UpdateState`.
- If waypoint flag `+0x52 == 0`: distance check vs spawn/home object at waypoint `+0x280`; if beyond **`waypoint+0x4c` (patrol distance)**, sets leash flag and **moves toward home** (`vtable +0x4c`).
- If waypoint flag `+0x52 != 0`: moves toward waypoint pose at `+0x20`, then `FUN_005d6de0`.
- Returns `1` if actively pathing home, `0` otherwise (DoLogic may then wander or pursue).

**Patrol distance** is the same field written by reaction **SetPatrolDistance** and initialized from spawn **`InitialPatrolDistance`**.

### 1.5 `DoVehiclePursue` (`005cfb60`)

- Requires a combat target at `AI+0x18 → +0xa0` (physics/target object).
- Issues move-to-target via vtable `+0x4c`.
- Optional flanking / standoff using weapon range at target vehicle `+0x124` and orientation bits (`flags & 0x40`, high bit of facing).
- Pure client steering math — not something the server should reimplement first.

### 1.6 Targeting

**`FindTargetToAttack` (`00639210`):**

- Uses owner faction/team helpers and a world query (`FUN_004ea350`) around self position with vision-related radius from clonebase AI table (`+0x4cc`).
- Filters corpses, invincible, wrong relation, level-adjusted range.
- Sets current target via `FUN_005172d0`.

**`GetTargetFromAggro` (`00638ec0`):**

- Walks aggro container from `FUN_0058d9c0`.
- Resolves each entry with `CVOGReaction_ResolveObjectTarget`.
- Prefers target (or its vehicle at `+0x250`) within **hearing/aggro range** squared (`AI table +0x4c8`).

### 1.7 Fire weapons (`005d7100`)

Builds a bit mask:

| Bit | Meaning (approx) |
|----:|------------------|
| 1 | Front weapon eligible (dot product + range) |
| 2 | Turret eligible |
| 4 | Rear eligible |

Then `FUN_005021d0(mask)` applies fire state. Server combat for map NPCs does not exist; client would drive presentation if AI ran.

### 1.8 Creature create without HBAI (`004c5c30`)

At end of create-from-packet path:

```c
if (creature[+0x304] == 0 && owner TFID is invalid (-1))
  log "Creature/character %s created from packet without an HBAI created...!!!";
```

So HBAI is expected to be non-null at `creature+0x304` after successful create. If AutoCore ghosts creatures without the client creating HBAI, they will not drive/fight.

---

## 2. Client: paths, waypoints, reactions

### 2.1 `CVOGMapPath` + map data

- RTTI `.?AVCVOGMapPath@@`
- Default asset name `"ed_mappath_default"`
- Path files under `"..\\maps\\paths\\"`
- AutoCore already parses the same point list in `MapPathTemplate`:

| Field | Meaning |
|-------|---------|
| `Position` | World XYZ |
| `AcceptDistance` | Arrival radius (squared check on client) |
| `ReactionCoid` | Reaction fired on arrival |
| `WaitTime` | Dwell (parsed; client advance path) |
| `ReverseDirection` | Path reverse flag on template |
| `PathName` | Debug / special-event naming |

### 2.2 Map path advance (`005df950`)

- Point stride **0x20** (matches AutoCore `MapPathPoint` + pad).
- Index `0xFFFFFFFF` → pick **nearest** point to current position.
- If outside accept radius → compute steer target for current index.
- If inside accept radius:
  - Resolve `ReactionCoid` → `CVOGReaction`, call activate `vtable +0x114`
  - Advance index (+1 or −1 if reversing)
  - Loop: if past end, either wrap to 0 or set reverse mode (`template ReverseDirection` at path `+0x68`)
- Also computes curvature / side distance for steering helpers (`param_7`, `param_8`).

### 2.3 `CVOGWaypoint` init (`005d5580`)

Called from **spawn** (`CVOGSpawnPoint_CreateCreature` and template vehicle spawn `FUN_00564290`):

```
waypoint+0x40/0x44 = MapPath COID (from spawn +0xa0/+0xa4)
waypoint+0x48      = -1 (extra / secondary?)
waypoint+0x4c      = InitialPatrolDistance (spawn +0x7c)
waypoint+0x50      = state (0 or 2 depending on “has path” flag)
waypoint+0x51      = flags
```

Spawn point fields align with AutoCore:

| Client spawn offset | AutoCore field |
|---------------------|----------------|
| `+0xa0` / `+0xa4` (64-bit) | `SpawnPointTemplate.MapPathCoid` |
| `+0x7c` | `SpawnPointTemplate.InitialPatrolDistance` |

### 2.4 Waypoint update (`005d6300` `CVOGWaypoint::UpdateState`)

State byte at `+0x50`:

| State | Handler |
|------:|---------|
| 0 | `FUN_005d5750` |
| 1 | `FUN_005d5960` |
| 2 | `FUN_005d5cc0` |
| 3 | `FUN_005d5680` |

Return-to-home and path-follow read waypoint pose at `+0x20..+0x2c`, patrol radius `+0x4c`, flag `+0x52`.

Path follow from heartbeat when “following path” (`FUN_005ce990`): reads waypoint (or vehicle’s waypoint at `vehicle+0xf8`), runs `UpdateState`, teleports/steps toward waypoint pose if active.

### 2.5 Reaction types (client dispatch `CVOGReaction_Dispatch`)

| Type | Enum (AutoCore) | Client behavior |
|-----:|-----------------|-----------------|
| **0x2b (43)** | `SetPath` | For each target object: `vtable +0x14c(&GenericVar1)` — **assign path COID** (`GenericVar1`) |
| **0x2c (44)** | `SetPatrolDistance` | Lookup map variable `GenericVar1` → float; for **Creature (type 18)** or **Vehicle (type 14)** write **`*(objectAI+0xf8)+0x4c`** |
| **0x4d (77)** | `Path` | Special-event style path name / UI packet path (`Client_SendLogicUiPacket` when name present) — related to scripted path sequences |

AutoCore currently drops all three into `Reaction.Unhandled` (still returns true so `0x206C` may fire for pure client-side apply).

### 2.6 Ghost vehicle path block (`VehicleNet_UnpackGhostVehicle` @ `005f7720`)

When optional path flag is set, client reads:

| Field | Approx client storage |
|-------|------------------------|
| Current path id (int18) | vehicle `+0x34a` |
| Extra path id (32 bits) | `+0x34b` |
| PathReversing (flag) | (read, applied) |
| PathIsRoad (flag) | (read, applied) |
| PatrolDistance (32 bits float) | `+0x34c` |

If flag clear → current path id forced to **`-1`**.

AutoCore `GhostVehicle` packs this block only when `WriteFlag(false)` — **never sent**. Create packet always writes `CurrentPathId = -1`, `PatrolDistance = 0`.

### 2.7 Special events

| Client type | AutoCore `SpecialEventType` |
|-------------|----------------------------|
| Respawn | `0` ✓ used |
| TeleportOut | `1` ✓ |
| TeleportIn | `2` ✓ |
| **Path** (`ClientSpecialEvent_Path`, `"specialevent_path_%s"`) | **missing** |

---

## 3. Client clonebase / data the AI reads

From `tCreature` SQL string in binary + AutoCore `CreatureSpecific`:

| Field | Role for AI |
|-------|-------------|
| `IDAIBehavior` → `AIBehavior` | Selects AI profile / table (not yet fully mapped to subclass) |
| `bitIsNPC` → `IsNPC` | Interactive quest NPCs (static); combat uses AI movement |
| `rlVisionArc/Range`, `rlHearingRange` | Target acquisition radii |
| `rlSpeed`, `rlRotationSpeed` | Movement |
| Skill sets keyed by phase byte | Used by `FUN_005d1280` skill pick |
| `bitHasTurret` | Turreted behaviors |

Vehicle:

| Field | Role |
|-------|------|
| `CBIDDefaultDriver` → `DefaultDriver` | Driver creature CBID for NPC vehicles |

AutoCore loads these; **no server code consumes them for AI**.

---

## 4. AutoCore inventory (what we already have)

### 4.1 Present

| Piece | Path | Notes |
|-------|------|-------|
| Map path geometry | `EntityTemplates/MapPathTemplate.cs` | Full point list |
| MapPath type | `CloneBaseObjectType.MapPath = 62` | Factory in `ObjectTemplate` |
| Spawn path link | `SpawnPointTemplate.MapPathCoid`, `InitialPatrolDistance` | **Applied at spawn (Phase A)** |
| Vehicle path on create | `CreateVehiclePacket`, `Vehicle.WriteToPacket` | **Writes assigned path state** |
| Ghost path + AI state | `GhostVehicle.cs`, `GhostCreature.cs` | **Path block when assigned**; AI state still zeros |
| AI clonebase fields | `CreatureSpecific` | Loaded only |
| Default driver | `VehicleSpecific.DefaultDriver` | **Used when template driver missing** |
| Reaction enums | `ReactionType.SetPath/SetPatrolDistance/Path` | Unhandled stubs (Phase B not restored) |
| Triggers / map events | `TriggerManager`, `SectorMap`, docs | **Working** event scripting |
| SpecialEvent | `SpecialEventPacket` | Respawn/teleport only |
| Mission patrol | `ObjectiveRequirementPatrol`, `AutoPatrol` | Player mission waypoints, not NPC drive |
| NPC interact | `NpcInteractHandler` | Dialog / use-object |
| Static NPC height | `SpawnPoint.ApplyStaticNpcSpawnHeight` | IsNPC placement; combat relies on client AI snap |

### 4.2 Phase A done / still open

**Done (Phase A):**
- `tVehicleTemplate` load from `wad.xml`
- Template and CBID vehicle spawn with driver, equipment, path/patrol
- Create + ghost path fields when path is assigned
- Driver level on non-character ghost owner

**Still open (not Phase A):**
```csharp
// GhostVehicle AI State mask still disabled
if (stream.WriteFlag((updateMask & StateMask) != 0 && false))

// SetPath / SetPatrolDistance reactions unhandled (Phase B)
// No server path follower tick (Phase C)
// No combat AI for map spawns (Phase C)
// No SpecialEventType for Path
```

### 4.3 Absent (post–Phase A)

- No `HBAI` / AI controller / path-follower service on sector tick (client still owns DoLogic)
- No `SetPath` / `SetPatrolDistance` server handlers
- No combat AI for map spawns (player vehicle combat only)
- No `SpecialEventType` for Path

---

## 5. Architecture: who owns driving?

```
┌─────────────────────────────────────────────────────────────┐
│ Retail design (inferred)                                      │
│                                                               │
│  Server: spawn vehicle + driver creature, set path COID,      │
│          patrol distance, faction/HP, optional ghost updates  │
│                         │                                     │
│                         ▼                                     │
│  Client: create objects → allocate HBAI → DoLogic each frame  │
│          follows MapPath / leashes / pursues / fires          │
│          path point reactions fire on client (and/or via      │
│          GroupReactionCall if server also notifies)           │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ AutoCore after Phase A                                        │
│                                                               │
│  Server: template/CBID vehicle + driver + equip + path/patrol │
│          on create/ghost; no server path follower yet (B/C)   │
│  Client: receives owner + path → can construct HBAIDriver     │
└─────────────────────────────────────────────────────────────┘
```

Evidence for **client-side simulation**:

1. DoLogic / Pursue / FireWeapons are full local math loops.
2. Create path warns when HBAI is missing on the client object.
3. Ghost optionally streams path/patrol; create packet carries the same fields.
4. Map path point reactions resolve and fire in client map code.

Server still needs to:

- Spawn correct objects (vehicle **and** driver creature as owner).
- Assign **MapPathCoid** and **InitialPatrolDistance** at create and/or ghost.
- Optionally reassign path via **SetPath** / leash via **SetPatrolDistance**.
- Keep combat HP/faction authoritative; validate damage if client AI fires.

---

## 6. Recommended reuse path for AutoCore

Ordered by leverage vs effort. **All phases stay mission-agnostic** — driven only by map spawn fields, clonebase, `tVehicleTemplate`, and reaction templates.

### Phase A — Make client HBAI actually engage (minimal) — **done for vehicles**

1. **Spawn driver** from `tVehicleTemplate.CBIDDriver` or `VehicleSpecific.DefaultDriver`; set as vehicle owner.
2. **Apply spawn path** from every `SpawnPointTemplate` (`MapPathCoid`, `InitialPatrolDistance`, reverse from `MapPathTemplate`).
3. Ghost/create path + owner bits so client `CVOGHBAIDriver` can run.
4. Still open in A-adjacent work: creature-only path/waypoint plumbing if foot NPCs need the same fields.

### Phase B — Scripted path control — **done (vehicles)**

1. **`ReactionType.SetPath` (43):** `GenericVar1` = path COID (≤0 clears). Targets via `ActOnActivator` / `Objects`. Applies to vehicles (or character → current vehicle). Reverse from `MapPathTemplate`. Preserves patrol distance. Still returns success so **0x206C** delivers client apply.
2. **`ReactionType.SetPatrolDistance` (44):** patrol = logic var[`GenericVar1`] (same pattern as client map-variable lookup). Preserves path id.
3. Path-point reactions fire from the Phase C follower on arrival; mid-session path ghost mask still initial-only.

### Phase C — Server path follower — **done (path pose only)**

| Item | Detail |
|------|--------|
| Tick | `SectorServer.MainLoop` → `MapManager.TickNpcPathVehicles(delta)` every 100 ms |
| Scope | Map NPC vehicles with assigned path, non-corpse, **not** player-owned |
| Motion | Move toward current `MapPathTemplate` point at clonebase-scaled speed (default 12 u/s) |
| Arrival | Snap to point, optional `WaitTime`, fire `ReactionCoid` via `SectorMap.TriggerReactions` |
| Advance | Wrap or ping-pong when `PathReversing` / template `ReverseDirection` |
| Ghost | `PositionMask` dirty on pose change |
| Combat | `NpcVehicleCombatAi` — faction aggro, leash, pursue into range, fire via `ProcessCombatIfFiring`; pauses path while not IdlePatrol |
| **Out of scope** | Full skill tables / call-for-help packets; foot-creature path follower |

### Phase D — RE cleanup (Ghidra) — **done**

See **§10** for renamed symbols, AI state notes, and open questions.

---

## 7. Critical file index

### AutoCore

| File | Why |
|------|-----|
| `src/AutoCore.Game/EntityTemplates/MapPathTemplate.cs` | Path geometry |
| `src/AutoCore.Game/EntityTemplates/SpawnPointTemplate.cs` | MapPathCoid, InitialPatrolDistance |
| `src/AutoCore.Game/Structures/VehicleTemplate.cs` | `tVehicleTemplate` row model |
| `src/AutoCore.Game/Managers/Asset/WadXmlWorldDataLoader.cs` | `LoadVehicleTemplates` |
| `src/AutoCore.Game/Entities/SpawnPoint.cs` | Template/CBID vehicle spawn + driver + path |
| `src/AutoCore.Game/Entities/Vehicle.cs` | Path state + create path fields |
| `src/AutoCore.Game/Packets/Sector/CreateVehiclePacket.cs` | Wire path fields |
| `src/AutoCore.Game/TNL/Ghost/GhostVehicle.cs` | Path block when assigned |
| `src/AutoCore.Game/CloneBases/Specifics/CreatureSpecific.cs` | AIBehavior → tCreatureAI |
| `src/AutoCore.Game/CloneBases/Specifics/VehicleSpecific.cs` | DefaultDriver |
| `src/AutoCore.Game/Entities/Reaction.cs` | Path reactions still unhandled (B) |
| `src/AutoCore.Sector/Network/SectorServer.cs` | Tick path followers |
| `src/AutoCore.Game/Managers/NpcInteractHandler.cs` | Interact / AutoPatrol |

### Ghidra anchors (Phase D renames applied)

| Address | Ghidra name (after Phase D) |
|--------:|-----------------------------|
| `005d7750` | `CVOGHBAIDriver_DoLogic` |
| `005d6e80` | `CVOGHBAIDriver_ReturnToNormalLocation` |
| `005cfb60` | `CVOGHBAICreatureBase_DoVehiclePursue` |
| `005d0310` | `CVOGHBAICreatureBase_OnHeartBeat` |
| `005d0840` | `CVOGHBAICreatureBase_DecideHeading` |
| `005d7100` | `CVOGHBAIFollowVehicle_FireWeapons` |
| `00639210` | `CVOGHBAIBase_FindTargetToAttack` |
| `00638ec0` | `CVOGHBAIBase_GetTargetFromAggro` |
| `005d5580` | `CVOGWaypoint_InitFromSpawn` |
| `005d6300` | `CVOGWaypoint_UpdateState` |
| `005df950` | `CVOGMapPath_AdvanceAndSteer` |
| `00564f60` | `CVOGSpawnPoint_CreateCreature` (pre-existing) |
| `00564290` | `CVOGSpawnPoint_CreateTemplateVehicle` |
| `0057c500` | `CVOGReaction_Dispatch` (pre-existing) |
| `005f7720` | `VehicleNet_UnpackGhostVehicle` (pre-existing) |
| `004c5c30` | `CVOGCreature_PostCreateFromPacket` |

---

## 8. Open RE / implementation questions

1. ~~HBAI factory from IDAIBehavior~~ — **closed** (§10.2).
2. ~~AI state byte~~ — **closed** (§10.3).
3. ~~Waypoint patrol layout~~ — **closed** (§10.4).
4. **Foot-creature path** create/ghost wire fields (vehicles done).
5. **Driver as CurrentOwner** — must map NPC drivers always ghost as vehicle owner for client DoLogic (implemented on spawn; verify live).
6. **Character vs Mine** RTTI split when both use ctor `0063d0b0`.

---

## 9. Answer to the original question (short)

**Is there already code that handles AI/NPC controllers for driving NPC vehicles, and/or scripted events/NPCs/AI?**

- **Yes on the retail client:** complete HBAI system with `CVOGHBAIDriver`, path following (`CVOGMapPath` / `CVOGWaypoint`), combat pursue/fire, and reactions `SetPath` / `SetPatrolDistance`.
- **AutoCore Phase A–C (vehicles):** generic spawn (template/CBID + driver + path), SetPath/SetPatrolDistance, **server path follower** for multiplayer pose — **no per-mission code**.
- **Still missing:** see §11 remaining optional work.

**Strategy:** client HBAI for combat; server path follower for shared pose authority along map paths.

---

## 10. Phase D — Ghidra RE notes (completed backlog)

### 10.1 Renamed symbols

Applied in open `autoassault.exe` Ghidra project. Key factory/ctors:

| Address | Name |
|--------:|------|
| `005d3d10` | `CVOGHBAI_CreateByAICode` — **factory switch on AICode** |
| `005d3b30` | `CVOGHBAIBase_ctor` |
| `005d3c40` | `CVOGHBAICreatureBase_ctor` |
| `005d3cf0` | `CVOGHBAIBot_ctor` |
| `0063d0b0` | `CVOGHBAICharacterOrMine_ctor` (shared mid-size parent) |
| `0063cb50` | `CVOGHBAIDriver_ctor` (extends CharacterOrMine) |
| `00639830` | `CVOGHBAIWalkingCreatureTurreted_ctor` |
| `0063c940` | `CVOGHBAIBase_Default_ctor` |
| `005c6880` | `CLoadNode_initAI` |
| `005d5580` | `CVOGWaypoint_InitFromSpawn` |
| `005df950` | `CVOGMapPath_AdvanceAndSteer` |
| `005d7750` | `CVOGHBAIDriver_DoLogic` |

### 10.2 `IDAIBehavior` → HBAI subclass (**mapped**)

**Data path**

```
CreatureSpecific.AIBehavior  (clonebase, was tCreature.IDAIBehavior)
        │
        ▼
tCreatureAI.AIID  →  row with AICode + val1..val20  (behavior profile / tuning)
        │
        ▼
AICode  →  CVOGHBAI_CreateByAICode  →  HBAI subclass ctor
```

**Factory** (`CVOGHBAI_CreateByAICode` @ `005d3d10`): switch on AICode as raw int (decompiler shows float bit-patterns `1.4e-45` = int 1, etc.):

| AICode | Enum | HBAI class | Ctor | Notes |
|-------:|------|------------|------|-------|
| 1 | `Character` | `CVOGHBAICharacter` | `0063d0b0` (or CreatureBase if val-based redirect) | Only profile: “Character/hazard mode AI” |
| 2 | `Creature` | `CVOGHBAICreatureBase` | `005d3c40` | 27 profiles — default foot AI |
| 3 | `Bot` | `CVOGHBAIBot` | `005d3cf0` | “Bot summons ai” |
| 4 | `Mine` | `CVOGHBAIMine` | `0063d0b0` | Shares mid-ctor with Character |
| 5 | `Driver` | `CVOGHBAIDriver` | `0063cb50` | “DR:” vehicle drivers |
| 6 | `WalkingCreatureTurreted` | `CVOGHBAIWalkingCreatureTurreted` | `00639830` | Factory case present; rare/absent in retail table |
| other | `Default` | `CVOGHBAIBase` | `0063c940` | Fallback |

**AutoCore load:** `WadXmlWorldDataLoader.LoadCreatureAiProfiles` → `AssetManager.GetCreatureAiProfile(aiId)`.
**Enums:** `HBAICode`, `HBAICombatState` in `Constants/HBAICode.cs`.

**Profile vals (val1–val7 used by DoLogic as thresholds):**  
Typical “Normal creature” (AIID 1): val1=8000 (ms-scale timer), val2–val4 flee/help ratios, val5 help enable, val6 help chance, val7 help range. Exact semantics remain client-side tuning; server stores them for future combat AI.

### 10.3 Ghost AI State byte (**mapped**)

| Item | Value |
|------|--------|
| Wire | 8 bits under creature/vehicle `StateMask` |
| Client field | Owner creature **`+0x26c`** |
| Server field | `Creature.AiCombatState` |
| Enum | `HBAICombatState`: **0 IdlePatrol**, **1 Engage**, **2 Combat** |

Evidence: `CVOGHBAIDriver_DoLogic` branches exclusively on `(char)owner+0x26c` with cases 0 / 1 / else(2). Ghost pack now writes `AiCombatState` (defaults IdlePatrol until a combat AI updates it).

### 10.4 Waypoint field layout (**confirmed**)

From `CVOGWaypoint_InitFromSpawn` (`005d5580`):

| Offset | Size | Field | Source |
|-------:|-----:|-------|--------|
| `+0x20..+0x2c` | 16 | Pose / target XYZ+pad | Cleared then filled by UpdateState |
| `+0x40` | 4 | MapPath COID low | Spawn `MapPathCoid` |
| `+0x44` | 4 | MapPath COID high | |
| `+0x48` | 4 | Extra path id | Default **-1** |
| **`+0x4c`** | **4** | **Patrol distance (float)** | Spawn `InitialPatrolDistance`; **SetPatrolDistance** writes here |
| `+0x50` | 1 | Waypoint FSM state | 0 or 2 on init (`param_6`) |
| `+0x51` | 1 | Flags | `param_7` |

**SetPatrolDistance (reaction 44):** resolve logic var → float → store at AI object `*(+0xf8)+0x4c` (waypoint pointer).  
**Server mirror:** `Vehicle.PatrolDistance` on create/ghost/path follower.

### 10.5 Create / HBAI attach

| Path | Notes |
|------|--------|
| Map creature spawn | `CVOGSpawnPoint_CreateCreature` + waypoint init |
| Template vehicle spawn | `CVOGSpawnPoint_CreateTemplateVehicle` |
| Network create | `CVOGCreature_PostCreateFromPacket` — HBAI required at `+0x304` or warn |
| Load-node AI | `CLoadNode_initAI` calls creature vtable `+0xc0` (create/attach HBAI) |

### 10.6 Closed vs open after this pass

| Item | Status |
|------|--------|
| IDAIBehavior → AIID → AICode → HBAI class | **Closed** (data + factory) |
| Ghost AI state byte enum | **Closed** (0/1/2) |
| Waypoint +0x4c patrol | **Closed** |
| Character vs Mine ctor distinction | **Partial** — both use `0063d0b0` |
| val1–val7 semantics | **Closed** (§10.7); val8–20 unused in recovered DoLogic |
| Server combat AI (aggro/leash/fire) | **Partial** (`NpcVehicleCombatAi`) |
| Mid-session path ghost mask | **Open** |
| Foot-creature path follower | **Open** |

### 10.7 `tCreatureAI` val1–val20 semantics

| Val | Accessor | Meaning |
|----:|----------|---------|
| 1 | `ValFleeOrEngageTimerMs` | Flee / engage timer (ms), e.g. 8000 |
| 2 | `ValFleeHpSecondary` | Secondary flee HP band (often 0 or ~0.3) |
| 3 | `ValFleeHpOrChance` | Primary flee trigger (HP ratio and/or chance; “50Flee” → 0.5) |
| 4 | `ValReengageThreshold` | Stop-flee / re-engage commitment (often ~1) |
| 5 | `ValHelpEnabled` | Call-for-help allow (0 = never) |
| 6 | `ValHelpChance` | Call-for-help chance (0–1) |
| 7 | `ValHelpRange` | Call-for-help / social range (world units) |
| 8–20 | `GetVal(7..19)` | Unused in recovered DoLogic; empty in most retail rows |

**Never flee** profiles zero val1–val3. **No help** profiles zero val5–val7.  
Server combat AI uses vision/hearing from clonebase and help range as vision fallback; **flee timers and call-for-help networking are not implemented yet** (vals are loaded for that next step).

---

## 11. Remaining optional work

Ordered by likely impact:

1. **Combat AI depth** — flee (val1–val4), call-for-help (val5–val7), skill cast from clonebase skill sets.
2. **Foot-creature path follower** — Phase C for walking NPCs.
3. **Mid-session path ghost updates** — non-initial mask for path id / patrol.
4. **SpecialEvent Path type** — client cutscene paths.
5. **Reaction type Path (77)** — named path UI (not SetPath 43).
6. **Character vs Mine ctor** — RTTI split at `0063d0b0`.
7. **Live validation** — path + combat in multiplayer.

---

*Last updated: 2026-07-09 — combat AI + val1–val7 semantics.*
