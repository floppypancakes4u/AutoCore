# Vehicle Brake Subsystem — Port Spec

Reverse-engineered from `autoassault.exe` (Ghidra project **AA-decode**, image base `0x400000`,
Havok 2.3 vehicle SDK). Read-only RE; no DB edits. Cross-referenced with
`0.3-friction-solver.md`, `0.8-struct-offsets.md`, `steering-spec.md`.

> **Premise correction (important).** The tasking assumed `VehicleAction+0x24` is a *brake* value
> ramped from the entity handbrake byte `+0x61c`. That is **wrong** and is already documented in
> `0.8-struct-offsets.md`: `VehicleAction+0x24` is the **steering-ramp stage** (ramps entity
> `+0x618` steer axis → `wheelsDesc+0x1c`), and `+0x28` is the speed-scaled final steer angle.
> **There is no dedicated service-brake float on the Havok VehicleAction, and neither
> `applyAction` (`0x598650`) nor `calcWheelTorque` (`0x598040`) applies a per-wheel braking
> torque.** Braking in the retail custom path is produced two ways (below).

---

## 1. How braking actually happens in the retail custom path

AA replaced Havok's engine component with its own `VehicleAction::calcWheelTorque` (`0x598040`),
which writes a **drive torque clamped to `[0, 1000]`** into the per-wheel array `wheelsDesc+0x28[i]`.
That array is the *drive-impulse* input to the friction solver (`0x6c4450` via
`hkVehicleFramework_postTickApplyForces` `0x64bc70`). Because the clamp floor is **0**, this path
can never emit a *negative* (retarding) torque — so **service deceleration is not a brake-torque
term; it emerges from the friction solver** opposing longitudinal slip when the driver releases
throttle or applies reverse (entity `+0x614` longitudinal axis, `Accel=-1 / Reverse=+1`).

The solver's longitudinal law (see `0.3-friction-solver.md` Phase D):
```
lambda_long = -Jv_long - clamp( invKeff·(drivePack·0.5 + Jv_long)·0.5, ±(mu·|N|·dt) )
```
With `drivePack≈0` (throttle released) the term reduces to `-Jv_long` bounded by the friction
circle `mu·|N|·dt` — i.e. the tire brakes the car down to rolling. Reverse throttle drives
`drivePack` opposite to motion, adding an active decel impulse. **No `hkDefaultBrake` torque is
summoned in this path.**

### 1a. Handbrake (entity `+0x61c`) — the one brake term in the custom code
`calcWheelTorque` (`0x598040`), per rear wheel only:
```
if (entity[+0x61c] != 0  &&  wheelIndex > vehicleData[+0x4cc] /*rear-start index*/):
        torque *= DAT_00a0f298      // 0.5
```
This is a **rear-traction cut**: it halves the rear wheels' *drive* torque so the rear breaks
traction for handbrake slides/burnouts. It is **not** a braking torque and does **not** touch the
front wheels. `entity+0x61c` is the handbrake byte, copied by `PushDriveAxesToController`
(`0x4fbc10`) to the input controller `[entity+0x1a0]+8` at `+0x24`. `DAT_00a0f298` (`0x00a0f298`
= `0x3f000000` = **0.5**) is shared with the solver's drive-impulse blend — it is **not**
`RearWheelFrictionScalar`. The real `RearWheelFrictionScalar` (`vehicleData+0x740`) scales the
rear friction-table entries at *setup* time (see `0.3-friction-solver.md`).

---

## 2. The `hkpVehicleDefaultBrake` config fields (DB → VehicleSpecific)

The six `rl…` brake fields exist in the vehicle DB and are parsed by
`VehicleDb_LoadCloneBase` (`0x7efb40`). Each column is bound by name and its float stored via
`MOVSS [EBP+off]`; they form a **contiguous 6-float block** in DB read order:

| DB column (string @addr) | VehicleSpecific off* | Havok `hkpVehicleDefaultBrake` field |
|---|---|---|
| `rlBrakesMaxTorqueFront`  (`0xa9344c`) | `+0xbc` | `WheelBrakingProperties[front].m_maxBreakingTorque` |
| `rlBrakesMaxTorqueRear`   (`0xa93420`) | `+0xc0` | `WheelBrakingProperties[rear].m_maxBreakingTorque` |
| `rlBrakesMinBlockTimeFront`(`0xa933ec`)| `+0xc4` | front `m_wheelsMinTimeToBlock` (wheel-lock delay) |
| `rlBrakesMinBlockTimeRear` (`0xa933b8`)| `+0xc8` | rear `m_wheelsMinTimeToBlock` |
| `rlBrakesPedalInputFront` (`0xa93388`) | `+0xcc` | front `m_minPedalInputToBlock` |
| `rlBrakesPedalInputRear`  (`0xa93358`) | `+0xd0` | rear `m_minPedalInputToBlock` |

Related: `wheelsIsConnectedToHandbrake` (`0x9e4d28`) = Havok
`WheelBrakingProperties.m_isConnectedToHandbrake`; the RTTI type strings `hkDefaultBrake`
(`0x9e4d94`) and `hkBrakeComponent` (`0x9e71cc`) confirm the standard Havok brake classes are
compiled in. Prefix/upgrade adjusters `rlBrakesMaxTorqueFrontAdjustPercent` /
`…RearAdjustPercent` (`0xa8c670` / `0xa8c628`) scale MaxTorque via the prefix system.

\* *Offsets are the `EBP`-relative store targets in the bind block at `0x7f2d61–0x7f2e78`. The
`EBP → final VehicleSpecific*` identity was not separately proven (the writeback also copies a
scrambled stack image into `param_4`); treat the **relative contiguous layout** as authoritative
and confirm the absolute base before wiring.*

### 2a. Standard Havok brake semantics (for the port)
- **MaxTorque (Front/Rear):** peak braking torque applied to a wheel at full pedal:
  `brakeTorque = m_maxBreakingTorque * pedalInput` (pedalInput ∈ [0,1]).
- **PedalInput (Front/Rear) = `m_minPedalInputToBlock`:** the minimum brake input at which the
  wheel is *allowed to block* (lock). Below it the wheel keeps rolling under braking; at/above it
  the block timer runs.
- **MinBlockTime (Front/Rear) = `m_wheelsMinTimeToBlock`:** the dwell time (seconds) the brake
  must stay ≥ `minPedalInputToBlock` before the wheel fully **blocks** (angular velocity forced to
  0 → locked/skidding tire). Larger = wheel resists locking longer; `0` = locks instantly. This is
  the ABS-like anti-instant-lock delay; a locked wheel switches the tire to sliding friction.
- **Service brake vs handbrake:** the *service* brake acts on **all** wheels via their MaxTorque.
  The *handbrake* input applies braking **only to wheels with `m_isConnectedToHandbrake`**
  (typically rear) and forces block regardless of pedal — the classic handbrake-turn behavior.

---

## 3. Interaction summary (calcWheelTorque rear-traction cut)

`calcWheelTorque` per-wheel drive torque (rear wheel, `i > vehicleData+0x4cc`):
```
torque  = mu[i] * upright * curve2D(...)            // engine/drive term
if (entity[+0x61c] /*handbrake*/):  torque *= 0.5   // DAT_00a0f298  — REAR ONLY
torque  = clamp(torque, 0, 1000)                    // DAT_00a0f520  → wheelsDesc+0x28[i]
```
So the handbrake byte simultaneously (a) halves rear drive torque here, and (b) via a live
`hkpVehicleDefaultBrake` would apply handbrake braking torque to the handbrake-connected wheels.

---

## 4. Function & constant reference

| Symbol | Addr | Role |
|---|---|---|
| `VehicleAction_applyAction`     | `0x598650` | per-tick driver; ramps steer (`+0x24`→`+0x28`), boost, calls calcWheelTorque. **No brake term.** |
| `VehicleAction_calcWheelTorque` | `0x598040` | per-wheel drive torque; **handbrake rear ×0.5 cut** is the only brake-adjacent term |
| `hkVehicleFramework_postTickApplyForces` | `0x64bc70` | aggregates drive torque per axle → friction solver |
| `hkVehicleFrictionSolver_solve` | `0x6c4450` | longitudinal impulse = where service deceleration actually resolves |
| `VehicleDb_LoadCloneBase`       | `0x7efb40` | parses all 6 `rlBrakes*` fields (bind block `0x7f2d61–0x7f2e78`) |
| `PushDriveAxesToController`     | `0x4fbc10` | copies entity `+0x61c` handbrake → controller `+0x24` |

| DAT | Address | Value | Use |
|---|---|---|---|
| `DAT_00a0f298` | `0x00a0f298` | `0.5` | **rear handbrake drive-torque cut** (calcWheelTorque) & solver drive blend |
| `DAT_00a0f520` | `0x00a0f520` | `1000.0` | drive-torque clamp ceiling (floor is 0 → no negative/brake torque) |
| `DAT_00a10e74` | `0x00a10e74` | `2.0` | throttle ramp rate; also rear ×2 driver-modifier factor |
| `DAT_00a0f698` | `0x00a0f698` | `0.8` | upright dot threshold (traction falloff) |
| `DAT_00aaa7a4` | `0x00aaa7a4` | `15.0` | low-speed traction-boost threshold |
| `DAT_00a0f70c` | `0x00a0f70c` | `0.2` | low-speed traction-boost slope |

---

## 5. Ambiguities / open items

1. **Is the `hkpVehicleDefaultBrake` component actually ticked at runtime?** The 6 config fields
   are loaded and the Havok brake RTTI is compiled in, but the per-tick **service-brake torque
   call site was not located** — the AA custom driver (`applyAction`/`calcWheelTorque`) does not
   invoke it, and the retail deceleration observed is fully explained by the friction solver +
   reverse throttle. The brake fields may be (a) consumed by a Havok brake component ticked inside
   the framework step that this pass did not decompile, or (b) partially vestigial config carried
   from the Havok template. **Resolve:** trace the framework per-tick component dispatch (callers
   of `0x64bc70` / the `preUpdate` path) for a `hkpVehicleDefaultBrake::*` call, or breakpoint-hold
   the brake and watch `wheelsDesc` angular velocities for a forced block.
2. **VehicleSpecific absolute base for `+0xbc…+0xd0`.** Relative contiguous layout is solid; the
   `EBP → VehicleSpecific*` identity should be confirmed against the struct used by the physics
   build before wiring the port.
3. **Front/Rear split of `m_wheelsMinTimeToBlock`.** Stock Havok has a single
   `m_wheelsMinTimeToBlock`; AA exposes Front/Rear separately (per-wheel-group), implying a custom
   or per-wheel application — confirm at the setup site.
