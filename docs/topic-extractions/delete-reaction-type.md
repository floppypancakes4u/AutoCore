# Topic Extraction: Delete (ReactionType)

## Executive Summary

### What the fork currently does

`ReactionType.Delete` is enum value **3**. When a trigger fires and `SectorMap.TriggerReactionsInternal` processes a reaction whose template type is Delete, `Reaction.TriggerIfPossible` dispatches to `HandleDelete(activator)`.

`HandleDelete` has two branches driven by map-authored `ReactionTemplate.ActOnActivator`:

1. **`ActOnActivator == true`**: calls `activator.SetMap(null)` (remove the activating object from the sector map).
2. **`ActOnActivator == false`**: for each COID in `Template.Objects`, looks up `map.GetObjectByCoid` and, if found, calls `obj.SetMap(null)`. Missing objects are treated as **client-side-only** (debug log only; still considered success).

`SetMap(null)` → `SectorMap.LeaveMap`: removes the object from `Objects` (and `Triggers` / `Reactions` if applicable), and clears `TriggerManager` latches for that object COID (or for a removed trigger, all latches on that trigger).

**Networking for Delete specifically:**

- `HandleDelete` does **not** construct or send `DestroyObjectPacket` (opcode `0x2020`).
- On successful `TriggerIfPossible` (Delete always returns `true` after `CanTrigger`), the map layer still builds a nested **`LogicStateChangePacket` (Reaction)** inside **`GroupReactionCallPacket` (0x206C)** and sends it via `SendReactionPacket(ResolveCharacter(activator), …)`. So **GroupReactionCall still fires after Delete** for that reaction entry, as long as the activator’s character can be resolved and has an owning connection.
- Compare: creature death and item pickup both do `SetMap(null)` **and** broadcast `DestroyObjectPacket` to map characters. Delete does only map removal.

Live trigger entry for players is **vehicle movement** (`Vehicle.HandleMovement` → `TriggerManager.CheckTriggersFor`), not TNL `PrepareWritePacket` (that path was deliberately emptied with a comment citing TransferMap / Delete re-entrancy).

### Soundness assessment

**Incomplete and risky for server-authoritative objects; plausible for client-only map props.** Server removes map membership only. Client visual/logic deletion for Delete is implicitly expected to come from the **0x206C GroupReactionCall** reaction apply path (same as other “client-side” reaction types), not from `DestroyObject`. That is consistent with comments that many `Template.Objects` COIDs never exist as server entities. It is **not** consistent with how the server despawns ghosted world entities (loot items, creatures), which use `DestroyObject`.

**ActOnActivator on a player vehicle** is especially dangerous: the live activator is the vehicle; Delete would yank the vehicle off the map without character teardown, ghost reset, or map transfer.

### Biggest risks

1. **ActOnActivator + vehicle activator** removes the player vehicle from the map mid-trigger chain (broken map membership; no `DestroyObject` / ghost teardown).
2. **Delete mid-batch** sets `activator.Map = null`, so **later reactions in the same list / child chains** fail `CanTrigger` (`activator.Map is null`).
3. **Server ghosted entities** deleted only via `SetMap(null)` without `DestroyObject` may desync clients that still have ghosts/create packets.
4. **No tests** cover `HandleDelete` or Delete reaction networking.

---

## Scope

### In scope

- `ReactionType.Delete = 3` and `Reaction.HandleDelete`
- `ReactionTemplate` fields used by Delete (`ActOnActivator`, `Objects`)
- Map removal via `ClonedObjectBase.SetMap(null)` / `SectorMap.LeaveMap` / `EnterMap`
- Whether destroy/despawn is networked: `DestroyObjectPacket` (0x2020) usage elsewhere vs Delete reaction
- Side effects during trigger fire (movement → TriggerManager → TriggerReactions) including re-entrancy notes vs old PrepareWritePacket path
- Object-list vs ActOnActivator branches; client-side-only object handling
- Ghost teardown related to map leave (or lack thereof)
- Interaction with GroupReactionCall after Delete

### Out of scope

- Inventory item destroy / equip COID destroy rules (except comparison to `DestroyObject` usage)
- Mail delete, character-delete login packets
- Creature death loot generation details (only shared `SetMap(null)` + `DestroyObject` pattern)
- Full mission/trigger system redesign

---

## Relevant Files

| File | Purpose | Relevant symbols / methods |
|---|---|---|
| `src/AutoCore.Game/Entities/Reaction.cs` | Enum + Delete dispatch/handler | `ReactionType.Delete = 3`, `TriggerIfPossible`, `HandleDelete`, `CanTrigger` |
| `src/AutoCore.Game/EntityTemplates/ReactionTemplate.cs` | Map binary fields for Delete targets | `ActOnActivator`, `Objects`, `Read` |
| `src/AutoCore.Game/Entities/ClonedObjectBase.cs` | Map pointer + enter/leave | `SetMap`, `Map`, `Ghost`, `ClearGhost` |
| `src/AutoCore.Game/Map/SectorMap.cs` | Object registries; reaction fire + S2C | `EnterMap`, `LeaveMap`, `GetObjectByCoid`, `TriggerReactionsInternal`, `ResolveCharacter`, `SendReactionPacket` |
| `src/AutoCore.Game/Packets/Sector/DestroyObjectPacket.cs` | S2C destroy (not used by Delete) | `Opcode = DestroyObject`, `Write` |
| `src/AutoCore.Game/Constants/GameOpcode.cs` | Opcode IDs | `DestroyObject = 0x2020`, `GroupReactionCall = 0x206C` |
| `src/AutoCore.Game/Packets/Sector/GroupReactionCallPacket.cs` | Nested reaction notify | `Write` BitStream entries |
| `src/AutoCore.Game/Packets/Sector/LogicStateChangePacket.cs` | Nested reaction entry fields | ctor `(reactionCoid, activator, singleClientOnly)` |
| `src/AutoCore.Game/Managers/TriggerManager.cs` | Edge fire → `TriggerReactions` | `CheckTriggersFor`, `ClearTriggersFor`, `ClearTrigger` |
| `src/AutoCore.Game/Entities/Trigger.cs` | Volume gate | `CanTrigger` (requires `activator.Map`) |
| `src/AutoCore.Game/Entities/Vehicle.cs` | Live trigger check site | `HandleMovement` → `CheckTriggersFor` |
| `src/AutoCore.Game/TNL/TNLConnection.cs` | Explicit non-check on write prep | `PrepareWritePacket` comment re Delete |
| `src/AutoCore.Game/TNL/TNLConnection.Sector.cs` | Item pickup destroy comparison | `HandleItemPickupPacket` → `SetMap(null)` + `DestroyObjectPacket` |
| `src/AutoCore.Game/Entities/Creature.cs` | Death despawn comparison | `OnDeath` → `SetMap(null)` + `DestroyObjectPacket` |
| `src/AutoCore.Game/TNL/Ghost/GhostObject.cs` | Ghost parent link | `OnGhostRemove` → `ClearGhost`; no LeaveMap coupling |
| `docs/topic-extractions/server-triggers.md` | Prior trigger subsystem extraction | Reaction fire / GroupReactionCall context (partially outdated vs current send path) |
| `Documentation/PACKET STRUCTURES.md` | DestroyObject inventory caveat | “Do not send DestroyObject for equipped weapon COID” |
| `src/AutoCore.Game.Tests/Managers/TriggerManagerTests.cs` | Trigger latch tests | Uses `SetMap(null)` on trigger for leave; **no Delete reaction tests** |

---

## Packet / Network Structures

### Packets involved in Delete reaction path

| Packet / opcode | Direction | Role in Delete | Sent by HandleDelete? |
|---|---|---|---|
| **GroupReactionCall** `0x206C` | S→C | After successful `TriggerIfPossible`, map adds `LogicStateChange` Reaction entry for this reaction COID + activator TFID | **No** — sent by `SectorMap.TriggerReactionsInternal` after handler returns true |
| Nested **LogicStateChange** (type Reaction) | inside 0x206C | `ReactionCoid` (19-bit), activator Coid (u64), Global flag, SingleClientOnly flag | Built as `new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false)` |
| **DestroyObject** `0x2020` | S→C | Used elsewhere to force client remove of a TFID | **No** — not referenced in `HandleDelete` or Delete path |

### DestroyObjectPacket structure (comparison only)

```text
Opcode: 0x2020 (GameOpcode.DestroyObject)
Write order:
  int UnknownField (default 0)
  TFID ObjectId (WriteTFID)
```

Source: `DestroyObjectPacket.Write`.

### Production send sites for DestroyObject (not Delete)

| Site | When | Pattern |
|---|---|---|
| `Creature.OnDeath` | After loot; save TFID + map; `SetMap(null)`; then foreach character on map send destroy | Server entity despawn + client notify |
| `TNLConnection.Sector` item pickup | After validate; save TFID + map; `SetMap(null)`; broadcast destroy | Same pattern |

**Confirmed:** `HandleDelete` does **not** send `DestroyObjectPacket`. Grep shows only Creature death, item pickup, opcode definition, and the packet class.

### GroupReactionCall still fires after Delete?

**Yes**, under normal trigger processing, when:

1. `Reaction.CanTrigger(activator)` is true (`activator` non-null and `activator.Map` non-null **before** Delete runs).
2. `HandleDelete` returns `true` (always returns true in current code, including “object not found” and “activator has no map” early paths).
3. `TriggerReactionsInternal` then enqueues the LogicStateChange and later calls `SendReactionPacket(ResolveCharacter(activator), clientPacket)`.
4. Character resolve succeeds: `activator.GetAsCharacter() ?? activator.GetSuperCharacter(false)` (vehicle with owner character works).
5. `character.OwningConnection` non-null.

So Delete is not “silent server-only”: the map layer still notifies the client of the **reaction COID**, relying on client clonebase/map data to interpret type Delete.

**Caveats:**

- If Delete runs as `ActOnActivator` first in a multi-reaction list, later reactions in the **same** batch may not pass `CanTrigger` because map is already null — those later entries won’t be added to the packet.
- Child reaction lists still run with the same (possibly map-less) activator after the parent batch send.
- `DoForAllPlayers` broadcast uses the same LogicStateChange entries and `SendBroadcastToMap` (implemented). `DoForConvoy` still logs skip (no convoy system).

### No dedicated “DeleteObject” reaction packet

There is no separate opcode for reaction-type Delete. Client notify is the generic reaction entry in 0x206C (same as Activate, Text, GiveMissionDialog, etc.).

---

## Server-Side Flow

### A. Data load (map `.fam`)

1. `ReactionTemplate.Read` loads:
   - `ReactionType` (byte) — Delete = 3
   - `ActOnActivator` (bool)
   - For non-`TransferMap` types: `Objects` list of COIDs (`ReadCOIDFromFile`)
   - Child `Reactions` list, conditions, etc.
2. Template `Create()` → `Reaction` entity placed on map via `SetMap` → `EnterMap` → registered in `SectorMap.Reactions` and `Objects`.

### B. Trigger fire (live player path)

```text
Client VehicleMoved
  → TNLConnection handles movement
  → Vehicle.HandleMovement updates pose
  → if Map != null: TriggerManager.CheckTriggersFor(this)  // try/catch logged
    → snapshot map.Triggers
    → for each trigger: CanTrigger(vehicle) + edge latch + delays/counts
    → on fire: vehicle.Map?.TriggerReactions(vehicle, trigger.Template.Reactions)
```

`TNLConnection.PrepareWritePacket` **does not** call `CheckTriggersFor`. Comment:

> Volume triggers are checked after vehicle movement (see Vehicle.HandleMovement), not during TNL write prep (avoids re-entrancy with TransferMap / Delete).

### C. Reaction batch processing

```text
TriggerReactionsInternal(activator, reactionCoids, depth)
  depth >= 10 → stop
  foreach reactionCoid:
    lookup local non-global Reaction on map
    if reaction.TriggerIfPossible(activator):   // ← HandleDelete here
      build LogicStateChange(reaction.Coid, activator.ObjectId, singleClientOnly: false)
      add to clientPacket (+ broadcast/convoy if flags)
      queue child Template.Reactions
  SendReactionPacket(ResolveCharacter(activator), clientPacket)
  recurse children
  optional DoForAllPlayers broadcast
```

### D. HandleDelete (exact behavior)

```text
HandleDelete(activator):
  map = activator.Map
  if map == null:
    log debug; return true

  if Template.ActOnActivator:
    log; activator.SetMap(null)
  else:
    foreach objectCoid in Template.Objects:
      obj = map.GetObjectByCoid(objectCoid)
      if obj != null:
        log; obj.SetMap(null)
      else:
        log "not found on server (client-side only)"
  return true
```

**Evidence answers:**

| Question | Answer |
|---|---|
| Does HandleDelete send DestroyObjectPacket? | **No** |
| What does SetMap(null) do? | If `Map != null`, call `Map.LeaveMap(this)`, set `Map = null`. Does **not** clear Ghost, does **not** send packets, does **not** dispose entity |
| Does GroupReactionCall still fire after delete? | **Yes** if Delete’s `TriggerIfPossible` returned true (it does after CanTrigger), and character+connection resolve |
| Activator is often a vehicle — ActOnActivator? | **Yes risk**: live path passes **vehicle**; ActOnActivator removes **vehicle** from map |

### E. SetMap / LeaveMap detail

`ClonedObjectBase.SetMap(SectorMap map)`:

- Only acts when `Map != map`.
- Leaving: `Map.LeaveMap(this)` then `Map = map`.
- Entering: assign then `Map.EnterMap(this)`.

`SectorMap.LeaveMap`:

1. If Trigger: remove from `Triggers`; `TriggerManager.ClearTrigger(triggerCoid)`.
2. If Reaction: remove from `Reactions`.
3. If not in `Objects` → **throw** `InvalidOperationException("This object is not on the map!")`.
4. Remove from `Objects`.
5. `TriggerManager.ClearTriggersFor(clonedObject.ObjectId.Coid)`.

`EnterMap` symmetrically registers Trigger/Reaction and adds to `Objects` (throws if already present).

### F. Object list vs ActOnActivator

| Branch | Target | Server action | Missing target |
|---|---|---|---|
| ActOnActivator | The activator entity (often player **vehicle**) | `activator.SetMap(null)` | N/A (activator exists) |
| Objects list | Map entities by COID | `GetObjectByCoid` + `SetMap(null)` | Log client-side-only; still return true |

`GetObjectByCoid` scans `Objects` by COID ignoring Global flag. Only **instantiated server map objects** can be removed. Many map graphics never get ghosts (`InitializeLocalObjects` comments out `CreateGhost` for graphics templates); some may not even be in `Objects` depending on template create results — HandleDelete already assumes frequent misses.

### G. Ghost teardown

**Delete path does not:**

- Call `ClearGhost()`
- Call `ResetGhosting` / unghost APIs
- Send `DestroyObject`
- Unregister from `ObjectManager` (not part of LeaveMap)

Ghost lifecycle hooks that **do** clear parent ghost:

- `GhostObject.OnGhostRemove` → `Parent?.ClearGhost()`
- Disconnect: `ClearGhost` on character + vehicle after `SetMap(null)`
- Map transfer: `ResetGhosting` / reestablish path in `MapManager` / `TNLConnection.Sector`

If a deleted object had a live `Ghost` and was only removed from `SectorMap.Objects`, scoping (`ObjectsInRange` walks `Objects` ghosts) will stop offering it for new scope selection, but **no explicit destroy/unghost** is issued by Delete. For map-static objects that never created ghosts, this is moot. For anything that used create packets + ghosts (creatures, loot items), Delete’s behavior diverges from the intentional despawn pattern.

### H. Side effects of deleting activator during trigger fire

Order when Delete with ActOnActivator runs inside a reaction list:

1. `HandleDelete` → vehicle `LeaveMap` → latches for vehicle COID cleared (`ClearTriggersFor`).
2. `TriggerIfPossible` returns true → LogicStateChange for Delete still queued.
3. Subsequent reactions in the **same** foreach: `CanTrigger` requires `activator.Map != null` → **false** → skipped (no server effect, no packet entry).
4. After batch: `SendReactionPacket` still works if vehicle’s Owner character is set (`GetSuperCharacter`).
5. Child reaction recursion uses same map-less activator → child `CanTrigger` fails.
6. Outer `CheckTriggersFor` continues over remaining triggers on the **snapshot list**, but `CanTrigger` fails for all remaining because Map is null.

Vehicle with `Map == null` after this:

- Further `HandleMovement` trigger checks skip (`if (Map != null)`).
- Character may still be on map with `CurrentVehicle` pointing at an off-map vehicle.
- No evidence of reconnect/transfer recovery specific to this path.

Deleting a **Trigger** entity via Objects list: LeaveMap clears that trigger’s latches globally and removes it from `Triggers` — future volume checks won’t see it (snapshot mid-`CheckTriggersFor` may still hold a reference for the rest of that single call).

Deleting a **Reaction** entity via Objects list: removes from `Reactions`/`Objects`; later reaction COID lookups in the same or future batches fail with “Reaction object isn’t found”.

---

## State and Persistence

### Runtime state changed by Delete

| State | Where | Change |
|---|---|---|
| Map membership | `ClonedObjectBase.Map`, `SectorMap.Objects` | Null map; removed from dictionary |
| Trigger registry | `SectorMap.Triggers` | If deleted object is Trigger |
| Reaction registry | `SectorMap.Reactions` | If deleted object is Reaction |
| Trigger latches | `TriggerManager._activeTriggers` | Cleared for deleted object COID; if deleted is Trigger, all keys with that trigger COID |
| Ghost | `ClonedObjectBase.Ghost` | **Unchanged** by Delete/SetMap |
| DB / persistence | — | **None** observed for Delete reaction |

### What is not persisted

Delete is pure session/map runtime. Process restart reloads map templates and recreates local objects from `.fam` (`SectorMap` ctor → `InitializeLocalObjects`). No durable “this COID was deleted for this player” store for Delete reaction.

Per-player client-side delete of map props (if intended) would be client local state driven by 0x206C; server does not track that.

### Latch interaction

Deleting the **activator** clears that activator’s latches, so the current edge-enter state is wiped even while the client may still be physically inside the volume. Re-arm would require the object to re-enter map membership and re-enter the volume (edge enter again). With vehicle off map, that does not happen cleanly.

---

## Responsibility Boundary Review

| Concern | Owner in fork | Appropriate? |
|---|---|---|
| Decide Delete vs other reaction types | `Reaction.TriggerIfPossible` | Yes |
| Apply server map removal | `HandleDelete` → `SetMap` | Reasonable coordination |
| Map dictionary + latch cleanup | `SectorMap.LeaveMap` / `TriggerManager` | Yes |
| Client reaction apply (visual Delete) | Implied client via 0x206C | Consistent with other “client-side” reaction types |
| Networked despawn of ghosted entities | **Not** in Delete; exists in Creature/Item paths | **Gap** if Delete targets ghosted server entities |
| Character/vehicle session integrity | Not handled on ActOnActivator vehicle delete | **Missing** ownership boundary |
| Packet recipient resolve | `SectorMap.ResolveCharacter` | Correct helper for vehicle activators (uses `GetSuperCharacter`) |

**Layering note:** `HandleDelete` is thin and domain-correct for “remove from sector map.” It does not own networking, which matches Activate/Text-style reactions. The engineering risk is that **server authority objects** share the same “client will handle it” assumption without the destroy path used for combat/loot despawn.

---

## Engineering Concerns

Prioritized, evidence-based:

1. **Critical — ActOnActivator can remove player vehicle from map**
   - Location: `HandleDelete` + live activator = vehicle from `CheckTriggersFor(this)` in `Vehicle.HandleMovement`.
   - Problem: `activator.SetMap(null)` on vehicle without character leave, transfer, ghost reset, or DestroyObject.
   - Why it matters: map/session invariants break; subsequent reactions in chain aborted.
   - Fix direction: Never ActOnActivator-delete player-controlled vehicles/characters; or treat as special case (ignore / transfer / full leave sequence). Validate map data flags.

2. **High — No DestroyObject for server-present objects**
   - Location: `HandleDelete` vs `Creature.OnDeath` / item pickup.
   - Problem: Only `SetMap(null)`; clients that know the entity via create/ghost may keep it.
   - Why: Desync for anything that was server-spawned and networked.
   - Fix direction: If object is server-authoritative and was created to clients, broadcast `DestroyObject` (or rely solely on documented client reaction Delete for local-only props — pick one model and enforce).

3. **High — Mid-chain activator Map null**
   - Location: `Reaction.CanTrigger` + batch order in `TriggerReactionsInternal`.
   - Problem: Delete early in reaction list cancels later siblings/children for that activator.
   - Why: Map authors chaining Delete with other server effects get silent partial execution; GroupReactionCall incomplete for skipped entries.
   - Fix direction: Defer map removal until after full reaction batch/packet build; or process Delete last; or clone activator context.

4. **Medium — Always `return true` including soft failures**
   - Location: `HandleDelete` map-null early return; missing objects still success.
   - Problem: Always emits LogicStateChange if CanTrigger passed.
   - Why: Client may Delete props the server never had; often intentional. Hides real server miss configuration when objects should exist.
   - Fix direction: Metrics/log levels; distinguish “expected client-only” vs “expected server entity missing”.

5. **Medium — LeaveMap throws if double-remove**
   - Location: `LeaveMap` if object not in `Objects`.
   - Problem: Two Deletes on same COID in one chain → second `SetMap(null)` is no-op (Map already null). Two different code paths calling LeaveMap without SetMap could throw. Same object in Objects list twice is unlikely (list-driven once per COID per reaction). Concurrent deletes of same object from two players: second SetMap null is safe; first LeaveMap removes; second SetMap sees Map already null.
   - Residual risk: any caller that calls `LeaveMap` directly after SetMap null — not observed on Delete path.

6. **Medium — Ghost orphan potential**
   - Location: SetMap/LeaveMap do not clear Ghost; Delete does not DestroyObject.
   - Problem: Parent still holds Ghost reference while not on map.
   - Why: Stale ghost updates if something still references parent; scoping may be inconsistent.
   - Fix direction: Align with creature/item despawn (destroy packet + ghost lifecycle).

7. **Low — No unit tests for HandleDelete**
   - Location: `AutoCore.Game.Tests` — no `ReactionType.Delete` coverage.
   - Fix direction: Tests for Objects list remove, missing COID success, ActOnActivator, GroupReactionCall still sent, subsequent reaction skip after map null, no DestroyObject unless policy changes.

8. **Low — Import / AI commentary style in Reaction.cs**
   - Verbose debug logs and “client-side only” comments without protocol references.
   - Not a functional bug; indicates AI-assisted incomplete reaction surface.

### Note on outdated server-triggers.md

`docs/topic-extractions/server-triggers.md` still describes PrepareWritePacket as the live CheckTriggersFor site and send path as `GetAsCharacter()` only. **Current code** moved checks to `Vehicle.HandleMovement` and uses `ResolveCharacter` / `SendReactionPacket` with `GetSuperCharacter`. Delete-specific conclusions in **this** document use current sources.

---

## Crash / Stability Risks

| Risk | Evidence | Severity |
|---|---|---|
| Player vehicle removed from map via ActOnActivator | HandleDelete + vehicle activator | **High** (gameplay/session break) |
| Reaction chain partial execution after Delete | CanTrigger requires Map | **High** for multi-reaction triggers |
| LeaveMap InvalidOperationException | Object not in Objects | **Low–Medium** if double-leave without SetMap; SetMap(null) twice is safe |
| Exception in CheckTriggersFor during movement | Vehicle.HandleMovement try/catch logs and continues | Mitigated for movement path |
| Exception during old PrepareWritePacket | No longer runs CheckTriggersFor | Mitigated (comment cites Delete) |
| Client desync ghosted entity | No DestroyObject on Delete | **Medium** for server-spawned entities; **Low** for pure client map props |
| Dictionary mutation during TriggerReactions | TriggerManager snapshots Triggers; LeaveMap mutates Objects/Reactions | Medium if concurrent multi-thread access; single-threaded movement path less likely |
| NRE send | `character?.OwningConnection?.SendGamePacket` | Mitigated |
| Depth infinite loop | Max depth 10 | Mitigated |

---

## Comparison to Expected Behavior

Sources: in-repo reaction comments, GroupReactionCall usage for client-side reaction types, DestroyObject usage for world despawn, `Documentation/PACKET STRUCTURES.md` (DestroyObject inventory warning only), `server-triggers.md` (reaction pipeline).

| Expected (inferred from sibling systems + map data fields) | Fork behavior | Difference | Risk |
|---|---|---|---|
| Delete reaction type 3 exists in map data | Enum + handler present | OK | — |
| ActOnActivator vs Objects list target selection | Implemented | OK | ActOnActivator on player vehicle unsafe |
| Client applies Delete for map-local/client-only props via reaction notify | GroupReactionCall still sent; no server entity required | Plausible match | Depends on client (client not in this repo) |
| Server-authoritative despawn notifies clients with DestroyObject | Creature/item do; Delete does not | Inconsistent | Desync |
| Ghost teardown on remove | Disconnect/map transfer handle ghosts; Delete does not | Incomplete | Orphans / scope oddities |
| Full reaction chain still runs after Delete | Later reactions need activator.Map | Broken if Delete first | Content bugs |
| Delete does not mean inventory/mail/character login delete | Separate codepaths; Delete is reaction-only | Correct scope separation | — |

**Critical evidence checklist:**

| Claim | Result |
|---|---|
| HandleDelete sends DestroyObjectPacket? | **Refuted** — does not |
| SetMap(null) behavior | LeaveMap + Map=null; no packets; no ClearGhost |
| GroupReactionCall after delete? | **Confirmed** when TriggerIfPossible succeeds (always true post-CanTrigger) and character connection exists |
| ActOnActivator removes vehicle if vehicle is activator? | **Confirmed** by code path identity |

---

## Questions for the User

1. In original Auto Assault map content, is `ActOnActivator` on Delete ever set for **player-facing** volume triggers, or only for NPC/script activators?
2. Should client-only map object Delete be **only** via 0x206C (no server entity), with server `Objects` list empty — i.e. is the current “missing COID is OK” model intentional retail behavior?
3. When Delete targets a **server-spawned** creature/prop, should the server broadcast `DestroyObject` (0x2020) in addition to GroupReactionCall?
4. Is there retail documentation or client reverse-engineering for how ReactionType.Delete is applied client-side (hide graphics, unload collision, full destroy)?
5. Should Delete of the activator defer until after the full reaction batch and packet send to avoid canceling sibling reactions?
6. Are there known maps in this fork that fire Delete with ActOnActivator on drive-over triggers?

---

## Recommended Follow-Up Fix Issues

1. **Guard ActOnActivator Delete for player vehicles/characters**  
   Skip or special-case when activator is a player-owned vehicle/character; log error. Add unit test.

2. **Defer LeaveMap until end of reaction batch (or process Delete last)**  
   Preserve activator.Map for sibling/child reactions and stable GroupReactionCall contents; apply removals after send.

3. **Policy for networked despawn**  
   If deleted object had server presence that clients were created for, send `DestroyObjectPacket` to map connections (mirror creature/item), and define ghost cleanup. Do **not** send DestroyObject for inventory-equipped COIDs (see PACKET STRUCTURES warning — different domain).

4. **Tests (TDD)**  
   - Objects list: entity removed from `SectorMap.Objects`.  
   - Missing COID: no throw, returns true.  
   - ActOnActivator: activator.Map null.  
   - No DestroyObject sent (or assert sent after policy change).  
   - GroupReactionCall still attempted after Delete.  
   - Subsequent reaction in same list does not run after current immediate SetMap(null) (documents bug) / does run after deferral fix.

5. **Telemetry**  
   Count Delete with ActOnActivator, missing server objects, and post-Delete skipped reactions for map QA.

6. **Refresh server-triggers.md**  
   Align PrepareWritePacket vs HandleMovement and ResolveCharacter with current code (separate doc chore).

---

*Extraction only. No production code was modified. Evidence limited to repository sources listed above.*
