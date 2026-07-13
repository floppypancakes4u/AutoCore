# Mission Coverage Report

## Baseline (this effort)

| Metric | Value | Date |
| ------ | ----- | ---- |
| Scoped line coverage (`mission.coverlet.runsettings`) | **89.3%** (1927/2158) | 2026-07-13 |
| Scoped classes | 29 | |
| Mission/* test methods (all) | 89 | 2026-07-13 |
| Broader mission-related filter | 301+ pass | 2026-07-13 |

## Per-file highlights (after hardening)

| File | Rate | Notes |
| ---- | ---- | ----- |
| Requirement models (most) | 100% | Unserialize + use |
| CharacterQuest | 97.3% | |
| MissionKillProgress | 87.2% | Edge match paths remain |
| NpcInteractHandler | 84.1% | Large; soft-pedal / rare branches |
| AutoPatrolPacket / UseObjectPacket | 16–25% | Wire Read paths — low value for unit chase |
| CompleteDynamicObjective / FailMission / ObjectiveState packets | 100% | |

## How measured

```powershell
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj `
  --collect:"XPlat Code Coverage" `
  --settings src/AutoCore.Game.Tests/mission.coverlet.runsettings `
  --results-directory TestResults/mission-cov-hardening `
  --filter "TestCategory=MissionCritical|FullyQualifiedName~Mission|FullyQualifiedName~NpcInteract|FullyQualifiedName~ApplyMission"

powershell -File scripts/measure-mission-coverage.ps1 `
  -CoverageFile TestResults/mission-cov-hardening/<guid>/coverage.cobertura.xml
```

## Gate status

`measure-mission-coverage.ps1` requires **≥90% overall and per file**. Current run is **89.3% overall** and fails mainly on:

- Packet `Read` methods for UseObject/AutoPatrol (serialization only)
- Residual NpcInteractHandler / MissionKillProgress branches

**Decision:** Do **not** chase trivial packet Read coverage. Prefer invariant/mutation detection over percent theater. Raise gate after expanding include list for `MissionPersistence` / mission `Reaction` handlers with dedicated contract coverage.

## Intentionally outside current coverlet Include

| Component | Why |
| --------- | --- |
| `Mission.cs` / `MissionObjective.cs` WAD `Read` | Asset I/O; CreateForTests used instead |
| Full `Reaction.cs` | Large non-mission surface; mission paths covered by contract tests |
| `TriggerManager.cs` | Covered by contract tests; not in legacy include |
| `MissionPersistence.cs` | Covered by persistence tests; not in legacy include |

Recommend a future `mission-critical.coverlet.runsettings` that adds persistence + mission reaction handlers without packet Read noise.
