# Task D3 report — Lifecycle wiring (`ClearPhysicsInstance`)

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`  
**Scope:** hook `Vehicle.ClearPhysicsInstance()` on discontinuous teleport / respawn / death  
**Not in scope:** opt-in config flip, Launcher, C7 clamps

## What landed

### Production: `Vehicle.cs`

- **`PhysicsDiscontinuityDistance = 8f`** — same scale as `NpcVehiclePhysicsController.ResyncDriftThreshold`.
- **`MaybeClearPhysicsOnDiscontinuousMove(newPos)`** — clears only when:
  1. `PhysicsInstance != null`, and
  2. entity pose → new pos is discontinuous, and
  3. sim body → new pos is also discontinuous  
  Dual-check keeps the instance when the controller already recovered the body to the published pose (path-divergence / freefall recovery → `ApplyServerMove` publish must not drop sim state). Continuous streaming deltas stay under 8u and never clear.
- **`SetPosition(position)`** — public discontinuous reposition API (clears then writes `Position`). Prefer over raw `Position =` when a physics instance may exist.
- **`ApplyServerMove`** — calls `MaybeClearPhysicsOnDiscontinuousMove` before writing pose (continuous ticks keep instance).
- **`OnDeath`** — always `ClearPhysicsInstance()` at entry (player + NPC paths).

### Production: call sites

| Site | Behavior |
|------|----------|
| `Vehicle.ApplyServerMove` | Clear only if discontinuous (dual-check) |
| `Vehicle.SetPosition` | Clear only if discontinuous (dual-check) |
| `Vehicle.OnDeath` | Always clear |
| `RespawnManager.ApplyPose` | Always clear + `SetPosition` (lifecycle teleport) |
| `MapManager` map-transfer | Always clear + `SetPosition` (map teleport) |

### Tests: `NpcVehiclePhysicsControllerTests.cs`

| Test | Contract |
|------|----------|
| `ApplyServerMove_ContinuousMove_KeepsPhysicsInstance` | ~1u stream keeps same instance |
| `Apply_AfterDiscontinuousTeleport_RecreatesAndReGrounds` | teleport clears → next `Apply` recreates + first-create ReGround seats |
| `SetPosition_Discontinuous_ClearsPhysicsInstance` | `SetPosition` jump clears |
| `OnDeath_ClearsPhysicsInstance` | death drops instance (player path) |
| `Apply_DivergenceFromPath_TeleportsAndReGrounds` (extended) | recovery **publish** keeps instance (body already at hard) |

## Verification (related paths)

- **NpcTicker wait-hold ghost-dirty:** `NpcTickerTests` + `NpcPathPaceRegressionTests` (holding re-dirties `PositionMask`) — green; D3 does not touch the hold branch (same-position / thr=0 / sharp=1 path never discontinuous-clears).
- **ForeignNpcDriverWire / ghost packing:** `ForeignNpcDriverWireTests` + `GhostVehicleWireRegressionTests` — green; thr/steer/sharp/angVel packing unchanged (clear only nulls `PhysicsInstance`).
- **Respawn:** `RespawnManagerTests` green with `ApplyPose` always clearing.

## Suite result

```
Passed:  2962
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    4  (pre-existing C-phase residuals)
Total:   2967
```

Delta vs D2 gate: +4 passed (D3 contracts). Zero new failures.

## Residuals / notes

1. **Raw `Position =` still bypasses clear** on non-hooked call sites (tests, spawn, etc.). Production lifecycle teleports go through `SetPosition` / `ApplyServerMove` / `OnDeath` / Respawn / MapManager. Residual: any future discontinuous script that assigns `Position` directly should call `SetPosition` or `ClearPhysicsInstance`.
2. **Opt-in unchanged:** physics still requires `NpcVehiclePhysicsEnabled` + `controllerTier=physics`.
3. **Next:** Phase F / config flip is user-approved live only; no default flip in this task.

## Files touched

**Production**
- `src/AutoCore.Game/Entities/Vehicle.cs`
- `src/AutoCore.Game/Managers/RespawnManager.cs`
- `src/AutoCore.Game/Managers/MapManager.cs`

**Tests**
- `src/AutoCore.Game.Tests/NpcAi/NpcVehiclePhysicsControllerTests.cs`

**Docs**
- `docs/agents/task-D3-report.md` (this file)
