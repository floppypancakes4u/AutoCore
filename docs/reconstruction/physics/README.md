# Phase 0 — Client Havok Vehicle Physics RE (COMPLETE) + Resume Point

**Branch:** `feature-NPC-Retail-Driving` (worktree `.worktrees/feature-NPC-Retail-Driving`, off clean `origin/master` 1773abd)
**Date:** 2026-07-15 · **Target:** `autoassault.exe` (base 0x400000), Ghidra project AA-decode
**Plan:** `~/.claude/plans/cheerful-swimming-reef.md` · **Reference:** `docs/NPCDriving.md`

This directory holds the Phase 0 reverse-engineering evidence for the bit-exact server-side Havok
vehicle physics port, plus Phase 2–4 port status.

---

## Status

| Phase | Status | Notes |
|-------|--------|-------|
| **0** RE evidence | **DONE** | All 14 evidence files below + large `verified/` re-gate set |
| **1** ServerConfig scaffold | **DONE** | Config/substep knobs; physics OFF by default |
| **2** `HkVehicleData` | **DONE** | Setup + CBID cache + unit mass/RVInertia |
| **3** Subsystems | **DONE** | Reduced friction residuals documented; see `PHASE_2_4_COMPLETION.md` |
| **4** Integrator + orchestrator | **DONE** | `VehicleActionSim` / `VehiclePhysicsInstance` + characterization tests |
| **5** NPC controller + ticker | **DONE** | `NpcVehiclePhysicsController` + tier resolve; opt-in only |
| **6** Ghost streaming | **DONE** | sharp→Handbreak, sim angVel, thr/steer pack, wheelset on foreign create |
| **7** Live verify | **NOT STARTED** | Needs approved Launcher A/B |

**Gate doc:** [`PHASE_2_4_COMPLETION.md`](PHASE_2_4_COMPLETION.md)  
**Port rules:** [`PORTING_RULES.md`](PORTING_RULES.md)  

**Resume at:** Phase 7 live A/B (after approval), or friction/geometry fidelity if handling looks wrong.

---

## Evidence index

| File | Subsystem | Key result | Primary anchors |
|------|-----------|-----------|-----------------|
| `0.1-step-rate.md` | Timestep | Sub-stepped: `substep_dt = frameDt/(floor(frameDt·30)+1)`, frameDt≤0.1; max 1/30s, 60fps→1/60s | StepTo `0x4d6c80`; cap `0x9cc798`=29.9999998 |
| `0.2-mass-inertia.md` | Mass/inertia/COM | Mass baked in `.hkx` asset (server lacks it); **use mass=1.0, inertia=RVInertia** (Havok normalizes per unit mass); COM=asset+`CenterOfMassModifier` | `FUN_005fc620` inertia; `0x5fd390` setup |
| `0.3-friction-solver.md` | Friction | Solved over **2 axle** constraint points; friction-circle clamp; drive impulse = Σ(wheelTorque·wheel+0x88)/axleCount | solve `0x6c4450`; postTick `0x64bc70` |
| `0.4-suspension.md` | Suspension | `F = ((restLen−len)·strength·(1/len) − dampCoef·closingSpeed)·(1/RB[0x2c])`; closingSpeed<0→compress coeff | `hkDefaultSuspension::update 0x64de50` |
| `0.5-wheel-collide.md` | Wheel cast | **Havok broadphase cast** (`TtPhantom::castRay 0x580ed0`), not terrain ray; compression=(radius+restLen)·hitFrac−radius | preUpdate `0x64cf20` → `0x64bbd0` |
| `0.6-aerodynamics.md` | Aero | `F_drag=−0.5ρACd|v|v·fwd`; `F_lift=+0.5ρACl·v²·up` (neg Cl=downforce); `F_xg=mass·extraGravity` | `hkDefaultAerodynamics::update 0x64dae0` |
| `0.7-transmission.md` | Engine/gears | **No real engine curve**: torqueCurve2D args are contact X/Z → bins out of range → constant `float[0]`; gears vestigial/HUD | `0x598040`, `0x4a9750`, preUpdate `0x64cf20` |
| `0.8-struct-offsets.md` | Layout | Body `+0x30..3c`=quaternion, `+0x40..48`=linVel, `+0x50..5c`=angVel, `+0xb0..b8`=pos, `+0x2c`=invMass; VA `+0x28`=final steer | multiple |
| `engine-torque-spec.md` | Engine port | torqueCurve2D verified + 6 golden vectors; `&7` = 8 levels; truncation toward zero | `0x4a9750` |
| `steering-spec.md` | Steering | `angle = MaxAngle·steer·(speed≤FSL?1:(FSL/speed)²)` **quadratic**; ramp ±0.05/tick; speedFactor `min(speed/20,1)` | `hkDefaultSteering::update 0x64f840` |
| `brake-spec.md` | Brake | AA `applyAction`/`calcWheelTorque` add **no** brake torque directly, but the framework's `hkDefaultBrake` component **is ticked** every substep (Task B8) and applies real per-wheel brake torque + wheel lock, fed by the throttle axis's reverse component; handbrake = rear drive-torque ×0.5 **and** (if handbrake-connected) Havok lock | `0x598040`, `0x64e6f0`, `0x636a60` |
| `avd-airstab-spec.md` | AVD/air-stab | Continuous `hkAngularVelocityDamperAction` (`update 0x64d810`): `w*=max(0,1−d·dt)`, collision branch **speed-triggered**; upright-restore when dot(up,worldUp)<0.7 | `0x64d810`, `0x598320` |
| `drive-controller-spec.md` | AI controller | thr/steer/sharp math + 4 golden vectors; **throttle sign inverted** (fwd = base −1) | `MoveToTarget3DPoint 0x4fc650` |
| `setup-field-mapping.md` | Setup | `VehicleSpecific` (offsets 0x4c0–0x740) → Havok components; top-speed precomputed to `vehicle+0x110` | `Vehicle_buildHavokVehicleFramework 0x5fd390` |

---

## Cross-cutting facts for the port

1. **Timestep:** accumulator, `N=floor(frameDt·30)+1` sub-steps, max 1/30s. `ServerConfig.SubstepHz`
   (default 60) should be reworked into this `frameDt/N` rule.
2. **Mass/inertia:** mass=1.0, diagonal inertia = `mass·RVInertia{Yaw,Roll,Pitch}` (server has these);
   absolute mass only matters for collision momentum (optional footprint-box estimate).
3. **Friction:** 2-axle solve, friction-circle (traction-ellipse) clamp, `RearWheelFrictionScalar`
   (`vehicleData+0x740`) baked into rear μ at setup (`FUN_005fcce0 0x5fcce0`).
4. **Engine:** effectively a **constant drive-torque factor** (LUT degenerates); real torque ≈
   `μ·upright·factor`, clamp [0,1000]. Do NOT build a full gear/RPM engine for parity.
5. **Brake:** deceleration is friction-solver coast/reverse-drive **plus** a live Havok
   `hkDefaultBrake` per-wheel torque + lock (Task B8: confirmed ticked every substep, pedal from
   the throttle axis's reverse component); handbrake = rear drive-torque ×0.5 **and** Havok lock
   on handbrake-connected wheels.
6. **Steering:** quadratic inverse-speed falloff; front/rear steer flags at `VehicleSpecific+0x4cc`/`+0x5f0`.
7. **Aero + AVD** give the ramp/air behaviour: lift sign from Cl, extraGravity, upright-restore impulse,
   continuous angular damping.

---

## Resolved during reconciliation

- **`0xaf3388` = 20.0** (raw read); the `0.6` is the neighbor `0xaf3384`. Speed-factor = `min(speed/20,1)`.
  (Old `NPCDriving.md`/plate said 0.6 — off by 4 bytes.)
- **SpeedLimiter/AbsoluteTopSpeed** (was UNCONFIRMED): precomputed to `vehicle+0x110` in setup tail.
- **VehicleAction `+0x24`** = steering-ramp stage, **not** a brake float (0.8 + brake agents agree).
- **`DAT_00a110d8` = 10.0** is the re-ground Y-raise, not an angular-damping additive.
- **AVD collision branch** is angular-speed-triggered, not the 6400ms window.

## Corrections to `docs/NPCDriving.md` (apply when convenient)

- Throttle **sign is inverted** (forward driving = base −1); reverse gate `fAlign<−0.4`; throttle
  speed-gate 5.0; steer gain 2.0; cruise threshold 0.1.
- `DAT_00af3388` is 20.0 (not 0.6); `0x00a10e74` is the rear ×2.0 driver-mod (mislabeled).
- §6.1 "movement mode 0x02 speedFactor min(|v|/0.6,1)" → `/20.0`.
- §6.3 AVD: continuous damper is a Havok action (speed-triggered collision branch); 10.0 = Y-raise.
- §6 "brake" on VehicleAction+0x24 → steering-ramp; no service-brake torque.

## Open ambiguities (deferred — need live debugger / asset; NOT blocking Phase 2)

- Friction solver: exact 2×2 row binding (a/d/b), `circleProjection` helper internals, softness composition.
- Mass/inertia: RVInertia Roll/Pitch/Yaw→axis pairing and COM-modifier apply site (bulk-copied struct).
- Engine: whether the position-indexed LUT constant-factor is intentional (vs terrain traction map).
- ~~Brake: exact `hkpVehicleDefaultBrake` runtime call site (likely vestigial).~~ **RESOLVED
  (Task B8): TICKED.** `hkDefaultBrake_update` (`0x64e6f0`) runs every substep as framework child
  5/7 via `VehicleAction_tickSubsystems` (`0x636a60`), fed a pedal derived from the throttle
  axis's reverse component (`hkDefaultAnalogDriverInput_calcStatus` `0x5fe520`) + the raw
  handbrake byte; its per-wheel torque output is folded into the friction-solver input in
  `postTickApplyForces` (`0x64bc70`) and its lock flag zeroes wheel spin in `preUpdate`
  (`0x64cf20`). See `brake-spec.md` §5 item 1 for full call chain and port implications.
- Wheel-collide: retail **client + server** cast against **full Havok body geometry**. Phase-2 server starts
  **terrain-heightfield only** (`MapTerrainHeightfield`); full geometry lands later via `IVehicleCollisionQuery`.
- Wheel `+0x88` drive scale: exact writer site still open; port maps from `TorqueRatio` provisionally.

---

## Porting process (mandatory)

See **`PORTING_RULES.md`**. Every C# subsystem must re-verify its client function via Ghidra
(`decompile_function` / `batch_decompile` + `read_memory` for constants; `emulate_function` when
practical). Phase 0 markdown is a map — **the binary wins on conflict**. Full wheel-collide geometry
(retail client+server had it) is **planned** — plug point: `IVehicleCollisionQuery`.

## Production layout (Phase 2–4)

Under `src/AutoCore.Game/Physics/Vehicle/` (representative):

- **Setup:** `HkVehicleData`, `HkVehicleDataCache`, `HkWheelSetup`
- **Subsystems:** `HkVehicleSubstep`, `HkVehicleSteering`, `HkVehicleSuspension`, `HkVehicleWheelCollide`,
  `HkVehicleEngine`, `TorqueCurve2D`, `HkVehicleBrake`, `HkVehicleAerodynamics`, `HkVehicleFrictionSolver`,
  `HkVehicleVelocityDamper`, `HkVehicleAirStabilization`, `HkVehicleTransmission`, `HkVehicleWheelKinematics`
- **Orchestrator:** `HkRigidBody`, `VehicleActionSim`, `VehiclePhysicsInstance`
- **Collision:** `IVehicleCollisionQuery`, `TerrainHeightfieldCollisionQuery`
- **Drive axes (pure):** `VehicleDriveController` (`0x4fc650`)
- **Phase 5 wire:** `Npc/NpcVehiclePhysicsController.cs` + `NpcTicker` tier branch +
  `Vehicle.PhysicsInstance` lifecycle

Tests: `src/AutoCore.Game.Tests/Physics/` + `NpcAi/NpcVehiclePhysicsControllerTests.cs`.

## Next actions (resume here)

1. **Phases 5–6 DONE** (opt-in physics tier + ghost thr/steer/sharp/angVel).
2. **Phase 7:** approved live Launcher A/B; full friction Jacobian / world geometry if handling gaps show up.
