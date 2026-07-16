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
> torque.** That part remains true — but it is **not the whole story**.

> **Task B8 update (RESOLVED).** The framework-level `hkpVehicleDefaultBrake` component **is**
> ticked every substep — it is one of the framework's 7 fixed child components dispatched by
> `VehicleAction_tickSubsystems` (`0x636a60`), called from `applyAction`. Its computed per-wheel
> brake torque is read back in `hkVehicleFramework_postTickApplyForces` (`0x64bc70`) and folded
> into the friction-solver's per-wheel torque input, and its per-wheel lock flag is read in
> `hkVehicleFramework_preUpdate` (`0x64cf20`) to force wheel spin speed to **zero** (full lock).
> So retail braking is the **sum** of (a) the friction-solver coast/reverse behavior described in
> §1 below (driven through `calcWheelTorque`'s drive-torque clamp) **and** (b) a live Havok
> service-brake torque + wheel-lock, driven by a pedal signal synthesized from the throttle axis's
> reverse-direction component. Full evidence and call chain: **§5, item 1**.

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
| `VehicleAction_tickSubsystems`  | `0x636a60` | per-substep framework dispatcher; calls `hkDefaultBrake_update` as fixed child 5 of 7 (`fw+0x24`) |
| `hkVehicleFramework_wireComponents` | `0x636940` | wires `desc[6]` (brake) → `fw+0x24` |
| `hkDefaultAnalogDriverInput_calcStatus` | `0x5fe520` | derives brake-pedal status (`driverInput+0x10`) from the throttle axis's positive/reverse component; handbrake status (`+0x18`) = raw `driverInput+0x24` |
| `hkDefaultBrake_update`         | `0x64e6f0` | **TICKED** — vtbl slot `+0x14` of `PTR_FUN_009e4cb8`; computes per-wheel brake torque (`brake+0x10[i]`) + lock flags (`brake+0x1c[i]`) from pedal/handbrake status |
| `hkVehicleFramework_postTickApplyForces` | `0x64bc70` | reads `brake+0x10[i]`, folds into friction-solver per-wheel torque input |
| `hkVehicleFramework_preUpdate`  | `0x64cf20` | reads `brake+0x1c[i]` (isBlocked); if set, zeroes wheel spin speed (`wheel+0x8c`) — full wheel lock |

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

1. ~~Is the `hkpVehicleDefaultBrake` component actually ticked at runtime?~~
   **RESOLVED (Task B8) — TICKED, and its output is consumed downstream.**

   **Why `get_xrefs_to` / `get_function_callers` on `hkDefaultBrake_update` (`0x64e6f0`) find no
   caller:** the only static reference is the vtable data slot itself
   (`get_xrefs_to(0x64e6f0)` → `From 009e4ccc [DATA]`, i.e. `PTR_FUN_009e4cb8+0x14`). The call is
   an **indirect vtable dispatch** (`(**(code**)(*(int*)param_1[9] + 0x14))(param_2)` inside
   `tickSubsystems`) — Ghidra's static call graph does not resolve indirect calls through a
   pointer loaded from a component array, so callee-side xref/caller tools give a false negative
   here (same is true for every other Havok component `update`, e.g. `calcStatus` at `0x5fe520` —
   only xref is its own vtable slot `0x9dd37c`). The proof comes from the **caller side**:
   decompiling `tickSubsystems` and `wireComponents` and confirming the vtable slot address.

   **Call chain (every physics substep):**
   ```
   VehicleAction::applyAction        (0x598650, call site 0x5987a2)
     → VehicleAction_tickSubsystems  (0x636a60)                — this = hkVehicleFramework
         fw.vtbl+0x14  = preUpdate            (0x64cf20)   [step 0]
         fw+0x14.vtbl+0x14 = driverInput.calcStatus (0x5fe520) [step 1]
         fw+0x18.vtbl+0x14 = steering.update        (0x64f840) [step 2]
         fw+0x1c.vtbl+0x14 = engine-slot.update      (0x5d66a0) [step 3]
         fw+0x20.vtbl+0x14 = transmission.update     (0x64f510) [step 4]
         fw+0x24.vtbl+0x14 = hkDefaultBrake_update    (0x64e6f0) [step 5]  ← THE BRAKE, TICKED HERE
         fw+0x28.vtbl+0x14 = suspension.update        (0x64de50) [step 6]
         fw+0x2c.vtbl+0x14 = aero.update               (0x64dae0) [step 7]
         fw.vtbl+0x18  = postTickApplyForces  (0x64bc70)   [step 8]
   ```
   `wireComponents` (`0x636940`) wires `desc[6]` → `fw+0x24` = the brake object built by
   `FUN_005fcb00` / `hkDefaultBrake_ctor` (`0x64ed40`) — the exact same object this doc's §1/§2
   already track. Vtable `PTR_FUN_009e4cb8` slot `+0x14` = `0x64e6f0` (confirmed by `read_memory`
   in `fn_0064ed40_brakeCtor.md` §7).

   **Pedal input is real, not zero.** `hkDefaultAnalogDriverInput_calcStatus` (`0x5fe520`, tick
   step 1, same dispatcher) computes, from the driver's raw throttle axis at `driverInput+0x20`
   (= `entity+0x614`, `Accel=-1`/`Reverse=+1`), optionally sign-flipped when a
   transmission-reverse-gear flag (`*(transmission+0x14)`) **and** a local flag
   (`driverInput+0x3c`) are both set:
   ```
   accel(+0xc)      = (v <= 0) ? -v : 0        // forward component → drive (unused by AA engine)
   brakePedal(+0x10) = (v >= 0) ?  v : 0        // reverse/brake component → Havok brake pedal
   handbrake(+0x18)  = driverInput+0x24          // raw entity+0x61c byte, verbatim
   ```
   So **whenever the driver's throttle axis is in the reverse direction** (or, if already
   confirmed in reverse gear, whenever the flip condition does *not* hold), that magnitude is fed
   as a genuine `[0,1]`-ish Havok brake-pedal value into `hkDefaultBrake_update`, which applies the
   documented (§2a) `peak = pedal * maxBrakingTorque[i]` per-wheel torque and the
   `minPedalInputToBlock` / instant-block (`minTimeToBlock=0`) lock logic — using the **same**
   `wheelsMaxBrakingTorque` / `wheelsMinPedalInputToBlock` / `wheelsIsConnectedToHandbrake` arrays
   documented in §2/§5fcb00.

   **Output is consumed, not dead.** In `hkVehicleFramework_postTickApplyForces` (`0x64bc70`):
   ```c
   local_3ec = ( *(float*)(*(int*)(fw+0x24) + 0x10)[i]     // brake+0x10[i] — hkDefaultBrake_update output
              +  *(float*)(*(int*)(fw+0x20) + 0x20)[i] )   // transmission residual
              / *(float*)(wheels+0x10)[i];                  // per-wheel inertia-like divisor
   ```
   `local_3ec` is written into the per-wheel friction-solver input row (`acStack_280[...-4]`),
   which is passed as `arg2`/`arg4` to `hkVehicleFrictionSolver_solve` (`0x6c4450`) alongside the
   drive-torque term (`wheels+0x28[i] * wheel+0x88`, a separate slot in the same row). And in
   `hkVehicleFramework_preUpdate` (`0x64cf20`, next substep):
   ```c
   if ( *(char*)(*(int*)(fw+0x24) + 0x1c)[i] == 0 )         // brake+0x1c[i] isBlocked == false
        wheel[i].spinSpeed(+0x8c) = (contactTorque + residual) / wheelInertia;  // normal spin
   else
        wheel[i].spinSpeed(+0x8c) = 0;                       // LOCKED — full wheel lock/skid
   ```
   i.e. a locked wheel (handbrake-connected + handbrake asserted, or brake-armed past the
   instant-block timer) has its angular velocity forced to **zero** every substep — a real,
   externally-observable effect (skid marks / locked-wheel friction), not vestigial config.

   **Conclusion: TICKED.** `hkpVehicleDefaultBrake` runs every physics substep as a first-class
   framework child, consumes a real (throttle-derived) pedal + the raw handbrake byte, and its two
   outputs (per-wheel brake torque, per-wheel lock) are both read by other framework stages in the
   same tick loop. The §1 "friction solver only" story was based on `applyAction`/`calcWheelTorque`
   alone and is **incomplete**: retail deceleration = friction-solver coast/reverse-drive **plus**
   this Havok brake torque/lock layer, running in parallel every substep.

   **Implications for the C# port (flagged, not applied — no C# changed in this task):**
   - `HkVehicleBrake.cs` (see `docs/reconstruction/RESUME.md` production layout) needs to be a
     live per-substep subsystem, not just a config holder: it must (1) receive a pedal value
     derived the same way (`max(reverseComponentOfThrottleAxis, 0)`, gear-flip caveat above) and
     the raw handbrake bit, (2) compute `peak = pedal * maxBrakingTorque[i]` opposing-spin torque
     per wheel, (3) apply instant-lock semantics (`minTimeToBlock` from DB is always **0** per the
     builder — §2/`fn_005fcb00_brakeBuilder.md` §4c — so block is immediate once
     `pedal >= minPedalInputToBlock` on an armed wheel or handbrake-connected wheel gets handbrake asserted).
   - The port's friction solver / wheel-spin integrator must **consume** this brake output the
     same way retail does: fold brake torque into the per-wheel torque/inertia term feeding the
     friction solver, and force wheel angular velocity to zero when locked — currently these two
     read sites (`postTickApplyForces`, `preUpdate`) are exactly where the port's equivalent
     integration step must add the same terms if not already doing so.
   - The transmission-reverse-gear flip condition (`transmission+0x14` && `driverInput+0x3c`) was
     **not** independently re-verified in this task (out of scope for B8); treat the "reverse
     axis → brake pedal, unless confirmed-in-reverse-gear → drive" rule as **high confidence but
     the exact gear-flag semantics as a follow-up if bit-exact reverse/brake blending is required.**
2. **VehicleSpecific absolute base for `+0xbc…+0xd0`.** Relative contiguous layout is solid; the
   `EBP → VehicleSpecific*` identity should be confirmed against the struct used by the physics
   build before wiring the port.
3. **Front/Rear split of `m_wheelsMinTimeToBlock`.** Stock Havok has a single
   `m_wheelsMinTimeToBlock`; AA exposes Front/Rear separately (per-wheel-group), implying a custom
   or per-wheel application — confirm at the setup site.
