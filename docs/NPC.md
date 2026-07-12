# NPC / AI / Vehicle Driving — Research Inventory

**Status:** Research / RE / design inventory **plus** the as-built record for the `feature/npc-ai`
branch. All four phases (A–D) are implemented **server-side** on this branch; the sections below
describe both the retail client design (RE) and the AutoCore server code that consumes it.

**Binary:** `autoassault.exe` (Ghidra)  
**Related docs:** [TRIGGER_SYSTEM](../Documentation/TRIGGER_SYSTEM.md), [NPC_SPAWN_HEIGHT](../Documentation/NPC_SPAWN_HEIGHT.md), [auto-patrol](topic-extractions/auto-patrol.md)

### Implementation status (2026-07-10, `feature/npc-ai`)

**Design rule:** every handler is **data-driven and mission-agnostic**. No mission, continent, or
named NPC is special-cased anywhere. Behaviour is driven only by map spawn fields, clonebase rows,
`tVehicleTemplate`, `tCreatureAI`, and reaction templates.

| Phase | Piece | Status | Key server code |
|------:|-------|--------|-----------------|
| A | `tVehicleTemplate` load + template/CBID spawn + driver + path/patrol on create & ghost | **Implemented** | `VehicleTemplate.cs`, `WadXmlWorldDataLoader.LoadVehicleTemplates`, `SpawnPoint.cs`, `Vehicle.cs`, `GhostVehicle.cs` |
| B | `SetPath` (43) / `SetPatrolDistance` (44) reactions | **Implemented** | `Reaction.cs` |
| C | Server path follower — vehicles **and** foot creatures (pose + point reactions + `WaitTime` dwell) | **Implemented** | `Npc/NpcPathFollower.cs`, `Npc/NpcTicker.cs` |
| C | Server combat AI (aggro scan / engage / pursue / fire / leash / flee / call-for-help) | **Implemented** | `Npc/NpcCombatAi.cs`, `Npc/NpcAiState.cs`, `Npc/FactionHostility.cs` |
| D | Interest management (spatial hash + tiered scope + priority) | **Implemented** | `Map/SpatialHashGrid.cs`, `Map/InterestSelector.cs`, `TNL/Ghost/GhostObject.cs` |
| D | Ghidra renames + RE closure notes for HBAI/path | **Complete** (§10, §12) |  |
| — | `tCreatureAI` val1–val20 profile semantics | Documented §10.7 / §12.5 | `Structures/CreatureAiProfile.cs` |
| — | NPC vehicle Health on the wire (`EnableMinimalForeignHealthBlock` + resend window) | **Implemented** (§14) | `GhostVehicle.cs`, `WireIsolationLevers.cs` |
| — | Target-frame Cur/Max via CreateCreature driver + CreateVehicle owner attach | **Implemented** (§14.4); live confirm Cur/Max after redeploy | `CreateCreaturePacket`, `SectorMap.TrySendForeignDriverCreate` |

**What is deliberately NOT on the server:** the client still owns the fine physics simulation
(`CVOGHBAIDriver::DoLogic` steering math, weapon-geometry fire masks, skill VFX). The server holds
authoritative pose along map paths, faction/HP, aggro/leash/flee state, and the firing bit; the
client renders. Call-for-help spreads aggro server-side but emits no client "shout" packet yet.

---

## Executive summary

| Layer | NPC vehicle driving AI? | Scripted events / NPCs? |
|-------|-------------------------|-------------------------|
| **Retail client** | **Yes** — full **HBAI** stack, including `CVOGHBAIDriver` | Map paths, waypoints, SpecialEvent Path, reactions SetPath / SetPatrolDistance |
| **AutoCore server** | Path follower + combat AI (aggro/leash/fire); spawn path/driver; SetPath/SetPatrol | Triggers/reactions, interact NPCs, mission patrol notify, SpecialEvent respawn/teleport |

**Practical implication for AutoCore:** the client's HBAI renders driving once a creature/vehicle is
created with HBAI attached and path/patrol fields set. On `feature/npc-ai` the server now does the
plumbing (spawns drivers, feeds path id + patrol distance + AI state into create/ghost, handles the
path reactions) **and** runs an authoritative server-side path follower + combat brain, rather than
reimplementing the client's `DoLogic` steering math. The client still owns fine physics and weapon
presentation; the server owns pose along paths, faction/HP, and aggro/flee state (§5, §12, §13).

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

**RE confirmed (§12.3):** the current path id is read as an **18-bit unsigned int** (`BitStream_readInt(0x12)`),
zero-extended — not sign-extended. The `-1` sentinel is carried by the *absence* of the block
(presence flag clear), never by an all-ones 18-bit value.

AutoCore `GhostVehicle` now **sends** this block whenever a path is assigned, writing the current
path id with the same 18-bit width (`CoidCurrentPathBits = 18`), then the 32-bit extra path id, the
`PathReversing` / `PathIsRoad` flags, and the 32-bit `PatrolDistance` float — matching the client
unpack field-for-field.

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
| Ghost path + AI state | `GhostVehicle.cs`, `GhostCreature.cs` | **Path block when assigned + AI combat-state byte** |
| AI clonebase fields | `CreatureSpecific`, `CreatureAiProfile` | Loaded **and consumed** by `NpcCombatAi` |
| Default driver | `VehicleSpecific.DefaultDriver` | **Used when template driver missing** |
| Reaction handlers | `ReactionType.SetPath/SetPatrolDistance` | **Handled** (Phase B); `Path` (77) still stubbed |
| Triggers / map events | `TriggerManager`, `SectorMap`, docs | **Working** event scripting |
| SpecialEvent | `SpecialEventPacket` | Respawn/teleport only |
| Mission patrol | `ObjectiveRequirementPatrol`, `AutoPatrol` | Player mission waypoints, not NPC drive |
| NPC interact | `NpcInteractHandler` | Dialog / use-object |
| Static NPC height | `SpawnPoint.ApplyStaticNpcSpawnHeight` | IsNPC placement; combat relies on client AI snap |

### 4.2 Implemented on `feature/npc-ai` (Phases A–D)

- `tVehicleTemplate` load from `wad.xml`; template and CBID vehicle spawn with driver, equipment,
  path/patrol; driver level on non-character ghost owner.
- Create + ghost path block **written** when a path is assigned (18-bit id, §12.3); the AI
  combat-state byte is packed under `StateMask`.
- `SetPath` / `SetPatrolDistance` reaction handlers (Phase B).
- Server path follower for vehicles **and** foot creatures, plus `WaitTime` dwell and arrival
  reactions (`NpcTicker` / `NpcPathFollower`).
- Server combat brain: aggro scan, engage/pursue/fire, leash home, flee (val1–val4),
  call-for-help aggro spread (val5–val7) (`NpcCombatAi`).
- Interest management: spatial hash, tiered scope selection, update-priority formula (§13).

### 4.3 Still absent

- Client-owned fine physics/steering & weapon VFX (by design — server stays authoritative on pose,
  faction/HP, aggro/flee state, firing bit).
- Skill casting from clonebase skill sets (client `FUN_005d1280`).
- Mid-session path ghost updates (non-initial mask for path id / patrol).
- Call-for-help "shout" packet (aggro already spreads server-side).
- `SpecialEventType` for `Path`; reaction type `Path` (77).

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

### Phase A — Make client HBAI actually engage (minimal) — **done**

1. **Spawn driver** from `tVehicleTemplate.CBIDDriver` or `VehicleSpecific.DefaultDriver`; set as vehicle owner.
2. **Apply spawn path** from every `SpawnPointTemplate` (`MapPathCoid`, `InitialPatrolDistance`, reverse from `MapPathTemplate`).
3. Ghost/create path + owner bits so client `CVOGHBAIDriver` can run.

### Phase B — Scripted path control — **done**

1. **`ReactionType.SetPath` (43):** `GenericVar1` = path COID (≤0 clears). Targets via `ActOnActivator` / `Objects`. Applies to vehicles (or character → current vehicle). Reverse from `MapPathTemplate`. Preserves patrol distance. Still returns success so **0x206C** delivers client apply.
2. **`ReactionType.SetPatrolDistance` (44):** patrol = logic var[`GenericVar1`] (same pattern as client map-variable lookup). Preserves path id.
3. Path-point reactions fire from the Phase C follower on arrival; mid-session path ghost mask still initial-only.

### Phase C — Server path follower + combat — **done (vehicles and foot creatures)**

| Item | Detail |
|------|--------|
| Tick | `SectorServer.MainLoop` → `MapManager.TickNpcPathVehicles(delta)` every 100 ms |
| Scope | Map NPC `Vehicle` **and** `Creature` in `IdlePatrol` with an assigned path, non-corpse |
| Motion | Move toward current `MapPathTemplate` point at clonebase speed (fallback 12 u/s vehicle, 2.5 u/s foot) |
| Arrival | Snap to point, `WaitTime` dwell in **ms** (see 12.1), fire `ReactionCoid` via `SectorMap.TriggerReactions` |
| Advance | Wrap or ping-pong when `PathReversing` / template `ReverseDirection` |
| Ghost | Position/state masks dirtied on pose / combat-state change |
| Combat | `NpcCombatAi` faction aggro scan, engage, leash home, pursue, fire via `ProcessCombatIfFiring`, flee (val1-val4), call-for-help (val5-val7); pauses patrol while not `IdlePatrol` |
| **Out of scope** | Skill casting from clonebase skill sets; client call-for-help shout packet |

### Phase D — RE closure (Ghidra) + interest management — **done**

See **§10** for renamed symbols and AI-state notes, **§12** for the five RE closures (WaitTime, faction, 18-bit path id, creature pose-only, flee HP-band), and **§13** for interest management.

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

### Ghidra anchors — health / target-frame RE (2026-07-11, §14)

| Address | Identity | Note |
|--------:|----------|------|
| `004c49d0` | `Creature::SetVehicle` | THE creature↔vehicle link: `creature+0x250 = vehicle`, copies vehicle COID to `+0x210/+0x214`, calls vehicle `vtbl+0x158` (owner ptr `vehicle+0xAC`) |
| `00505590` | `Vehicle_applyCreatePacket` | Resolves CreateVehicle `+0xd8` (`CoidCurrentOwner`) → `SetVehicle`; requires creature to already exist (no retry); skipped when `IsTrailer` |
| `004fedc0` | `Vehicle::PostCreateFromPacket` | Same owner attach on the create-from-ghost path |
| `0080a4b0` | CreateVehicle (0x201D) game-packet handler | Reads CBID +4, `CoidCurrentOwner` +0xd8 |
| `008078b0` | Client net pump / pending-ghost processor | "Creating object from ghost %I64d" vs bind-only "Assigned a ghost to waiting %I64d" — bind-only **ignores** the parsed ghost CurrentOwner block |
| `005f5de0` | `VehicleNet_PackUpdate` (client’s own pack) | Confirms `GhostVehicle.cs` owner-block encoding byte-compatible |
| `005f5ad0` | Owner sub-packet allocator | Ghost owner block → nested 0x2013/0x2015 create with `+0xf8/+0xfc` = vehicle COID (ghost-first materialize auto-attaches) |
| `0080af70` | CreateCreature (0x2013) game-packet handler | Same as ghost nest create; no-op if TFID already exists. AutoCore sends this for NPC drivers before CreateVehicle |
| `00521310` | Character create apply | Player path: `CurrentVehicleCoid` → `SetVehicle` (why player vehicles always showed HP) |
| `008c5960` | **Self** status HUD update | Reads local player global `DAT_00d1b6d8`; earlier notes calling this the target HUD were wrong |
| `00839ff0` / `00838e20` | Target frame (`i_d_target.xml`) ctor / updater | Target at panel `+0x518`; vehicle-target `"%i/%i"` HP text gated on `vehicle+0xAC` owner ptr non-null + owner type Creature + `vehicle+0x17c` bit 10 clear |
| `0093e120` / `005172d0` | UI target route / set attack target | Click stores picked object **as-is** (no vehicle→driver redirect) at HUDmgr `+0x3048` / player `+0xa0` |
| `004bb040` | `ResolveObjectTarget→GetAsCreature` | Owner COID resolve used by the attach; null-safe |

---

## 8. Open RE / implementation questions

1. ~~HBAI factory from IDAIBehavior~~ — **closed** (§10.2).
2. ~~AI state byte~~ — **closed** (§10.3).
3. ~~Waypoint patrol layout~~ — **closed** (§10.4).
4. ~~Foot-creature path follower~~ — **closed**: `NpcTicker` drives both `Vehicle` and `Creature`; the creature create/ghost path (`CVOGCreature_PostCreateFromPacket`) is **pose-only** (§12.4), so foot NPCs patrol via the server follower rather than a wire path block.
5. ~~WaitTime units~~ — **closed** (§12.1): ms.
6. ~~Faction "wrong relation" filter~~ — **closed** (§12.2): matches `FactionHostility`.
7. ~~18-bit path id widening~~ — **closed** (§12.3): zero-extended 18-bit, `-1` via absence flag.
8. ~~Flee HP-band semantics~~ — **closed** (§12.5): deterministic `hpRatio <= band` gate.
9. ~~Driver as CurrentOwner~~ — **closed** (§14.4): client links driver↔vehicle via
   `CreateVehiclePacket.CoidCurrentOwner` → `SetVehicle` (`004c49d0`). Ghost CurrentOwner is ignored
   on bind-only. Server sends **`CreateCreature` (0x2013) for the NPC driver first**, then
   CreateVehicle with driver coid; delayed destroy+CreateVehicle remains a one-shot recovery.
10. **Character vs Mine** RTTI split when both use ctor `0063d0b0` (does not affect server behaviour).

---

## 9. Answer to the original question (short)

**Is there already code that handles AI/NPC controllers for driving NPC vehicles, and/or scripted events/NPCs/AI?**

- **Yes on the retail client:** complete HBAI system with `CVOGHBAIDriver`, path following (`CVOGMapPath` / `CVOGWaypoint`), combat pursue/fire, and reactions `SetPath` / `SetPatrolDistance`.
- **AutoCore `feature/npc-ai` (Phases A–D):** generic spawn (template/CBID + driver + path), `SetPath` / `SetPatrolDistance`, a **server path follower** for vehicles and foot creatures, a **server combat brain** (aggro / engage / pursue / fire / leash / flee / call-for-help), and **interest management** (spatial hash + tiered scope + update priority) — **no per-mission code**.
- **Still missing:** see §11 remaining optional work.

**Strategy:** server holds authoritative pose along map paths plus faction/HP/aggro/flee state and the firing bit; the client's HBAI renders the fine steering and weapon presentation.

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
The server combat AI (`NpcCombatAi`) now consumes val1–val7: flee band from val2/val3, re-engage from
val4, flee/engage timer from val1, and call-for-help from val5–val7 (§12.5). vals 8–20 remain loaded
but unused (empty in most retail rows). The call-for-help **spreads aggro server-side** but does not
yet emit the client "shout" packet.

---

## 11. Remaining optional work

**Done since this list was written:** foreign NPC vehicle **Health / HealthMax on the wire** via
`EnableMinimalForeignHealthBlock` + 5 s resend window, and **target-frame Cur/Max** via
`CreateVehiclePacket.CoidCurrentOwner` = driver creature coid — see **§14**.

Ordered by likely impact:

1. **Skill casting** — cast from clonebase skill sets (client `FUN_005d1280` / `CVOGReaction_CastSkillOnTarget`); the server flee/help loop is in, skills are not.
2. **Mid-session path ghost updates** — non-initial mask for path id / patrol.
3. **Call-for-help shout packet** — client-facing help notification (aggro already spreads server-side).
4. **SpecialEvent Path type** — client cutscene paths.
5. **Reaction type Path (77)** — named path UI (not SetPath 43).
6. **Character vs Mine ctor** — RTTI split at `0063d0b0`.
7. **Live validation** — path + combat + interest scope in multiplayer.

---

## 12. Phase D — RE closure (2026-07-10)

Five open Risk items were re-checked directly in Ghidra against the open `autoassault.exe`. Every
finding **confirms** the current server behaviour; **no code change was required**. Evidence is the
decompiled pseudocode / disassembly of the anchor functions in §7.

### 12.1 `MapPathPoint.WaitTime` units — **milliseconds**

- The point struct stride is **0x20**; `WaitTime` is an `int32` at point **+0x18** (`Position` 0x00–0x08,
  `AcceptDistance` +0x0c, `ReactionCoid` +0x10/+0x14, `WaitTime` +0x18, 4-byte pad +0x1c) — matching
  AutoCore `MapPathTemplate.MapPathPoint`.
- `CVOGMapPath_AdvanceAndSteer` (`005df950`) reads only `[0..3]` (pose + accept radius) and `[4..5]`
  (`ReactionCoid`). It **never reads `+0x18`** — the dwell is applied by the waypoint/heartbeat caller,
  not by the advance step.
- Every timer in the AI/path stack uses the global tick counter `DAT_00b041cc`, which is
  **millisecond-scaled** (the engage timer `val1` is documented as ms with values like 8000; the
  path-follow interpolation in `FUN_005ce990` scales `DAT_00b041cc` deltas by speed constants). There
  is no seconds→ms conversion anywhere on the path.
- **Server:** `NpcPathFollower.Step` computes `WaitUntilMs = nowMs + point.WaitTime`, i.e. treats the
  raw value as ms. **Consistent with the client ms tick base — correct, no change.**

### 12.2 Faction "wrong relation" filter — **matches `FactionHostility`**

- `CVOGHBAIBase_FindTargetToAttack` (`00639210`) filters each candidate through `FUN_00512440`.
- `FUN_00512440` walks the **owner chain** (`+0xac` → parent, repeatedly) up to the root object and
  returns the root's relation/faction id at **`+0x10`**. The value **`-100`** is the never-aggro
  sentinel: `if (relation == -100) skip`. The self object's class byte at `+0x278` also gates
  (values `2` and `3` short-circuit).
- This lines up with the server heuristic in `FactionHostility`: **`-1` (unset) / `-100` (neutral)
  never aggro**; player races (0/1/2) never mutually aggro; NPC factions (>= 3) aggro any distinct
  real faction. The binary's `-100` sentinel is the load-bearing match. **No change.**
  *(Note: `FactionHostility.cs` still carries a "Pending RE refinement (NPC.md Risk 2)" caveat in its
  XML doc — now resolved by this section; left untouched to keep this a docs-only stage.)*

### 12.3 Ghost path id — **18-bit, zero-extended**

- `VehicleNet_UnpackGhostVehicle` (`005f7720`): the path block reads the current path id via
  `BitStream_readInt` with a bit-count of **`0x12` (18)** — disassembly at the call site is
  `PUSH 0x12 / CALL BitStream_readInt`. `BitStream_readInt(n)` reads `n` bits and masks to `n` bits
  (**zero-extend**, not sign-extend), storing to vehicle `+0xd28` (field `0x34a`).
- The rest of the block: extra path id `readBits(0x20)` → `+0xd2c`; `PathReversing` flag; `PathIsRoad`
  flag; `PatrolDistance` `readBits(0x20)` as float → `+0xd30`. The `-1` "no path" sentinel is conveyed
  by the **presence flag being clear** (then `+0xd28` is forced to `0xffffffff`), never by an
  18-bit all-ones value.
- **Server:** `GhostVehicle` writes the path id with `CoidCurrentPathBits = 18` and the same field
  order. **Matches the client field-for-field — correct, no change.**

### 12.4 Creature create-from-packet — **pose-only, no path/waypoint fields**

- `CVOGCreature_PostCreateFromPacket` (`004c5c30`) reads the packet **owner COID** at `param_2+0xf8/+0xfc`
  (used to attach a driver creature to a vehicle owner) — **not** a path or waypoint id.
- When the owner is invalid (`-1`) it attaches HBAI via creature vtable `+0xc0`, building the AI node
  from the creature's **own current pose** (`param_1-0x2d8..-0x2cc`) with a path id argument of
  `0xffffffff` (**-1**). No path/waypoint block is unpacked for foot creatures.
- **Server:** foot NPCs are treated as pose-only (no wire path block); their patrol is driven by the
  server-side `NpcTicker`/`NpcPathFollower` instead. **Consistent — no change.**

### 12.5 Flee / help semantics — **deterministic HP-band gate**

- `CVOGHBAIDriver_DoLogic` (`005d7750`) branches on the combat-state byte at owner `+0x26c`
  (0 idle / 1 engage / else combat) and reads the profile table (`piVar4`) as ordered floats:
  `[5]` (+0x14) engage/flee **timer** (ms, `val1`), `[6]` (+0x18) re-engage HP threshold, `[7]` (+0x1c)
  flee HP band, `[8]` (+0x20) flee chance `/(encounterCount+1)`, `[10]` (+0x28) skill HP band.
- The HP ratio is `vtable+0x1b0 (current HP) / vtable+0x1ac (max HP)`, and each transition is a hard
  `hpRatio <= threshold` **band** check, optionally ANDed with a random roll (`CVOGReaction_RandomUnitScalar`).
- **Server** (`NpcCombatAi`) takes the deterministic HP-band and drops the client's extra random roll:
  - flee when `hpRatio <= max(val2, val3)` (`ShouldFlee`);
  - while fleeing, run home for `val1` ms; on expiry re-engage if `hpRatio >= val4`, else re-extend;
  - wire state pinned to `Engage` during flee (client renders circling);
  - call-for-help: one roll of `val6` per engagement (gated by `val5` enable and `val7` range),
    spreading target+Engage to same-faction idle NPCs within `val7`.
- The deterministic HP-band interpretation is confirmed; the server simplification (no per-tick random)
  is intentional and documented. **No change.**

---

## 13. Interest management (Phase D)

Scope selection (which entities each connection ghosts) and per-object update priority are
data-driven and mission-agnostic. Three cooperating pieces:

### 13.1 Spatial hash — `Map/SpatialHashGrid.cs`

- Uniform hash over the **XZ plane**, **cell size `128f`**. Keeps aggro-radius queries (~60 u) to
  at most ~4 cells and scope queries (400+ u) to ~49 cells versus a full map scan.
- `QueryRadius(center, radius, buffer)` returns every tracked entity within `radius` (XZ distance).
- Triggers, reactions, and spawn points are never bucketed. `RebucketSweep()` re-homes any entity
  whose position drifted into a new cell — one O(N) pass per main-loop tick, so the grid is immune to
  writers that forget `EnterMap`/`LeaveMap`.

### 13.2 Tiered scope selection — `Map/InterestSelector.cs`

Pure policy (unit-tested without a TNL connection). Tiers, highest priority first:

| Tier | Members | Add radius | Drop radius (hysteresis) |
|-----:|---------|-----------:|-------------------------:|
| 0 | The scope object itself | always | always |
| 1 | Players (characters) | always | always |
| 2 | Mission givers | **800** (`MissionGiverAddRadius`) | **920** (`MissionGiverDropRadius`) |
| 3 | Everything else nearby | **400** (`BaseScopeAddRadius`) | **460** (`BaseScopeDropRadius`) |

- **Hysteresis:** an entity already ghosted (`isGhosted` predicate, backed by TNL's own bookkeeping) is
  retained out to the *drop* radius; a new entity is only added inside the *add* radius. This prevents
  scope flicker at the boundary.
- **Soft cap / budget `ScopeSoftCap = 700`:** already-kept ghosts consume budget **before** any new
  adds, so a nearer new NPC can never displace an already-visible one. Within each group, entities are
  taken **nearest-first**.
- **Town/field filter** (mirrors the retired `ObjectsInRange`): vehicles are hidden in towns,
  characters are hidden in the field; the scope object itself always passes.

### 13.3 Update priority — `TNL/Ghost/GhostObject.GetUpdatePriority`

Per-object float priority the ghost manager uses to order packet space each frame:

```
if (this == scopeObject)                 -> 1.0            // self, always max
if (viewer.Target == Parent)             -> 1.0            // current target pinned
if (Parent == null || viewer == null)    -> updateSkips * 0.01
else                                     -> typeWeight * falloff + updateSkips * 0.01
```

- `typeWeight`: **Character 0.5** > **mission-giver Creature 0.3** > **everything else 0.15**
  (players update most often, then mission givers, then the rest).
- `falloff = clamp(1 - distance / BaseScopeDropRadius(460), 0, 1)` — linear XZ-distance decay.
- `updateSkips * 0.01` is starvation protection: an object skipped many frames climbs the queue so it
  eventually updates regardless of distance/type.

---

## 14. NPC health on the wire (2026-07-11)

Server-side NPC health already worked end to end (spawn HP pools, shared player/NPC damage pipeline
`Vehicle.ProcessCombatInternal` → `TakeDamage` → `OnDeath` → loot → `MissionKillProgress` kill
credit). What clients never saw was **HP under the minimal foreign profile**: with
`EnableMinimalForeignInitialProfile` on (production), foreign GhostVehicle masks were rewritten to
pose|wheel only, stripping `HealthMask` (0x008) and `HealthMaxMask` (0x040) from both initial and
delta packs — NPC bars stayed a mute CreateVehicle full-green.

### 14.1 Lever

| Item | Value |
|------|-------|
| Lever | `GhostVehicle.EnableMinimalForeignHealthBlock` |
| Code default | **false** (production-safe reset) |
| Env var | `AUTOCORE_WIRE_MINIMAL_FOREIGN_HEALTH` |
| Production JSON | **true** in root, Launcher, and Sector `wire-isolation.levers.json` |
| Console | `wire set EnableMinimalForeignHealthBlock <0|1>` (live) |

**Cur/Max also needs owner on the wire.** With `EnableMinimalForeignInitialProfile=true`, set **both**:

| Lever | Why |
|-------|-----|
| `EnableMinimalForeignHealthBlock` | Live HP setters / bar updates |
| `EnableMinimalForeignOwnerBlock` + `EnableOwnerWire` | Ghost builds the driver so CreateVehicle `CoidCurrentOwner` can attach (`vehicle+0xAC`) |

Incomplete lever files (old Sector copies that only listed Scope/Path/Owner/Template) leave Health/Owner admissions at **false** after `ResetToDefaults` and silently strip NPC numbers. Startup logs `WireIsolationLevers: loaded <path>` — confirm that path and that `GetNpcCombatLeverWarnings` does not fire errors.

When on, the minimal-profile mask rewrite admits `HealthMask | HealthMaxMask` on **initial** packs
and through the **delta** filter (`Position | WheelSet | Health | HealthMax`). With the production
lever set (minimal + initial hardpoint + health) the foreign initial mask is exactly
**`0x10000004A`** = pose(0x2) | Health(0x8) | HealthMax(0x40) | WheelSet(0x100000000).

### 14.2 Post-materialize re-apply (keep-dirty after initial)

The client only calls the HP combat setters (`vtable +0x240/+0x248`) against the **live** vehicle
object. If the ghost initial races ahead of CreateVehicle materialize, HP is cached on ghost slots
`0x21/0x22` and never reaches the target-frame getters (`vtable +0x1b0/+0x1ac`). The server
therefore keeps `HealthMask | HealthMaxMask` dirty for **`HealthResendWindowMs` (default 5000 ms)**
after any foreign initial that packed HP, so every delta in that window re-sends HP until the live
object has certainly materialized. A single +430 ms re-send was live-observed (2026-07-11, coid
1342195382) to still lose the race — blank bar despite `hp=1 cur=150 max=150` on the wire. Expected
WireDiag signature: initial `hp=1`, then non-initial packs also `hp=1` for ~5 s (log shows at most
`MaxPartialGhostPacksPerCoid = 3` non-initial packs per coid — later re-sends are real but unlogged).

When the lever is **off** under the minimal profile, stripped health bits are preserved in the
TNL return mask (same pattern as the WheelSet dirty-preserve), so a live lever flip ships HP
without needing a fresh damage event.

### 14.3 Diagnostics

`WireDiag` GhostVehicle pack detail now includes `hp=<0|1> cur=<HP> max=<MaxHP>`. Live checks:

1. Startup `WireIsolationLevers active:` lists `EnableMinimalForeignHealthBlock = true`.
2. Foreign NPC ghost initial: `mask=10000004A ... hp=1 cur=… max=… profile=minimal`, followed by a
   non-initial pack with `hp=1` (the keep-dirty re-send).
3. In-game: damage → `HealthMask` dirty (`SimpleObject.TakeDamage`) → bar drop per delta → at 0 HP
   the NPC vehicle despawns (`Vehicle.OnDeath` NPC path: loot roll, `SetMap(null)`,
   `BroadcastDestroy`) and mission kill credit fires.

### 14.4 Target-frame cur/max text — CreateVehicle owner attach (Ghidra, 2026-07-11)

Live A/B with the health lever ON showed the wire fully correct (initial `mask=10000004A hp=1
cur=150 max=150`, re-send delta `hp=1`) and the bar depleting on damage — but the target-frame
**numbers** never rendered for NPC vehicles (walking NPCs fine). Deep RE closed it:

- **`FUN_008c5960` is the SELF status HUD** (its global `DAT_00d1b6d8` is the local player, not
  the UI target — earlier notes calling it the target HUD were wrong). The real target frame is
  the `i_d_target.xml` panel: ctor `FUN_00839ff0`, updater **`FUN_00838e20`**, tracked target at
  panel `+0x518`.
- In `FUN_00838e20`, a **Vehicle target renders `"%i/%i"` HP text only when `vehicle+0xAC` (the
  client owner-object pointer) is non-null and the owner's type is Creature (0x12)** — otherwise
  the branch renders the name only. The gauge bar reads the vehicle directly, which is why it
  depletes while the numbers stay blank. (Secondary gate: bit 10 of `vehicle+0x17c` must be clear.)
- `vehicle+0xAC` / `driverCreature+0x250` are linked ONLY by **`Creature::SetVehicle`
  (`004c49d0`)**, reached from `Vehicle_applyCreatePacket` (`00505590`) /
  `Vehicle::PostCreateFromPacket` (`004fedc0`) resolving **`CreateVehiclePacket.CoidCurrentOwner`
  (packet `+0xd8`)**. The vehicle **ghost** CurrentOwner block is parsed into a pending owner
  sub-packet but is **ignored on the bind-only path** — which is the path AutoCore always takes
  because it pre-sends CreateVehicle. There is no retry.
- **Server fix (owner coid):** `Vehicle.WriteToPacket` sends
  `CoidCurrentOwner = DBData?.CharacterCoid ?? Owner?.ObjectId.Coid ?? 0` — NPC vehicles carry
  their driver creature coid.
- **Server fix (driver materialize):** bind-only ghost path (`FUN_008078b0` "Assigned a ghost to
  waiting") **never** runs nested `0x2013` CreateCreature for the driver — only ghost-first does.
  AutoCore pre-sends CreateVehicle, so the driver object never existed on the client and
  `FUN_004bb040(CoidCurrentOwner)` always failed. Fix: **`CreateCreaturePacket` (0x2013)** for
  pure-creature owners **before** every foreign `CreateVehicle` / owner-attach reapply
  (`SectorMap.TrySendForeignDriverCreate`). Layout matches nest offsets: vehicle link at `+0xF8`,
  level at `+0x114`. WireDiag: `ForeignDriverCreate coid=… driverCoid=…`.
- **Server fix (attach race):** even with driver create first, keep one-shot delayed
  **`CreateCreature` + `DestroyObject` + `CreateVehicle` (`IsItemLink=0`)** after ghost
  (`ForeignOwnerAttachReapplyMilliseconds`, default **1000 ms**) so a raced first apply still
  gets a full `SetVehicle`. **IsItemLink re-apply is wrong** (tooltips, blank numbers). WireDiag:
  `ForeignOwnerAttachReapply coid=… driverCoid=… destroy+create isItemLink=0`.

### 14.5 Scope notes

- **GhostCreature (foot NPCs) unchanged** — the minimal profile never filtered creature ghosts;
  their Health/HealthMax pack whenever the mask bits are dirtied.
- **Death is conveyed by the destroy broadcast**, not the corpse flag: `Vehicle.OnDeath` dirties
  `HealthMask` then tears the ghost down in the same call, so the corpse delta usually never ships
  for NPC vehicles. Intentional — do not force a corpse delta before destroy.
- **HealthMax is never re-dirtied mid-life** — acceptable: NPC max HP is fixed at spawn before the
  ghost initial, which always carries HealthMax under the lever.
- Tests: `GhostVehicleWireRegressionTests` (`PackInitial/PackDelta_ForeignMinimal_Health*`) and
  `WireIsolationLeversTests` cover mask math, stream layout, keep-dirty, strip+preserve, and lever
  registry (name/env/JSON/reset/status).

---

*Last updated: 2026-07-11 — §14.4 CreateCreature driver materialize for Cur/Max; previously health lever + owner attach reapply.*
