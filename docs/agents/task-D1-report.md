# Task D1 report — Controller tests first (sim authority)

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`  
**Scope:** tests only — no D2 controller rewrite, no config flip, no Launcher

## What landed

### 1. `NpcVehiclePhysicsControllerTests.cs`

| Action | Tests |
|--------|--------|
| **Kept** | `Apply_CreatesPhysicsInstance_AndSetsDriveInputs`, `Apply_WaitHold_ZerosThrottleAndSetsSharp`, `Apply_NoCloneBase_FailClosedReturnsHard`, `ExtractBasis_Identity_RightX_ForwardZ` |
| **Rewritten** (sim-driven tolerances) | `Apply_PathCruise_AdvancesAlongPathAtNearHardSpeed`, `Apply_VelocityAlignsWithFacing_NotLateralSlide` |
| **Deleted** (12) | All `IntegrateVertical_*` (heuristic vertical integrator deleted in D2) |
| **Added** `[Ignore("D2")]` contracts (4) | See below |

**Path cruise rewrite:** assert path progress (`Z > 2` over ~1s), bounded lateral slip (`|X| < 4`), and non-trivial planar speed (`> 1`) — not hard-speed kinematic matching.

**Velocity-align rewrite:** looser yaw reorient (`|yaw| < 0.85`), facing alignment `> 0.7`, and explicit lateral-slip ratio `< 0.55`.

### 2. `VehiclePhysicsStabilityTests.cs`

Deleted soft-pull / ride-height tests that only cover helpers D2 removes:

- `SoftPullPlanar_ClampsDrift`
- `SoftPullVertical_ClampsLaunchAndSink`
- `SoftPullPlanarVelocity_ClampsExcess`
- `ResolveRideHeight_DefaultsNearZero_NotFloatingConstant`

Retained stability contracts (suspension clamp flag, integrate speed clamps, grounded drive, null-map collision query).

### 3. Ignore-until-D2 contracts

| Test | Contract for D2 |
|------|-----------------|
| `Apply_PublishesSimPoseVerbatim` | Published pose/vel/**angVel** equal free-running `inst.Body` after `Step` (no force-restore / kinematic yaw-rate substitute) |
| `Apply_SpawnSeatsOnTerrain` | First-create seats chassis via ReGround; quiet vertical velocity after seat |
| `Apply_RecoversWhenBodyFallsOutOfWorld` | `PosY < supportY − 50` → `SetPose(hard)` + ReGround |
| `Apply_DivergenceFromPath_TeleportsAndReGrounds` | Planar `|body − hard| > ResyncDriftThreshold` → teleport to hard + ReGround |

These are marked `[Ignore("D2: …")]` so the suite gate stays green; D2 un-`Ignore`s them when the hybrid force-restore rewrite lands.

## Suite result

```
Passed:  2954
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    8  (4 pre-existing C-phase residuals + 4 new D2 contracts)
Total:   2963
```

Zero **new** failures vs baseline gate.

## Residuals / handoff to D2

1. Un-`[Ignore]` the four D2 contracts above and make them green.
2. Delete production `IntegrateVertical`, force-restore block, `SoftPull*`, pitch-from-probes, dead ride-height helpers (per physics handoff § Phase D2).
3. Do **not** re-apply kinematic reverse-throttle deceleration when physics is authoritative (C8 brake double-decel residual).
4. D3 hooks `ClearPhysicsInstance` on teleport/respawn/death after D2.

## Files touched

**Tests**
- `src/AutoCore.Game.Tests/NpcAi/NpcVehiclePhysicsControllerTests.cs`
- `src/AutoCore.Game.Tests/Physics/Vehicle/VehiclePhysicsStabilityTests.cs`

**Docs**
- `docs/agents/task-D1-report.md` (this file)

**Production:** none (tests-only task).
