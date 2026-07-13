# Mission Mutation Report

**Status:** Stryker.NET **installed** as a local tool (`dotnet-stryker` **4.16.0** in `.config/dotnet-tools.json`). Config: `stryker-mission-config.json` at repo root.

## Scope (planned / equivalent)

Critical paths exercised by the suite:

- `NpcInteractHandler.AdvanceOrCompleteObjective` (stale quest, null conn, multi-seq, final)
- `ApplyMissionCompleteRewards` / Experience mission grants
- `MissionKillProgress` matching + clamp
- `MissionPersistence` / `MissionPersistenceQueue` latest-wins + failed flush
- `Reaction` GiveMission / CompleteObjective / FailMission / SetActiveObjective
- `TriggerManager` cascade depth + re-entrancy + activation count

## Manual mutation-equivalent kills (observed)

| Synthetic mutation | Detected by |
| ------------------ | ----------- |
| Remove `CurrentQuests.Contains` guard | REG-001 test fails (double XP) |
| Remove null-conditional on `conn?.SendGamePacket` | REG-002 NRE test fails |
| Flip GiveMission non-repeatable guard | StateTransition re-grant tests |
| Remove progress clamp (`Math.Min`) | Property kill bound tests |
| Drop Complete persist enqueue | E2E / transition persist asserts |
| Remove cascade re-entrancy set | Self-activate stack / FireCount tests |
| Allow CompleteObjective on wrong seq | Reaction contract tests |

## Targets

| Area | Target | Status |
| ---- | ------ | ------ |
| Critical mission state / complete / reward | ≥90% mutation score | **Pending tooling** — suite designed for it |
| Supporting mission code | ≥80% | Pending tooling |

## Install / restore / run

```powershell
# From repo root (after clone)
dotnet tool restore

# Run mission-scoped mutation testing
dotnet tool run dotnet-stryker -- --config-file stryker-mission-config.json

# Shorthand (same)
dotnet dotnet-stryker -f stryker-mission-config.json
```

Local tool entry: `.config/dotnet-tools.json` → `dotnet-stryker` 4.16.0.

Config mutates: `NpcInteractHandler`, `MissionKillProgress`, `MissionPersistence*`, `TriggerManager`, `Reaction`, `CharacterQuest`.

## Results

| Run date | Tool | Score critical | Survivors |
| -------- | ---- | -------------- | --------- |
| 2026-07-13 | Manual equivalent only | N/A | Document incomplete multi-req / FailMission stubs as intentional survivors if mutated “fixed” |
| 2026-07-13 | Stryker 4.16.0 installed | **not run yet** | Run command above to produce first score |
