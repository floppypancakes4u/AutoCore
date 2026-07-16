# Phase 3 quality gate

**Branch / worktree:** `feature-NPC-Retail-Driving`  
**Refreshed:** 2026-07-15  
**Scope:** Phase 3 subsystem modules  
(`WheelCollide → Suspension → Engine → Brake → Steering → Aero → Friction → AVD` per `docs/reconstruction/physics/README.md`)  
**Gate rules:** `docs/reconstruction/physics/PORTING_RULES.md`  
**Program:** `autoassault.exe` (image base `0x400000`)

## How boxes are marked

Checked only from artifacts present on disk (this snapshot):

| Checkbox | Checked when |
|----------|----------------|
| **decompile** | Matching `docs/reconstruction/physics/verified/fn_*.md` exists for the **required** primary address |
| **constants** | That verified note includes a constants / `read_memory` section for the module’s formula DATs (or the only fixed literals the formula uses) |
| **tests** | A compile-ready `*.cs` test under `src/AutoCore.Game.Tests/Physics/Vehicle/` targets the module (`.wip` does **not** count) |

Production port presence is listed under **src** for context (`src/AutoCore.Game/Physics/Vehicle/`). It is not a separate checkbox.

**Suite note:** individual module test *files* exist; at refresh time the full
`FullyQualifiedName~Physics` filter was **not green** (orchestrator/API drift mid-edit).
Treat “tests” as “dedicated test type present on disk,” not “all Physics tests pass.”
Pass criterion for **starting Phase 5** is still the completion checklist + green filter
(see `../PHASE_2_4_COMPLETION.md`).

---

## Summary

| Module | Required client address(es) | decompile | constants | tests | Production src |
|--------|-----------------------------|:---------:|:---------:|:-----:|----------------|
| WheelCollide | `0x64bbd0` (+ preUpdate `0x64cf20`, cast `0x580ed0`) | [x] | [x] | [x] | `HkVehicleWheelCollide.cs` |
| Suspension | `0x64de50` | [x] | [x] | [x] | `HkVehicleSuspension.cs` |
| Engine | `0x4a9750`, `0x598040` | [x] | [x] | [x] | `TorqueCurve2D.cs` + `HkVehicleEngine.cs` |
| Brake | `0x598040` (handbrake / no service-brake path) | [x] | [x] | [x] | `HkVehicleBrake.cs` |
| Steering | `0x64f840` | [x] | [x] | [x] | `HkVehicleSteering.cs` |
| Aero | `0x64dae0` | [x] | [x] | [x] | `HkVehicleAerodynamics.cs` |
| Friction | `0x6c4450` (primary); aggregation `0x64bc70` | [x] | [x] | [x] | `HkVehicleFrictionSolver.cs` (reduced; residuals listed in source) |
| AVD | `0x64d810` (primary continuous); air-stab `0x598320` | [x] | [x] | [x] | `HkVehicleVelocityDamper.cs` + `HkVehicleAirStabilization.cs` |

**Pass criterion (module):** all three of decompile / constants / tests checked.  
**Phase 3 RE gate (this table):** every row above passes.

**Phase 3 port completeness (ActionSim use):** not the same as this RE gate. Friction writeback,
air-stab call-site, and wheel closingSpeed on the orchestrator path remain incomplete — see
`../PHASE_2_4_COMPLETION.md`.

---

## Per-module detail

### 1. WheelCollide

| Field | Value |
|-------|--------|
| Required address | **`0x64bbd0`** `hkVehicleWheelCollide::collide` |
| Related | preUpdate `0x64cf20`; `TtPhantom::castRay` `0x580ed0` |
| Phase-0 map | `0.5-wheel-collide.md` |

- [x] **decompile** — `fn_0064bbd0_wheelCollide.md` (+ `fn_0064cf20_preUpdate.md`, `fn_00580ed0_castRay.md`)
- [x] **constants** — verified notes (`read_memory`, e.g. `DAT_00aaa668`)
- [x] **tests** — `HkVehicleWheelCollideTests.cs` (+ heightfield query tests)
- **src:** `HkVehicleWheelCollide.cs` (+ `IVehicleCollisionQuery.cs`, `TerrainHeightfieldCollisionQuery.cs`)
- **Orchestrator gap:** `ComputeClosingSpeed` exists; ActionSim must pass chassis velocity into `CastWheel` for damper to be live

---

### 2. Suspension

| Field | Value |
|-------|--------|
| Required address | **`0x64de50`** `hkDefaultSuspension::update` |
| Related | force apply in postTick `0x64bc70` |
| Phase-0 map | `0.4-suspension.md` |

- [x] **decompile** — `fn_0064de50_suspension.md`
- [x] **constants** — `g_flOne` @ `0x00a0f2a0` = 1.0 (invMass scale)
- [x] **tests** — `HkVehicleSuspensionTests.cs`
- **src:** `HkVehicleSuspension.cs`
- **Related:** `fn_0064bc70_suspImpulse.md` (impulse = force·dt apply site)

---

### 3. Engine

| Field | Value |
|-------|--------|
| Required addresses | **`0x4a9750`** `VehicleEngine::torqueCurve2D`; **`0x598040`** `VehicleAction::calcWheelTorque` |
| Phase-0 maps | `engine-torque-spec.md`, `0.7-transmission.md` |

- [x] **decompile** — `fn_004a9750_torqueCurve2D.md`, `fn_00598040_calcWheelTorque.md` (+ upright pow note)
- [x] **constants** — both verified notes list DATs (`0.5`, `0.8`, `15`, `0.2`, `1000`, `2.0`, …)
- [x] **tests** — `TorqueCurve2DTests.cs`, `HkVehicleEngineTests.cs`, oracle goldens
- **src:** `TorqueCurve2D.cs`, `HkVehicleEngine.cs` (`ComputeWheelTorque`, upright factor)

---

### 4. Brake

| Field | Value |
|-------|--------|
| Required address | **`0x598040`** (handbrake rear ×0.5 inside `calcWheelTorque`) |
| Note | No service-brake torque on retail custom path; decel from friction / reverse throttle |
| Phase-0 map | `brake-spec.md` |

- [x] **decompile** — covered by `fn_00598040_calcWheelTorque.md`
- [x] **constants** — handbrake scale `DAT_00a0f298` = 0.5 (and related clamp DATs in same note)
- [x] **tests** — `HkVehicleBrakeTests.cs`
- **src:** `HkVehicleBrake.cs`

---

### 5. Steering

| Field | Value |
|-------|--------|
| Required address | **`0x64f840`** `hkDefaultSteering::update` |
| Related | applyAction ramps `0x598650`; builder `0x5fc710` |
| Phase-0 map | `steering-spec.md` |

- [x] **decompile** — `fn_0064f840_steering.md` (+ `fn_00598650_steerRamp.md`)
- [x] **constants** — verified DATs (`0xa10e78`=0.05, `0xaf3388`=20.0, stage-1 rate, clamps)
- [x] **tests** — `HkVehicleSteeringTests.cs` (promoted from `.wip`)
- **src:** `HkVehicleSteering.cs` (`ComputeWheelAngles`, `RampStage1`, `RampSteer`, `ModeSpeedFactor`)

---

### 6. Aero

| Field | Value |
|-------|--------|
| Required address | **`0x64dae0`** `hkDefaultAerodynamics::update` |
| Related setup | builder `0x5fc4f0` → `fn_005fc4f0_aeroBuilder.md` |
| Phase-0 map | `0.6-aerodynamics.md` |

- [x] **decompile** — `fn_0064dae0_aero.md`
- [x] **constants** — verified `DAT_00a0f298` = +0.5, `DAT_00aaa6cc` = −0.5
- [x] **tests** — `HkVehicleAerodynamicsTests.cs`
- **src:** `HkVehicleAerodynamics.cs`

---

### 7. Friction

| Field | Value |
|-------|--------|
| Required address | **`0x6c4450`** `hkVehicleFrictionSolver_solve` |
| Related | axle aggregation / drivePack / solver call site **`0x64bc70`** `postTickApplyForces` |
| Phase-0 map | `0.3-friction-solver.md` |

- [x] **decompile** — `fn_006c4450_frictionSolve.md` (+ `fn_0064bc70_postTick.md`, `fn_friction_circleProjection.md`)
- [x] **constants** — solver DATs in frictionSolve note (`0xa0d2f4` eps, `0xa0f298`=0.5, `0xa0f704`=0.25, …)
- [x] **tests** — `HkVehicleFrictionSolverTests.cs` (aggregate, circle clamp, reduced Solve)
- **src:** `HkVehicleFrictionSolver.cs` — **reduced** model with explicit residual list (no full J M⁻¹ Jᵀ, no Phase C/D writeback, lat 0.25 not in Jv build)
- **Orchestrator gap:** ActionSim uses drive-pack aggregation → longitudinal force only; does **not** call full `Solve` writeback for long+lat at contacts

---

### 8. AVD (angular velocity damper + air stabilization)

| Field | Value |
|-------|--------|
| Required addresses | **`0x64d810`** `hkAngularVelocityDamper_update` (continuous, primary); **`0x598320`** `VehicleAction_airStabilization` |
| Phase-0 map | `avd-airstab-spec.md` |

- [x] **decompile** — `fn_0064d810_avd.md` (continuous) + `fn_00598320_airStab.md` + `fn_upright_restore.md`
- [x] **constants** — continuous AVD uses setup rates (no plate float beyond `g_flOne`); air-stab / upright notes document `DAT_00a110d8`=10.0, 0.7 / 0.1 gates, plate thr
- [x] **tests** — `HkVehicleVelocityDamperTests.cs`, `HkVehicleAirStabilizationTests.cs`
- **src:** `HkVehicleVelocityDamper.cs` @ `0x64d810`; `HkVehicleAirStabilization.cs` (upright-restore essentials; collision-window **DEFERRED** in remarks)
- **Orchestrator gap:** continuous AVD is called from ActionSim; **air-stab / upright-restore not wired** (explicit skip comment)

---

## Related verified notes (not Phase 3 module gates)

These support setup / Phase 4+ and must not be confused with the eight subsystem rows:

| Verified file | Address | Role |
|---------------|---------|------|
| `fn_005fd390_buildFramework.md` | `0x5fd390` | Phase 2 setup framework build |
| `fn_005fc710_steeringBuilder.md` | `0x5fc710` | Steering descriptor builder |
| `fn_005fc4f0_aeroBuilder.md` | `0x5fc4f0` | Aero descriptor builder |
| `fn_004d6c80_stepTo.md` | `0x4d6c80` | Phase 4 substep rule |
| `fn_00598650_applyAction.md` | `0x598650` | Phase 4 orchestrator order |
| `fn_00636a60_tickSubsystems.md` | `0x636a60` | Framework child update order |
| `fn_004fc650_driveController.md` | `0x4fc650` | Phase 5 AI drive controller (math only until Phase 5) |
| `fn_entity_driveAxes_offsets.md` / `server_*` | n/a | Wire / offset notes |

---

## Gaps to close (beyond this RE table)

This quality gate’s eight rows are **checked**. Remaining work is **orchestrator / Phase 4 completeness**, not missing RE notes:

1. **Friction ActionSim writeback** — call reduced `Solve` (or finish residuals) and apply long+lat impulses at contact points; prefer `DriveScale` in aggregation.
2. **Air-stab ActionSim wire** — upright-restore essentials; keep 6400ms collision-window deferred with written reason.
3. **ClosingSpeed on orchestrator** — pass chassis `LinVel` into `CastWheel`.
4. **Green suite** — `dotnet test … --filter FullyQualifiedName~Physics` must pass before Phase 5.
5. **Phase 5 still blocked** — no `NpcVehiclePhysicsController` / ticker physics branch until `PHASE_2_4_COMPLETION.md` is fully done.

---

## Inventory used for this mark

**`verified/` Phase-3 primaries (present):**  
`fn_0064bbd0_wheelCollide.md`, `fn_0064de50_suspension.md`, `fn_004a9750_torqueCurve2D.md`,
`fn_00598040_calcWheelTorque.md`, `fn_0064f840_steering.md`, `fn_0064dae0_aero.md`,
`fn_006c4450_frictionSolve.md`, `fn_0064bc70_postTick.md`, `fn_0064d810_avd.md`,
`fn_00598320_airStab.md`

**`src/.../Physics/Vehicle/` production (this snapshot):**  
`HkVehicleWheelCollide.cs`, `HkVehicleSuspension.cs`, `TorqueCurve2D.cs`, `HkVehicleEngine.cs`,
`HkVehicleBrake.cs`, `HkVehicleSteering.cs`, `HkVehicleAerodynamics.cs`, `HkVehicleFrictionSolver.cs`,
`HkVehicleVelocityDamper.cs`, `HkVehicleAirStabilization.cs`, `HkVehicleTransmission.cs`,
`HkVehicleSubstep.cs`, `HkRigidBody.cs`, `VehicleActionSim.cs`, `VehiclePhysicsInstance.cs`,
`HkVehicleData.cs`, `HkVehicleDataCache.cs`, …

**`src/.../Physics/Vehicle/` tests (representative):**  
`HkVehicleWheelCollideTests.cs`, `HkVehicleSuspensionTests.cs`, `TorqueCurve2DTests.cs`,
`HkVehicleEngineTests.cs`, `HkVehicleBrakeTests.cs`, `HkVehicleSteeringTests.cs`,
`HkVehicleAerodynamicsTests.cs`, `HkVehicleFrictionSolverTests.cs`,
`HkVehicleVelocityDamperTests.cs`, `HkVehicleAirStabilizationTests.cs`,
`VehicleActionSimTests.cs`, `VehiclePhysicsInstanceTests.cs`,
`VehiclePhysicsCharacterizationTests.cs`, …
