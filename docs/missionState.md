# Mission State — Reactions, Triggers, Missions & Persistence

**Purpose.** Reverse-engineer how missions, objectives, triggers, and reactions work across the
client (`autoassault.exe`, via Ghidra) and the AutoCore server, and lay out everything needed to
build **SQL-backed mission persistence** so a returning player resumes the *exact* mission state
they left — every active mission with its current objective and per-objective progress, plus a
ledger of completed missions.

**Source conventions.** Client facts cite Ghidra addresses (`@0x…`). Server facts cite
`file:line`. Anything asserted from a single unverified source is flagged in
[§9 Open questions](#9-open-questions--risks).

---

## 1. Overview & goal

Auto Assault is a client-authoritative game: the retail client owns mission *presentation* (journal
UI, objective evaluators, waypoint HUD) and even evaluates some objective completion locally. But
the **server owns the authoritative mission state** — the set of active/completed missions and their
progress — and streams it to the client at login inside the character-create packet. The client
then rebuilds its journal from that stream.

"Resume exact mission state" therefore reduces to one problem: **the server must remember, across a
disconnect, what it currently only holds in memory, and re-stream it at login.** Specifically:

- Which missions are active, and for each: the current objective (sequence) and per-objective
  progress counters.
- Which missions are completed (needed for prerequisite gating, repeatable gating, and logic-variable
  conditions that open gates / enable NPC dialog).

Today **none of this is persisted.** Mission state lives only on the in-memory `Character` object and
is lost on disconnect or server restart. On the next login the server starts with empty quest lists
and re-grants starter missions by re-firing the map's per-player load trigger — mid-mission progress
is simply gone.

Triggers and reactions are examined here too, but the conclusion (see [§5](#5-triggers--reactions))
is that their runtime state is **session-derived and does not need its own persistence** — once
mission state persists, the mission-gated logic variables that drive triggers reconstruct
themselves automatically.

---

## 2. Client mission model (reverse-engineered)

### 2.1 Per-character state on `CVOGCharacter`

The client keeps mission state in per-character hash tables (offsets on the character object):

| Offset | Contents |
|---|---|
| `+0x538` | **Completed** missions hash |
| `+0x540` | **Active** missions hash |
| `+0x548` | Active **objectives** hash (keyed by objective id) |
| `+0x55c` | Pending objectives |
| `+0x531` | Faction |
| `+0x532` | Race |

Confirmed by `CVOGReaction_GiveMission` @`0x005327C0` (looks up `+0x540`/`+0x548`/`+0x538` before
granting) and `CVOGCharacter_ApplyCreateFromPacket` @`0x00535174` (populates them from the create
packet — see [§3](#3-login-restore-channel-createcharacterextended-0x2016)).

### 2.2 Mission / objective definition structs (from clonebase data)

Mission definition struct (key offsets, from `MISSION_SYSTEM.md` RE):

- `+0x10` MissionID, `+0x90` RequiredRace (`0xFFFF`=none), `+0x92` RequiredFaction,
  `+0x94` MinLevel, `+0x98` MaxLevel (0=none), `+0x9C` 4× prerequisite mission ids
  (`0xFFFFFFFF`=none), `+0x104/+0x108` required currency + threshold, `+0x118` flag-condition id,
  `+0x130` objectives count, `+0x13C` objectives array, `+0x138` repeatable.

Objective definition: `+0x10` objective id; `+0x158/+0x15c` an array of evaluator function objects.
The client evaluates objective completion locally via vtable callbacks (`+0x4` precheck, `+0x8`
eval, `+0x20` action + LogicUI packet); the "type 3" evaluator is the use-object path (see
`Client_RecvObjectiveState` below).

### 2.3 Client lifecycle functions (Ghidra addresses)

| Function | Address | Role |
|---|---|---|
| `CVOGReaction_GiveMission` | `0x005327C0` | Grant mission: adds objective, unlocks continent object, inserts into active hash, toasts "Received Mission". |
| `CVOGReaction_CompleteObjective` | `0x00533F90` | Complete/advance an objective. |
| `CVOGMission_AddActiveObjective` | `0x00531B00` | Push objective into active-objectives structure. |
| `CVOGCharacter_SearchAutoMissions` | `0x00532B60` | Auto-grant eligible auto-assign missions. |
| `CVOGCharacter_CheckMissionPrerequisites` | `0x00536540` | Prereq gate (completed-mission ids). |
| `CVOGCharacter_CheckMissionRequirements` | `0x005462B0` | Race/faction/level/currency/flag gate. |
| `CVOGCharacter_CompleteMissionObjectives` | `0x00536080` | Bulk complete. |
| `CVOGCharacter_EvaluatePendingObjectives` | `0x00534920` | Re-evaluate pending objective set. |
| `Client_RecvObjectiveState` | `0x0080FF00` | Handles `0x2071`: looks up objective at packet `+0x10`, applies progress, may complete + auto-`UseObject` on the matching world target. |
| `Client_RecvNpcMissionDialog` | `0x00815070` | Handles `0x206D` (NPC mission dialog). |
| `Client_MissionDialogHandleButton` | `0x008AE7C0` | Dialog buttons: state 0=accept-request, 1=accept/claim reward, 2=abandon confirm, 3=NPC dialog. |
| `Client_UpdateMissionJournal` | `0x008AE130` | Refresh journal UI. |

Debug strings around `0x00A28B00` (`"Completed Missions:"`, `"Instanced Completed Missions:"`,
`"Mission(%d)(%S) Objective(%d)(%d)(%S)"`, `"Current Missions:"`, `testmissionadd`,
`incompletemissions`) confirm the client tracks completed missions, per-instance completed missions,
and current missions with objective tuples.

### 2.4 `eMissionSavedState`

The client enum `eMissionSavedState { NONE=0, NEW=1, UPDATE=2, DELETE=3 }` (from
`PACKET STRUCTURES.md`) shows the original protocol modeled mission changes as **deltas**. Worth
mirroring as a per-row state column if a delta-streaming approach is ever needed; not required for
the create-packet restore path.

---

## 3. Login restore channel: `CreateCharacterExtended` (0x2016)

This is the single channel by which the server hands the client its mission state at spawn. The
client parser is `CVOGCharacter_ApplyCreateFromPacket` @`0x00535174`.

**Verified packet offsets (absolute, from start of packet incl. opcode):**

| Offset | Field | Client handling |
|---|---|---|
| `+0x1A8` | `NumCompletedQuests` (int) | Loop count. |
| tail (ptr `+0x1340`) | Completed-mission id array — plain **int32 × `NumCompletedQuests`** | Each id looked up + inserted into the **completed hash** (`FUN_0053c360`). Lives in the variable-length tail **after skills, before achievements** (`+0x1340` is a runtime pointer the client computes, not a fixed packet offset). |
| `+0x1AC` | `NumCurrentQuests` (int) | Loop count. |
| tail (ptr `+0x1354`) | Current-quest records, **stride `0x48` = 72 bytes** | Each inserted into active hash + pending objectives. Layout: mission id `+0x00`, reserved `+0x04`, ten mission-state dwords `+0x08..+0x2F`, active global objective id `+0x30`, four objective-state slots `+0x34..+0x43`, reserved `+0x44`. |
| `+0x1B8` | Continent block, 50 × 12 bytes | Exploration. |
| `+0x8EC` | FirstTimeFlags[4] (uint) | Hints UI (→ char `+0xD30`). |
| `+0x410` | QuickBar items, 100 × int64 | — |
| `+0x730` | QuickBar skills, 100 × int32 | — |

Fixed body size when variable tails are empty = `0x1358` bytes including opcode.

**Server side** (`src\AutoCore.Game\Packets\Sector\CreateCharacterExtendedPacket.cs`):

- `FirstTimeFlagsPacketOffset = 0x8EC`, `FixedPacketSizeIncludingOpcode = 0x1358` (lines 18–21) —
  match the client offsets above.
- Current quests **are** written (lines 165–172): `foreach (quest in CurrentQuests) quest.Write(writer)`.
- **Gaps (zero-filled TODO):** `Character.WriteToPacket` hard-sets `NumCompletedQuests = 0`
  (`Character.cs:304`) and never passes any ids; the packet then zero-fills the tail slot
  (`CreateCharacterExtendedPacket.cs:147-151`). Achievements (153–157) and disciplines (159–163) are
  zeroed the same way. So the server sends **zero completed missions** and the client's completed
  hash is empty every login — even though the client both *parses* the array and *needs* it (its
  `CVOGCharacter_CheckMissionPrerequisites` @`0x00536540` reads the completed hash `+0x538` to gate
  offers and auto-missions). Populating this array is a required implementation step — see
  [§8.5](#85-implementation-checklist).
- **Layout pitfall:** extended HP is written as `(short)` (line 98), not int32 — matches client
  `+0x8D6` int16 read. Any field added before the quest tail must preserve total offsets or the
  entire tail shifts (see `FIRST_TIME_FLAGS.md`).

**`CharacterQuest` 72-byte record** (`src\AutoCore.Game\Structures\CharacterQuest.cs`):

| Offset | Field | Type |
|---|---|---|
| 0 | `MissionId` | int32 |
| 4 | `ActiveObjectiveSequence` | byte |
| 5 | `State` (0=active, 1=completed) | byte |
| 6 | padding | int16 |
| 8 | 8 × (`ObjectiveProgress[i]` int32, `ObjectiveMax[i]` int32) | 64 bytes |

`Write` at lines 43–57; `MaxObjectives = 8`, `StructureSize = 72` (lines 19–20). `ObjectiveMax` is
re-derived from the mission template (`PopulateFromMission`, lines 65–89: `ObjectiveMax[seq] =
CompleteCount`), so only `ObjectiveProgress` and `ActiveObjectiveSequence`/`State` are genuinely
per-player mutable.

---

## 4. Server mission system

### 4.1 Definition loading pipeline (read-only)

`AssetManager.Initialize(gamePath, serverType)` requires `<gamePath>\exe\autoassault.exe`.
`LoadAllData()` runs four loaders:

| Source | File(s) | Loader | Mission/trigger content |
|---|---|---|---|
| WAD binary | `<GamePath>\clonebase.wad` (version 27) | `Managers\Asset\WADLoader.cs:73-77` | **Missions** (`Mission.Read`), CloneBases incl. Trigger (type 56) & Reaction clone types, skills. |
| GLM archives | `<GamePath>\*.glm` (esp. `misc.glm`) | `GLMLoader.cs` | Per-map `<MapFileName>.fam` and per-mission `<mission.Name>.xml` (mission text). |
| Map binaries | `<MapFileName>.fam` inside GLMs | `MapDataLoader.cs` → `Map\MapData.cs:211-220` | Trigger + Reaction templates, map-level trigger COIDs, map Variables, MissionStrings, music. |
| World DB / `wad.xml` | MySQL tables, else `<GamePath>\wad.xml` | `WorldDBLoader.cs` / `WadXmlWorldDataLoader.cs` | ContinentObjects (`Objective`, `ContestedMission`), areas, exp levels, loot, new-char config, vehicle/creature templates. |

`Mission.Read` (`Mission\Mission.cs:75-159`) fields: `Id`, `Name`, `Type`, `NPC` (giver CBID),
`ReqRace/ReqClass/ReqLevelMin/Max`, `ReqMissionId[4]` (prereqs), `IsRepeatable`,
`Item/ItemTemplate/ItemValue/ItemIsKit/ItemQuantity[4]` (reward arrays), `AutoAssign`,
`ActiveObjectiveOverride`, `Continent`, `Achievement`, discipline fields, `RequirementEventId`,
`RequirementsOred/Negative`, `NumberOfObjectives`, `Objectives` (`Dictionary<byte,MissionObjective>`
keyed by Sequence). Text (Title/Description/CompleteText/…) comes from `<mission.Name>.xml`.

`MissionObjective` (`Mission\MissionObjective.cs`): `QuestId`, `ObjectiveId` (global),
`Sequence` (byte, dictionary key + progress index), reward fields, `Requirements`
(`List<ObjectiveRequirement>`), `CompleteCount` (target count → `ObjectiveMax`).

`ObjectiveRequirement` base (`Mission\Requirements\ObjectiveRequirement.cs`): `RequirementType`
(17 types), `FirstStateSlot` (byte — indexes `ObjectiveStatePacket.SlotProgress[]`). Subclasses
(Kill, KillAggregate, Collect, Deliver, Escort, Patrol, CrazyTaxi, Km, Stunt, Money, TimePlayed,
UseItem, CharacterLevel, Mission) are pure static definition parsed from XML.

`AssetManager` accessors: `GetMission(id)`, `GetMissionByObjectiveId`, `GetObjectiveById`,
`GetAutoAssignMissions`, `GetMissionsForContinent` (`Managers\AssetManager.cs:196-304`).

### 4.2 Per-player runtime state (`src\AutoCore.Game\Entities\Character.cs`)

- `CurrentQuests : List<CharacterQuest>` (line 136) — active missions. **In-memory only.**
- `CompletedMissionIds : HashSet<int>` (line 138) — finished mission ids. **In-memory only.**
- `LogicVariables : LogicVariableStore` (line 141) — per-map, rebuilt on map change
  (`EnsureLogicVariables`, 146–155); derives boolean flags from the two collections above.
- `WriteToPacket` → `CreateCharacterExtendedPacket` (302–319): sets `NumCurrentQuests`, copies
  `CurrentQuests`, `NumCompletedQuests = 0`.
- `LoadFromDB` (210–246): loads clan/position/exploration/cargo — **no mission load**.
- Constructor (158–162) deliberately does **not** pre-seed missions: the client rejects a later
  `GiveMission` for a mission whose quest blob was already in the create packet.

### 4.3 Mission lifecycle (server code)

**Grant** — both paths create `new CharacterQuest(missionId, 0)` + `PopulateFromAssets()` +
`CurrentQuests.Add`:
1. Reaction `GiveMission` (type 30): `Reaction.HandleGiveMission` (`Entities\Reaction.cs:715-749`),
   `GenericVar1` = mission id; followed by `TriggerManager.OnMissionStateChanged`.
2. NPC dialog accept: `NpcInteractHandler.GrantMission` (`Managers\NpcInteractHandler.cs:644-673`),
   reached from `HandleMissionDialogResponse` (0x206E) after `CanOfferMission` (547–585: not already
   active, not completed-unless-repeatable, giver match, level/continent, `ReqMissionId` prereqs vs
   `CompletedMissionIds`).

**On login / map load** — `SectorMap.FireOnLoadPlayerMissions(activator)`
(`Map\SectorMap.cs:278-305`), invoked from `TNL\TNLConnection.Sector.cs:190` after create packets.
Resolves `MapData.PerPlayerLoadTrigger` (only if the COID is a live findable entity;
`TryGetPerPlayerLoadTrigger` 253–269; COID ≤ 0 = disabled) and runs its reactions (e.g. GiveMission).
This is how retail seeds starter missions each session (e.g. map 707 grants mission 554).

**Set active objective** — `Reaction.HandleSetActiveObjective` (type 60): sets
`quest.ActiveObjectiveSequence` if quest owned; logs it does not yet send ObjectiveState / persist.

**Progression:**
- Kills: `MissionKillProgress.NotifyObjectKilled` (`Managers\MissionKillProgress.cs:20-93`) from
  `ClonedObjectBase.OnDeath`; matches Kill/KillAggregate by CBID/template/faction/continent,
  increments `ObjectiveProgress[seq]`; `< needed` → `ObjectiveStatePacket`; complete →
  `AdvanceOrCompleteObjective`.
- Patrol: `NpcInteractHandler.HandleAutoPatrol` (0x20B3).
- Use item/object: `HandleUseObject` (0x2072) → `TryCompleteUseItemObjective`.
- Deliver turn-in: `TryCompleteDeliverFromDialog` — removes quest, adds to `CompletedMissionIds`,
  sends `CompleteDynamicObjectivePacket`.

**Advance/complete** — `NpcInteractHandler.AdvanceOrCompleteObjective` (1001–1105): send
`CompleteDynamicObjectivePacket`; if a higher `Sequence` exists → set `ActiveObjectiveSequence`,
mark old progress full, send `ObjectiveStatePacket`; else remove from `CurrentQuests` + add to
`CompletedMissionIds`. **Rewards not applied and state not persisted** — logged via
`IncompleteHandlerLog.Warn` (e.g. 1099).

**Fail / abandon** — `Reaction.FailMission` (type 72) is a **stub** (`Reaction.cs:235-240`);
`FailMissionPacket` exists but is not wired to any lifecycle sender; there is no abandon path.

**Journal refresh** — `PushJournalMissionList` (1107–1113) sends `ConvoyMissionsResponsePacket`
after each change; on-demand via `HandleConvoyMissionsRequest` (0x800F,
`TNL\TNLConnection.Global.cs:103-120`).

**Mission-state → world reactivity** — `TriggerManager.OnMissionStateChanged`
(`Managers\TriggerManager.cs:127-153`) re-evaluates logic-variable-gated triggers (types 9/11/12) so
gates/dialogs open without a movement packet; coalesces nested GiveMission (re-entrancy guarded).

---

## 5. Triggers & reactions

### 5.1 Triggers

- Definitions: `EntityTemplates\TriggerTemplate.cs` (Range/Scale, TargetType, `Reactions`
  (List<long> COIDs), `Conditions`, `ActivationCount`, `RetriggerDelay`, `ActivateDelay`).
- Runtime: `Entities\Trigger.cs` — only mutable field is `FireCount` (int), compared to
  `Template.ActivationCount`. **World-shared** (one Trigger instance per map). Not persisted.
- Firing: `Managers\TriggerManager.cs` (singleton) holds two in-memory latch dictionaries:
  `_activeTriggers` keyed by `(objectCoid, triggerCoid)` (physical enter/leave), and
  `_firedConditionalTriggers` keyed by `(actorCoid, triggerCoid)` (one-shot condition-driven fires).
  Both **process-memory only, re-armed on leave/reset/relog/restart.** Live path: movement →
  `Vehicle.HandleMovement` → `CheckTriggersFor` → `FireTriggerReactions` → `SectorMap.TriggerReactions`.
- `RetriggerDelay`/`ActivateDelay`/`ActivationCount` are loaded but effectively session-only.

### 5.2 Logic variables

- `Structures\Variable.cs` — map-defined variable definitions (Id, Type, InitialValue, …) in
  `MapData.Variables`; world-shared, read-only.
- `Structures\LogicVariableStore.cs` — **per-character, per-map** mutable store, seeded from
  `InitialValue` and rebuilt whenever the character changes map (`Character.EnsureLogicVariables`).
  Variable types: `0` Constant (mutable), `9` HasCompletedMission (reads `CompletedMissionIds`),
  `11` HasActiveMission (reads `CurrentQuests`), `12` HasActiveObjective (reads active objective
  sequence). **Never persisted.**

**Key consequence:** types 9/11/12 are computed live from mission state. Persisting mission state
therefore **indirectly restores every mission-gated logic variable, trigger condition, and NPC
dialog gate** — no separate trigger/logic persistence is required. Type-0 constants reset to
`InitialValue` each map entry by design.

### 5.3 Reactions

- Definitions: `EntityTemplates\ReactionTemplate.cs` (`ReactionType` byte enum, 88 values;
  `GenericVar1/2/3`; `Objects`/`Reactions` COID lists; `DoForAllPlayers`; `Conditions`).
- Runtime: `Entities\Reaction.cs` holds only `Template` — **no per-instance mutable state.** Effects
  mutate other entities or the character:
  - Per-character: `VariableSet/Add/…`, `GiveMission` (30), `SetActiveObjective` (60),
    `TransferMap` (10), `MarkRepairStation` (29).
  - World-shared: `Create` (2), `Delete/Death` (3/8), invincible/faction
    (`ReactionObjectStateEffects.cs`), `SetPath/SetPatrolDistance` (43/44).
  - Client-only stubs: activate/deactivate/enable/disable, waypoints/text/UI, `CompleteObjective`
    (31), `FailMission` (72), `AddMissionString/DelMissionString` — logged via `IncompleteHandlerLog`.
- Reaching the client: `SectorMap.TriggerReactions` (`Map\SectorMap.cs:493-588`) builds a
  `LogicStateChangePacket(reactionCoid, activator.ObjectId, singleClientOnly:false)` per reaction and
  packs them into a `GroupReactionCallPacket` (opcode `0x206C`), sent to the activator's connection
  (or broadcast if `DoForAllPlayers`). The client looks up the reaction in its clonebase and applies
  the visual/UI effect. `LogicStateChangePacket` (`Packets\Sector\LogicStateChangePacket.cs`) has two
  variants: `Reaction=0` (coid + activator TFID + singleClientOnly) and `Variable=1` (varId + float).

---

## 6. Mission-relevant packets

| Packet (C# class) | Opcode | Dir | Wire format (read/write order) | When |
|---|---|---|---|---|
| `NpcMissionDialogPacket` | `0x206D` | S→C | +8 NPC TFID(16B); +24 count(i32); per entry stride 40: missionId(i32) @base, 8× itemCOID(i32) @base+8 (-1=empty) | Open NPC mission dialog. Client `Client_RecvNpcMissionDialog` @0x815070. |
| `MissionDialogResponsePacket` | `0x206E` | C→S | MissionId(i32); Accepted(bool)+pad; MissionGiver TFID | Player OK/Accept. |
| `CompleteDynamicObjectivePacket` | `0x2070` | S→C | +16 lookup id = `ObjectiveId>0 ? ObjectiveId : MissionId` (i32) | Objective advanced / mission complete. |
| `ObjectiveStatePacket` | `0x2071` | S→C | +16 ObjectiveBitmask(u32); +20 ObjectiveId(i32); +24 4× SlotProgress(float) | Objective progress / active-objective change. Client `Client_RecvObjectiveState` @0x80FF00. |
| `FailMissionPacket` | `0x20B2` | S→C | +4 pad; CharacterCoid(i64); MissionId(i32); +4 pad | Mission failed. **Defined but not wired to any sender.** |
| `GroupReactionCallPacket` | `0x206C` | S→C | count(8b); per entry type(8b); Reaction→coid(19b)+activatorCoid(u64)+Global(1b)+SingleClientOnly(1b); Variable→varId(16b)+float. Bit-packed, LSB-first. | Trigger/reaction visual+UI effects. Client name `EMSG_Sector_MissionDialog`. |
| `LogicStateChangePacket` | `0x206B` | S→C | (as above; single entry) | Individual logic-state change. |
| `ConvoyMissionsRequestPacket` | `0x800F` | C→S | no-op payload (not reverse-engineered) | Client requests journal. |
| `ConvoyMissionsResponsePacket` | `0x8010` | S→C | count(i32); each `CharacterQuest.Write` (72B) | Journal refresh. |
| `CreateCharacterExtendedPacket` | `0x2016` | S→C | see [§3](#3-login-restore-channel-createcharacterextended-0x2016) | Local-player create at spawn (mission restore channel). |
| `MissionDialogPacket` | — | — | `[Obsolete(…, true)]` — throws; do not use | Deprecated. |

---

## 7. State inventory

Per-player unless noted. "Persisted today" = written to DB now. "Needs persisting" = required to
restore exact mission state.

| State | Location | Scope | Persisted today | Needs persisting | Notes |
|---|---|---|---|---|---|
| `CurrentQuests` (list) | `Character.cs:136` | Per-player | **No** | **Yes** | The core gap. |
| — `MissionId` | `CharacterQuest` | Per-player | No | Yes | Row key. |
| — `ActiveObjectiveSequence` | `CharacterQuest` | Per-player | No | Yes | See §9 (sequence vs objective-id). |
| — `State` (0/1) | `CharacterQuest` | Per-player | No | Yes | Semantics unverified (§9). |
| — `ObjectiveProgress[]` | `CharacterQuest` | Per-player | No | Yes | Per-objective counters. |
| — `ObjectiveMax[]` | `CharacterQuest` | Per-player | No | No | Re-derivable from template `CompleteCount`. |
| `CompletedMissionIds` (set) | `Character.cs:138` | Per-player | **No** | **Yes** | Prereq/repeatable gating + logic vars 9. |
| `LogicVariableStore` type 0 | `LogicVariableStore.cs` | Per-player/map | No | No | Resets to InitialValue each map entry by design. |
| `LogicVariableStore` types 9/11/12 | computed | Per-player | No | No (indirect) | Restored automatically once mission state persists. |
| `Trigger.FireCount` | `Trigger.cs` | World-shared | No | No | Re-armed on restart. |
| `_activeTriggers` / `_firedConditionalTriggers` | `TriggerManager` | Per-COID | No | No | Session latches; re-fire on map enter. |
| Reaction map mutations (Create/Delete/faction/path) | `SectorMap` objects | World-shared | No | No | Rebuilt from map data. |
| Per-player mission strings | not tracked | Per-player | No | No | Server has no per-player string state today. |
| First-time flags | `account` row | Per-account | **Yes** | (already done) | The proven persistence template. |
| Exploration bits | `character_exploration` | Per-player | **Yes** | (already done) | The table-shape template. |

**Bottom line:** the only durable, per-player, currently-unpersisted mission state is
`CurrentQuests` (minus the re-derivable `ObjectiveMax`) and `CompletedMissionIds`.

---

## 8. Proposed SQL persistence design

Mirror the two proven precedents: the `character_exploration` table shape and the
first-time-flags load→create-packet→save round-trip.

### 8.1 Schema (EF Core 8 + Pomelo MySQL, code-first)

```
character_quest
  CharacterCoid            BIGINT   -- FK -> character.Coid
  MissionId                INT
  ActiveObjectiveSequence  TINYINT
  State                    TINYINT  -- 0=active, 1=completed (verify §9)
  PRIMARY KEY (CharacterCoid, MissionId)

character_quest_objective
  CharacterCoid            BIGINT
  MissionId                INT
  Sequence                 TINYINT  -- objective sequence within mission
  Progress                 INT
  PRIMARY KEY (CharacterCoid, MissionId, Sequence)
  -- (ObjectiveMax intentionally NOT stored; re-derived from template CompleteCount)

character_completed_mission
  CharacterCoid            BIGINT
  MissionId                INT
  CompletionCount          INT      -- optional; for repeatable missions
  PRIMARY KEY (CharacterCoid, MissionId)
```

`character_quest_objective` may instead be collapsed into a `Progress` blob column on
`character_quest` (8 int32s) if a child table feels heavy — the 72-byte record only exposes 8 slots
on the wire anyway. A child table is preferred for queryability and to allow > 8 objectives to
persist even though only 8 stream to the client.

Add via `CharContext` DbSets + `OnModelCreating` composite keys, created through the existing
idempotent `EnsureCreated()` + `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE` pattern (see
`CharContext.EnsureInventorySchema`).

### 8.2 Load

In `Character.LoadFromDB` (`Character.cs:210`), beside `LoadExplorations(context)`: read
`character_quest` (+ objectives) into `CurrentQuests` (call `PopulateFromAssets()` to fill
`ObjectiveMax`, then overlay stored `Progress`), and `character_completed_mission` into
`CompletedMissionIds`. Because `WriteToPacket` already streams `CurrentQuests` into the create
packet, the journal then restores at spawn automatically — **provided the create packet is also
extended to write the completed-id array** at `+0x1340` (currently zero-filled, §3), which the
client already parses.

### 8.3 Save

Two viable styles, both already present in the codebase:

- **Queue-on-mutation** (preferred for mid-session durability): follow `ExplorationManager` +
  `ExplorationPersistenceQueue` — enqueue an upsert at each mutation hook
  (`HandleGiveMission`, `AdvanceOrCompleteObjective`, `MissionKillProgress`, deliver turn-in,
  `HandleSetActiveObjective`), background-flushed. Survives a crash mid-mission.
- **Snapshot-at-logout**: follow `CharacterWorldStatePersistence.PersistFromCharacter` (called from
  `TNLConnection.EndCharacterSession` before `SetMap(null)`) — write the whole quest set on
  disconnect. Simpler but loses progress on a hard crash.

A hybrid (queue for completed-mission inserts + objective progress, snapshot for the rest) is
reasonable.

### 8.4 Critical caveat — re-granting missions (including already-completed ones)

`FireOnLoadPlayerMissions` (PerPlayerLoad trigger) and any map trigger/reaction can fire
`GiveMission` repeatedly. The re-grant path **must skip missions the player already has or has
finished.** There are two distinct cases:

1. **Already active** — `HandleGiveMission` (`Entities\Reaction.cs:730`) already dedupes against
   `CurrentQuests`, so an active mission is not re-added. Confirm the client won't receive a
   duplicate `GiveMission` (0x206C) for a mission whose quest blob was in the create packet (the
   `Character` constructor comment at 158–162 warns the client rejects this).

2. **Already completed** — **this is currently unguarded on the server.** `HandleGiveMission` checks
   only `CurrentQuests`; it does **not** consult `CompletedMissionIds`, and it never checks the
   mission's `IsRepeatable` flag. So a trigger/reaction firing `GiveMission` for a mission the player
   already completed will re-add it as an active quest. The client masks this today — its
   `CVOGReaction_GiveMission` @`0x005327C0` looks up the completed hash (`+0x538`) and, for a
   non-repeatable/non-instanced mission, returns early without re-showing it — but the **server
   desyncs**: it now believes the completed mission is active again. **Once mission state is
   persisted this becomes a data-corruption bug:** the re-granted completed mission is written back
   as active, so the player "un-completes" a mission on relog.

   **Fix (implemented):** `HandleGiveMission` rejects the grant when
   `CompletedMissionIds.Contains(missionId)` **unless** `AssetManager.GetMission(missionId).IsRepeatable`
   — mirroring `NpcInteractHandler.CanOfferMission` (`NpcInteractHandler.cs:547-585`).

   **Critical nuance — the decline must `return false`, not `true`.** `SectorMap.TriggerReactions`
   (`SectorMap.cs:541-545`) packs a reaction into the client `GroupReactionCall` (0x206C) **iff
   `TriggerIfPossible` returns true**. On relog the map `PerPlayerLoad` trigger re-fires
   `GiveMission`; if the handler returns true on a declined/duplicate grant, the server still
   broadcasts `GiveMission(id)` to the client, which **re-adds the mission as active** even though
   the server keeps it completed-only (from the create packet). Result: the client shows a
   finished mission as active-and-un-turn-in-able while the server offers the follow-up. Therefore
   **both** decline branches in `HandleGiveMission` (already-active and completed-non-repeatable)
   return `false` so the reaction is not sent to the client. Only a genuine grant returns `true`.

### 8.5 Implementation checklist

Ordered so each step is testable before the next. All file paths under `src\`.

1. **Schema.** Add three POCO models under `AutoCore.Database\Char\Models\`
   (`CharacterQuestData`, `CharacterQuestObjectiveData`, `CharacterCompletedMissionData`), register
   DbSets + composite keys in `Char\CharContext.cs OnModelCreating`, and create them with an
   idempotent `CREATE TABLE IF NOT EXISTS` block modeled on `CharContext.EnsureInventorySchema`.
2. **Persistence service.** Add a `MissionPersistence` (queue-on-mutation, modeled on
   `Managers\ExplorationManager.cs` + `ExplorationPersistenceQueue.cs`) with a DI-overridable
   `PersistRow`/`ProductionSave` seam for unit tests, marking the EF path
   `[ExcludeFromCodeCoverage]`. Upsert semantics: find row → update else Add.
3. **Enqueue on every mutation site** (all currently in-memory-only):
   - `Reaction.HandleGiveMission` (`Entities\Reaction.cs:715`) — insert quest row.
   - `NpcInteractHandler.GrantMission` (`Managers\NpcInteractHandler.cs:644`) — insert quest row.
   - `MissionKillProgress.NotifyObjectKilled` (`Managers\MissionKillProgress.cs:20`) — update objective progress.
   - `NpcInteractHandler.AdvanceOrCompleteObjective` (`~1001`) — update active sequence + progress, or
     on final completion delete quest rows + insert completed-mission row.
   - `NpcInteractHandler.TryCompleteDeliverFromDialog` (`~767`) — same completion path.
   - `Reaction.HandleSetActiveObjective` (`Entities\Reaction.cs:755`) — update active sequence.
4. **Load on login.** In `Character.LoadFromDB` (`Entities\Character.cs:210`), beside
   `LoadExplorations(context)`: read quest rows into `CurrentQuests` (call `PopulateFromAssets()`
   first to fill `ObjectiveMax`, then overlay stored `Progress` and `ActiveObjectiveSequence`/`State`),
   and completed rows into `CompletedMissionIds`.
5. **Deliver completed missions to the client.** Add `List<int> CompletedMissionIds` to
   `CreateCharacterExtendedPacket`; in `Write`, replace the `WriteZeros(4 * NumCompletedQuests)`
   (lines 147–151) with `foreach (var id in CompletedMissionIds) writer.Write(id)`. In
   `Character.WriteToPacket` (`Character.cs:302-319`, currently `NumCompletedQuests = 0` at line 304)
   set `NumCompletedQuests = CompletedMissionIds.Count` and pass the ids. (Current quests already flow.)
6. **Fix the re-grant guard** (see [§8.4](#84-critical-caveat--re-granting-missions-including-already-completed-ones)):
   in `HandleGiveMission`, also reject when
   `character.CompletedMissionIds.Contains(missionId) && !AssetManager.Instance.GetMission(missionId).IsRepeatable`.
7. **Guard PerPlayerLoad re-grant** against DB-loaded state so `FireOnLoadPlayerMissions` does not
   duplicate a mission whose quest blob was already in the create packet (the `Character` ctor
   comment at 158–162 warns the client rejects this).

### 8.6 Verifying the implementation

Doc-level checks (schema covers every "needs-persisting" field; packet offsets match the client)
are necessary but not sufficient. Add these runtime checks against a live server + retail client:

1. **Active-mission round-trip.** Accept a mission from an NPC, partially progress a kill/collect
   objective, log out, restart the sector server, log back in. Expect: mission present in the
   journal at the same active objective with the same progress count; no duplicate grant.
2. **Multi-objective sequence.** On a mission with ≥2 sequenced objectives, complete the first, then
   relog mid-second. Expect: `ActiveObjectiveSequence` restored to the second objective, first shown
   complete.
3. **Completed-mission persistence + gating.** Complete a non-repeatable mission, relog, then re-enter
   the map / re-approach the giver NPC. Expect: mission shows completed in the journal (proves the
   `+0x1340` array populates), and it is **not** re-offered or re-granted (proves the §8.4 guard and
   client `CheckMissionPrerequisites`).
4. **Repeatable mission.** Complete a repeatable mission, relog, re-approach the giver. Expect: it
   **is** offered again (proves the `IsRepeatable` branch of the guard).
5. **Prereq chain.** Complete mission A (prereq of B), relog, approach B's giver. Expect: B now
   offered (proves `CompletedMissionIds` survives and feeds prereq gating).
6. **Reaction re-grant.** On a map whose PerPlayerLoad trigger grants a mission the character has
   already completed, relog and re-enter. Expect: no re-grant, no corruption of the completed row.

Existing unit tests to keep green / extend: `CharacterQuestAndMissionStringTests`,
`MissionKillProgressTests`, `PerPlayerLoadMissionGrantTests`, `MissionStateTriggerReevalTests`,
`MissionPacketCoverageTests`.

---

## 9. Open questions & risks

Items to resolve (further RE or in-game testing) before implementing:

1. **[RESOLVED — now an implementation step, see [§8.5](#85-implementation-checklist) step 5.]**
   The completed-id array in `0x2016` is a plain `int32[]` of mission ids (verified in the client
   parse loop `@0x00535174`: reads `NumCompletedQuests` then `array[i*4]`, 4-byte stride, inserting
   each into the completed hash). The client *does* rely on it — `CheckMissionPrerequisites`
   @`0x00536540` gates offers/auto-missions off the completed hash. The server just needs to write
   it (currently zero-filled).
2. **[RESOLVED] `CharacterQuest` 72-byte layout.** Ghidra analysis of
   `CVOGCharacter_ApplyCreateFromPacket` verified the layout documented in §3. The former
   sequence/state plus 8×(progress,max) serializer was incorrect and has been replaced.
3. **`ActiveObjectiveSequence` vs global `ObjectiveId`.** Reaction `GenericVar1` and
   `SetActiveObjective`/`UnlockContObj` semantics are unresolved — determine which the client expects
   as the "active objective" pointer on restore (a `Sequence` byte or a global objective id).
4. **Objective progress authority.** `ObjectiveState (0x2071)` carries 4 `SlotProgress` floats while
   `CharacterQuest` carries 8×(progress,max) ints. Determine which is authoritative on restore and
   whether the server must **replay** `ObjectiveState`/`CompleteDynamicObjective` after login or the
   create-packet tail alone rebuilds the journal correctly.
5. **`MissionDialogResponse 0x206D` write format is undetermined** (per
   `MISSION_DIALOG_CLIENT_ANALYSIS.md`). Needed to fully round-trip accept/abandon/claim so persisted
   state matches what the client believes it did.
6. **Reward idempotency.** Rewards (XP/credits/items, mission item type `0x14`) are applied
   in-memory only and completion isn't currently persisted. Once completion persists, ensure relog
   does not re-grant rewards or re-complete objectives — there is no completed-objective ledger yet.
7. **Repeatable/prereq consistency.** `IsRepeatable` + `ReqMissionId[4]` gating reads
   `CompletedMissionIds`; the schema must store completed ids (and completion counts for repeatables)
   to keep NPC offers and logic-variable conditions correct across sessions.
8. **Fail/abandon path is missing.** `Reaction.FailMission` is a stub and `FailMissionPacket` has no
   sender. If failed/abandoned missions must persist a distinct state, the lifecycle must be wired
   first.
9. **Trigger/reaction re-fire vs persisted grant.** Decide whether one-shot load-trigger grants
   (e.g. mission 554 on map 707) should become idempotent against DB state, or continue re-firing
   each enter with `HandleGiveMission` dedupe as the guard (see §8.4).
10. **[RESOLVED] `GiveMission` re-broadcast on relog.** `HandleGiveMission` dedupes against
    `CurrentQuests`/`CompletedMissionIds` and now returns `false` on decline so the reaction is not
    sent to the client (see §8.4 case 2). Previously it returned `true`, so the map `PerPlayerLoad`
    re-fire on relog broadcast a `GiveMission` 0x206C that re-added a completed mission as active
    client-side while the server kept it completed — the observed "finished mission still shows as
    active, can't turn in, follow-up offered anyway" desync.

---

## Appendix: key source references

- **Client (Ghidra `autoassault.exe`):** `CVOGCharacter_ApplyCreateFromPacket` @`0x00535174`,
  `CVOGCharacter_SerializeCreatePacket` @`0x0052F650`, `CVOGReaction_GiveMission` @`0x005327C0`,
  `CVOGReaction_CompleteObjective` @`0x00533F90`, `Client_RecvObjectiveState` @`0x0080FF00`,
  `Client_PacketDispatch` @`0x00815710`, `Client_RecvCreateCharacter` @`0x008146B0`.
- **Definitions:** `src\AutoCore.Game\Mission\Mission.cs`, `MissionObjective.cs`, `MissionString.cs`,
  `Mission\Requirements\*.cs`.
- **Runtime state:** `src\AutoCore.Game\Structures\CharacterQuest.cs`,
  `Entities\Character.cs:136-246,302-319`, `Structures\LogicVariableStore.cs`, `Structures\Variable.cs`.
- **Lifecycle:** `Managers\NpcInteractHandler.cs`, `Managers\MissionKillProgress.cs`,
  `Entities\Reaction.cs`, `Managers\TriggerManager.cs`, `Map\SectorMap.cs:253-305,493-588`.
- **Packets:** `Packets\Sector\{NpcMissionDialog,MissionDialogResponse,ObjectiveState,
  CompleteDynamicObjective,FailMission,GroupReactionCall,LogicStateChange,CreateCharacterExtended}Packet.cs`,
  `Packets\Global\ConvoyMissions{Request,Response}Packet.cs`, `Constants\GameOpcode.cs`.
- **Loading:** `Managers\Asset\{WADLoader,WadXmlWorldDataLoader,WorldDBLoader}.cs`,
  `Managers\AssetManager.cs`, `Map\MapData.cs`.
- **Persistence precedents:** `src\AutoCore.Database\Char\{CharContext.cs,Models\CharacterExploration.cs,
  Models\CharacterData.cs,Models\Account.cs}`, `Managers\CharacterWorldStatePersistence.cs`,
  `Managers\ExplorationManager.cs`, `Structures\CharacterWorldStateSnapshot.cs`.
- **Prior docs:** `Documentation\{MISSION_SYSTEM,TRIGGER_SYSTEM,REACTION_SYSTEM,
  MISSION_DIALOG_CLIENT_ANALYSIS,FIRST_TIME_FLAGS,PACKET STRUCTURES}.md`,
  `docs\topic-extractions\{server-triggers,set-active-objective,unlock-contobj}.md`.
