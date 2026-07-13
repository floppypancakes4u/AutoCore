# Mission Testing Progress

**Last updated:** 2026-07-13  
**Current phase:** **COMPLETE** for planned defensive hardening (tests + Stryker + coverage gates + docs).

## Status summary

| Item | Status |
| ---- | ------ |
| Component map / invariants / plans | Done |
| Shared infrastructure | Done |
| State transitions / rewards / concurrency | Done |
| Reaction / trigger / objective contracts | Done |
| E2E scenarios / property / fuzz / fault injection | Done |
| Chat mission admin contracts | Done |
| Stryker installed + runs | Done — critical **75.22%** (pass) |
| Coverlet hard-scoped gate | Done — **98.77%** (pass) |
| Mission/* suite | **105/105 pass** |
| REG-001 / REG-002 | Fixed + documented |

## Final commands

```powershell
# Full mission namespace tests
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter "FullyQualifiedName~AutoCore.Game.Tests.Mission"

# Critical category
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter TestCategory=MissionCritical

# Mutation (critical core)
dotnet tool restore
dotnet tool run dotnet-stryker -- --config-file stryker-mission-critical-config.json

# Mutation (broader handlers)
dotnet tool run dotnet-stryker -- --config-file stryker-mission-config.json

# Coverage gate
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj `
  --collect:"XPlat Code Coverage" `
  --settings src/AutoCore.Game.Tests/mission.coverlet.runsettings `
  --results-directory TestResults/mission-cov-done `
  --filter "FullyQualifiedName~Mission|FullyQualifiedName~NpcInteract|..."
powershell -File scripts/measure-mission-coverage.ps1 -CoverageFile TestResults/mission-cov-done/<guid>/coverage.cobertura.xml
```

## Measurements

| Metric | Value |
| ------ | ----- |
| Mission/* tests | 105 |
| MissionCritical (approx) | 68+ |
| Hard line coverage gate | **98.77%** PASS |
| Critical mutation score | **75.22%** PASS (break 65) |
| Broader mutation score | **58.09%** PASS (break 50) |
| Queue mutation | **100%** |

## Remaining product/test debt (explicit, not blocking)

1. Raise critical mutation toward 90% (kill remaining CharacterQuest/Persist survivors)  
2. Broader NpcInteractHandler mutation score  
3. Product: FailMission, multi-req evaluation, multi-mission kill-all policy  
4. Optional: soak/load tests  

## Files modified (this completion pass)

### Tooling
- `.config/dotnet-tools.json` (dotnet-stryker 4.16.0)
- `stryker-mission-config.json`
- `stryker-mission-critical-config.json`
- `scripts/measure-mission-coverage.ps1` (soft/hard gate)
- `src/AutoCore.Game.Tests/mission.coverlet.runsettings` (expanded include)

### Tests
- `Mission/Chat/MissionChatCommandContractTests.cs`
- `Mission/Properties/CharacterQuestMutationHardeningTests.cs`
- (prior session suites under Mission/*)

### Production (earlier)
- `NpcInteractHandler.cs` REG-001, REG-002

### Docs
- All `docs/testing/*` updated to final state
