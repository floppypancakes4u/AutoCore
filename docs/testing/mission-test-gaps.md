# Mission Test Gaps

Explicit inventory of untested, partially tested, or N/A paths. Prefer documenting gaps over hiding them.

## Production incomplete (characterization only unless defect)

| Gap | Evidence | Test coverage |
| --- | -------- | ------------- |
| FailMission does not clear quests | `Reaction` stub | `MissionReactionContractTests.FailMission_Stub_*` |
| Multi-req objective one-shot complete | Advance IncompleteHandlerLog | `ObjectiveProgressContractTests.MultiRequirement_*` |
| CompleteCount > 1 ignored | Advance log | Characterization in Advance path |
| Multi-waypoint/sequential/lap patrol | **fixed** — `MissionPatrolProgress` | `AutoPatrolTests` multi-pad + `MissionPatrolProgressTests` |
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

## Closed this completion pass

| Item | Resolution |
| ---- | ---------- |
| Stryker install + critical run | 75.22% pass (`stryker-mission-critical-config.json`) |
| Stryker broader run | 58.09% pass (`stryker-mission-config.json`) |
| Coverlet expand + hard gate | 98.77% hard-scoped pass |
| Chat admin mission contracts | `MissionChatCommandContractTests` |
| Packet Read paths | UseObject/AutoPatrol 100% under mission filter |

## Remaining priority gaps

| Priority | Gap | Notes |
| -------- | --- | ----- |
| P2 | Critical mutation → 90% | 8 CharacterQuest + 6 MissionPersistence survivors |
| P2 | Broader handler mutation | NpcInteractHandler ~63% killed/survived |
| P2 | Multi-mission kill-all policy | Product decision (first-only characterized) |
| P3 | Soak/load | Not in unit suite |

## Flaky tests

_None tracked._

## Skipped tests

_None._
