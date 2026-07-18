# Mission System Testing Map

> **Canonical handler behavior:** [missionHandler.md](../missionHandler.md) — lifecycle, requirement-type processing rules, and consistency checklist. This file is the **test inventory** companion.

Living inventory of mission-related production code and how it must be tested.  
Trace source: implementation as of 2026-07-12 (not enum wish-lists).

## 1. Lifecycle state-transition table

| Current State | Operation / Event | Preconditions | Expected Next State | Side Effects | Invalid / Guard Cases |
| ------------- | ----------------- | ------------- | ------------------- | ------------ | --------------------- |
| Not tracked | `Reaction.GiveMission` (G1=missionId) | Character resolved; missionId > 0 | Active seq=0, progress empty | Persist Upsert; optional ObjectiveState + journal; `OnMissionStateChanged` | Already active → no re-add, return false (no 0x206C); completed + non-repeatable → no re-grant |
| Not tracked | `NpcInteractHandler.GrantMission` | `CanOfferMission` | Active seq=0 | Same as grant | Level/continent/NPC/prereq fail → no grant |
| Not tracked | Dialog accept (`MissionDialogResponse`) | Offer path | Active | GrantMission | Ineligible → no-op |
| Active | Kill credit (`MissionKillProgress`) | Active kill/kill_aggregate req matches | Progress++ or complete | ObjectiveState on partial; Advance on threshold | Wrong player/CBID; already completed mission skipped |
| Active | Collect drop/pickup (`MissionCollectProgress`) | Active collect + optional target + drop % | Progress from inventory; final waits giver | Mission ground loot; absolute ObjectiveState | Wrong CBID; pct=0; already at NumToCollect |
| Active | UseItem UseObject | Active useitem req | Advance/complete | AdvanceOrCompleteObjective | Wrong target |
| Active | AutoPatrol | Patrol AutoComplete + in radius | Partial pad progress → advance/complete **or** ensure deliver NPC | Sibling deliver blocks complete | Multi-waypoint/sequential/laps via `MissionPatrolProgress` |
| Active | Deliver / kill / collect dialog turn-in | Active deliver NPC match or kill/collect giver ready | Advance **or** complete | Soft-pedal (no 0x2070 when client already completed); rewards | Wrong NPC; not deliver objective |
| Active mid-seq | `AdvanceOrCompleteObjective` (has next) | Called from kill/patrol/useitem/reaction | seq → next | 0x2070; progress max on old seq; Upsert; ObjectiveState next; phase replay | Multi-req treated satisfied (incomplete) |
| Active final | `AdvanceOrCompleteObjective` (no next) | Final objective | Not active; in CompletedMissionIds | Remove quest; Complete persist; rewards; journal; phase replay | Duplicate call after remove → no quest |
| Active | `Reaction.CompleteObjective` | G1 objective id matches **active** seq | Advance or complete | Shared Advance path; returns false (no 0x206C) | Wrong/stale objective seq → no-op |
| Active | `Reaction.FailMission` | Any | **Unchanged (STUB)** | IncompleteHandlerLog only | No fail state applied |
| Active | `ForceCompleteMission` | Quest present | Completed | Rewards + 0x2070 + Complete persist | Missing quest → no-op |
| Active | Progress after complete | Mission in CompletedMissionIds | No active quest | Kill loop skips completed | Must not re-progress |
| Completed non-rep | GiveMission / offer | IsRepeatable==0 | Still completed only | No CurrentQuests add | Client 0x206C suppressed on re-fire |
| Completed repeatable | GiveMission | IsRepeatable!=0 | May re-grant | New active quest | (coverage gap if rare) |
| Any | `LoadMissions` / `SetMissionsForTests` | DB rows or test rows | Memory rebuilt | Max from template; progress overlay | Missing template → quest still loaded with defaults |
| Active | `/clearAllMissions` / DeleteAll | Admin | Empty active+completed memory+DB | Queue drop | Diagnostic only |
| Active | `/removeCurrentMission` | Admin | Active cleared; completed kept | Active DB delete | Diagnostic only |

Legend: **STUB** = implemented as no-op / log only today.

## 2. Component cards

### 2.1 Mission template (`Mission/Mission.cs`)

| Field | Detail |
| ----- | ------ |
| Responsibility | Clonebase mission definition (prereqs, rewards metadata, objectives dict) |
| Inputs | Binary WAD reader; `CreateForTests` |
| Outputs | Template fields consumed by grant/eligibility/rewards |
| State read | None (immutable template) |
| State mutates | None at runtime (must remain immutable) |
| Events | None |
| Dependencies | `MissionObjective`, AssetManager |
| Invariants | Template never mutated as instance state |
| Existing tests | `CharacterQuestAndMissionStringTests` (CreateForTests, Read) |
| Missing tests | Req-mutation under grant/progress; prereq field boundary offer checks |
| Risks | Accidental mutation of shared AssetManager instance |
| Categories | Unit, invariant |

### 2.2 MissionObjective + Requirements (`Mission/MissionObjective.cs`, `Requirements/*`)

| Field | Detail |
| ----- | ------ |
| Responsibility | Objective metadata + requirement list (14 types) |
| Inputs | Binary + optional XML unserialize |
| Outputs | Matchers for kill/deliver/patrol/useitem; CompleteCount; XP fields |
| Existing tests | `ObjectiveRequirementUnserializeTests` |
| Missing tests | Progress contracts per type; boundaries; multi-req evaluation (char. incomplete) |
| Risks | Incomplete multi-req / CompleteCount handling in Advance |
| Categories | Unit, contract, characterization |

**Requirement types:** Kill, KillAggregate, Collect, Deliver, Escort, CrazyTaxi, Km, Mission, Money, Patrol, Stunt, CharacterLevel, TimePlayed, UseItem.

### 2.3 CharacterQuest (`Structures/CharacterQuest.cs`)

| Field | Detail |
| ----- | ------ |
| Responsibility | Per-character active quest wire shape (72 bytes) + progress arrays |
| Mutates | ActiveObjectiveSequence, State, ObjectiveProgress/Max |
| Existing tests | Write layout, PopulateFromMission growth |
| Missing tests | Slot normalization clamps; multi-slot FirstStateSlot |
| Categories | Unit, serialization |

### 2.4 Character mission lists + load (`Character.cs`, `Character.Missions.cs`)

| Field | Detail |
| ----- | ------ |
| Responsibility | `CurrentQuests`, `CompletedMissionIds`; load from CharContext; create-packet emit |
| Mutates | Lists on grant/complete/load/admin clear |
| Existing tests | Persistence load hooks, create-packet completed ids |
| Missing tests | Multi-player isolation on same map; corrupted progress blob |
| Categories | Integration, persistence |

### 2.5 MissionPersistence + Queue

| Field | Detail |
| ----- | ------ |
| Responsibility | Latest-wins upsert/complete enqueue; background flush; disconnect drain |
| Mutates | DB rows via injectables |
| Events in | OnQuestChanged, OnMissionCompleted |
| Existing tests | 35 methods (queue, pack, flush fail-retry, grant enqueue) |
| Missing tests | Concurrent flush stress; multi-char isolation stress; load missing template |
| Risks | Memory/DB divergence on failed flush; double Complete |
| Categories | Integration, concurrency, fault injection |

### 2.6 NpcInteractHandler

| Field | Detail |
| ----- | ------ |
| Responsibility | UseObject, dialog offer/accept, deliver turn-in, AutoPatrol, UseItem, Grant, ForceComplete, Advance, rewards |
| Inputs | Packets, character, map NPCs |
| Outputs | Packets, quest mutations, persist, triggers, rewards |
| Existing tests | NpcInteractUseObject*, Deliver*, UseItem*, AutoPatrol*, CoverageGap*, HealthGated*, SoftPedal*, GiverCbid*, **MissionRogersUseObjectHeavyRegression*** |
| Missing tests | Full state-transition matrix; duplicate complete; prereq chain offer; force-complete rewards once |
| Risks | Soft-pedal timing; deliver bypassing shared path history; multi-req complete |
| Categories | Component, E2E, transition |

### 2.7 MissionKillProgress

| Field | Detail |
| ----- | ------ |
| Responsibility | Kill credit + threshold complete |
| Existing tests | Unit matchers + integration progress |
| Missing tests | Two missions same kill; post-complete ignore; player B isolation |
| Categories | Unit, contract, isolation |

### 2.7b MissionCollectProgress

| Field | Detail |
| ----- | ------ |
| Responsibility | Collect kill-to-loot drop (`OptionalDropPercent`), inventory progress, giver turn-in |
| Existing tests | `MissionCollectProgressUnitTests`, `MissionHideAndSeekCollectHeavyRegressionTests`, ObjectiveState/Cargo take specs |
| Missing tests | Convoy share; pct=0 world collect; multi-target optional lists |
| Categories | Unit, HeavyRegression |

### 2.8 Reaction mission handlers (`Entities/Reaction.cs`)

| Type | Behavior | Status |
| ---- | -------- | ------ |
| GiveMission (30) | Track quest + persist + resync | Implemented |
| CompleteObjective (31) | Advance if active seq | Implemented |
| FailMission (72) | Log only | **STUB** |
| GiveMissionDialog (37) | Client via 0x206C | Client-notify |
| SetActiveObjective (60) | Updates sequence + persist | Partial UI packets |
| Add/DelMissionString | Log | STUB |

Existing: PerPlayerLoadMissionGrantTests, various Reaction* for non-mission types.  
Missing: unified mission-reaction contract suite + discovery gate.

### 2.9 TriggerManager

| Field | Detail |
| ----- | ------ |
| Responsibility | Volume enter, mission re-eval, variable re-eval, cascade depth 16, re-entrancy set |
| Existing tests | MissionStateTriggerReeval, LogicVariableAndTriggerCoverage |
| Missing tests | Cascade overflow safety; latch isolation multi-player; post-complete no fire for mission conditions |
| Categories | Contract, concurrency (re-entry), cascade |

### 2.10 MissionWorldPhaseRules + map phase

| Field | Detail |
| ----- | ------ |
| Responsibility | Blocking deliver siblings; force client complete rules; spawn/suppress replay |
| Existing tests | MissionWorldPhaseRulesTests, MissionPhaseWorldHandlerTests, SpawnReplay |
| Missing tests | More phase transitions on multi-seq chains |
| Categories | Component, E2E |

### 2.11 Rewards (`ApplyMissionCompleteRewards` + ExperienceService)

| Field | Detail |
| ----- | ------ |
| Existing tests | ApplyMissionCompleteRewardsTests (21) |
| Missing tests | Double complete through Advance; XP fail then no second grant; isolation |
| Categories | Reward safety, fault injection |

### 2.12 Packets

| Packets | Tests |
| ------- | ----- |
| MissionDialog*, ObjectiveState, CompleteDynamicObjective, FailMission, Convoy*, UseObject, AutoPatrol | Mission*Packet*, NpcMissionDialog* |
| Missing | Malformed read fuzz; FailMission emit when stub fixed |

### 2.13 Chat admin

| Commands | Behavior |
| -------- | -------- |
| /giveMission, /completeMission, list, clearAll, removeCurrent | Mutate server state |
| Existing | Partial via force complete paths |
| Missing | Full command contract isolation |

### 2.14 Features N/A in this codebase

| Prompt concept | AutoCore status |
| -------------- | --------------- |
| Daily/weekly reset engine | Not present as dedicated system |
| Procedural mission generation | MissionType includes random categories; no generator service found |
| Shared group mission instances | Not implemented |
| Separate mission-instance identity | Mission id per character only |
| Abandon as first-class state | Dialog path + admin clear; no Failed enum terminal |

## 3. Event / data flow (happy path)

```
Map PerPlayerLoad / NPC dialog accept / reaction GiveMission
  → CharacterQuest added → MissionPersistence Upsert
  → ObjectiveState + journal → OnMissionStateChanged → triggers

Kill / UseItem / AutoPatrol / Deliver / CompleteObjective reaction
  → progress or AdvanceOrCompleteObjective
  → 0x2070 (server-driven) OR soft-pedal (dialog deliver)
  → rewards on final → Complete persist → completed set
  → OnMissionStateChanged → phase world replay
```

## 4. Primary risk ranking

1. Double reward / double complete  
2. Re-grant completed non-repeatable (client desync)  
3. Persistence latest-wins loss or failed-flush divergence  
4. Cross-player mutation  
5. Cascade loops / re-entrancy corruption  
6. Incomplete multi-req / patrol semantics silently wrong  
7. FailMission stub if maps depend on fail clearing state  

## 5. Existing test inventory (files)

See progress doc and repo under `src/AutoCore.Game.Tests/` (Managers/Mission*, NpcInteract*, Entities/PerPlayerLoad*, Experience/ApplyMission*, Map/Mission*, Mission/*, Packets/Mission*, Entities/Reaction*).

## 6. Required new test categories (summary)

| Category | Location target |
| -------- | --------------- |
| State transition matrix | `Mission/StateTransition/` |
| Mission reaction contracts | `Mission/Reactions/` |
| Trigger contracts (mission paths) | `Mission/Triggers/` |
| Objective progress contracts | `Mission/Objectives/` |
| E2E scenarios | `Mission/Scenarios/` |
| Shared harness | `Mission/Infrastructure/` |
| Concurrency | extend MissionPersistenceTests |
| Property/fuzz | `Mission/Properties/` |
