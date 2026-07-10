# Topic Extraction: DeleteReaction

## Executive Summary

**There is no symbol, type, opcode, packet, class, method, or file named `DeleteReaction` in this repository.**

What *does* exist — and is the only closely related production behavior — is:

| Concept | Exists? | Location |
|---|---|---|
| Named system / type `DeleteReaction` | **No** | — |
| Opcode / packet `DeleteReaction` | **No** | — |
| Client doc symbol `CVOGReaction_Delete` | **No** in this repo | Only generic `CVOGReaction_Dispatch` notes |
| `ReactionType.Delete = 3` | **Yes** | `src/AutoCore.Game/Entities/Reaction.cs` |
| Handler `HandleDelete` | **Yes** | same file |
| Log strings `"Delete reaction {COID}: …"` | **Yes** | same file (not a type name) |
| Nested child list `ReactionTemplate.Reactions` | **Yes** | chain of child reaction COIDs, **not** “delete reactions” |
| Removing a `Reaction` entity from map registries | **Yes** (generic) | `SectorMap.LeaveMap` via `ClonedObjectBase.SetMap(null)` |

**Bottom line:** Treat “DeleteReaction” as a **name that does not exist**. The related feature is **reaction type Delete (enum value 3)**: when a map reaction of that type fires, the server removes target map objects by calling `SetMap(null)`. Client notification for the reaction itself is the generic **GroupReactionCall / LogicStateChange** path (same as all other reaction types). There is **no** dedicated delete-reaction packet. There are **no unit tests** for `HandleDelete`.

Do not invent a separate DeleteReaction feature from this name; the confusion points are documented below.

---

## Scope

### In scope (this report)

- Exhaustive search for `DeleteReaction`, `DelReaction`, `delete reaction`, `CVOGReaction_Delete`, and related spellings
- Closest related behavior: `ReactionType.Delete` / `HandleDelete`
- How `Reaction` entities are loaded, registered (`EnterMap`), left (`LeaveMap`), and cleaned up
- Whether any path “deletes” a reaction object or reaction COID from server state
- Nested child lists (`ReactionTemplate.Reactions`)
- Packet/network path used when a Delete-type reaction fires
- Engineering quality and crash risks of the related path
- Evidence of AI-style stubs / incomplete delete behavior

### Out of scope

- Inventing a design for a missing `DeleteReaction` system
- Full re-audit of all reaction types
- Full trigger system (see `docs/topic-extractions/server-triggers.md`)
- Client binary reverse-engineering beyond what already exists in repo docs

### Explicit disambiguation

Three different ideas share “delete” wording and must not be conflated:

1. **`DeleteReaction` as a named packet/opcode/type** — **absent** from this codebase.
2. **`ReactionType.Delete = 3`** — a **reaction behavior** that deletes *other map objects* (or the activator), not a “delete the reaction definition” API.
3. **Removing a `Reaction` entity from `SectorMap.Reactions`** — generic leave-map cleanup; not specific to type Delete, and not what `HandleDelete` primarily does unless the object COID being removed *is* a reaction instance.

---

## Relevant Files

### Production (primary)

| Absolute path | Role |
|---|---|
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Reaction.cs` | `ReactionType` enum (`Delete = 3`), `TriggerIfPossible` switch, `HandleDelete` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\EntityTemplates\ReactionTemplate.cs` | Map template fields: `Objects`, nested `Reactions`, `ActOnActivator`, load from `.fam` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Map\SectorMap.cs` | `Reactions` / `Objects` / `Triggers` dicts; `EnterMap` / `LeaveMap`; `TriggerReactionsInternal` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\ClonedObjectBase.cs` | `SetMap` → `LeaveMap` / `EnterMap` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\EntityTemplates\ObjectTemplate.cs` | Allocates `ReactionTemplate` for `CloneBaseObjectType.Reaction` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Constants\ClonebaseObjectType.cs` | `Reaction = 58` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Constants\GameOpcode.cs` | `DestroyObject = 0x2020`, `LogicStateChange = 0x206B`, `GroupReactionCall = 0x206C` — **no DeleteReaction opcode** |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Packets\Sector\GroupReactionCallPacket.cs` | S2C wrapper used for all fired reactions |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Packets\Sector\LogicStateChangePacket.cs` | Nested entry: reaction COID + activator TFID |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Packets\Sector\DestroyObjectPacket.cs` | Generic destroy S2C; **not used by `HandleDelete`** |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Managers\TriggerManager.cs` | Edge-enter fire path that eventually calls `TriggerReactions` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Vehicle.cs` | Calls `CheckTriggersFor` after movement (with try/catch) |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Creature.cs` | Contrast: death path does `SetMap(null)` **and** broadcasts `DestroyObjectPacket` |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\TNL\TNLConnection.cs` | Comment: triggers moved off `PrepareWritePacket` to avoid re-entrancy with TransferMap / Delete |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\TNL\TNLConnection.Sector.cs` | Item pickup uses `SetMap(null)` + `DestroyObjectPacket` (another contrast path) |

### Tests (related; none target Delete)

| Absolute path | Role |
|---|---|
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game.Tests\Map\SectorMapTriggerTests.cs` | GroupReactionCall send for Activate-type reactions only |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game.Tests\Managers\TriggerManagerTests.cs` | Trigger latch / leave-map latch clear; uses Activate reactions |
| `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game.Tests\Managers\RespawnManagerTests.cs` | MarkRepairStation reactions only |

### Docs (mentions only)

| Absolute path | Relevance |
|---|---|
| `C:\Users\josh\Documents\GitHub\AutoCore\docs\topic-extractions\server-triggers.md` | Lists Delete among “implemented server-ish” reaction types; notes transfer/delete side-effects on write path historically |
| `C:\Users\josh\Documents\GitHub\AutoCore\docs\codeAudit.md` | Ghidra note `CVOGReaction_Dispatch` includes delete among reaction types; **no `CVOGReaction_Delete` function named** |
| `C:\Users\josh\Documents\GitHub\AutoCore\Documentation\PACKET STRUCTURES.md` | `DestroyObject` notes; unrelated inventory `bDelete` field |
| `C:\Users\josh\Documents\GitHub\AutoCore\Documentation\MISSION_DIALOG_CLIENT_ANALYSIS.md` | GroupReactionCall / 0x206C analysis (generic reaction client path) |

### Search results (exhaustive name search)

Patterns searched (case-insensitive / literal as appropriate) across the whole repo:

| Pattern | Hits relevant to topic |
|---|---|
| `DeleteReaction` | **0** |
| `DelReaction` | **0** |
| `CVOGReaction_Delete` | **0** |
| `Reaction_Delete` | **0** |
| `delete reaction` / `"Delete reaction"` | **Only** debug log strings in `Reaction.HandleDelete` + mentions in `server-triggers.md` |
| `ReactionType.Delete` | `Reaction.cs` case + handler only |
| `HandleDelete` | `Reaction.cs` only |
| `Reactions.Remove` | `SectorMap.LeaveMap` only |

**Finding:** Absence of a named `DeleteReaction` system is a **valid, complete** finding for that spelling. Related behavior lives under **`ReactionType.Delete`**.

---

## Packet / Network Structures

### No dedicated DeleteReaction packet

`GameOpcode` has no entry for delete-reaction. Relevant opcodes that *are* used when any reaction (including Delete type) succeeds:

| Opcode | Name | Role for Delete-type reactions |
|---|---|---|
| `0x206C` | `GroupReactionCall` | Outer S2C packet carrying one or more logic-state entries |
| `0x206B` | `LogicStateChange` | Nested conceptual type; written **inside** GroupReactionCall bit-stream, not as a standalone packet in the fire path |
| `0x2020` | `DestroyObject` | **Not** sent by `HandleDelete` (used by creature death, item pickup remove, etc.) |

### What the client receives when Delete fires

On successful `TriggerIfPossible` (including type Delete), `SectorMap.TriggerReactionsInternal` always builds:

```text
GroupReactionCallPacket
  └── LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, singleClientOnly: false)
```

Evidence (`SectorMap.cs`): for each reaction that returns true from `TriggerIfPossible`, a `LogicStateChangePacket` is added to the client `GroupReactionCallPacket`, then sent via `ResolveCharacter(activator)?.OwningConnection?.SendGamePacket`.

Wire format for `0x206C` (from `GroupReactionCallPacket.Write` + comments):

- Bit-packed `BitStream`
- `count` (8 bits)
- Per entry:
  - `entryType` (8 bits) — `LogicStateChangeType.Reaction = 0` or `Variable = 1`
  - If Reaction: reaction COID (19 bits), activator COID (u64), activator Global flag, SingleClientOnly flag

**Implication:** The client is expected to look up the reaction COID in clonebase/map data, see type Delete, and apply client-side object removal visuals. The server does **not** tell the client “destroy TFID X” via `DestroyObject` for this path.

### Contrast: actual server object removal packets

| Path | Map remove | Client destroy notify |
|---|---|---|
| `Reaction.HandleDelete` | `obj.SetMap(null)` | Only generic GroupReactionCall for the **reaction COID** |
| `Creature` death | `SetMap(null)` | Broadcasts `DestroyObjectPacket` to characters on map |
| Item pickup | `item.SetMap(null)` | `DestroyObjectPacket` to picking connection |

### Nested child reactions (not delete)

`ReactionTemplate.Reactions` is a `List<long>` of **child reaction COIDs** loaded from the map file. After a parent reaction succeeds, those lists are processed recursively (`TriggerReactionsInternal`, max depth 10). This is **not** a “list of deletes” and is **not** named DeleteReaction.

---

## Server-Side Flow

### A. How Reaction entities enter the world

1. Map load: `SectorMap` constructor → `InitializeLocalObjects`.
2. For each template in `MapData.Templates`:
   - `template.Value.Create()` — for reactions, `ReactionTemplate.Create()` → `new Reaction(this)`.
   - `SetCoid`, faction, layer, then `obj.SetMap(this)`.
3. `ClonedObjectBase.SetMap(map)` when map is non-null calls `map.EnterMap(this)`.
4. `EnterMap`:
   - If `Reaction` → `Reactions.Add(reaction.ObjectId, reaction)`
   - Always → `Objects.Add(clonedObject.ObjectId, clonedObject)`
5. Templates come from map assets; type selection uses `CloneBaseObjectType.Reaction` (`ObjectTemplate.AllocateTemplateFromCBID`).

Reactions are **local map objects** (non-global TFIDs in the fire lookup: `!o.Key.Global`).

### B. How a Delete-type reaction fires

1. Vehicle movement → `Vehicle.HandleMovement` → `TriggerManager.CheckTriggersFor(vehicle)` (try/catch; not during TNL `PrepareWritePacket` — comment explicitly mentions TransferMap / **Delete** re-entrancy).
2. Trigger edge-enter / timing / activation count → `map.TriggerReactions(activator, trigger.Template.Reactions)`.
3. `TriggerReactionsInternal`:
   - Resolve each COID to a live `Reaction` on the map (`Objects` first-or-default matching Coid, non-global).
   - Call `reaction.TriggerIfPossible(activator)`.
4. For `Template.ReactionType == ReactionType.Delete` → `HandleDelete(activator)`.

### C. `HandleDelete` behavior (closest related behavior)

Source: `Reaction.HandleDelete` in `Reaction.cs`.

```text
HandleDelete(activator):
  map = activator.Map
  if map == null:
    log "Activator has no map"
    return true   // still counts as "triggered" for client packet

  if Template.ActOnActivator:
    log "Removing activator {coid} from map"
    activator.SetMap(null)     // LeaveMap on activator only
  else:
    for each objectCoid in Template.Objects:
      obj = map.GetObjectByCoid(objectCoid)  // Objects dict only, ignore Global flag
      if obj != null:
        log "Removing object {coid} from map"
        obj.SetMap(null)
      else:
        log "Object not found on server (client-side only)"
  return true  // always true
```

Notes from code evidence only:

- Targets are either the **activator** or COIDs in **`ReactionTemplate.Objects`**, not the nested `Reactions` list.
- Nested `Template.Reactions` are still queued by `TriggerReactionsInternal` **after** a successful trigger (Delete returns true), so child reactions can still run even if parent deleted the activator’s map membership — subsequent handlers that need `activator.Map` may see `null`.
- Always returns `true`, so GroupReactionCall entry is still emitted when lookup of the reaction entity itself succeeded.

### D. Map registry removal (generic LeaveMap)

`SetMap(null)`:

```text
if Map != null → Map.LeaveMap(this)
Map = null
```

`LeaveMap` for a `Reaction` instance:

- `Reactions.Remove(reaction.ObjectId)`
- `Objects.Remove`
- `TriggerManager.ClearTriggersFor(objectCoid)` for that entity’s COID

`LeaveMap` for a `Trigger` also clears latches for that trigger COID.

**When does a Reaction entity itself leave `SectorMap.Reactions`?**

- Only if something calls `SetMap(null)` **on that Reaction instance** (or moves it to another map).
- `HandleDelete` does **not** remove the firing reaction from the map.
- A Delete reaction could remove another object whose COID happens to be a reaction **if** that COID is listed in `Template.Objects` and exists in `Objects`.

### E. Create vs Delete pairing

| Type | Enum | Server handling |
|---|---|---|
| `Create` | 2 | Log “not yet fully implemented”; return true |
| `Delete` | 3 | `HandleDelete` as above |

Create is a stub; Delete is partially implemented (map registry only).

### F. What is *not* done on Delete

From code inspection of `HandleDelete` and call graph:

- No `DestroyObjectPacket`
- No `ClearGhost` / ghost unscope
- No `ObjectManager` unregister (that path is character-session oriented)
- No DB / persistence write
- No special handling if target is `Character` / `Vehicle` / player-owned entity
- No removal of reaction definition from `MapData.Templates` (templates remain; only live instances leave `Objects`/`Reactions`)
- No dedicated client opcode for “delete reaction”

---

## State and Persistence

### Runtime state changed by Delete-type reaction

| State | Location | Change |
|---|---|---|
| Map membership | `SectorMap.Objects` | Target removed via `LeaveMap` |
| Reaction registry | `SectorMap.Reactions` | Only if removed object is a `Reaction` |
| Trigger registry | `SectorMap.Triggers` | Only if removed object is a `Trigger` |
| Trigger latches | `TriggerManager` | Cleared for removed object’s COID (`ClearTriggersFor`) |
| Entity `Map` property | `ClonedObjectBase.Map` | Set to `null` |
| Ghost / net scope | `Ghost` | **Not** cleared by `HandleDelete` |
| Nested fire queue | in-memory list | Child reaction COIDs still processed after parent |

### Persistence

- **None** for Delete-type reaction effects.
- Map objects removed at runtime are **not** written to DB.
- On map rebuild / sector restart, `InitializeLocalObjects` recreates entities from templates again — deleted local objects can reappear unless content is designed client-side only.

### Reaction template data (static / map asset)

Loaded fields relevant to Delete:

| Field | Meaning for Delete |
|---|---|
| `ReactionType` | Must be `Delete` (3) |
| `ActOnActivator` | If true, delete activator instead of `Objects` list |
| `Objects` | COIDs of objects to remove when not ActOnActivator |
| `Reactions` | Child reactions to fire after this one (any types) |
| Conditions / DoForAllPlayers / DoForConvoy | Same as other reactions |

No separate “DeleteReaction” template type.

---

## Responsibility Boundary Review

| Concern | Owner in current code | Assessment |
|---|---|---|
| Reaction type dispatch | `Reaction.TriggerIfPossible` | Correct level for type switch |
| Delete side-effect (remove from map) | `Reaction.HandleDelete` | Domain-ish; coordinate-only would be better if delete policy grows |
| Map registry integrity | `SectorMap.EnterMap` / `LeaveMap` | Correct ownership |
| Client reaction playback | Client via GroupReactionCall + clonebase | Server only sends reaction COID |
| Authoritative destroy of ghosted entities | Incomplete for Delete | Creature death owns a fuller “remove + notify” pattern; Delete does not reuse it |
| Packet assembly / send | `SectorMap.TriggerReactionsInternal` | Keeps network out of `HandleDelete` for the reaction call — good; but also means delete has no extra S2C destroy |
| Nested reaction chaining | `SectorMap` | Correct place; depth-limited |

**Boundary smell:** `HandleDelete` mutates map membership of arbitrary objects (including potentially the player vehicle if `ActOnActivator`) while still living inside the reaction entity. There is no guardrail that delete targets are “content props only.”

---

## Engineering Concerns

1. **No tests for `HandleDelete`**
   - No test asserts ActOnActivator, Objects list, missing COID, LeaveMap of Reaction vs GraphicsObject, or interaction with GroupReactionCall.
   - Coverage of Delete path is effectively **zero** in the test suite.

2. **Incomplete server/client authority split**
   - Server removes from `Objects` if present.
   - Many map graphics are never server-instantiated as live entities; code assumes “client-side only” when missing — server state then never had them, client must delete via reaction COID alone.
   - For objects that *are* server-side and ghosted, absence of `DestroyObjectPacket` / ghost teardown can leave **client/server desync**.

3. **Asymmetry with other remove paths**
   - Creature death and item pickup: `SetMap(null)` + `DestroyObjectPacket`.
   - Delete reaction: `SetMap(null)` only.
   - Suggests incomplete port / AI partial implementation rather than intentional parity with original full stack (original client may fully own delete visuals from reaction dispatch — **not verified** beyond generic dispatch note in `codeAudit.md`).

4. **Always returns true**
   - Failure modes (no map, object missing) still return success → client still plays the reaction.
   - May be intentional for client-side objects; hides server-side no-ops.

5. **ActOnActivator on player vehicle**
   - Live path typically fires with **vehicle** as activator.
   - Delete with `ActOnActivator` calls `vehicle.SetMap(null)`:
     - Removes vehicle from `Objects`
     - Clears trigger latches for vehicle COID
     - Does **not** disconnect player, transfer map, clear character map, or send destroy
   - High gameplay risk if map data uses ActOnActivator Delete against players (evidence of data usage not present in repo).

6. **Child reactions after activator removed**
   - Parent Delete may null the activator’s map; nested handlers still run with same activator reference.
   - Handlers that need `activator.Map` degrade to “no map” logs / no-ops.

7. **`GetObjectByCoid` linear scan**
   - O(n) over all map objects per listed COID — fine for small maps; not a Delete-specific bug.

8. **`LeaveMap` throws if object not on map**
   - `SetMap` only calls LeaveMap when `Map != null` and map is changing, so normal path is safe.
   - Double-delete of same instance is avoided by Map already null.

9. **Create stub vs Delete partial**
   - Type 2 Create logs unimplemented; type 3 Delete mutates state — asymmetric for content that pairs spawn/despawn.

10. **AI / maintainability patterns (observed)**
    - Verbose string-interpolated debug logs (“Delete reaction {COID}: …”) consistent with AI-assisted handlers elsewhere in `Reaction.cs`.
    - Comments asserting “client-side only” without client verification artifacts for Delete specifically.
    - No dedicated type/packet invented under the name DeleteReaction (good — no fake opcode), but incomplete lifecycle (no destroy packet / no tests) matches partial AI fill-in of the enum case.

11. **No fake `DeleteReaction` dead code**
    - Search found **no** stub class, unused opcode, or unreachable method named DeleteReaction.
    - Risk is **naming confusion**, not a dead API to delete.

---

## Crash / Stability Risks

| Risk | Evidence | Severity |
|---|---|---|
| Player vehicle removed from map mid-session via ActOnActivator Delete | `HandleDelete` → `activator.SetMap(null)` with vehicle activators | **High** if content uses it |
| Nested reaction / subsequent logic after activator left map | Delete returns true; children still fire; map null | Medium |
| Dictionary mutation during trigger iteration | Mitigated: `TriggerManager` snapshots `map.Triggers.Values.ToList()` | Low (mitigated) |
| Exception during delete while handling movement | `Vehicle.HandleMovement` wraps `CheckTriggersFor` in try/catch; comment on PrepareWritePacket mentions Delete re-entrancy | Medium residual (logged, not rethrown) |
| `LeaveMap` InvalidOperationException if inconsistent Map/Objects state | Would require Map non-null but object already removed | Low if SetMap is sole entry |
| Client desync for ghosted deleted entities | No DestroyObject / ClearGhost on Delete path | Medium–High for gameplay |
| Infinite reaction chains | Depth cap 10 in `TriggerReactionsInternal` | Mitigated |
| Convoy / broadcast delete visibility | DoForAllPlayers broadcast path exists; convoy still skipped (log only) | Medium multiplayer consistency |
| Memory leak of orphaned entities | Entities with Map=null may remain referenced only if held elsewhere; map no longer holds them | Low–Medium |

---

## Comparison to Expected Behavior

Sources limited to **this repo’s code and docs** (no new client RE performed for this report).

| Expected (inferred from structure / docs) | Fork behavior | Difference |
|---|---|---|
| Named server API / packet `DeleteReaction` | **Does not exist** | Name is a false lead |
| Client `CVOGReaction_Delete` documented in repo | **Not present** | Only `CVOGReaction_Dispatch` noted in `codeAudit.md` as switching reaction types including delete |
| Reaction type 3 deletes configured objects | **Implemented** via `SetMap(null)` | Matches a minimal server-side interpretation |
| Client informed of reaction fire | GroupReactionCall with reaction COID | Matches generic reaction model |
| Server authoritative destroy of live entities | Partial: registry only | Missing destroy/ghost cleanup used elsewhere |
| Delete of reaction *definition* or permanent map edit | Not implemented (and no evidence it should be) | Map templates unchanged |
| Nested `Reactions` mean “deletes to apply” | **No** — they are child reaction COIDs | Naming confusion if someone calls them “delete reactions” |

**Honest status:**  
`DeleteReaction` as a named system is **absent**.  
`ReactionType.Delete` is a **real, partially implemented** reaction behavior.  
Whether the original game relied purely on client clonebase dispatch for visuals is **suggested** by the GroupReactionCall design and G-3 audit note, but **`CVOGReaction_Delete` is not documented in this fork**.

---

## Questions for the User

1. Was “DeleteReaction” meant as **`ReactionType.Delete` (type 3)** only, or did you expect a separate packet/system from another fork/doc?
2. Do production maps use **ActOnActivator** on Delete-type reactions against players/vehicles?
3. Should server Delete of **ghosted** entities also send **`DestroyObject` (0x2020)** like creature death, or is client reaction dispatch considered sufficient?
4. Is there an external Ghidra / client dump naming `CVOGReaction_Delete` that should be imported into `docs/` for a follow-up RE pass?
5. Should deleting a **Trigger** via Objects list also guarantee latch cleanup beyond `LeaveMap`’s existing `ClearTrigger` (it already does for trigger entities leaving the map)?
6. For content that lists client-only graphics COIDs, is server “not found” logging noise acceptable, or should those COIDs never be treated as server authority?

---

## Recommended Follow-Up Fix Issues

These are **recommended issues only** — **not implemented** by this audit.

1. **Clarify naming in docs/code comments**  
   - Explicitly document: there is no `DeleteReaction` type; use `ReactionType.Delete` / `HandleDelete`.  
   - Avoid introducing a type named DeleteReaction without protocol evidence.

2. **Add TDD coverage for `HandleDelete`** (per `Agents.md`)  
   - ActOnActivator removes activator from `Objects` and sets `Map == null`.  
   - Objects list removes each found COID.  
   - Missing COID does not throw; still returns true.  
   - Removing a `Reaction` target clears `SectorMap.Reactions`.  
   - Removing a `Trigger` target clears `Triggers` + latches.  
   - GroupReactionCall still sent for Delete type.

3. **Decide and implement client notify policy for server-owned deletes**  
   - Either: after `SetMap(null)`, broadcast `DestroyObjectPacket` for ghosted/server-owned targets (align with Creature/item).  
   - Or: document intentional reliance on GroupReactionCall + client clonebase and ensure ghosts are not created for pure content props.

4. **Guard ActOnActivator Delete against player entities**  
   - Reject or special-case Character/Vehicle activators unless map transfer / death systems own that flow.

5. **Ghost lifecycle on leave-map**  
   - On `SetMap(null)` for ghosted entities, ensure unscope / `ClearGhost` consistency so deleted objects do not remain in net scope.

6. **Child reaction ordering when parent Delete nulls activator.Map**  
   - Snapshot map reference before delete, or define order (children first vs parent first) with tests.

7. **Optional client RE ticket**  
   - Locate client handler for reaction type 3 (possibly under `CVOGReaction_Dispatch` switch, not necessarily `CVOGReaction_Delete`).  
   - Document whether server DestroyObject is redundant, required, or harmful.

8. **Do not implement a new opcode named DeleteReaction** without wire evidence from the client binary.

---

## Appendix: Code anchors (absolute)

### Enum value

- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Reaction.cs` — `ReactionType.Delete = 3` (line ~16)

### Dispatch

- Same file — `case ReactionType.Delete: return HandleDelete(activator);`

### Handler

- Same file — `private bool HandleDelete(ClonedObjectBase activator)` (~lines 295–329)

### Registry

- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Map\SectorMap.cs` — `EnterMap` / `LeaveMap` / `TriggerReactionsInternal`

### SetMap bridge

- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\ClonedObjectBase.cs` — `SetMap`

### Nested children

- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\EntityTemplates\ReactionTemplate.cs` — `List<long> Reactions`

### Explicit absence

- No matches for identifier `DeleteReaction` in `src/`, `docs/`, `Documentation/`, or tests at time of audit.

---

*Report generated as a read-only topic extraction. No production code or tests were modified.*
