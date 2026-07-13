# Mission Test Gaps

Explicit inventory of untested, partially tested, or N/A paths. Prefer documenting gaps over hiding them.

## Production incomplete (characterization only unless defect)

| Gap | Evidence | Test coverage |
| --- | -------- | ------------- |
| FailMission does not clear quests | `Reaction` stub | `MissionReactionContractTests.FailMission_Stub_*` |
| Multi-req objective one-shot complete | Advance IncompleteHandlerLog | `ObjectiveProgressContractTests.MultiRequirement_*` |
| CompleteCount > 1 ignored | Advance log | Characterization in Advance path |
| Multi-waypoint/sequential/lap patrol | AutoPatrol logs | Existing incomplete warnings |
| Client-only reactions (UI/waypoints/text) | return true + 0x206C | Out of mission authority scope |
| Kill credits only first matching mission | MissionKillProgress early return | `Scenario_TwoMissions_SameKill_*` documents contract |

## Features not in codebase (N/A)

- Daily/weekly mission reset service  
- Procedural mission generator with seeds  
- Shared/group mission instances  
- Mission expiry timers as first-class state  

## Closed this effort (was gap)

| Item | Resolution |
| ---- | ---------- |
| State-transition matrix | MissionStateTransitionTests |
| Double complete / double reward | REG-001 + idempotency tests |
| Player isolation | PlayerIsolation_KillByA_DoesNotMutateB |
| Template immutability | Template_NotMutated_ByGrantAndComplete |
| Cascade / re-entrancy | TriggerCascadeContractTests |
| Mission reaction contracts + discovery | MissionReactionContractTests |
| Persist concurrency / fault | MissionPersistenceConcurrency + FaultInjection |
| Null conn crash on complete | REG-002 |
| E2E reload mid-progress | Scenario_PartialKill_FlushReload_Finish |
| Property/fuzz pack + malformed | MissionPropertyAndFuzzTests |

## Remaining priority gaps

| Priority | Gap | Notes |
| -------- | --- | ----- |
| P1 | Stryker full run + score gate | Tool installed; first run pending |
| P1 | Coverlet expand to Persistence/Reaction/TriggerManager | Legacy include list |
| P2 | Chat admin full contract | Partial via ForceComplete |
| P2 | Packet Read paths | Low value (UseObject/AutoPatrol) |
| P2 | Multi-mission kill-all policy | Product decision |

## Flaky tests

_None tracked._

## Skipped tests

_None._
