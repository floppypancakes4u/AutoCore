# Task C4 report — retail friction solver + wheel-relative slip

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`

## What changed

### `HkVehicleFrictionSolver.cs`
- Coupled long/lat solve via `SolveCoupledImpulses` when `AxleFrictionInput.Coupling ≠ 0` (standard `Keff⁻¹` 2×2; diagonal fast path when coupling is 0).
- New helpers: `ComputeInvKeffFromContact`, `ComputeKeffCoupling` (chassis lin+ang `J·M⁻¹·Jᵀ`).
- `AxleFrictionInput` extended: `Coupling`, `Softness`, `CircleProduct`.
- Solve path uses **`CircleProjection`** (optional anisotropic scale table from `BuildCircleProjectionScales`) instead of `ClampFrictionCircle`.
- Drive path uses soft-regularized `invKeff_reg` when `Softness > 0`.
- Class XML docs updated for C4 ported vs residual gaps.

### `VehicleActionSim.cs` (`TryApplyFriction` / `BuildAxleInput` / `ApplyAxleImpulses`)
- **Crawl fix:** `slipLong = chassisLong − spin·radius` (wheel-relative), not absolute chassis speed.
- **|N|:** suspension force magnitude only — gravity-share floor removed.
- **μ curve:** `Mu0 = setup.Friction`, `MuSlope = WheelsViscosityFriction (0.001)`, `MuMax = μ0 × 1.5`.
- **r×F:** axle impulses applied at averaged contact point via `ApplyPointImpulse` (not COM-only).
- Per-axle InvKeff + Coupling from contact arms fed into Solve.

### `HkPhysicsConstants.cs`
- `WheelsViscosityFriction = 0.001`, `WheelsMuMaxScale = 1.5` (wheels builder constants).

### Tests
- `HkVehicleFrictionSolverTests`: coupled 2×2, InvKeff/coupling helpers, CircleProjection-in-Solve.
- `RetailParityTests`: crawl downhill fixture now asserts **non-crawl** speed (`meanSpeed ≥ 1`).
- Three at-speed parity contracts + PortSolve remain `[Ignore]` with **updated residual reasons** (see below).

## Oracle status

| Contract | Status |
|----------|--------|
| `FrictionCircleWriteback_ZeroUnderGrip_ActiveOnlyWhenSaturated` | **Green** (discriminating grip/slide signature) |
| `DecodedFields_DecodeBitExactFromRawBlobs` / blob self-consistency | **Green** |
| `PortSolve_ReproducesRetailImpulses_BitExact` | **`[Ignore]`** — no full cb/setup blob Solve |

Bit-exact PortSolve needs dual-body Phase A/C/D writeback, cross-axle 2×2, 1/mag² pre-scale + ordered dual-axle `circleProjection` couple feedback, and setup mix0/mix1. Current Solve is the reduced `AxleFrictionInput` API with long/lat coupling + CircleProjection — not a drop-in for live `cb`/`out` blobs.

## Parity tests

| Test | Status |
|------|--------|
| `Downhill_ContinuousGrade_CrawlSpeed_StaysGrounded` | **Green** (now demands post-C4 speed ≥ 1 m/s) |
| `ConstantRadiusTurn_AtSpeed_StaysGrounded_NoUpwardDrift` | **`[Ignore]`** residual: unit-mass turn bleeds seeded 8 → ~2.0 m/s mean (&lt; 3) |
| `Downhill_ContinuousGrade_AtSpeed_StaysGrounded_NoBounce` | **`[Ignore]`** residual: contactRatio ≈ 97.3% (&lt; 99%) micro-hop |
| `RampExit_GenuineLiftoffAtLip_FollowsBallisticArc` | **`[Ignore]`** residual: cannot climb 10 m / 2 m ramp under unit mass + MinTorqueFactor |

## Residual gaps (IMPLEMENTATION-GAPS style)

1. **Full retail cb Solve** — blob-level bit-exact against `frictionSolver_goldens.json` (see PortSolve Ignore).
2. **Cross-axle coupled 2×2** — decompile may couple ax0/ax1 primary rows; port couples long/lat per axle.
3. **Dual-body contact RB** terms in Keff / writeback.
4. **ω×r contact velocity in slip** — deliberately omitted under unit mass (pitch-from-susp false slip fought drive); COM long − spin·r is the crawl fix.
5. **Unit mass** — weak cornering hold / ramp climb / micro-hop; expected to improve with **C-mass**.
6. **C5 torque path** — ramp climb also needs non-trivial drive torque beyond MinTorqueFactor OOR LUT.

## Suite result

```
Passed:  2931
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    5
Total:   2937
```

Zero new failures vs baseline gate.
