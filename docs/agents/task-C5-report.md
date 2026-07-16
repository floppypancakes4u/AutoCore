# Task C5 report — Engine upright pow + wheel drive-scale contact gate

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`

## What landed

### 1. Upright `pow` operand order (`HkVehicleEngine.ComputeUprightFactor`)

Retail `_CIpow` uses **base = |bodyUp·worldUp|**, **exp = 4.0** (`fn_00598040_uprightPow.md`).  
Implementation was already `MathF.Pow(absDot, UprightPowExponent)`; C5 locks it with an explicit
operand-order test that would fail if swapped to `pow(4, |dot|)`, plus a comment on the call site.

### 2. `wheel+0x88` = per-wheel contact gate (B3)

- New `HkVehicleEngine.ComputeContactDriveScale(bool inContact)` → `1.0` grounded / `0.0` airborne.
- `VehicleActionSim.TryApplyFriction` feeds that into `AggregateDrivePack` scales (was wrongly using
  `setup.TorqueRatio`).
- Removed interim setup field `HkWheelSetup.DriveScale` (contact gate is runtime, not construction-time).

### 3. `tRatio` fold-in moved to calcWheelTorque path

- `HkVehicleEngine.ComputeWheelTorque(..., torqueRatio: …)` multiplies the µ · upright · t product by
  the wheel’s torque ratio **before** handbrake cut and clamp (lands in wheels+0x28 analogue =
  `DriveTorque`).
- `VehicleActionSim.ApplyEngineTorque` passes `setup.TorqueRatio`.
- `HkVehicleData.FromVehicleSpecific` still builds `TorqueRatio` (front/rear + rear µ fold) but no
  longer copies it into a setup DriveScale.

### 4. Tests

| Area | Coverage |
|------|----------|
| Pow order | `ComputeUprightFactor_PowOperandOrder_BaseIsAbsDot_ExpIsFour` |
| Contact gate | `ComputeContactDriveScale_Grounded_IsOne_Airborne_IsZero` |
| tRatio in torque | `ComputeWheelTorque_TorqueRatio_*` |
| Setup mapping | DriveScale tests rewritten for TorqueRatio-only setup |
| Aggregate scales | `AggregateDrivePack_UsesContactGateNotTorqueRatio` |
| ActionSim | grounded torque expectations include `TorqueRatio` (undriven front = 0) |

## Suite result

```
Passed:  2936
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    5
Total:   2942
```

Zero **new** failures vs baseline gate. (+5 passed vs C4 baseline 2931 from new C5 tests.)

## Residuals

1. **Engine torque LUT still trivial** — `FromVehicleSpecific` still uses `engineRows/Cols = 0` so
   `torqueCurve2D` always OOR → `factors[0] = MinTorqueFactor`. Authored byte LUT is not on
   `VehicleSpecific`. Parity ramp climb (`RampExit_…` `[Ignore]`) still needs non-trivial drive
   and/or **C-mass**. No goldens currently require a non-trivial LUT for this gate.
2. **`RearWheelFrictionScalar` on `TorqueRatio`** — still folded at setup for rear torque (legacy
   mapping). Verified wheels-builder note says `+0x740` multiplies friction only; leave as residual
   unless a golden forces a split.
3. **Parity `[Ignore]` set** — unchanged from C4 (turn bleed, downhill micro-hop, ramp climb, full
   PortSolve blob). Expected to improve with C-mass / later C tasks, not C5 alone.
4. **engine-torque-spec.md §3 GAP** still mentions upright pow as open in prose; closed by
   `fn_00598040_uprightPow.md` + this port. Doc refresh optional.

## Files touched

**Production**
- `src/AutoCore.Game/Physics/Vehicle/HkVehicleEngine.cs`
- `src/AutoCore.Game/Physics/Vehicle/HkWheelSetup.cs`
- `src/AutoCore.Game/Physics/Vehicle/HkVehicleData.cs`
- `src/AutoCore.Game/Physics/Vehicle/VehicleActionSim.cs`
- `src/AutoCore.Game/Physics/Vehicle/HkVehicleFrictionSolver.cs` (doc only)

**Tests**
- `src/AutoCore.Game.Tests/Physics/Vehicle/HkVehicleEngineTests.cs`
- `src/AutoCore.Game.Tests/Physics/Vehicle/HkVehicleDataTests.cs`
- `src/AutoCore.Game.Tests/Physics/Vehicle/VehicleActionSimTests.cs`

**This report**
- `docs/agents/task-C5-report.md`
