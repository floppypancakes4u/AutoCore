# Topic Extraction: Server Triggers (handling, per-player state, packets, systems)

## Executive Summary

### What the fork currently does

- Map files (`.fam`) load **Trigger** and **Reaction** templates into each `SectorMap`. Triggers are registered in `SectorMap.Triggers`; reactions in `SectorMap.Reactions`.
- On every TNL **prepare-write** for a connected sector client that is ghosting and has a vehicle on a map, the server calls `TriggerManager.CheckTriggersFor(CurrentCharacter.CurrentVehicle)`.
- `TriggerManager` keeps a **runtime-only** edge-enter latch: key `(ObjectCoid, TriggerCoid)` in a process-global `ConcurrentDictionary`. On enter (`CanTrigger` becomes true and not latched), it fires the trigger’s reaction list once; on leave it clears the latch so re-entry can fire again.
- Firing goes to `SectorMap.TriggerReactions` → depth-limited nested processing → per-reaction `Reaction.TriggerIfPossible` (server domain) → builds nested `LogicStateChangePacket` entries inside `GroupReactionCallPacket` (opcode **0x206C**).
- **Intended** client notify: send `GroupReactionCallPacket` to the activator’s owning connection.
- **Actual** client notify for the live path: **broken**. Activator is the **vehicle**; send uses only `activator.GetAsCharacter()`, which is null for vehicles. Server-side reaction handlers that resolve character via `GetAsCharacter() ?? GetSuperCharacter(false)` still run (e.g. MarkRepairStation, TransferMap).

### Soundness assessment

**Fragile / incomplete.** Core edge-detect is a clear, small pattern and matches notes in `docs/codeAudit.md`. Much map-authored trigger metadata is loaded but ignored. Condition evaluation is stubbed. Client delivery for the primary activator type is wrong. Broadcast paths are stubbed. Many reaction types log and return true without server authority. No unit tests target `TriggerManager` itself (only reaction side-effects such as MarkRepairStation).

### Biggest risks

1. Client never receives reaction/mission-dialog packets while driving (vehicle activator).
2. Side-effectful reactions during network write preparation (map transfer, object delete).
3. Conditions always evaluate with zeroed values → wrong fire/suppress behavior.
4. Trigger latches not character/account persistent; keyed by object COID only (vehicle on the live path).
5. O(all map triggers) scan every prepare-write per player.

---

## Scope

### In scope

- Server volume triggers: load, register, range/target checks, edge state, fire path.
- `TriggerManager` persistence model (runtime latch).
- `SectorMap` reaction chaining and S2C packaging.
- Packets: `GroupReactionCall` (0x206C), nested `LogicStateChange` reaction entries; related opcodes `LogicStateChange` (0x206B), `MissionDialogResponse` (0x206D).
- Map-level trigger COIDs (`PerPlayerLoadTrigger`, etc.) and object `TriggerEvents` as loaded-but-unused data.
- `ResetTrigger` reaction and cleanup on leave-map / disconnect.
- Reaction types invoked from triggers (as handlers exist today).
- Engineering quality and AI-smell patterns in this subsystem.

### Out of scope

- Full mission quest DB model beyond reaction stubs.
- Combat, loot, exploration except where reactions call them.
- Auth/global login except sector map placement that enables trigger scans.
- Rewriting or fixing the system (extraction only).
- Deep Ghidra of client trigger collision (only existing docs used for comparison).

---

## Relevant Files

| File | Purpose | Relevant symbols / methods |
|---|---|---|
| `src/AutoCore.Game/Managers/TriggerManager.cs` | Edge-enter latch; only runtime trigger “state” store | `CheckTriggersFor`, `ClearTriggersFor`, `ClearTrigger`, `ResetTriggerFor`, `_activeTriggers` |
| `src/AutoCore.Game/Entities/Trigger.cs` | Trigger entity + condition types | `TriggerTargetType`, `CanTrigger`, `TriggerIfPossible`, `TriggerConditional.Check` |
| `src/AutoCore.Game/EntityTemplates/TriggerTemplate.cs` | Deserialize trigger from map | `Read`, `Create`, fields: `RetriggerDelay`, `ActivateDelay`, `ActivationCount`, `TargetType`, `Reactions`, `Conditions`, … |
| `src/AutoCore.Game/Entities/Reaction.cs` | Reaction type enum + server handlers | `TriggerIfPossible`, `HandleResetTrigger`, `HandleTransferMap`, `HandleMarkRepairStation`, … |
| `src/AutoCore.Game/EntityTemplates/ReactionTemplate.cs` | Deserialize reactions; child reaction lists; dialog choice `TriggerCoid` | `Read`, `Reactions`, `Objects`, `Conditions`, `ReactionTextChoice.TriggerCoid` |
| `src/AutoCore.Game/Map/SectorMap.cs` | Map registries; fire reactions; send packets | `Triggers`, `Reactions`, `EnterMap`/`LeaveMap`, `TriggerReactions` / `TriggerReactionsInternal` |
| `src/AutoCore.Game/Map/MapData.cs` | Map-level load/kill/team trigger COIDs; music triggers list | `PerPlayerLoadTrigger`, `CreatorLoadTrigger`, `OnKillTrigger`, `LastTeamTrigger`, `MusicTriggers` |
| `src/AutoCore.Game/EntityTemplates/ObjectTemplate.cs` | Three `TriggerEvents` longs per object template | `TriggerEvents`, `ReadTriggerEvents`, `AllocateTemplateFromCBID` (Trigger → `TriggerTemplate`) |
| `src/AutoCore.Game/EntityTemplates/GraphicsObjectTemplate.cs` | Calls `ReadTriggerEvents` during graphics unserialize | `Read` |
| `src/AutoCore.Game/EntityTemplates/SpawnPointTemplate.cs` | Also reads trigger events | `Read` → `ReadTriggerEvents` |
| `src/AutoCore.Game/TNL/TNLConnection.cs` | Periodic check hook; disconnect leaves map | `PrepareWritePacket`, `OnConnectionTerminated` |
| `src/AutoCore.Game/TNL/TNLConnection.Sector.cs` | Place char/vehicle on map; mission dialog response handler | `HandleTransferFromGlobalPacket` (`SetMap`), `HandleMissionDialogResponse` |
| `src/AutoCore.Game/Packets/Sector/GroupReactionCallPacket.cs` | S2C 0x206C bit-packed payload | `Write`, `AddPacket` (max 255 entries) |
| `src/AutoCore.Game/Packets/Sector/LogicStateChangePacket.cs` | Nested entry type + standalone 0x206B byte layout | `LogicStateChangeType`, constructors, `Write` |
| `src/AutoCore.Game/Packets/Sector/MissionDialogPacket.cs` | Obsolete wrong dialog packet | Obsolete, throws |
| `src/AutoCore.Game/Packets/Sector/MissionDialogResponsePacket.cs` | C2S 0x206D best-effort | `Read` |
| `src/AutoCore.Game/Constants/GameOpcode.cs` | Opcode IDs and comments | `LogicStateChange=0x206B`, `GroupReactionCall=0x206C`, `MissionDialogResponse=0x206D` |
| `src/AutoCore.Game/Constants/ClonebaseObjectType.cs` | Clone type for triggers | `Trigger = 56` |
| `src/AutoCore.Game/Entities/Character.cs` | Vehicle ownership for `GetSuperCharacter` | `SetOwner` on load vehicle |
| `src/AutoCore.Game/Entities/ClonedObjectBase.cs` | Map enter/leave; character resolve defaults | `SetMap`, `GetAsCharacter`, `GetSuperCharacter` |
| `src/AutoCore.Game/Entities/Vehicle.cs` | Position updates used for range checks | `HandleMovement` |
| `src/AutoCore.Game/Managers/MapManager.cs` | TransferMap reaction destination | `TransferCharacterToMap` |
| `src/AutoCore.Game/TNL/Ghost/GhostVehicle.cs` | Ghost flag for `CoidOnUseTrigger` always false | write path stubs |
| `src/AutoCore.Game/TNL/Ghost/GhostCreature.cs` | Same stub | write path stubs |
| `src/AutoCore.Game.Tests/Managers/RespawnManagerTests.cs` | Creates triggers; exercises MarkRepairStation via reactions | `CreateTrigger`, many `TriggerIfPossible` tests |
| `docs/codeAudit.md` | Prior audit notes on TriggerManager / depth | tables referencing trigger system |
| `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md` | Client expects 0x206C for mission dialog path | opcode mapping |
| `Documentation/RESPAWN_SYSTEM.md` | Repair pad = drive over trigger → MarkRepairStation | flow docs |

---

## Packet / Network Structures

Triggers are **not** driven by a client “I entered trigger” packet. Client movement updates vehicle pose; server probes triggers on prepare-write.

| Packet / opcode | Direction | Fields | Serialization order | Sender | Receiver | Handler | Notes / risks |
|---|---|---|---|---|---|---|---|
| *(none)* — movement enables triggers | C→S | Vehicle pose etc. | `VehicleMovedPacket` | Client | Server | `TNLConnection.HandleVehicleMovedPacket` → `Vehicle.HandleMovement` | Updates `Position` used by `CanTrigger` distance |
| **GroupReactionCall** `0x206C` | S→C | count + entries | **BitStream**: `count` u8; per entry: `type` u8; if Variable: varId u16 + f32; if Reaction: reactionCoid **u19** + activator Coid **u64** + Global flag + SingleClientOnly flag | `SectorMap.TriggerReactionsInternal` → `SendGamePacket` | Client (mission dialog / reaction apply) | Client (not in this repo) | Built whenever reactions succeed; **send fails for vehicle activators** (see flow). Cap 255 entries per packet. |
| **LogicStateChange** nested (type Reaction) | inside 0x206C | `ReactionCoid`, `Activator` TFID, `SingleClientOnly` | Bit-packed as above (not the standalone Write) | Nested only | Client | Client reaction apply | Constructed as `new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false)` — always `SingleClientOnly=false`. Type defaults to `Reaction=0`. |
| **LogicStateChange** standalone `0x206B` | S→C (defined) | byte Type + pad3; Reaction: coid i64 + TFID + bool + pad7; or Variable: id + float + pad24 | Byte-aligned `LogicStateChangePacket.Write` | **No production send site found** | — | — | Layout exists; live path only nests into GroupReactionCall. |
| **MissionDialogResponse** `0x206D` | C→S | MissionId i32, MixedVar i64, MissionGiver TFID | Sequential Read | Client | Server | `HandleMissionDialogResponse` | Logged; not fully reverse-engineered per comments. Not a volume-trigger packet. |
| **MissionDialogPacket** (obsolete) | — | — | throws | — | — | — | Correct path is GroupReactionCall 0x206C. |
| GhostVehicle / GhostCreature `CoidOnUseTrigger` | S→C ghost | flag always false | WriteFlag(false); optional int never written | Ghost write | Client | — | Use-triggers not implemented server-side. |

### GroupReactionCall entry construction (server)

```text
For each successful reaction in batch:
  LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, singleClientOnly: false)
  → clientPacket.AddPacket(...)
  if DoForAllPlayers → also broadcastPacket (never sent; TODO)
  if DoForConvoy → also convoyPacket (never sent; TODO)
Then:
  activator.GetAsCharacter()?.OwningConnection.SendGamePacket(clientPacket)
```

---

## Server-Side Flow

### A. Load / spawn (map boot)

1. `MapManager.SetupMap` → `new SectorMap(continentId)`.
2. `SectorMap` loads `MapData` templates; `InitializeLocalObjects` creates each template via `Create()`.
3. For `CloneBaseObjectType.Trigger` → `TriggerTemplate` → `Trigger` with Position/Rotation/Scale from template.
4. `obj.SetMap(this)` → `EnterMap` → if Trigger, add to `Triggers[ObjectId]`; if Reaction, add to `Reactions`.
5. Map-level fields `PerPlayerLoadTrigger`, `CreatorLoadTrigger`, `OnKillTrigger`, `LastTeamTrigger` are **read from binary** and **never referenced for firing** in this codebase (grep: only MapData load).

### B. Player enters sector

1. `HandleTransferFromGlobalPacket`: character + `CurrentVehicle` `SetMap(map)`.
2. Vehicle is in `Objects`; trigger latches start empty for that vehicle COID.
3. Ghosting activates; movement packets update vehicle position.

### C. Periodic check (primary live entry)

1. TNL `PrepareWritePacket` (per connection, when preparing outbound traffic).
2. If `Ghosting && CurrentCharacter != null && CurrentVehicle != null && CurrentVehicle.Map != null`:
   - `TriggerManager.Instance.CheckTriggersFor(CurrentCharacter.CurrentVehicle)`.
3. For **each** `map.Triggers` entry:
   - `canTrigger = trigger.CanTrigger(vehicle)`:
     - null/map checks
     - **TargetType** filter:
       - Player character **or** vehicle with super-character → requires `TargetType == Players`
       - Vehicle without super-character → requires `Vehicles`
       - Creature → requires `Creatures`
       - Other `TargetType` values (`MapEnemies`, `List`, `SummonTemplate`, `SummonCBID`) never match these branches → effectively cannot fire from those activators as implemented
     - Distance: `DistSq(Position) > Scale*Scale` → false
     - Conditions: if any, run stub `TriggerConditional.Check` (see State section)
   - Latch key = `(vehicle.ObjectId.Coid, trigger.ObjectId.Coid)`
   - If canTrigger and not latched → set latch true → `map.TriggerReactions(vehicle, trigger.Template.Reactions)`
   - If !canTrigger and latched → remove latch (re-arm)

**Note:** Manager does **not** call `Trigger.TriggerIfPossible`; it duplicates the reaction call after `CanTrigger`. `Trigger.TriggerIfPossible` exists but is unused by the manager (would re-check CanTrigger and call same TriggerReactions).

### D. Reaction processing

1. `TriggerReactions` → `TriggerReactionsInternal(activator, reactions, depth=0)`.
2. Depth ≥ 10 → log error, stop (anti infinite chain).
3. For each reaction COID in list:
   - Lookup local non-global object with matching Coid that is `Reaction`; else log error, continue.
   - `reaction.TriggerIfPossible(activator)`:
     - Reaction conditions (same stub Check)
     - Switch on `ReactionType`:
       - Many types: return true with comment “client-side” (Activate/Deactivate/Enable/Disable, mission strings, waypoints, text, progress bars, …)
       - Implemented server-ish: Delete, Unlock/Relock ContObj (mostly log), SetActiveObjective (log), GiveMissionDialog (log only), TransferMap, MarkRepairStation, ResetTrigger
       - Create: log “not yet fully implemented”, return true
       - default: log unhandled, return true
   - On success: add LogicStateChange to client packet; queue child `reaction.Template.Reactions` for nested depth.
4. **Send:** `activator.GetAsCharacter()?.OwningConnection.SendGamePacket(clientPacket)`
   - Vehicle activator → **no send**.
   - Character activator → send (rare for this manager path).
   - If character non-null but `OwningConnection` null → **NullReferenceException** (null-conditional only covers GetAsCharacter).
5. Process child reaction lists recursively.
6. broadcast / convoy packets: constructed but **not sent** (TODO comments).

### E. ResetTrigger

1. Reaction type `ResetTrigger` (19).
2. `HandleResetTrigger`: for each COID in `Template.Objects`, or else `GenericVar1`, call `TriggerManager.ResetTriggerFor(activator.ObjectId.Coid, triggerCoid)`.
3. Clears latch for that **activator object COID** only (vehicle COID when driving), so next Check can re-fire without leaving volume.

### F. Cleanup

1. `ClonedObjectBase.SetMap(null)` → `LeaveMap` → `TriggerManager.ClearTriggersFor(objectCoid)` for **that** object’s COID.
2. Disconnect: character + vehicle `SetMap(null)` → clears latches for both COIDs.
3. `ClearTrigger(triggerCoid)` (all objects for one trigger) exists but has **no callers**.

### G. Secondary / unused paths

- `ObjectTemplate.TriggerEvents` (3× int64): loaded, never fired.
- Map music triggers: data only.
- Ghost `CoidOnUseTrigger`: always written as “none”.
- Dialog choice field `ReactionTextChoice.TriggerCoid`: loaded in template; no server handler wires choice → trigger fire (would be via client MissionDialogResponse path, incomplete).

---

## State and Persistence

### Runtime state changed

| State | Location | Keying | Meaning |
|---|---|---|---|
| Edge latch | `TriggerManager._activeTriggers` | `(ObjectCoid, TriggerCoid)` | Object is “inside” and already fired for this enter |
| Map membership | `SectorMap.Triggers` / `Reactions` / `Objects` | TFID | Live entities |
| Reaction side-effects | Character / map / ObjectManager | Varies | e.g. `Character.SetLastRepairStation`, map transfer, delete object |

**Not per-player account id:** keyed by **entity COID** (in live path, **vehicle COID**). Two characters never share a vehicle COID; if the same character changed vehicles without clearing latches, state would not transfer (vehicle leave-map clears vehicle key).

**Not per-character COID** for the latch when driving.

### Database state changed

- **Trigger latches: never written to DB.**
- No tables/entities for “player has fired trigger X”.
- Side-effects may persist if their own systems save (e.g. repair station fields on Character when character is saved elsewhere — not part of TriggerManager).

### Save timing

- None for trigger enter/leave state.
- Process restart = all latches lost (players re-enter volumes → fire again).

### Cleanup / unload

- Leave map / disconnect clears latches for that object COID.
- Trigger despawn does not call `ClearTrigger` automatically (only object leave clears by object COID; if a trigger is deleted while players are “inside”, their latch may remain until leave or ResetTrigger).

### Consistency risks

- Latch true + CanTrigger still true → no re-fire (by design).
- RetriggerDelay / ActivationCount / ActivateDelay **ignored** → design data cannot limit fire rate/count.
- Conditions stubbed → maps using conditionals may always fire or never fire incorrectly.
- Cross-map: leave clears object keys; OK for transfer. Shared local COID spaces theoretically could confuse only if latches not cleared between maps.
- Global singleton mixes all maps’ latches in one dictionary (OK if keys unique enough and cleanup runs).

---

## Responsibility Boundary Review

| Behavior | Current owner | Correct owner | Appropriate? | Direction |
|---|---|---|---|---|
| Edge enter/leave latch | `TriggerManager` singleton | TriggerManager or SectorMap/session scoped service | Mostly OK as coordinator | Keep; consider map-scoped or character-session keyed state; document vehicle vs character COID |
| Range / target filter | `Trigger.CanTrigger` | Trigger entity | Yes | Implement remaining TargetTypes + TargetList; honor DoConditionals |
| Condition evaluation | `TriggerConditional.Check` stub | Domain condition evaluator using map variables / activator stats | No — fake values | Implement real left/right sources |
| Fire reactions | `SectorMap.TriggerReactions` | SectorMap / Reaction service | Yes for map-local COID lookup | Fix packet recipient resolve |
| Reaction domain effects | `Reaction` methods | Reaction + Character/Map services | Mixed; some correct (MarkRepairStation on Character), many stubs in Reaction | Thin reaction dispatch to services; don’t pretend client-only when server authority needed |
| Client notify | `SectorMap` send | Connection/session from activator character | **Wrong resolve** | `GetCharacterFromActivator` then `OwningConnection?.SendGamePacket` |
| Periodic scan | `TNLConnection.PrepareWritePacket` | World/sim tick or movement handler | Questionable — network write path | Move to post-movement or map tick; exception boundary |
| Map load triggers | MapData fields only | Map enter / kill services | Incomplete | Wire PerPlayerLoadTrigger etc. when entering map / kills |
| Object TriggerEvents | Template data only | Entity interact / death / use | Incomplete | Fire on use/death if client model requires |

---

## Engineering Concerns

Prioritized:

1. **Critical — Client packet never sent for vehicle activators**
   - Location: `SectorMap.TriggerReactionsInternal` line using `GetAsCharacter()`
   - Problem: Live activator is vehicle; GetAsCharacter null → no 0x206C.
   - Why: Mission dialogs, client-side activate/deactivate, UI reactions fail while driving.
   - Evidence: `TNLConnection.PrepareWritePacket` passes vehicle; Character sets vehicle Owner so `GetSuperCharacter` works in Reaction handlers but not in send path.
   - Fix direction: Resolve character like `Reaction.GetCharacterFromActivator`; null-safe connection.

2. **High — Side effects during PrepareWritePacket**
   - Location: CheckTriggersFor → TransferMap / Delete
   - Problem: Map leave, ghost reset, packet floods nested inside TNL write prep; iterating `map.Triggers` while world mutates.
   - Why: Reentrancy, exceptions mid-write, hard-to-debug stalls.
   - Fix: Queue reaction work to sim tick; don’t transfer inside write prep.

3. **High — Condition evaluation is non-functional**
   - Location: `TriggerConditional.Check`
   - Problem: left/right always 0; TODO + commented Debugger.Break. OR-mode (`!AllConditionsNeeded`) returns true even if all fail.
   - Why: Conditional triggers wrong.
   - Fix: Bind IDs to map variables / activator properties; fix OR semantics (require ≥1 success).

4. **High — Map-authored timing/count flags ignored**
   - Location: `TriggerTemplate` fields vs TriggerManager
   - Problem: RetriggerDelay, ActivateDelay, ActivationCount unused.
   - Why: Infinite re-entry fire only gated by leave/reset; design data ignored.
   - Fix: Honor delays/counts in TriggerManager state.

5. **Medium — Broadcast paths stubbed**
   - Location: SectorMap DoForAllPlayers / DoForConvoy
   - Problem: Packets built, not sent.
   - Why: Multi-player shared reactions desync.
   - Fix: Broadcast to map connections / convoy members.

6. **Medium — Many reaction types “return true” without work**
   - Location: Reaction.TriggerIfPossible
   - Problem: Logs + true still emits LogicStateChange if send worked; server state may diverge.
   - Why: Fake success; clients may apply visuals without server authority.
   - Fix: Implement or don’t claim success for server-required types.

7. **Medium — O(triggers) per prepare-write per player**
   - Location: CheckTriggersFor
   - Problem: No spatial partition; scales poorly.
   - Why: CPU on busy maps.
   - Fix: Grid/partition; check on movement with distance threshold.

8. **Medium — Null OwningConnection NRE**
   - Location: send line without `?.` on OwningConnection
   - Problem: Crash if character without connection activates.
   - Fix: `?.SendGamePacket`.

9. **Low — Dead / unused APIs**
   - `ClearTrigger` unused; `Trigger.TriggerIfPossible` unused by manager; LeftObject/RightObject unused.
   - Risk: Confusion for AI/fork maintainers.
   - Fix: Use or remove with tests.

10. **Low — No TriggerManager tests**
    - Only Respawn/MarkRepairStation uses synthetic triggers.
    - Fix: Edge enter once, leave rearm, ResetTrigger, vehicle packet recipient.

---

## Crash / Stability Risks

| Risk | Evidence | Severity |
|---|---|---|
| NRE on send if character without connection | `GetAsCharacter()?.OwningConnection.SendGamePacket` | Medium |
| Exception during PrepareWritePacket (transfer/delete/reaction) | No try/catch around CheckTriggersFor | High (can break write path) |
| Dictionary iteration vs concurrent LeaveMap | `foreach map.Triggers` while reactions mutate map | Medium |
| Infinite reaction chains | Depth cap 10 + log | Mitigated |
| Memory growth of `_activeTriggers` | Cleared on leave; stuck if leave skipped | Medium if lifecycle bugs |
| Empty GroupReactionCall still serializes count 0 | Would send if character path worked | Low |
| Client desync | Missing 0x206C; missing map/convoy broadcast; client-only reactions | High for gameplay |
| Blocking I/O in trigger path | Not observed in TriggerManager itself; TransferMap sends packets synchronously | Medium |
| Race | ConcurrentDictionary for latches OK; SectorMap Dictionaries not concurrent | Medium under multi-thread assumptions |

---

## Comparison to Expected Behavior

Sources: `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md`, `Documentation/RESPAWN_SYSTEM.md`, `docs/codeAudit.md`, template fields implying original design.

| Expected (from data/docs) | Fork behavior | Difference | Risk |
|---|---|---|---|
| Client receives MissionDialog/reactions via 0x206C when server fires reaction | Packet built; not sent for vehicle activators | Client never applies | Dialogs/UI broken in field |
| Drive over repair pad → MarkRepairStation on character | Handler uses GetSuperCharacter; works server-side without 0x206C | Matches RESPAWN_SYSTEM for server station mark | OK if no client-only pad VFX required |
| Edge-enter not spam while standing in volume | Latch until leave | Matches audit “Edge enter” | OK |
| RetriggerDelay / ActivationCount | Loaded, ignored | Design data no-op | Over/under fire |
| Conditions from map variables | Stub zeros | Wrong gating | Soft/hard lock content |
| PerPlayerLoadTrigger on enter | Loaded, never fired | Missing onboarding triggers | Incomplete maps |
| DoForAllPlayers / convoy | Flags loaded | No broadcast | Multiplayer desync |
| Nested reactions depth-limited | Max 10 | Matches audit recommendation | OK |
| Reaction_Text choices with TriggerCoid | Loaded in template | No complete server choice→trigger | Incomplete dialogs |

---

## Questions for the User

1. Should trigger edge state be **per vehicle**, **per character**, or **per account**, and must it survive logout/relog or only current map session?
2. For client delivery, is 0x206C always required even when server fully applies the effect (e.g. MarkRepairStation), or only for client-visual reaction types?
3. Should `PrepareWritePacket` remain the check cadence, or should checks move to `Vehicle.HandleMovement` / a sector tick?
4. Are map `PerPlayerLoadTrigger` / `OnKillTrigger` required for your near-term content, or can they wait?
5. When `ResetTrigger` lists objects, are those always trigger COIDs (as code assumes)?

---

## Recommended Follow-Up Fix Issues

1. **TRIG-01: Fix GroupReactionCall recipient for vehicle activators**
   - Severity: Critical
   - Description: Resolve character via GetAsCharacter ?? GetSuperCharacter; null-safe OwningConnection; add regression test that send is invoked for vehicle activator.
   - TDD: Unit test SectorMap.TriggerReactions with vehicle owned by character with mock connection records packet.
   - Files: `SectorMap.cs`, new tests under `AutoCore.Game.Tests`.

2. **TRIG-02: Move trigger checks off PrepareWritePacket**
   - Severity: High
   - Description: Invoke CheckTriggersFor after movement (or map tick) with exception boundary; avoid reentrant transfer during TNL write.
   - TDD: Movement test fires enter once; write prep alone does not double-fire.
   - Files: `TNLConnection.cs`, `Vehicle.cs`, `TriggerManager.cs`.

3. **TRIG-03: Implement real TriggerConditional evaluation**
   - Severity: High
   - Description: Resolve LeftId/RightId against map Variables / activator; fix OR semantics.
   - TDD: EqualTo/NotEqual with non-zero values; AllConditionsNeeded true/false matrices.
   - Files: `Trigger.cs`, possibly MapData Variables.

4. **TRIG-04: Honor RetriggerDelay / ActivationCount / ActivateDelay**
   - Severity: Medium
   - Description: Extend latch state with timestamps and remaining counts.
   - TDD: Fake clock; assert no re-fire until delay; max activations.
   - Files: `TriggerManager.cs`, `TriggerTemplate.cs` consumers.

5. **TRIG-05: Implement DoForAllPlayers / DoForConvoy send**
   - Severity: Medium
   - Description: Broadcast GroupReactionCall to map / convoy members.
   - TDD: Two connections same map receive when flag set.
   - Files: `SectorMap.cs`, ConvoyManager.

6. **TRIG-06: Wire PerPlayerLoadTrigger on map enter**
   - Severity: Medium
   - Description: When character/vehicle enters map, fire map-level reaction COID if non-zero.
   - TDD: MapData with PerPlayerLoadTrigger invokes TriggerReactions once on enter.
   - Files: `SectorMap.cs` / `TNLConnection.Sector.cs` / `MapManager.cs`.

7. **TRIG-07: TriggerManager lifecycle tests + ClearTrigger on trigger delete**
   - Severity: Low–Medium
   - Description: Cover enter/leave/reset/delete; call ClearTrigger when trigger leaves map.
   - TDD: Dedicated TriggerManagerTests.
   - Files: `TriggerManager.cs`, `SectorMap.LeaveMap`, tests.

8. **TRIG-08: Inventory of reaction stubs that must not return success**
   - Severity: Medium (correctness)
   - Description: For server-authoritative types still stubbed, return false or implement; avoid client false confidence.
   - TDD: Per reaction type expected server mutation.
   - Files: `Reaction.cs`.

---

## Appendix: Template fields loaded but unused by runtime trigger logic

From `TriggerTemplate.Read`: Name, RetriggerDelay, ActivateDelay, ActivationCount, DoCollision, DoConditionals, ShowMapTransitionDecals, DoOnActivate, ApplyToAllColliders, TargetList, Color, TriggerId (loaded; only TargetType, Scale/Position, Reactions, Conditions/AllConditionsNeeded used in CanTrigger).

From `MapData`: PerPlayerLoadTrigger, CreatorLoadTrigger, OnKillTrigger, LastTeamTrigger, MusicTriggers — not fired by TriggerManager.

From `ObjectTemplate`: TriggerEvents[3] — not fired.
