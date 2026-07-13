# Mission Testing Progress

**Last updated:** 2026-07-13  
**Current phase:** Hardening pass complete; Stryker.NET installed as local tool (run pending).

## Status summary

| Item | Status |
| ---- | ------ |
| Component map | Done |
| Invariants (linked) | Done |
| Shared infrastructure | Done |
| State-transition matrix | Done |
| Reaction contracts + discovery | Done |
| Trigger cascade contracts | Done |
| E2E scenarios | Done |
| Objective contracts | Done |
| Property / fuzz | Done |
| Fault injection | Done |
| Concurrency (queue) | Done |
| Coverlet measurement | Done — **89.3%** scoped |
| Mutation (Stryker) | **Installed** (local tool 4.16.0); full run not yet executed |
| Final reports | Done |
| Mission/* suite green | **89/89 pass** |

## Tests under `Mission/` (2026-07-13)

| Suite | Methods |
| ----- | ------- |
| CharacterQuestAndMissionStringTests (legacy) | 11 |
| ObjectiveRequirementUnserializeTests (legacy) | 12 |
| MissionStateTransitionTests | 19 |
| MissionRewardIdempotencyTests | 2 |
| MissionPersistenceConcurrencyTests | 3 |
| MissionFaultInjectionTests | 6 |
| MissionReactionContractTests | 11 |
| TriggerCascadeContractTests | 7 |
| MissionEndToEndScenarioTests | 8 |
| MissionPropertyAndFuzzTests | 7 |
| ObjectiveProgressContractTests | 3 |
| **Total** | **89** |

New hardening ≈ **66** methods.

## Bugs fixed this effort

| ID | Summary |
| -- | ------- |
| REG-001 | Double XP on stale Advance |
| REG-002 | Null conn NRE on Advance |

## Coverage

Scoped mission coverlet include: **89.3%** (1927/2158). Gate script wants 90% + per-file 90%; packet Read stubs block formal gate. Documented in `mission-coverage-report.md`.

## Commands last run

```
dotnet test ... --filter FullyQualifiedName~AutoCore.Game.Tests.Mission
→ Passed 89

dotnet test ... (broader mission ecosystem)
→ Passed 301

coverlet + measure-mission-coverage.ps1
→ 89.3% scoped
```

## Exact next action (future agent)

1. Run first Stryker pass: `dotnet tool restore`; `dotnet tool run dotnet-stryker -- --config-file stryker-mission-config.json`  
2. Optional: expand coverlet Include for MissionPersistence / TriggerManager / mission Reaction  
3. Product decisions: multi-req evaluation, FailMission, multi-mission kill credit  
4. Chat admin contract suite  

## Assumptions

- Single-threaded sector logic; real concurrency = persistence queue  
- FailMission remains stub  
- Delete without DoForAllPlayers = personal suppress  
- Kill credits first matching quest only (characterized)  

## Files modified (full effort)

### Production
- `src/AutoCore.Game/Managers/NpcInteractHandler.cs` (REG-001, REG-002)

### Tests (new)
- `Mission/Infrastructure/*`
- `Mission/StateTransition/*`
- `Mission/Reactions/*`
- `Mission/Triggers/*`
- `Mission/Scenarios/*`
- `Mission/Properties/*`
- `Mission/Objectives/*`
- `Managers/MissionStateTriggerReevalTests.cs` (personal suppress asserts)

### Docs
- All of `docs/testing/*`
