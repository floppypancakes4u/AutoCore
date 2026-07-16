# Verified physics RE — index

Index of every file under `docs/reconstruction/physics/verified/` (plus sibling `../constants/`).  
One-line descriptions only. No production code.

**Program:** `autoassault.exe` (image base `0x400000`)  
**Related:** [`../PORTING_RULES.md`](../PORTING_RULES.md) · [`../README.md`](../README.md)

---

## Function gates (`fn_*` by address)

| File | One-line description |
|------|----------------------|
| [fn_004a9750_emulate.md](fn_004a9750_emulate.md) | Emulation of `VehicleEngine_torqueCurve2D` @ `0x4a9750` — engine-disabled early-out returns `1.0` in ST0. |
| [fn_004a9750_torqueCurve2D.md](fn_004a9750_torqueCurve2D.md) | `VehicleEngine_torqueCurve2D` @ `0x4a9750` — 2D LUT torque factor from RPM×throttle bins. |
| [fn_004cfe60_castTerrain.md](fn_004cfe60_castTerrain.md) | `CVOGMap_CastTerrainHeight` @ `0x4cfe60` — map terrain down-cast height (spawn / snap / grounding). |
| [fn_004d6c80_stepTo.md](fn_004d6c80_stepTo.md) | `CVOGSectorMap::StepTo` @ `0x4d6c80` — frame-dt clamp, sub-step count `N`, per-substep `dt`. |
| [fn_004e8ad0_basisExtract.md](fn_004e8ad0_basisExtract.md) | Quaternion basis extractors @ `0x4e8ad0` / `0x4e8a40` — unit quat → world right/forward (+ up sibling). |
| [fn_004f5550_wheelFriction.md](fn_004f5550_wheelFriction.md) | `FUN_004f5550` @ `0x4f5550` — per-wheel base friction accessor (thunk; callers apply μ mods). |
| [fn_004f5560_wheelCount.md](fn_004f5560_wheelCount.md) | `FUN_004f5560` @ `0x4f5560` — wheel-count helper for torque / setup loops. |
| [fn_004f5620_setSteerInput.md](fn_004f5620_setSteerInput.md) | `VehicleEntity_SetSteerInput` @ `0x4f5620` — gated write of normalized steer to `entity+0x618`. |
| [fn_004fb660_createVehicleAction.md](fn_004fb660_createVehicleAction.md) | `Vehicle_createVehicleAction` @ `0x4fb660` — entry path: framework build + VehicleAction registration. |
| [fn_004fbc10_pushDriveAxes.md](fn_004fbc10_pushDriveAxes.md) | `VehicleEntity_PushDriveAxesToController` @ `0x4fbc10` — entity thr/steer/sharp → input controller bridge. |
| [fn_004fc650_driveController.md](fn_004fc650_driveController.md) | `CVOGVehicle::MoveToTarget3DPoint` @ `0x4fc650` — AI drive-axis generator (throttle/steer/sharp). |
| [fn_0053eec0_networkApply.md](fn_0053eec0_networkApply.md) | `FUN_0053eec0` @ `0x53eec0` — client apply of network pose + lin/ang velocity. |
| [fn_00561910_islandStep.md](fn_00561910_islandStep.md) | Island `LtSimulate` step (`FUN_00561910` + `FUN_00629d90`) — substep_dt → VehicleAction apply. |
| [fn_00580ed0_castRay.md](fn_00580ed0_castRay.md) | `TtPhantom::castRay` @ `0x580ed0` — world ray vs phantom overlap list; closest hit + world normal. |
| [fn_00597e90_vehicleActionCtor.md](fn_00597e90_vehicleActionCtor.md) | `FUN_00597e90` — VehicleAction construction + vtable `applyAction` slot wiring. |
| [fn_00598040_calcWheelTorque.md](fn_00598040_calcWheelTorque.md) | `VehicleAction_calcWheelTorque` @ `0x598040` — AA engine replacement; per-wheel drive torque + airborne flag. |
| [fn_00598040_uprightPow.md](fn_00598040_uprightPow.md) | Upright `_CIpow` traction falloff scale inside `calcWheelTorque` @ `0x598040`. |
| [fn_00598320_airStab.md](fn_00598320_airStab.md) | `VehicleAction_airStabilization` @ `0x598320` — collision-window recovery / re-ground (not continuous AVD). |
| [fn_00598650_applyAction.md](fn_00598650_applyAction.md) | `VehicleAction_applyAction` @ `0x598650` — main VehicleAction tick: steer, torque, airStab, subsystem order. |
| [fn_00598650_steerRamp.md](fn_00598650_steerRamp.md) | Two-stage steer input ramp / clamp inside `applyAction` @ `0x598650` (mode-0x02 speed factor). |
| [fn_00598650_throttleRamp.md](fn_00598650_throttleRamp.md) | Throttle ramp / `DAT_00a10e74=2.0` / `entity+0x614` path inside `applyAction` @ `0x598650`. |
| [fn_005d6640_wheelCollideComp.md](fn_005d6640_wheelCollideComp.md) | `FUN_005d6640` @ `0x5d6640` — binary `hkDefaultEngine` ctor (historical “wheel-collide” label; size `0x3c`). |
| [fn_005d6ae0_steerBasis.md](fn_005d6ae0_steerBasis.md) | `FUN_005d6ae0` @ `0x5d6ae0` — rotate local vector by chassis column-major basis (`out.w = 0`). |
| [fn_005fc3d0_wheelCollideBuilder.md](fn_005fc3d0_wheelCollideBuilder.md) | `FUN_005fc3d0` @ `0x5fc3d0` — `hkDefaultEngine` descriptor fill (historical “wheel-collide” builder name). |
| [fn_005fc4f0_aeroBuilder.md](fn_005fc4f0_aeroBuilder.md) | `Vehicle_BuildAerodynamicsDescriptor` @ `0x5fc4f0` — 8-word aero descriptor from VehSpec (verbatim). |
| [fn_005fc620_vehicleDataInit.md](fn_005fc620_vehicleDataInit.md) | `FUN_005fc620` @ `0x5fc620` — definition → `hkVehicleData` descriptor init. |
| [fn_005fc710_steeringBuilder.md](fn_005fc710_steeringBuilder.md) | `Vehicle_BuildSteeringDescriptor` @ `0x5fc710` — steering descriptor from VehSpec × entity mults. |
| [fn_005fc840_transBuilder.md](fn_005fc840_transBuilder.md) | `Vehicle_BuildTransmissionDescriptor` @ `0x5fc840` — transmission descriptor builder for framework setup. |
| [fn_005fcb00_brakeBuilder.md](fn_005fcb00_brakeBuilder.md) | `FUN_005fcb00` @ `0x5fcb00` — brake descriptor builder → `hkDefaultBrake` ctor path. |
| [fn_005fcce0_rearFrictionScalar.md](fn_005fcce0_rearFrictionScalar.md) | `RearWheelFrictionScalar` (`VehSpec+0x740`) applied in wheels builder `FUN_005fcce0` @ `0x5fcce0`. |
| [fn_005fcce0_wheelsBuilder.md](fn_005fcce0_wheelsBuilder.md) | `FUN_005fcce0` @ `0x5fcce0` — wheels descriptor builder for `hkDefaultWheels` setup. |
| [fn_005fcff0_suspBuilder.md](fn_005fcff0_suspBuilder.md) | `Vehicle_BuildSuspensionDescriptor` @ `0x5fcff0` — suspension descriptor from VehSpec for framework. |
| [fn_005fd390_buildFramework.md](fn_005fd390_buildFramework.md) | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` — sole vehicle-physics setup: all components → framework. |
| [fn_005fd390_speedGovernor.md](fn_005fd390_speedGovernor.md) | Top-speed precompute written to `vehicle+0x110` (tail of buildFramework @ `0x5fd390`). |
| [fn_00636a60_tickSubsystems.md](fn_00636a60_tickSubsystems.md) | `VehicleAction_tickSubsystems` @ `0x636a60` — framework preUpdate + seven component `update` slots. |
| [fn_0064bbd0_wheelCollide.md](fn_0064bbd0_wheelCollide.md) | `hkVehicleWheelCollide::collide` @ `0x64bbd0` (+ `TtPhantom::castRay`) — suspension raycast/contact. |
| [fn_0064bc70_axleCount.md](fn_0064bc70_axleCount.md) | `axleCount = *(wheels+0x64)` path in `postTickApplyForces` @ `0x64bc70` (retail 2-axle loop). |
| [fn_0064bc70_postTick.md](fn_0064bc70_postTick.md) | `hkVehicleFramework_postTickApplyForces` @ `0x64bc70` — susp impulse, drive pack, friction solve, writeback. |
| [fn_0064bc70_suspImpulse.md](fn_0064bc70_suspImpulse.md) | Suspension impulse / `applyPointImpulse` detail inside `postTickApplyForces` @ `0x64bc70`. |
| [fn_0064cd30_frameworkCtor.md](fn_0064cd30_frameworkCtor.md) | `hkVehicleFramework_ctor` @ `0x64cd30` — framework construct: header, wire-up, precompute, action list. |
| [fn_0064cf20_preUpdate.md](fn_0064cf20_preUpdate.md) | `hkVehicleFramework_preUpdate` @ `0x64cf20` — per-wheel ray build, collide vtbl, compression write. |
| [fn_0064d810_avd.md](fn_0064d810_avd.md) | `hkAngularVelocityDamper_update` @ `0x64d810` — continuous per-step angular-velocity scale (Havok AVD). |
| [fn_0064d900_avdCtor.md](fn_0064d900_avdCtor.md) | `hkAngularVelocityDamper_ctor` @ `0x64d900` — copy three floats from stack descriptor into AVD action. |
| [fn_0064da90_aeroCtor.md](fn_0064da90_aeroCtor.md) | `hkDefaultAerodynamics_ctor` @ `0x64da90` — install aero vtable; copy 8-word desc → `this+0x30..+0x4c`. |
| [fn_0064dae0_aero.md](fn_0064dae0_aero.md) | `hkDefaultAerodynamics_update` @ `0x64dae0` — per-step drag + lift/downforce + extra-gravity·mass. |
| [fn_0064de50_suspension.md](fn_0064de50_suspension.md) | `hkDefaultSuspension_update` @ `0x64de50` — per-wheel spring + damper → `suspForce[i]`. |
| [fn_0064e510_suspCtor.md](fn_0064e510_suspCtor.md) | `hkDefaultSuspension_ctor` @ `0x64e510` — construct default suspension component from descriptor. |
| [fn_0064ed40_brakeCtor.md](fn_0064ed40_brakeCtor.md) | `hkDefaultBrake_ctor` @ `0x64ed40` — construct default brake component (size `0x54`) from descriptor. |
| [fn_0064f610_transCtor.md](fn_0064f610_transCtor.md) | `hkDefaultTransmission_ctor` @ `0x64f610` — construct transmission component (heap `0x60`) from descriptor. |
| [fn_0064f840_steering.md](fn_0064f840_steering.md) | `hkDefaultSteering_update` @ `0x64f840` — normalized steer input → per-wheel physical wheel angle. |
| [fn_0064fac0_steeringCtor.md](fn_0064fac0_steeringCtor.md) | `hkDefaultSteering_ctor` @ `0x64fac0` — construct default steering component from descriptor. |
| [fn_0064fc80_tankSteering.md](fn_0064fc80_tankSteering.md) | `TankSteering_ctor` @ `0x64fc80` — tank steering component when VehSpec mode selects tank path. |
| [fn_0064fdf0_chassisCtor.md](fn_0064fdf0_chassisCtor.md) | `hkDefaultChassis_ctor` @ `0x64fdf0` — construct chassis component (heap `0x40`); CCS basis + class vtable. |
| [fn_0064fee0_wheelsCtor.md](fn_0064fee0_wheelsCtor.md) | `hkDefaultWheels_ctor` @ `0x64fee0` — construct stock wheels component (heap `0x390`) from descriptor. |
| [fn_006c4450_frictionSolve.md](fn_006c4450_frictionSolve.md) | `hkVehicleFrictionSolver_solve` @ `0x6c4450` — 2-axle coupled friction solve + circle projection. |

---

## Offset / struct maps & special gates

| File | One-line description |
|------|----------------------|
| [fn_entity_driveAxes_offsets.md](fn_entity_driveAxes_offsets.md) | Entity drive axes `+0x614` thr / `+0x618` steer / `+0x61c` sharp — writers, PushDrive, consumers. |
| [fn_friction_circleProjection.md](fn_friction_circleProjection.md) | `hkVehicleFrictionSolver_circleProjection` @ `0x6c3f90` — friction-circle clamp helper for solver. |
| [fn_offsets_rigidbody.md](fn_offsets_rigidbody.md) | Chassis rigid-body field map via `physicsObj+0x3c` (pose, lin/ang vel, invMass, etc.). |
| [fn_offsets_vehicleAction.md](fn_offsets_vehicleAction.md) | `VehicleAction` field offsets used by `applyAction` @ `0x598650`. |
| [fn_offsets_wheel.md](fn_offsets_wheel.md) | Per-wheel runtime state field map — stride `0xC0`, array base/count, readers/writers. |
| [fn_steering_input_feed.md](fn_steering_input_feed.md) | Steer-input feed chain into `hkDefaultSteering_update` @ `0x64f840` (command before angle math). |
| [fn_upright_restore.md](fn_upright_restore.md) | Upright-restore impulse when `dot(up, worldUp) < 0.7` (applyAction non-mode-0x02 branch). |
| [fn_vehicleFlags_bits.md](fn_vehicleFlags_bits.md) | `sinVehicleFlags` / `VehSpec+0x5f0` bit map for vehicle capability / mode flags. |

---

## Server / wiring notes

| File | One-line description |
|------|----------------------|
| [server_ghost_pack_notes.md](server_ghost_pack_notes.md) | Phase 6 — server `GhostVehicle.PackUpdate` PositionMask pose layout (angVel, thr, steer). |
| [server_handbrake_wire.md](server_handbrake_wire.md) | Ghost pose wire: `Firing` then `VehicleFlags`; handbrake = `VehicleFlags` bit0. |
| [server_npcTicker_hook_notes.md](server_npcTicker_hook_notes.md) | Phase 5 — `NpcTicker` × `ServerConfig` physics-tier controller/tick wiring checklist. |

---

## Quality / process

| File | One-line description |
|------|----------------------|
| [QUALITY_GATE.md](QUALITY_GATE.md) | Phase 3 module quality gate: decompile / constants / tests checklist per subsystem. |

---

## Sibling: [`../constants/`](../constants/)

Raw plate constants from `read_memory` (RE evidence only).

| File | One-line description |
|------|----------------------|
| [../constants/batch_A.md](../constants/batch_A.md) | Batch A — common f32 plates (0.5/1.0/0, clamps, steer ramps, aero ±0.5, mode-0x02 divisor 20, etc.). |
| [../constants/batch_B.md](../constants/batch_B.md) | Batch B — step-rate ≈30 Hz, epsilons, world-up, gravity −9.81, airStab/low-speed gates, ang-damp defaults. |
| [../constants/c_a15880_rate_table.md](../constants/c_a15880_rate_table.md) | Pool @ `0xa15880` (32 bytes / 8×f32) — period-like floats vs sim dt (not sim dt itself). |
| [../constants/c_a15894_gravity.md](../constants/c_a15894_gravity.md) | Gravity plate @ `0xa15894` = −9.81 f32 (world Y; LOD/update pool twin noted). |
| [../constants/c_af3380_steer_block.md](../constants/c_af3380_steer_block.md) | Contiguous 16-byte block @ `0xaf3380` — steer speed-factor / upright-restore plate floats. |
| [../constants/c_wheels_fixed.md](../constants/c_wheels_fixed.md) | Wheels fixed (non-DB) scalars written by `FUN_005fcce0` into `hkDefaultWheels` descriptor. |
