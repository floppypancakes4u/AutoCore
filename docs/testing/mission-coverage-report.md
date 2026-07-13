# Mission Coverage Report

## Final measurement (2026-07-13)

| Metric | Value |
| ------ | ----- |
| Full scoped include (all patterns) | **84.06%** (2426/2886) |
| **Hard-scoped gate** (excl. soft-gate files) | **98.77%** (885/896) — **PASS ≥90%** |
| Soft-gate files | ChatCommandService, NpcInteractHandler, TriggerManager, MissionPersistence, MissionKillProgress, packets (now 100%) |

## Hard-scoped highlights (gate)

| File | Rate |
| ---- | ---- |
| CharacterQuest | **100%** |
| MissionWorldPhaseRules | **100%** |
| MissionString / IncompleteHandlerLog | **100%** |
| Requirement models (most) | **100%** |
| UseObjectPacket / AutoPatrolPacket | **100%** |
| MissionPersistenceQueue | **94%** |
| MissionClientSoftPedal | **93.5%** |

## Soft-gate (reported, not failing)

| File | Rate | Why soft |
| ---- | ---- | -------- |
| ChatCommandService | 32.5% | Multi-domain; mission commands covered by contract tests |
| MissionPersistence | 73.3% | ThreadPool background flush intentionally off in unit tests |
| NpcInteractHandler | 84.6% | Large multipath; residual soft-pedal / rare branches |
| TriggerManager | 85.1% | Skill pulse / deferred spawn edges |
| MissionKillProgress | 87.2% | Partial-progress packet branches |

## Commands

```powershell
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj `
  --collect:"XPlat Code Coverage" `
  --settings src/AutoCore.Game.Tests/mission.coverlet.runsettings `
  --results-directory TestResults/mission-cov-done `
  --filter "FullyQualifiedName~Mission|FullyQualifiedName~NpcInteract|FullyQualifiedName~ApplyMission|FullyQualifiedName~AutoPatrol|FullyQualifiedName~UseObjectPacket|FullyQualifiedName~PerPlayerLoad|FullyQualifiedName~MissionKill|FullyQualifiedName~TutorialNpc|FullyQualifiedName~HealthGated"

powershell -File scripts/measure-mission-coverage.ps1 `
  -CoverageFile TestResults/mission-cov-done/<guid>/coverage.cobertura.xml
```

## Include expansion (this effort)

Added to coverlet + measure script: `MissionPersistence*`, `MissionWorldPhaseRules`, `MissionClientSoftPedal`, `TriggerManager`, `ChatCommandService`.
