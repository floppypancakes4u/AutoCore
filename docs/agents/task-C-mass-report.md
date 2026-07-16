# Task C-mass report — Thread real chassis mass from SimpleObjectSpecific.Mass

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`

## What landed

### 1. `HkVehicleData.FromVehicleSpecific` — mass injection point

| Change | Detail |
|--------|--------|
| New `mass` arg | Default `0` → treated as non-positive → falls back to `HkPhysicsConstants.UnitMass` (1.0) |
| Resolve | `if (!(mass > 0f)) mass = UnitMass` (covers 0, negative, NaN) |
| Inertia | Already `mass × RVInertia{Roll,Pitch,Yaw}` — scales automatically |
| InvMass | `1 / mass` after resolve |

Evidence: live CE (`asset-mass-findings.md`) — chassis RB mass == `SimpleObjectSpecific.Mass` (rlMass) == vehicle UI weight; `I = mass × RVInertia` (B4 axes X←Pitch, Y←Yaw, Z←Roll).

### 2. Call-site threading

| Site | Mass source |
|------|-------------|
| `HkVehicleDataCache.BuildFromCloneBases` | `cv.SimpleObjectSpecific.Mass` |
| `HkVehicleDataCache.GetOrCompute` | new `mass` parameter (default 0 → unit) |
| `Vehicle.GetOrCreatePhysicsInstance` (cache miss) | `cv.SimpleObjectSpecific.Mass` |

Cache hit path already has mass baked at WAD build. CreateBody continues to `SetMass(data.Mass)` + inv-inertia from scaled `data.Inertia*`.

### 3. Tests

- Default unit mass still asserted (explicit unit-mass fixture path for characterization).
- Real mass (Callisto 1500 / Astimiax 2900) scales mass + inertia linearly.
- Non-positive / NaN mass falls back to unit.
- Cache build threads `SimpleObjectSpecific.Mass`; zero mass falls back.
- `VehiclePhysicsInstance` CreateBody matches live Callisto inv-inertia axes at m=1500.

### 4. Parity probes (post-mass)

| Test | Result |
|------|--------|
| `ConstantRadiusTurn_AtSpeed_StaysGrounded_NoUpwardDrift` | **Un-Ignored, green** with m=1500 fixture |
| `Downhill_ContinuousGrade_AtSpeed_StaysGrounded_NoBounce` | Still red — contactRatio≈96% (&lt;99%); re-Ignored mass-aware |
| `RampExit_GenuineLiftoffAtLip_FollowsBallisticArc` | Still red — no free-flight at lip / re-sticks frame 0; re-Ignored mass-aware |

## Suite result

```
Passed:  2959
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    4
Total:   2964
```

Zero **new** failures vs baseline gate. (+8 passed / −1 skipped vs C8: ConstantRadiusTurn un-Ignored + new mass tests.)

Physics opt-in defaults **unchanged**.

## Residuals

1. **Base COM** (hull-centroid from `physics.glm`) still a documented gap; only `CenterOfMassModifier` is applied.
2. **Downhill contact micro-hop** (~96% contact at m=1500) — r×F / suspension polish, not mass.
3. **Ramp climb / genuine lip** — trivial engine torque LUT + slope-damper residual; mass alone insufficient.
4. Characterization fixtures default to unit mass so existing oracles stay bit-stable; production path uses real rlMass when present.

## Files touched

**Production**
- `src/AutoCore.Game/Physics/Vehicle/HkVehicleData.cs`
- `src/AutoCore.Game/Physics/Vehicle/HkVehicleDataCache.cs`
- `src/AutoCore.Game/Physics/Vehicle/VehiclePhysicsInstance.cs` (comment only)
- `src/AutoCore.Game/Entities/Vehicle.cs`

**Tests**
- `src/AutoCore.Game.Tests/Physics/Vehicle/HkVehicleDataTests.cs`
- `src/AutoCore.Game.Tests/Physics/Vehicle/VehiclePhysicsInstanceTests.cs`
- `src/AutoCore.Game.Tests/Physics/Vehicle/RetailParityTests.cs`

**Docs**
- `docs/agents/task-C-mass-report.md` (this file)
