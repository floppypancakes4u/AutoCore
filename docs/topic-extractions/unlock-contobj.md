# Topic Extraction: UnlockContObj

## Executive Summary

`ReactionType.UnlockContObj` (value **32**) is a map-authored **reaction type** dispatched from `Reaction.TriggerIfPossible` into `HandleUnlockContObj`. In this fork the handler is a **log-only stub**: it optionally resolves a mission via `AssetManager.GetMissionByObjectiveId(Template.GenericVar1)`, logs template fields and character/object COIDs, and **always returns `true`** with **no server state mutation and no persistence**.

Downstream client notification is **not UnlockContObj-specific**. On any successful `TriggerIfPossible`, `SectorMap.TriggerReactionsInternal` nests a `LogicStateChangePacket` (type Reaction) into a `GroupReactionCallPacket` (opcode **0x206C**) and sends it to the activator’s owning character. Comments in `HandleUnlockContObj` assert that the client applies visual unlock from that path; this repo has **no Ghidra notes** for reaction type 32 itself—only general `CVOGReaction_Dispatch` architecture notes and mission-dialog packet analysis.

Companion type **`RelockContObj` (70)** is the inverse name-pair in the enum and is an even thinner log stub (no mission lookup, no map object resolve). It clarifies that lock/unlock are intended as a **paired reaction family**, but neither side implements authority.

**Name collision (out of system):** map-reveal **`UnlockRegion` (0x205B)** and client helpers `CVOGReaction_UnlockContinentObject` / `CVOGReaction_RelockContinentObject` belong to **exploration fog**, not reaction type 32/70. They share “unlock / Cont(inent)Obj” naming only.

**Soundness:** Incomplete / non-authoritative. Client may still react if 0x206C is delivered and clonebase defines the reaction; server cannot enforce, persist, or re-sync unlock state.

**Biggest risks for this topic:**

1. Fake success (`return true`) with no character/map unlock state → progress desync and no relog restore.
2. Relock/Unlock asymmetry and unused template gates (`ObjectiveIDCheck`, `ActOnActivator`) leave design data dead.
3. Mission lookup is O(all missions × objectives) and **discarded** after debug log.
4. No unit tests for Unlock/Relock ContObj.

---

## Scope

### In scope

| Item | Notes |
|------|--------|
| `ReactionType.UnlockContObj = 32` | Enum + switch dispatch |
| `HandleUnlockContObj` | Full handler body |
| `RelockContObj = 70` / `HandleRelockContObj` | Inverse pair only |
| Template fields | `GenericVar1/2/3`, `Objects`, `ActOnActivator`, `ObjectiveIDCheck` as used (or not) by unlock |
| Mission read path | `GetMissionByObjectiveId`, `Mission` / `MissionObjective` load |
| Client notify | `GroupReactionCall` + nested `LogicStateChange` as used for all successful reactions including unlock |
| Name disambiguation | vs map-reveal `UnlockRegion` / `UnlockContinentObject` |

### Out of scope

- Full mission/quest DB, rewards, give/complete/fail mission handlers (except adjacent stubs that share mission lookup)
- Full `TriggerManager` redesign
- Exploration fog implementation (`ExplorationManager`, bit masks, terrain TGA) beyond name contrast
- Production code changes (extraction only)

---

## Relevant Files

| File | Purpose | Relevant symbols |
|------|---------|------------------|
| `src/AutoCore.Game/Entities/Reaction.cs` | Enum values; dispatch; unlock/relock handlers; character resolve helper | `ReactionType.UnlockContObj`, `RelockContObj`, `TriggerIfPossible`, `CanTrigger`, `HandleUnlockContObj`, `HandleRelockContObj`, `GetCharacterFromActivator` |
| `src/AutoCore.Game/EntityTemplates/ReactionTemplate.cs` | Map-deserialized reaction fields | `ActOnActivator`, `ObjectiveIDCheck`, `GenericVar1`, `GenericVar2`, `GenericVar3`, `Objects`, `Reactions`, `DoForAllPlayers`, `DoForConvoy`, `Read` |
| `src/AutoCore.Game/Map/SectorMap.cs` | Fires reactions; builds/sends S2C packets | `TriggerReactions`, `TriggerReactionsInternal`, `ResolveCharacter`, `SendReactionPacket`, `SendBroadcastToMap`, `GetObjectByCoid` |
| `src/AutoCore.Game/Managers/AssetManager.cs` | Mission template lookup by objective id | `GetMissionByObjectiveId`, `GetMission` |
| `src/AutoCore.Game/Managers/Asset/WADLoader.cs` | Loads mission dictionary from WAD | `Missions`, `LoadInternal` (mission loop) |
| `src/AutoCore.Game/Mission/Mission.cs` | Mission template + objectives dictionary | `Mission.Read`, `Objectives`, `Id`, `Name`, `Title` |
| `src/AutoCore.Game/Mission/MissionObjective.cs` | Per-objective template fields | `ObjectiveId`, `Sequence`, `ContinentObject`, `ReadNew` |
| `src/AutoCore.Game/Packets/Sector/GroupReactionCallPacket.cs` | S2C 0x206C bit-packed batch | `Opcode`, `AddPacket`, `Write`, `Count` |
| `src/AutoCore.Game/Packets/Sector/LogicStateChangePacket.cs` | Nested (and standalone) reaction/variable entry | `LogicStateChangeType`, constructors, `Write` |
| `src/AutoCore.Game/Constants/GameOpcode.cs` | Opcode IDs / comments | `LogicStateChange = 0x206B`, `GroupReactionCall = 0x206C`, `UnlockRegion = 0x205B` |
| `src/AutoCore.Game/Entities/Trigger.cs` | Volume enter → reaction list (upstream) | `TriggerIfPossible` → `Map.TriggerReactions` |
| `src/AutoCore.Game/Managers/TriggerManager.cs` | Edge-enter latch calling map reactions | `CheckTriggersFor` |
| `src/AutoCore.Game/Packets/Sector/UnlockRegionPacket.cs` | **Contrast only** — exploration, not ContObj reaction | `UnlockRegion`, `UnlockFlag`, `ExploredBits` |
| `src/AutoCore.Game/Managers/ExplorationManager.cs` | **Contrast only** — sends UnlockRegion | `SendUnlockRegion`, `TryReveal` |
| `src/AutoCore.Game.Tests/Map/SectorMapTriggerTests.cs` | GroupReactionCall send for any successful reaction (Activate sample) | `TriggerReactions_VehicleActivator_SendsGroupReactionCallToOwningCharacter` |
| `docs/topic-extractions/server-triggers.md` | Prior trigger pipeline extraction; notes Unlock/Relock as mostly log | Reaction processing section |
| `docs/codeAudit.md` | Mission progress missing; Ghidra dispatch note | Responsibility table; G-3 `CVOGReaction_Dispatch` |
| `Documentation/MAP_REVEAL.md` | Name collision: client `CVOGReaction_UnlockContinentObject` on UnlockRegion | Exploration opcodes / client apply |
| `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md` | Confirms 0x206C as MissionDialog / reaction batch path | Opcode mapping, wire intent |

**Not found:** any `AutoCore.Game.Tests` coverage of `UnlockContObj` / `RelockContObj` / `HandleUnlockContObj` (grep: zero matches). No dedicated client Ghidra notes for reaction type 32 in this repo.

---

## Packet / Network Structures

UnlockContObj does **not** define its own opcode. It reuses the generic reaction notification path.

### A. Live path for UnlockContObj (when a trigger/reaction batch includes type 32)

| Packet / opcode | Direction | Role for UnlockContObj | Fields (as constructed) | Serialization | Sender | Receiver |
|-----------------|-----------|------------------------|-------------------------|---------------|--------|----------|
| **GroupReactionCall** `0x206C` | S→C | Batch container for reaction applies (client EMSG name also `EMSG_Sector_MissionDialog`) | `count` (u8) + entries | **BitStream**: per entry `type` u8; if Reaction: `reactionCoid` **u19**, activator `Coid` u64, `Global` flag, `SingleClientOnly` flag | `SectorMap.TriggerReactionsInternal` → `SendReactionPacket` / optional broadcast | Client (not in repo) |
| Nested **LogicStateChange** (type `Reaction = 0`) | inside 0x206C | Identifies which reaction template COID to apply | `ReactionCoid = reaction.ObjectId.Coid`, `Activator = activator.ObjectId`, `SingleClientOnly = false` | Bit-packed as above (not standalone `Write`) | Same | Client looks up clonebase / map reaction data by COID |
| Standalone **LogicStateChange** `0x206B` | S→C (type defined) | **Not used** by unlock path | Byte-aligned layout exists | `LogicStateChangePacket.Write` | No production send site found for unlock | — |

Construction site (`SectorMap.TriggerReactionsInternal`), after `reaction.TriggerIfPossible(activator)` succeeds:

```text
var packet = new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false);
clientPacket.AddPacket(packet);
// optional: broadcastPacket if DoForAllPlayers; convoyPacket if DoForConvoy
SendReactionPacket(ResolveCharacter(activator), clientPacket);
```

`ResolveCharacter` = `activator.GetAsCharacter() ?? activator.GetSuperCharacter(false)` (same idea as `Reaction.GetCharacterFromActivator`). Vehicle activators therefore resolve to the owning character when ownership is wired. `SendReactionPacket` uses `character?.OwningConnection?.SendGamePacket(packet)` and skips empty batches.

**Cap:** `GroupReactionCallPacket.AddPacket` refuses beyond **255** nested entries (returns false; caller does not check return value—overflow silent for that add).

**DoForAllPlayers:** duplicates entry into `broadcastPacket`, sent via `SendBroadcastToMap` to other characters on the map (excludes activator character).

**DoForConvoy:** packet is built then **skipped** with a debug log (no convoy membership system).

### B. Not involved (name collision only)

| Packet / opcode | System | Relation to UnlockContObj |
|-----------------|--------|---------------------------|
| **UnlockRegion** `0x205B` | Exploration fog (`ExplorationManager`) | Different opcode, payload (`ContinentId`, `UnlockFlag`, `ExploredBits`). Client handler may call `CVOGReaction_UnlockContinentObject` / `RelockContinentObject` — **map reveal**, not reaction type 32/70. |

UnlockContObj emits **no** `UnlockRegionPacket`.

### C. Upstream network (trigger enablement)

Triggers are not “I unlocked ContObj” client requests. Movement updates vehicle pose; `TriggerManager.CheckTriggersFor` may fire reaction COIDs that include UnlockContObj. See `docs/topic-extractions/server-triggers.md` for that pipeline.

---

## Server-Side Flow

### 1. Template load

1. Map `.fam` deserializes reaction templates via `ReactionTemplate.Read`.
2. Relevant fields always read for non-TransferMap reactions:
   - `ReactionType` (byte) — may be 32 or 70
   - `ActOnActivator` (bool)
   - `ObjectiveIDCheck` (int32)
   - `DoForConvoy` (bool)
   - `GenericVar1` (int32), `GenericVar2` (float), `GenericVar3` (int32)
   - `Objects` (list of COIDs via `ReadCOIDFromFile`)
   - nested `Reactions` (child reaction COIDs)
   - optional conditions / `DoForAllPlayers` (mapVersion ≥ 8)
3. `ReactionTemplate.Create()` → `new Reaction(this)` placed on the `SectorMap`.

**No UnlockContObj-specific template branch** exists; type-specific interpretation is only in the handler comments/code.

### 2. Fire path to the handler

```text
Trigger / other source
  → SectorMap.TriggerReactions(activator, reactionCoidList)
  → TriggerReactionsInternal(activator, reactions, depth)
     for each reactionCoid:
       lookup local Reaction entity
       if reaction.TriggerIfPossible(activator):
          HandleUnlockContObj  (when type == UnlockContObj)
          OR HandleRelockContObj (when type == RelockContObj)
          → always true on current stubs (if CanTrigger passed)
          add LogicStateChange to GroupReactionCall
          queue Template.Reactions children
     SendReactionPacket(ResolveCharacter(activator), clientPacket)
     recurse children (max depth 10)
     optional broadcast / convoy skip
```

### 3. `CanTrigger` (shared; not unlock-specific)

- Requires non-null activator with map.
- If `Template.Conditions` non-empty, evaluates stub condition checks (`TriggerConditional.Check` — known zero-valued stub per trigger extraction).
- **Does not** evaluate `ObjectiveIDCheck`.
- **Does not** evaluate mission ownership or unlock prerequisites.

### 4. `HandleUnlockContObj` (current behavior, step-by-step)

Evidence: `Reaction.cs` `HandleUnlockContObj`.

1. Resolve `character = GetCharacterFromActivator(activator)` (`GetAsCharacter() ?? GetSuperCharacter(false)`).
2. `objectiveId = Template.GenericVar1`.
3. `mission = AssetManager.Instance.GetMissionByObjectiveId(objectiveId)`.
4. If mission non-null: log mission Id/Name/Title.
5. Log `GenericVar1`, `GenericVar3`, `Objects.Count`, `ActOnActivator` (**log only**; ActOnActivator not branched).
6. If character non-null: log character name; comment TODO: track unlocked objectives per character.
7. If `Template.Objects.Count > 0`: for each `objectCoid`:
   - `map = activator.Map`; `obj = map?.GetObjectByCoid(objectCoid)`.
   - If found: log “Unlocking object {coid}” — **no property change, no enable/disable, no remove**.
   - If not found: log “client-side only”.
8. `return true`.

**`GenericVar2`:** never read in this handler.  
**Mission object:** never used after logging (no mission grant, objective unlock flag, or character assignment).  
**Missing mission:** not treated as failure; no dedicated log when `mission == null` (unlike `HandleSetActiveObjective`, which logs “no mission found”).

### 5. `HandleRelockContObj` (inverse pair)

1. Resolve character (same helper).
2. Log `GenericVar1`, `GenericVar3` (no mission lookup).
3. If character: log “Relocking for character”.
4. If Objects non-empty: log each COID as “Relocking object” — **no map lookup**, **no state clear**.
5. `return true`.

Asymmetry vs unlock: relock does not call `GetMissionByObjectiveId` or `GetObjectByCoid`.

### 6. Mission lookup read path

`AssetManager.GetMissionByObjectiveId(int objectiveId)`:

```text
foreach mission in WADLoader.Missions.Values
  foreach objective in mission.Objectives.Values
    if objective.ObjectiveId == objectiveId → return mission
return null
```

- Missions loaded once from WAD version 27 (`WADLoader.LoadInternal`).
- `Mission.Read` builds `Objectives` keyed by **sequence byte**, each `MissionObjective` holding `ObjectiveId` (int from binary) plus XML extras when `{mission.Name}.xml` exists in GLMs.
- Lookup is **linear** over all missions/objectives; result used only for debug string in UnlockContObj.

### 7. Client notification after success

Because the handler returns `true` (when `CanTrigger` passed), the map always attempts to notify the client with the **reaction entity COID**, not GenericVar1/objective id. The client is expected (per comments and mission-dialog analysis pattern) to load reaction metadata from clonebase/map and apply type-specific UI/logic—including whatever UnlockContObj means client-side.

Server does not send objective IDs, object lists, or lock bitmasks in a dedicated unlock packet.

### 8. Contrast: map-reveal unlock (not this system)

```text
ExplorationManager reveal path
  → UnlockRegionPacket (0x205B): ContinentId + UnlockFlag + ExploredBits
  → Client Client_RecvUnlockRegion → may call CVOGReaction_UnlockContinentObject / RelockContinentObject
```

That path persists `character_exploration` and is documented in `Documentation/MAP_REVEAL.md`. It does **not** go through `ReactionType.UnlockContObj`.

---

## State and Persistence

| State | Present for UnlockContObj? | Location | Notes |
|-------|----------------------------|----------|-------|
| Server unlocked-objectives / ContObj flags | **No** | — | Explicit TODO in handler |
| Character mission progress for this type | **No** | — | Mission system generally incomplete (`docs/codeAudit.md`) |
| Map object “unlocked” property | **No** | — | Objects only looked up for logging |
| Runtime trigger latch (upstream) | Yes, but generic | `TriggerManager._activeTriggers` | Edge-enter for the **trigger**, not unlock semantic state |
| DB / account persistence | **No** | — | Nothing written |
| Client-side apply from 0x206C | Possible if client implements type 32 | Client binary | Server cannot re-sync after relog from this handler |

**Save timing:** N/A for unlock content.  
**Relog:** Any client-only unlock visual would not be reconstructed by this server path.  
**Relock:** Cannot clear server state that was never set.

---

## Responsibility Boundary Review

| Behavior | Current owner | Correct owner (from architecture + audit notes) | Appropriate? | Direction |
|----------|---------------|--------------------------------------------------|--------------|-----------|
| Detect volume / fire reaction COID | `TriggerManager` + `SectorMap` | Trigger/map coordinator | Yes (shared pipeline) | Unchanged for this topic |
| Interpret UnlockContObj template fields | `Reaction.HandleUnlockContObj` | Thin reaction dispatch → **mission/character service** (if mission-gated unlock) or **map object service** (if ContObj lock flags) | **No** — logging is not domain work | Resolve intended semantics with client RE; implement in service; keep Reaction as switch |
| Mission template resolve | `AssetManager.GetMissionByObjectiveId` | Asset/catalog OK | Yes as **read API** | Cache/index by ObjectiveId if used hot; don’t call only for logs |
| Persist unlock / objective gate | *(missing)* | Character (or mission progress service) per `codeAudit` mission row | Missing | Authoritative character/mission state + login rehydrate |
| Client visual apply | Client via 0x206C reaction COID | Client presentation; server sends event | OK pattern if server also authoritative | Keep GroupReactionCall; don’t invent UnlockRegion reuse |
| Exploration fog | `ExplorationManager` + `UnlockRegion` | Separate system | Correctly separate in code | Keep name disambiguation in docs |
| Template gate `ObjectiveIDCheck` | Loaded, unused | Reaction `CanTrigger` or mission service | Dead data | Implement or document unused |

`HandleDelete` shows a better pattern for template fields: it **branches** on `ActOnActivator` and mutates map membership. Unlock logs `ActOnActivator` but does not branch—boundary is inconsistent within the same class.

---

## Engineering Concerns

Prioritized for **UnlockContObj / RelockContObj** (and only the shared notify path as it affects unlock delivery).

### 1. Critical — No server authority; success always claimed

| | |
|--|--|
| **Severity** | Critical (gameplay correctness) |
| **Location** | `Reaction.HandleUnlockContObj`, `HandleRelockContObj` |
| **Problem** | Handlers only log; `return true` still causes `LogicStateChange` emission. TODO admits missing tracking. |
| **Why** | Clients may unlock UI/containers; server cannot validate progression, anti-cheat, or multiplayer consistency. Relog/desync. |
| **Evidence** | TODO comment; loop only writes logs; no Character/map field writes. `docs/codeAudit.md`: “Mission progress mostly missing server-side”. |
| **Fix direction** | Define authoritative unlock state; mutate it on unlock/relock; only return true when applied (or still notify client but rehydrate from server). Ghidra case 32 first. |

### 2. High — Template design data ignored

| | |
|--|--|
| **Severity** | High |
| **Location** | `ReactionTemplate` fields vs unlock handlers; `CanTrigger` |
| **Problem** | `ObjectiveIDCheck` never checked. `ActOnActivator` logged for unlock but unused. `GenericVar2` unused. `GenericVar3` logged only. Relock ignores mission id semantics of GenericVar1. |
| **Why** | Map authors may rely on gates that never run → wrong unlocks or no-ops. |
| **Evidence** | Grep: `ObjectiveIDCheck` only property + one SetActiveObjective log line. Unlock logs ActOnActivator without `if`. |
| **Fix direction** | After client RE of field meanings, implement gates; unit-test field matrices. |

### 3. Medium — Dead mission lookup cost + noise

| | |
|--|--|
| **Severity** | Medium |
| **Location** | `HandleUnlockContObj` + `AssetManager.GetMissionByObjectiveId` |
| **Problem** | Full nested scan of all missions for every unlock fire; result only for debug log. Null mission silent. |
| **Why** | Wasted CPU on busy maps; false confidence that mission integration exists. |
| **Evidence** | Double foreach in AssetManager; mission only in log format string. |
| **Fix direction** | Build `Dictionary<objectiveId, Mission>` at WAD load if needed; use result for real logic or remove call until then. |

### 4. Medium — Unlock/Relock implementation asymmetry

| | |
|--|--|
| **Severity** | Medium |
| **Location** | `HandleUnlockContObj` vs `HandleRelockContObj` |
| **Problem** | Unlock resolves map objects; Relock only logs COIDs. Unlock does mission lookup; Relock does not. |
| **Why** | Inverse pair will diverge further when someone implements only one side. |
| **Evidence** | Side-by-side handler bodies in `Reaction.cs`. |
| **Fix direction** | Shared helper for ContObj target resolution; symmetric apply/clear. |

### 5. Medium — Silent GroupReactionCall entry drop at 255

| | |
|--|--|
| **Severity** | Medium (batch edge) |
| **Location** | `GroupReactionCallPacket.AddPacket`; `TriggerReactionsInternal` ignores return |
| **Problem** | If a batch exceeds 255, further LogicStateChange entries (including UnlockContObj) may not be added without error. |
| **Why** | Rare large chains miss client apply. |
| **Evidence** | `AddPacket` returns false at 255; caller never checks. |
| **Fix direction** | Assert/log on false; flush/send additional packets. |

### 6. Low–Medium — Convoy unlock notify stubbed

| | |
|--|--|
| **Severity** | Low–Medium (multiplayer) |
| **Location** | `SectorMap.TriggerReactionsInternal` DoForConvoy branch |
| **Problem** | Convoy packet built, not sent. |
| **Why** | If unlock reactions are convoy-scoped in maps, co-drivers miss 0x206C. |
| **Evidence** | Debug log: “DoForConvoy GroupReactionCall skipped”. |
| **Fix direction** | Convoy membership + send (shared with all reaction types). |

### 7. Low — Comment/intent speculation without client RE

| | |
|--|--|
| **Severity** | Low (maintainability) |
| **Location** | Comments in `HandleUnlockContObj` |
| **Problem** | Comments assert “mission objectives/containers” and client visual unlock via LogicStateChange without a type-32 Ghidra note in-repo. Name “ContObj” may mean continent object (see clone type / MAP_REVEAL client names) rather than “container”. |
| **Why** | Future AI/human implementers may build wrong domain model. |
| **Evidence** | Comment-only claims; `codeAudit` G-3 only confirms dispatch architecture, not type 32 semantics. |
| **Fix direction** | Ghidra `CVOGReaction_Dispatch` case 32/70 before implementing state. |

### 8. Low — No tests

| | |
|--|--|
| **Severity** | Low–Medium |
| **Location** | `AutoCore.Game.Tests` |
| **Problem** | Zero tests for Unlock/Relock ContObj. SectorMap tests cover Activate → packet only. |
| **Why** | Regressions invisible; TDD standards in `Agents.md` not met for this feature surface. |
| **Evidence** | Grep tests: no matches. |
| **Fix direction** | Tests listed under Follow-Up Fix Issues. |

---

## Crash / Stability Risks

| Risk | Evidence | Severity for this topic |
|------|----------|-------------------------|
| NRE in unlock handler | Character/map access is null-checked; `GetObjectByCoid` uses `map?` | **Low** (handler itself) |
| NRE on packet send | `SendReactionPacket` uses `?.` on character and connection | **Low** (current code) |
| Exception mid-trigger chain | Unlock does no I/O/mutate; still runs inside broader trigger path that can throw for other reaction types | **Low** for unlock body; **High** for shared pipeline (see server-triggers) |
| Infinite reaction recursion | Depth cap 10 in `TriggerReactionsInternal` | Mitigated |
| Unlock handler blocking | No DB/network inside handler | **None observed** |
| Incorrect success → client apply while server empty | `return true` always | Gameplay desync, not crash |
| AssetManager null Instance | Singleton assumed when handler runs after init | Normal process assumption |

UnlockContObj is **not** a high crash risk; it is a **correctness / progress** risk.

---

## Comparison to Expected Behavior

Sources used: in-repo comments, `docs/topic-extractions/server-triggers.md`, `docs/codeAudit.md` (G-3, mission ownership), `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md` (0x206C), `Documentation/MAP_REVEAL.md` (name collision only). **No in-repo Ghidra dump of reaction type 32.**

| Expected / documented | Fork behavior | Difference | Risk |
|----------------------|---------------|------------|------|
| Reaction types dispatch server-side with nested client notify (`CVOGReaction_Dispatch` architecture) | Switch + GroupReactionCall on success | Matches structure | OK |
| UnlockContObj “unlocks” something meaningful for the player | Log-only | No authority | Progress/UI desync |
| Relock reverses unlock | Log-only | No reverse state | Same |
| Client receives reaction apply via 0x206C when fire succeeds | Built and sent via `ResolveCharacter` | Matches intended notify path (vehicle fix present in current `SectorMap`) | Depends on client type-32 handler |
| Mission templates available for objective id | WAD load + linear lookup | Lookup exists; unused for logic | Dead integration |
| `ObjectiveIDCheck` / mission gates (field exists on template) | Not applied | Map gates ignored | Wrong fire conditions |
| Map-reveal UnlockContinentObject / UnlockRegion | Separate ExplorationManager path | Correctly not mixed into HandleUnlockContObj | Name confusion only if developers conflate APIs |
| Mission progress server-owned (`codeAudit`) | Unlock TODO + mission reactions “client-side” comments for GiveMission/CompleteObjective | Consistent incompleteness | Content incomplete |

**Note on `server-triggers.md`:** That extraction previously described vehicle activators failing to receive 0x206C via `GetAsCharacter()` only. **Current** `SectorMap` uses `ResolveCharacter` and tests assert vehicle → character send. UnlockContObj benefits from that fix for **delivery**, but still has no server unlock state.

---

## Questions for the User

1. **Semantics of ContObj:** Should UnlockContObj be treated as **mission objective unlock**, **map object / continent-object lock flag**, or **client-only visual**, based on any private client notes or map-author docs not in this repo?
2. **GenericVar1:** Is it always `MissionObjective.ObjectiveId`, a sequence index, a continent-object id, or mixed by map version?
3. **GenericVar3 / ActOnActivator / ObjectiveIDCheck:** Do you have authored meaning for these on type 32/70 (even anecdotal)?
4. **Authority:** Must unlock survive relog and be anti-cheat validated, or is “send 0x206C and trust client” acceptable for your short-term private server goals?
5. **Relock:** Is type 70 used in any target maps you care about, or can it stay stub until unlock is correct?
6. Should we Ghidra **case 32 and case 70** of `CVOGReaction_Dispatch` @ `0057c500` (or current addresses) before any implementation issue is scheduled?

---

## Recommended Follow-Up Fix Issues

### UCO-01: Reverse-engineer client UnlockContObj / RelockContObj

| | |
|--|--|
| **Severity** | Critical (blocks correct fix) |
| **Description** | Ghidra `CVOGReaction_Dispatch` cases for types 32 and 70; document field use of GenericVar1/2/3, Objects, ActOnActivator, ObjectiveIDCheck; contrast with `CVOGReaction_UnlockContinentObject` (exploration). |
| **TDD test** | Doc fixture / golden notes; optional table-driven test of parsed meanings once known (no production change until semantics clear). |
| **Files** | New notes under `Documentation/` or `docs/`; later `Reaction.cs` |

### UCO-02: Implement authoritative unlock state (after UCO-01)

| | |
|--|--|
| **Severity** | Critical |
| **Description** | Persist unlock/relock on Character (or mission progress service); apply on HandleUnlock/Relock; rehydrate on login/sector enter as client requires. |
| **TDD test** | Unlock → character state set; Relock clears; second unlock idempotent; no state change when preconditions fail. |
| **Files** | `Reaction.cs`, Character/mission model, DB entity if needed, tests |

### UCO-03: Gate UnlockContObj with ObjectiveIDCheck / mission preconditions

| | |
|--|--|
| **Severity** | High |
| **Description** | Honor `ObjectiveIDCheck` (and any client-confirmed mission checks) in `CanTrigger` or handler; return false when not met so 0x206C is not emitted incorrectly. |
| **TDD test** | ObjectiveIDCheck mismatch → TriggerIfPossible false → no GroupReactionCall entry for that reaction. |
| **Files** | `Reaction.cs`, tests |

### UCO-04: Index missions by ObjectiveId

| | |
|--|--|
| **Severity** | Medium |
| **Description** | Replace linear `GetMissionByObjectiveId` scan with dictionary built at WAD load; define conflict policy if IDs collide. |
| **TDD test** | Load synthetic missions; lookup known ObjectiveId; missing returns null; document duplicate-id behavior. |
| **Files** | `AssetManager.cs` / `WADLoader.cs`, tests |

### UCO-05: Symmetric RelockContObj path

| | |
|--|--|
| **Severity** | Medium |
| **Description** | Share target resolution with unlock; ensure relock clears the same server state unlock sets. |
| **TDD test** | Unlock then Relock restores prior state; Relock without prior unlock is no-op or safe. |
| **Files** | `Reaction.cs`, tests |

### UCO-06: Assert/split GroupReactionCall when AddPacket fails

| | |
|--|--|
| **Severity** | Low–Medium |
| **Description** | Log or flush when 255 entry cap hit so UnlockContObj in a large batch is not silently dropped. |
| **TDD test** | 256 successful reactions → either 2 packets or error log + documented behavior. |
| **Files** | `SectorMap.cs`, `GroupReactionCallPacket.cs`, tests |

### UCO-07: Regression tests for UnlockContObj notify packaging

| | |
|--|--|
| **Severity** | Medium (quality gate) |
| **Description** | Even while domain state is stubbed, test that type 32 success produces GroupReactionCall with expected reaction COID for vehicle activator. |
| **TDD test** | Mirror `SectorMapTriggerTests` with `ReactionType.UnlockContObj` template. |
| **Files** | `AutoCore.Game.Tests/Map/` or `Entities/` |

### UCO-08: Documentation cross-link name disambiguation

| | |
|--|--|
| **Severity** | Low |
| **Description** | In MAP_REVEAL and reaction docs, explicitly state UnlockContObj ≠ UnlockRegion / UnlockContinentObject. |
| **TDD test** | N/A (docs). |
| **Files** | `Documentation/MAP_REVEAL.md`, this topic file, optional server-triggers cross-link |

---

## Appendix A: Template field usage matrix (Unlock / Relock)

| Field | Loaded? | UnlockContObj uses | RelockContObj uses |
|-------|---------|--------------------|--------------------|
| `ReactionType` | Yes | Dispatch | Dispatch |
| `GenericVar1` | Yes | As `objectiveId` for mission lookup (log) | Logged only |
| `GenericVar2` | Yes | **Unused** | **Unused** |
| `GenericVar3` | Yes | Logged only | Logged only |
| `Objects` | Yes | Map lookup + log | Log COID only (no lookup) |
| `ActOnActivator` | Yes | Logged only | **Unused** |
| `ObjectiveIDCheck` | Yes | **Unused** | **Unused** |
| `Reactions` (children) | Yes | Chained by SectorMap after success | Same |
| `DoForAllPlayers` | Yes | SectorMap broadcast path | Same |
| `DoForConvoy` | Yes | Built then skipped | Same |
| `Conditions` / `AllConditionsNeeded` | Yes | Shared `CanTrigger` stub eval | Same |

## Appendix B: Name disambiguation cheat sheet

| Name | Kind | Opcode / value | System |
|------|------|----------------|--------|
| `ReactionType.UnlockContObj` | Reaction enum | **32** | Map reaction → mission/object unlock **stub** |
| `ReactionType.RelockContObj` | Reaction enum | **70** | Inverse reaction **stub** |
| `GameOpcode.UnlockRegion` | Network | **0x205B** | Exploration fog bits |
| `CVOGReaction_UnlockContinentObject` | Client (MAP_REVEAL) | Called from UnlockRegion apply | Map continent exploration entry |
| `CVOGReaction_RelockContinentObject` | Client (MAP_REVEAL) | UnlockFlag == 0 | Map continent relock |
| `CloneBaseObjectType.ContinentObject` | Clone type | **34** | Asset type for continents / maps |
| `MissionObjective.ContinentObject` | Mission field | int | Objective’s continent reference |

## Appendix C: Suspicious AI-generated patterns (evidence-based)

Patterns observed specifically on the Unlock/Relock ContObj surface:

1. **Verbose multi-line debug logging without side effects** — entire handlers are log scaffolding.
2. **Hedging comments** (“may contain the objective ID”) without verification against client or sample maps.
3. **TODO + return true** — presents feature as handled in the switch while incomplete.
4. **Side-effect-free “integration”** — mission lookup wired for logs only (looks like progress, isn’t).
5. **Inconsistent sibling implementations** — Unlock vs Relock differ without documented reason; SetActiveObjective has better null-mission logging.
6. **ActOnActivator logged “for completeness” but unused**, unlike Delete in the same file.
7. **No tests** for the new-looking handlers despite project TDD standards (`Agents.md`).

These do not prove AI authorship alone, but match the “stub feature theater” pattern common in poorly supervised AI edits on this fork.
