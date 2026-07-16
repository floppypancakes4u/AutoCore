# Phases 2–4 completion checklist (gate for Phase 5)

**Do not start Phase 5** (`NpcVehiclePhysicsController`, `NpcTicker` physics branch, ghost wiring)
until this checklist is green.

**Snapshot:** 2026-07-15 (worktree `feature-NPC-Retail-Driving`)  
**Suite:** `dotnet test … --filter FullyQualifiedName~Physics` → **282 passed, 0 failed**

## Out of scope (Phase 5+)

- `Npc/NpcVehiclePhysicsController.cs`
- `NpcTicker` ServerConfig tier branch
- `Vehicle` entity physics-instance lifecycle on the ticker
- Ghost packing / live `ApplyServerMove` NPC integration

Pure-math `VehicleDriveController` exists under `Physics/Vehicle/` — that is **not** Phase 5.

---

## Phase 2 — `HkVehicleData` — DONE

- [x] Setup map from `VehicleSpecific` (wheels, susp, steer, brake, aero, AVD, inertia, COM, rear μ, torque ratios, gears)
- [x] Per-wheel **DriveScale** (`wheel+0x88` analogue; provisional = torque ratio; RE writer open)
- [x] CBID cache + tests
- [x] Unit mass + RVInertia model
- [x] Optional air-density override path / gravity on data

## Phase 3 — Subsystems — DONE (reduced friction residuals documented)

| Module | Status |
|--------|--------|
| Substep | DONE |
| Steering | DONE (stage-1/2 + quadratic) |
| Suspension | DONE |
| WheelCollide | DONE (cast, compression, closingSpeed from linVel) |
| Engine + TorqueCurve2D | DONE (upright^4, OOR constant factor, handbrake rear) |
| Brake | DONE |
| Aero | DONE |
| AVD continuous | DONE |
| Air-stab upright restore | DONE (6400 ms entity window **deferred**) |
| Friction | DONE reduced 2-axle (slip, drive pack, μ·N·dt floor, circle clamp, Solve writeback long/lat). Full J·M⁻¹·Jᵀ + anisotropic circleProjection table path residual |
| Transmission | DONE vestigial |
| Wheel spin | DONE preUpdate-style integrate |

## Phase 4 — Integrator + orchestrator — DONE for Phase 5 entry

- [x] `HkRigidBody` integrate (gravityY from data, impulse, quat)
- [x] `VehicleActionSim` mapped to applyAction / tickSubsystems spine (documented in source)
- [x] Suspension `F·dt` point impulses
- [x] Friction Solve → long/lat impulses at contacts; gravity-share load floor when susp≈0
- [x] Steering stages; throttle sign preserved
- [x] `VehiclePhysicsInstance.Step` via `HkVehicleSubstep`
- [x] Characterization tests (free fall, ground settle, forward drive, substep N=2, AVD)

## Exit criterion

| Check | Result |
|-------|--------|
| Physics filter green | **PASS (282/282)** |
| Phase 5 files not required | **PASS** |
| Known residuals documented | friction full Jacobian; air-stab collision window; driveScale RE writer; world collision geometry |

**Status: READY TO START PHASE 5** (library complete; integration not started).
