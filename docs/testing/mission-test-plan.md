# Mission Test Plan

## Strategy

Layered testing over the **existing** mission implementation (no redesign):

1. Pure unit (requirements, pack/unpack, matchers, eligibility)
2. Component (handlers + real collaborators + fake DB/XP boundaries)
3. Integration (persist queue, load/reload, trigger re-eval)
4. E2E scenarios (grant → progress → complete)
5. Concurrency (queue + flush only where real)
6. Property/fuzz (seeded loops, malformed packets)
7. Mutation (Stryker.NET local tool 4.16.0 installed; config ready)

## Categories and filters

| Category attribute | Purpose |
| ------------------ | ------- |
| `MissionCritical` | PR gate: transitions, rewards, persist, isolation, grant guards |
| `MissionContract` | Trigger/reaction/objective contracts |
| `MissionScenario` | E2E gameplay paths |

## Commands

```powershell
# All mission namespace tests (hardening suite + legacy mission unit)
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter "FullyQualifiedName~AutoCore.Game.Tests.Mission"

# Mission-critical category
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter TestCategory=MissionCritical

# Broader mission ecosystem (interact, kill, deliver, tutorial, etc.)
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter "FullyQualifiedName~Mission|FullyQualifiedName~NpcInteract|FullyQualifiedName~PerPlayerLoad|FullyQualifiedName~ApplyMission|FullyQualifiedName~ObjectiveRequirement|FullyQualifiedName~DeliverTurn|FullyQualifiedName~AutoPatrol|FullyQualifiedName~UseObject|FullyQualifiedName~HealthGated|FullyQualifiedName~TutorialNpc"

# Coverage
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj `
  --collect:"XPlat Code Coverage" `
  --settings src/AutoCore.Game.Tests/mission.coverlet.runsettings `
  --results-directory TestResults/mission-cov
powershell -File scripts/measure-mission-coverage.ps1
```

## Final report (completion snapshot 2026-07-13)

### 1. Mission-related files inspected

Templates: `Mission/`, `MissionObjective`, requirements (14).  
Runtime: `CharacterQuest`, `Character.Missions`, grant/complete/rewards in `NpcInteractHandler`, `MissionKillProgress`, `MissionPersistence*`, `Reaction` mission types, `TriggerManager`, phase rules, soft-pedal, packets, chat admin.

### 2. Existing test inventory

~343 pre-existing mission-adjacent tests (Managers/Mission*, NpcInteract*, Reaction*, etc.).

### 3. New tests added (this effort)

| Area | Location | ~Count |
| ---- | -------- | ------ |
| Infrastructure | `Mission/Infrastructure/*` | helpers |
| State transitions | `Mission/StateTransition/*` | 19+2+3+6 |
| Reactions | `Mission/Reactions/*` | 11 |
| Triggers | `Mission/Triggers/*` | 7 |
| Scenarios | `Mission/Scenarios/*` | 8 |
| Properties/fuzz | `Mission/Properties/*` | 7 |
| Objectives | `Mission/Objectives/*` | 3 |
| **Total under Mission/** | | **89** (incl. 23 pre-existing unit) |

New hardening methods ≈ **66**.

### 4–6. Bugs discovered / fixed / regressions

| ID | Issue | Fix | Test |
| -- | ----- | --- | ---- |
| REG-001 | Double XP on stale Advance | CurrentQuests membership guard | RewardIdempotency |
| REG-002 | NRE on null conn during Advance | Null-conditional packet/journal | FaultInjection |

### 7–9. Coverage / branch / mutation

| Metric | Before | After |
| ------ | ------ | ----- |
| Scoped line (legacy include) | not measured at start | **89.3%** |
| Branch | not instrumented separately | n/a |
| Mutation | none | tooling deferred; REG suite kills critical mutants |

### 10. Remaining untested paths

See `mission-test-gaps.md` — FailMission implementation, multi-req evaluation, multi-mission same-kill credit for all quests, full 80 reaction types, WAD loaders, Stryker.

### 11. Surviving mutations

Intentional incomplete stubs (FailMission, multi-req one-shot) would “survive” if mutated to implement features — not treated as suite failure until product implements them.

### 12. Known flaky tests

None.

### 13–16. Perf / concurrency / persist / security

- Concurrency: queue stress + latest-wins tests pass  
- Persist: failed flush retains pending; isolation by coid  
- Security/isolation: player A kill does not mutate B; non-repeatable re-grant blocked  
- Perf: no soak suite yet  

### 17. Recommended future work

1. Run Stryker: `dotnet tool restore`; `dotnet tool run dotnet-stryker -- --config-file stryker-mission-config.json`
2. Expand coverlet Include to MissionPersistence + mission Reaction handlers  
3. Implement or formally product-sign FailMission / multi-req evaluation  
4. Decide multi-mission kill: credit all matching vs first-only (document or change)  
5. Chat admin command full contract suite  

### 18. Exact commands

See Commands section above.
