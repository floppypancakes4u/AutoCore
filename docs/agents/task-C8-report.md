# Task C8 report — Wire ticked service brake into friction path

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`

## What landed

### 1. `HkVehicleBrake` — live `hkDefaultBrake_update` (0x64e6f0)

| API | Role |
|-----|------|
| `DeriveBrakePedal(thr)` | Reverse component of throttle axis (`max(0, thr)`, Accel=−1 / Reverse=+1) |
| `ComputeServiceBrakeTorque` | Peak = `maxBreakingTorque * pedal` (was vestigial) |
| `ComputeOpposingSpinBrakeTorque` | `raw = −spin · r · mass · invDt · r`, clamp ±peak |
| `ComputeIsBlocked` | `(hbConn && hb) \|\| (minPedal <= pedal)` — AA `minTime=0` |
| `UpdateWheel` | Full per-wheel outputs |
| `BrakeTorqueToFrictionForce` | Retail `local_3ec = T/r` helper |

`wheelsMass` = `HkPhysicsConstants.WheelsMassScale` = 15.0 (`DAT_00aaa7a4` / `wheel+0x84`).

### 2. Runtime + sim wiring

- `HkWheelRuntimeState.BrakeTorque` / `.IsBlocked`
- `VehicleActionSim.ApplyBrakeUpdate` after collide/spin, before friction (retail child order)
- Spin integrate: `integrate = InContact && !IsBlocked` (lock zeros ω)
- Friction: axle-averaged `BrakeTorque * contactGate` folded into `DrivePack`
- **Double-decel guard:** engine pack uses `throttleSign = thr < 0 ? −1 : 0` — reverse thr is pedal only, not reverse drive

### 3. Tests / docs

- `brake_goldens.json` (8 vectors + pedal derivation) + `BrakeOracleTests.cs`
- Expanded `HkVehicleBrakeTests.cs` (unit + ApplyAction integration)
- `docs/reconstruction/physics/brake-spec.md` §5/§6 C8 applied notes + D2 warning

## Suite result

```
Passed:  2951
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    5
Total:   2957
```

Zero **new** failures vs baseline gate. (+15 passed vs C5 baseline 2936 from new C8 tests.)

## Residuals

1. **Gear-flip reverse** (`transmission+0x14` && `driverInput+0x3c`) — not ported; reverse thr always maps to brake pedal, never reverse drive.
2. **Friction fold-in is reduced** — retail keeps `local_3ec = T/r` in a separate axle slot; port adds signed brake torque into `DrivePack` (same drive-bias path). Full dual-slot layout is residual with C4 blob Solve.
3. **invDt vs AA step-info secondary** — formula uses `1/dt` (classic Havok kill-spin stiffness). AA may pack throttle as the secondary float into some paths; documented in brake-spec. Goldens assert the invDt formula.
4. **minTimeToBlock dwell** — always 0 in AA builder; port has no timer state.
5. **D2** must not re-apply kinematic reverse-throttle deceleration when physics is authoritative (documented in brake-spec).

## Files touched

**Production**
- `src/AutoCore.Game/Physics/Vehicle/HkVehicleBrake.cs`
- `src/AutoCore.Game/Physics/Vehicle/HkPhysicsConstants.cs`
- `src/AutoCore.Game/Physics/Vehicle/HkWheelRuntimeState.cs`
- `src/AutoCore.Game/Physics/Vehicle/VehicleActionSim.cs`

**Tests**
- `src/AutoCore.Game.Tests/Physics/oracles/brake_goldens.json`
- `src/AutoCore.Game.Tests/Physics/oracles/BrakeOracleTests.cs`
- `src/AutoCore.Game.Tests/Physics/Vehicle/HkVehicleBrakeTests.cs`

**Docs**
- `docs/reconstruction/physics/brake-spec.md`
- `docs/agents/task-C8-report.md` (this file)
