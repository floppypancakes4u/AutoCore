# Topic Extraction: SetActiveObjective

## Executive Summary

### What the fork currently does

`ReactionType.SetActiveObjective` is enum value **60**. When a map reaction of that type fires, `Reaction.TriggerIfPossible` dispatches to `HandleSetActiveObjective`. That handler:

1. Resolves a `Character` from the activator (`GetAsCharacter()` or vehicle super-character).
2. Reads `Template.GenericVar1` as an objective id.
3. Looks up a mission via `AssetManager.GetMissionByObjectiveId` **for logging only**.
4. Logs `GenericVar3` and `ObjectiveIDCheck`.
5. Returns `true` without mutating any character/mission state.

There is **no** write to `Character.CurrentQuests`, **no** update of `CharacterQuest.ActiveObjectiveSequence`, **no** dedicated ObjectiveState / CompleteDynamicObjective packet send from this path, and **no** DB persistence of active objectives.

Related mission reaction types (`GiveMission`, `CompleteObjective`, `FailMission`, `AddMissionString`, `DelMissionString`) share a single stub case: debug log + return true, with an explicit comment that server mission tracking is future work. After a successful server-side reaction, `SectorMap.TriggerReactionsInternal` still packages a `LogicStateChange` (reaction COID + activator) into `GroupReactionCallPacket` (0x206C) for the client, so the **client** may apply clonebase reaction semantics independently of server authority.

Login/create serialization **does** send a 72-byte-per-quest structure (`CharacterQuest`) via `CreateCharacterExtendedPacket`, but the only default content is a hardcoded mission **554** with active sequence **0** in the `Character` constructor. `HandleSetActiveObjective` never touches that list.

### Soundness assessment

**Stub / non-authoritative.** Template fields and mission WAD data are partially wired for diagnostics. Runtime mission progress and active-objective authority are not implemented. Client may still react to the GroupReactionCall COID; server state will diverge.

### Biggest risks

1. Server never stores or advances active objectives → re-login and Convoy mission UI disagree with map-driven progression.
2. Semantic confusion in code: comment says GenericVar1 is “objective index”; lookup treats it as `MissionObjective.ObjectiveId` (int), while `CharacterQuest` stores `ActiveObjectiveSequence` (byte sequence).
3. Sibling mission reactions are also stubs; `FailMissionPacket` exists but is never sent.
4. Opcodes `ObjectiveState` (0x2071) and `CompleteDynamicObjective` (0x2070) are declared with client struct docs, but have **no** packet classes and are **not** referenced from SetActiveObjective (or any production sender found).

---

## Scope

### In scope

- `ReactionType.SetActiveObjective = 60` and `HandleSetActiveObjective`.
- Brief coverage of related mission reaction stubs in the same switch: GiveMission, CompleteObjective, FailMission, AddMissionString, DelMissionString (and GiveMissionDialog only as contrast for the GroupReactionCall path).
- Character create/login objective serialization (`CreateCharacterExtendedPacket` / `CharacterQuest`).
- Opcodes `ObjectiveState` (0x2071), `CompleteDynamicObjective` (0x2070) where declared/documented.
- Template fields used by SetActiveObjective: `GenericVar1`, `GenericVar3`, `ObjectiveIDCheck`.
- Asset lookup `GetMissionByObjectiveId`.

### Out of scope

- Full objective requirement type engines (kill/collect/etc.) except that they are not called from this handler.
- Full trigger volume system (covered in `docs/topic-extractions/server-triggers.md`).
- Inventory/combat systems.
- Fixes or production code changes (this document is extraction only).

---

## Relevant Files

| Path | Role |
|---|---|
| `src/AutoCore.Game/Entities/Reaction.cs` | `ReactionType` enum; switch dispatch; `HandleSetActiveObjective`; sibling mission stubs; character resolve helper |
| `src/AutoCore.Game/EntityTemplates/ReactionTemplate.cs` | Loads `ObjectiveIDCheck`, `GenericVar1/2/3`, conditions, missions lists |
| `src/AutoCore.Game/Map/SectorMap.cs` | `TriggerReactionsInternal`: runs reactions, builds/sends `GroupReactionCallPacket` |
| `src/AutoCore.Game/Managers/AssetManager.cs` | `GetMission`, `GetMissionByObjectiveId` |
| `src/AutoCore.Game/Mission/Mission.cs` | Mission template incl. `ActiveObjectiveOverride`, objectives dictionary |
| `src/AutoCore.Game/Mission/MissionObjective.cs` | `ObjectiveId`, `Sequence`, quest linkage, requirements load from XML |
| `src/AutoCore.Game/Mission/MissionString.cs` | Map mission-string asset type (not used by reaction stubs) |
| `src/AutoCore.Game/Map/MapData.cs` | Loads `MissionStrings` from map (unused by SetActiveObjective) |
| `src/AutoCore.Game/Entities/Character.cs` | `CurrentQuests`; hardcoded mission 554; write into extended create packet |
| `src/AutoCore.Game/Structures/CharacterQuest.cs` | 72-byte SVOG-style current quest layout; `ActiveObjectiveSequence` |
| `src/AutoCore.Game/Packets/Sector/CreateCharacterExtendedPacket.cs` | Serializes current quests tail |
| `src/AutoCore.Game/Packets/Global/ConvoyMissionsResponsePacket.cs` | Same 72-byte quest list for convoy UI refresh |
| `src/AutoCore.Game/Packets/Sector/GroupReactionCallPacket.cs` | 0x206C bit-packed reaction batch |
| `src/AutoCore.Game/Packets/Sector/LogicStateChangePacket.cs` | Nested reaction entry (COID + activator TFID) |
| `src/AutoCore.Game/Packets/Sector/FailMissionPacket.cs` | FailMission wire shape; **never sent** from FailMission reaction |
| `src/AutoCore.Game/Constants/GameOpcode.cs` | `CompleteDynamicObjective=0x2070`, `ObjectiveState=0x2071`, `FailMission=0x20B2`, `GroupReactionCall=0x206C` |
| `src/AutoCore.Game/TNL/TNLConnection.Sector.cs` | Mission dialog response best-effort adds to `CurrentQuests` |
| `src/AutoCore.Game/Entities/Trigger.cs` | `TriggerConditional.Check` stub (conditions never real values) |
| `Documentation/PACKET STRUCTURES.md` | Client structs for ObjectiveState / CompleteDynamicObjective |
| `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md` | Client 0x206C / mission dialog notes; `bActiveObjectiveOverride` as definition field |
| `docs/topic-extractions/server-triggers.md` | Classifies SetActiveObjective as log-only “server-ish” |
| `src/AutoCore.Game.Tests/Packets/FirstTimeFlagsPacketTests.cs` | Asserts 72-byte current-quest tail size when NumCurrentQuests=1 |

---

## Packet / Network Structures

### Path A — Reaction notify (what SetActiveObjective actually rides on)

When `HandleSetActiveObjective` returns `true`, `SectorMap.TriggerReactionsInternal` adds a nested reaction entry and may send:

| Item | Value |
|---|---|
| Opcode | `GameOpcode.GroupReactionCall` = **0x206C** |
| Packet class | `GroupReactionCallPacket` |
| Nested type | `LogicStateChangeType.Reaction` = 0 |
| Nested fields | `ReactionCoid` (19-bit int in bitstream), activator COID + Global flag, `SingleClientOnly` flag |
| Direction | Server → client |

Wire packing (from `GroupReactionCallPacket.Write`):

- 8-bit entry count
- Per entry: 8-bit type
  - Type Reaction: u19 reactionCoid + u64 activator COID + 2 flags
  - Type Variable: u16 variableId + f32 value

**No SetActiveObjective-specific payload.** Client is expected to resolve the reaction COID against local map/clonebase data and apply type 60 itself (same pattern as documented for GiveMissionDialog in `Reaction.HandleGiveMissionDialog` comments and `MISSION_DIALOG_CLIENT_ANALYSIS.md`).

Send target: `SectorMap.SendReactionPacket(ResolveCharacter(activator), clientPacket)` where `ResolveCharacter` = `GetAsCharacter() ?? GetSuperCharacter(false)`, so vehicle activators resolve to the owning character when set. Empty batches are not sent (`packet.Count == 0`).

Standalone `LogicStateChange` opcode **0x206B** exists as `LogicStateChangePacket.Opcode` but the live map path embeds entries only inside 0x206C.

### Path B — Character create / login quest tail

| Item | Value |
|---|---|
| Opcode | `GameOpcode.CreateCharacterExtended` = **0x2016** (via base create path) |
| Packet | `CreateCharacterExtendedPacket` |
| Counts written early | `NumCompletedQuests`, `NumCurrentQuests`, … |
| Variable tail | After skills / completed / achievements / disciplines: foreach `CurrentQuests` → `CharacterQuest.Write` (72 bytes) |

`CharacterQuest` layout as implemented (comments claim partial RE):

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | `MissionId` (int) |
| 4 | 1 | `ActiveObjectiveSequence` (byte) |
| 5 | 1 | `State` (byte; comment: 0=active, 1=completed, etc.) |
| 6 | 2 | padding short 0 |
| 8 | 64 | 8 × (progress int + max int) |

Comment on packet: “SVOGCharacterObjective[] - 72 bytes each”.

**Completed quests** for `NumCompletedQuests > 0` are still TODO zero-fill (`4 * NumCompletedQuests`), not real ids.

### Path C — Convoy mission list refresh

| Item | Value |
|---|---|
| Opcode | Convoy missions response (used as `ConvoyMissionsResponsePacket`) |
| Body | int count + N × 72-byte `CharacterQuest` |

Sent after mission dialog response when server best-effort adds a mission to `CurrentQuests`. Not sent from SetActiveObjective.

### Path D — Declared but unused for this topic

#### `ObjectiveState` = 0x2071

- Enum only in `GameOpcode.cs`.
- **No** `ObjectiveStatePacket` class in `src/`.
- Client struct docs (`Documentation/PACKET STRUCTURES.md`):

```text
SMSG_Sector_ObjectiveState // Size=0x28
  coidCharacter   // 0x8, 8 bytes (docs show int at offset; size table says 8)
  lChangeBitmask  // 0x10, 4
  SVOGObjectiveState objectiveState // 0x14, 0x14
    IDObjective   // int
    fSlots[4]     // float[4]
```

No production code constructs or sends this for SetActiveObjective (or elsewhere found).

#### `CompleteDynamicObjective` = 0x2070

- Enum only.
- Client struct:

```text
SMSG_Sector_CompleteDynamicObjective // Size=0x18
  coidCharacter // 0x8
  IDObjective   // 0x10
```

No packet class / senders found.

#### `FailMission` = 0x20B2

- Packet class `FailMissionPacket`: pad 4, write `CharacterCoid`, `MissionId`, pad 4.
- **Zero call sites** constructing or sending it. FailMission **reaction** does not use it.

---

## Server-Side Flow

### 1. Template load (map `.fam`)

`ReactionTemplate.Read`:

- `ReactionType` byte (60 = SetActiveObjective).
- `ActOnActivator`, **`ObjectiveIDCheck`** (int32), `DoForConvoy`.
- **`GenericVar1`** (int32), `GenericVar2` (float), **`GenericVar3`** (int32).
- Objects list (unless TransferMap).
- Child reaction COIDs.
- Optional text, conditions (mapVersion ≥ 8), waypoint/misc fields by type.
- Mission type/id lists only when `mapVersion == 16` **or** (`mapVersion > 16` **and** `ReactionType == GiveMissionDialog`). **SetActiveObjective does not load a dedicated mission-id list** from the template beyond generic vars.

### 2. Fire path (trigger → reactions)

1. Trigger enter (or other code calling `SectorMap.TriggerReactions`) supplies reaction COID list.
2. For each COID, map finds local `Reaction` instance.
3. `reaction.CanTrigger(activator)`:
   - Null activator/map → false.
   - Conditions: `TriggerConditional.Check` currently uses **leftValue=rightValue=0.0f** for all comparisons (stub). `ObjectiveIDCheck` on the **reaction template is not part of CanTrigger**.
4. `TriggerIfPossible` → switch → `HandleSetActiveObjective`.

### 3. `HandleSetActiveObjective` (complete logic)

```text
character = GetCharacterFromActivator(activator)
objectiveId = Template.GenericVar1
mission = AssetManager.GetMissionByObjectiveId(objectiveId)
  // log mission name/title or "no mission found"
// log ObjectiveID, GenericVar3, ObjectiveIDCheck
if character != null:
  // log "Setting active objective to {objectiveId} for character {Name}"
  // TODO: Store active objective on character when mission tracking is implemented
  // TODO: Give the player this mission if they don't have it yet
return true
```

**Side effects:** log lines only. Returns **true** even if character is null (still allows client packet inclusion for the reaction COID).

### 4. After handler success

1. Build `LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, singleClientOnly: false)`.
2. Add to per-client `GroupReactionCallPacket`; optionally to broadcast/convoy batches if template flags set.
3. Queue child `Template.Reactions` for recursive depth (max 10).
4. `SendReactionPacket` to resolved character connection.
5. Recurse children; broadcast if `DoForAllPlayers`; convoy send is logged as skipped (no convoy membership).

### 5. Sibling mission reactions (brief)

In the same switch:

| Type | Value | Behavior |
|---|---|---|
| GiveMission | 30 | Shared stub: debug log “Mission reaction …”; return true |
| CompleteObjective | 31 | same |
| FailMission | 72 | same — **does not** send `FailMissionPacket` |
| AddMissionString | 33 | same — map `MissionStrings` never referenced |
| DelMissionString | 34 | same |
| GiveMissionDialog | 37 | Dedicated handler: logs mission ids from `Template.Missions`; relies on GroupReactionCall for client dialog |

Comment on shared stub:

> “Mission-related reactions are primarily client-side / Server will need to track mission state in the future”

### 6. Mission data lookup used by SetActiveObjective

`AssetManager.GetMissionByObjectiveId(objectiveId)`:

- Linear scan of all `WADLoader.Missions` and each mission’s `Objectives` values.
- Match on `MissionObjective.ObjectiveId == objectiveId`.
- Returns owning `Mission` or null.
- Used **only for logging** in SetActiveObjective / UnlockContObj.

`MissionObjective` identifiers:

- `ObjectiveId` (int) — global-ish objective id from WAD.
- `Sequence` (byte) — order key in `Mission.Objectives` dictionary.
- `QuestId` (int) — read from binary; separate field.

`CharacterQuest.ActiveObjectiveSequence` is a **byte sequence**, not `ObjectiveId`. Handler never maps GenericVar1 → sequence.

### 7. Character list updates that *do* exist (not SetActiveObjective)

- `Character` ctor: `CurrentQuests.Add(new CharacterQuest(554, 0));` always.
- `LoadFromDB`: does **not** load quests from DB (no quest tables found under `AutoCore.Database`).
- `HandleMissionDialogResponse`: if `MissionId > 0` and not already present, add `CharacterQuest(missionId, 0)` and send `ConvoyMissionsResponsePacket`.

None of these update active objective from map reactions.

---

## State and Persistence

| State | Location | Written by SetActiveObjective? | Persistence |
|---|---|---|---|
| Active objective sequence | `CharacterQuest.ActiveObjectiveSequence` | **No** | In-memory only; serialized on create extended / convoy response |
| Current quest list | `Character.CurrentQuests` | **No** (TODO says give mission if missing) | In-memory; not DB |
| Objective progress | `CharacterQuest.ObjectiveProgress/Max` | **No** | Defaults 0/1 in ctor |
| Mission WAD templates | `WADLoader.Missions` via AssetManager | Read-only lookup | Static load |
| Map reaction templates | `ReactionTemplate.GenericVar1`, `ObjectiveIDCheck`, … | Read | Map file |
| Trigger conditions | `TriggerConditional` | Not used as objective gate | Stub values |
| Client reaction visual/state | Via 0x206C COID | Indirect only | Client-side clonebase |

**Database:** No character-quest / active-objective tables or load/save paths found in this investigation.

**Restart behavior:** Any client-side objective changes from reactions are lost on server re-login; server re-sends hardcoded mission 554 sequence 0 (plus any missions accepted in-session via dialog response while the process lived).

---

## Responsibility Boundary Review

| Concern | Owner today | Notes |
|---|---|---|
| Detect reaction type 60 | `Reaction.TriggerIfPossible` | Correct dispatch |
| Map author params | `ReactionTemplate` | GenericVar1 = intended objective id per comments/logs |
| Server mission authority | **Missing** | TODOs only |
| Client apply reaction | Client via GroupReactionCall COID | Documented pattern for dialogs; assumed same for other reaction types |
| Login snapshot of quests | `Character` + `CreateCharacterExtendedPacket` | Present but incomplete / hardcoded |
| Runtime objective progress packets | Opcodes exist; **no senders** | ObjectiveState / CompleteDynamicObjective |
| Fail mission S2C | Packet class only | Not wired to reaction 72 |
| Domain vs packet | Handler logs in entity; packets assembled in SectorMap | Acceptable boundary; domain never updates state |

**Boundary smell:** Handler pretends to “set active objective for character X” in log text while doing nothing — false confidence for debugging and for AI-authored follow-ons.

**ID model mismatch:** Mission domain uses `ObjectiveId` + `Sequence`; character packet uses sequence byte; SetActiveObjective never bridges them.

---

## Engineering Concerns (prioritized)

1. **Non-implementation presented as handler**  
   Dedicated method + mission lookup + “Setting active objective…” logs imply behavior that does not exist. Risk: QA and tools assume server tracks progression.

2. **GenericVar1 semantic inconsistency**  
   Comment: “objective index”. Variable/log names: `ObjectiveID`. Lookup: `GetMissionByObjectiveId` on `MissionObjective.ObjectiveId`. `CharacterQuest` needs **sequence**. Without a definitive mapping (and client RE of reaction type 60), implementing storage incorrectly is likely.

3. **Sibling mission reactions all stubs**  
   Give/complete/fail/string reactions return true so client packets still fire, but server never grants, completes, fails, or tracks strings. `FailMissionPacket` dead code.

4. **No use of ObjectiveState / CompleteDynamicObjective**  
   Client docs show progress slots (`fSlots[4]`) and dynamic complete. If map flow depends on server pushing progress, those packets are absent entirely.

5. **Hardcoded mission 554**  
   Every character starts with mission 554 sequence 0 regardless of race/class/progress; not related to SetActiveObjective but pollutes the only quest serialization path.

6. **CreateCharacterExtended incomplete tails**  
   Completed quests zero-filled; current quest structure “partially reverse-engineered” per `CharacterQuest` comments. Wrong layout would corrupt login even if active objective were set.

7. **ObjectiveIDCheck unused**  
   Loaded on every reaction template; logged by SetActiveObjective; never gates CanTrigger or handler. Unknown whether original game used it as prerequisite objective filter.

8. **GetMissionByObjectiveId O(missions × objectives)**  
   Acceptable for rare log-only calls; would need an index if used on hot path later.

9. **Trigger conditions always zero**  
   Any reaction (including SetActiveObjective) behind conditionals may fire or suppress incorrectly independent of mission state.

10. **No tests for SetActiveObjective**  
    SectorMap tests cover GroupReactionCall delivery; RespawnManager tests MarkRepairStation; **no** test asserts CurrentQuests / ActiveObjectiveSequence after type-60 reaction.

---

## Crash / Stability Risks

| Risk | Severity | Evidence |
|---|---|---|
| Null character | Low for crash | Handler returns true; no NRE on null character |
| Packet send NRE | Mitigated | `character?.OwningConnection?.SendGamePacket` |
| Unbounded reaction chain | Mitigated | Max depth 10 with error log |
| GroupReactionCall entry cap | Low | AddPacket stops at 255 entries silently |
| GetMissionByObjectiveId null | None | Logged, continues |
| Login quest tail size | Medium if layout wrong | 72-byte assumption used in tests for length only, not field correctness |
| FailMission / ObjectiveState | None currently | Unused |

No evidence of SetActiveObjective itself throwing under normal paths.

---

## Comparison to Expected Behavior

Sources: code comments, `Documentation/PACKET STRUCTURES.md`, `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md`, `docs/topic-extractions/server-triggers.md`.

### Expected (from evidence, not full client RE of type 60)

1. Map reaction type 60 carries an objective selector in template generic fields (server uses GenericVar1).
2. Client is notified via **0x206C** with reaction COID so it can execute clonebase reaction logic (established pattern for mission dialog / reactions).
3. Server **should** eventually track active objective per character (`TODO` comments; `CharacterQuest.ActiveObjectiveSequence` field exists for that purpose).
4. Progress/state updates may need **0x2071 ObjectiveState** (`IDObjective` + float slots) and/or **0x2070 CompleteDynamicObjective** when objectives complete dynamically.
5. Mission fail may need **0x20B2 FailMission** (packet present).
6. Mission definition field `ActiveObjectiveOverride` / `bActiveObjectiveOverride` exists on WAD/XML side — definition data only; not applied at runtime by SetActiveObjective.

### Actual

| Expected | Actual |
|---|---|
| Set active objective on character | Log only |
| Auto-grant mission if missing | TODO only |
| Persist across login | No |
| Objective progress S2C | Opcodes unused |
| Fail mission S2C | Packet unused |
| Use ObjectiveIDCheck | Logged only |
| Align with CharacterQuest sequence | Never written by this handler |
| Client reaction notify | Yes, if reaction succeeds and character connection exists (GroupReactionCall) |

`server-triggers.md` correctly classifies SetActiveObjective as **“(log)”** among partially implemented reactions.

---

## Questions for the User

1. **GenericVar1 meaning:** Confirm against client/map tools whether reaction type 60’s GenericVar1 is absolute `ObjectiveId`, objective **sequence**, or something else (e.g. mission-local index). Code currently treats it as `ObjectiveId`.

2. **Client authority:** Does the retail client apply SetActiveObjective purely from clonebase when it receives 0x206C, and only need server state for login sync / anti-cheat? Or must the server also send ObjectiveState?

3. **ObjectiveIDCheck:** What is the authored meaning? Prerequisite objective? Mission filter? Ignore?

4. **GenericVar3:** Logged but unused — any known meaning for type 60?

5. **Mission 554:** Is hardcoding “New Day Dawning” intentional for all new characters, and should SetActiveObjective advance that mission’s sequence?

6. **CharacterQuest 72-byte layout:** Is the ActiveObjectiveSequence + 8×(progress,max) layout verified against client, or still partial RE that might be wrong before implementing storage?

7. **When to send 0x2071 vs only 0x206C:** Does setting active objective require ObjectiveState, or is reaction COID sufficient until progress changes?

---

## Recommended Follow-Up Fix Issues

*(Issues only — not implemented in this audit.)*

1. **Implement server active-objective mutation**  
   - Map GenericVar1 → mission + sequence using WAD data.  
   - Update or create `CharacterQuest` on the character.  
   - Decide whether to auto-grant mission (existing TODO).  
   - Unit tests: reaction 60 with known mission/objective changes `ActiveObjectiveSequence` / list membership.

2. **Clarify and document GenericVar1 / ObjectiveIDCheck / GenericVar3**  
   - Client or map-editor RE; fix misleading “objective index” comment if it is ObjectiveId.

3. **Wire or retire FailMissionPacket**  
   - Either send from FailMission reaction with mission id from template vars, or mark obsolete.

4. **Implement ObjectiveState / CompleteDynamicObjective packet classes**  
   - Match `PACKET STRUCTURES.md` sizes; call from complete/progress paths when those systems exist.  
   - Not necessarily from SetActiveObjective if 0x206C alone is correct for “set active”.

5. **Persist CurrentQuests**  
   - DB schema + LoadFromDB / save; stop relying on ctor hardcode alone.

6. **Remove or gate hardcoded mission 554**  
   - Or load from config/auto-assign missions (`AssetManager.GetAutoAssignMissions` exists unused by Character ctor in this path).

7. **Index GetMissionByObjectiveId**  
   - Dictionary objectiveId → mission if used on hot path after implementation.

8. **Implement TriggerConditional.Check**  
   - So mission-gated SetActiveObjective reactions fire correctly.

9. **TDD per Agents.md**  
   - Failing tests first for handler state changes and packet size of any new S2C objective packets.

10. **Do not “fix” by only logging more**  
    - Avoid further AI-style verbose logs without state; they already obscure incompleteness.

---

## Evidence Index (symbols)

| Symbol | File |
|---|---|
| `ReactionType.SetActiveObjective = 60` | `src/AutoCore.Game/Entities/Reaction.cs` |
| `HandleSetActiveObjective` | same |
| Mission stub case GiveMission…DelMissionString | same ~193–201 |
| `ReactionTemplate.ObjectiveIDCheck`, `GenericVar1` | `src/AutoCore.Game/EntityTemplates/ReactionTemplate.cs` |
| `TriggerReactionsInternal` | `src/AutoCore.Game/Map/SectorMap.cs` |
| `GetMissionByObjectiveId` | `src/AutoCore.Game/Managers/AssetManager.cs` |
| `Character.CurrentQuests`, ctor mission 554 | `src/AutoCore.Game/Entities/Character.cs` |
| `CharacterQuest` 72-byte write | `src/AutoCore.Game/Structures/CharacterQuest.cs` |
| Current quest tail | `src/AutoCore.Game/Packets/Sector/CreateCharacterExtendedPacket.cs` |
| `GameOpcode.CompleteDynamicObjective`, `ObjectiveState` | `src/AutoCore.Game/Constants/GameOpcode.cs` |
| `FailMissionPacket` (unreferenced send) | `src/AutoCore.Game/Packets/Sector/FailMissionPacket.cs` |
| Client ObjectiveState structs | `Documentation/PACKET STRUCTURES.md` |

---

*Extraction only. No production code was modified.*
