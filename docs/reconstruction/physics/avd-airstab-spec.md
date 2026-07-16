# AVD / Air-Stabilization / Collision-Recovery — Bit-Exact Port Spec

**Target:** `autoassault.exe`, image base `0x00400000`. Ghidra project *AA-decode*.
Every address / decompile re-pulled fresh. Read-only analysis.

This spec corrects two earlier misreadings in `docs/NPCDriving.md §6.3`:
1. **Continuous AVD is a real Havok action** — `hkAngularVelocityDamperAction`, updated every
   physics step (`hkAngularVelocityDamper_update @ 0x64d810`). Its "collision" damping is **not**
   gated by the 6400-tick collision timer; it is gated by **current angular speed** vs
   `AVDCollisionThreshold`.
2. **`DAT_00a110d8 = 10.0` is NOT an angular-damping additive.** In `airStabilization` it is a
   **terrain re-ground raise** (chassis Y + 10.0 before the down-cast), used only in
   post-collision recovery.

---

## 1. Continuous AVD — `hkAngularVelocityDamper` (THE application site)

The three `rlAVD*` clonebase fields feed a Havok `hkAngularVelocityDamperAction` that is added to
the vehicle framework and **runs its `update` every simulation step** — this is the continuous
angular-velocity damping, independent of any collision timer.

### 1.1 Construction — `hkAngularVelocityDamper_ctor @ 0x64d900`
Called once from `Vehicle_buildHavokVehicleFramework @ 0x5fd390` (the sole vehicle-physics setup
fn, itself called by `Vehicle_createVehicleAction @ 0x4fb660`). Damper is the last component built
before `hkVehicleFramework_ctor`, and is pushed into the framework action list.

Damper instance layout:
| Off | Field | Source (vehicleData = clonebase struct at `entity+0x3c`) |
|----:|-------|----------------------------------------------------------|
| `+0x00` | vtable `PTR_FUN_009e4a68` | — |
| `+0x08` | `normalSpinDamping`    | `vehicleData+0x5b8` × `vehicle[0x84]` (mass/scale factor) |
| `+0x0c` | `collisionSpinDamping` | `vehicleData+0x5bc` × `vehicle[0x84]` (mass/scale factor) |
| `+0x10` | `collisionThreshold`   | `vehicleData+0x5c0` (NOT scaled) |

`vehicle[0x84]` = float at `vehicle+0x210`, a per-vehicle scale applied to both damping rates but
**not** to the threshold.

### 1.2 Per-step update — `hkAngularVelocityDamper_update @ 0x64d810`
`(this=damper, param_2=&dt, param_3=action-context)`. `body = *(ctx+0x30)` (chassis component);
`rb = *(body+0x3c)` (hkRigidBody). Angular velocity at `rb+0x50/54/58` (x/y/z), `+0x5c` = 4th slot.

```
w  = (rb+0x50, rb+0x54, rb+0x58)              // angular velocity xyz
w2 = wx*wx + wy*wy + wz*wz
if w2 <= collisionThreshold*collisionThreshold:      // |w| <= threshold
    d = normalSpinDamping    * dt
else:                                                 // spinning faster than threshold
    d = collisionSpinDamping * dt
f = 1.0 - d
if f < 0.0: f = 0.0
rb.angVel(+0x50,+0x54,+0x58,+0x5c) *= f
rb.setAngularVelocity(w)                              // hkRigidBody vtbl +0x54
```

**Semantics:** each step scales angular velocity by `max(0, 1 − rate·dt)`, on **all three axes**
(roll/yaw/pitch) plus the 4th slot. `normalSpinDamping` is the gentle cruise damping;
`collisionSpinDamping` (larger) kicks in automatically the instant `|w|` exceeds
`AVDCollisionThreshold` (rad/s) — i.e. when a hit or landing spins the car up. Purely
speed-triggered; no timer.

---

## 2. Field mapping (VehicleSpecific → runtime)

| SQL column (vPrefixVehicle / clonebase) | vehicleData off | Damper off | Applied in |
|-----------------------------------------|-----------------|-----------|-----------|
| `rlAVDNormalSpinDamping`    (str `@0xa9316c`) | `+0x5b8` ×scale | `+0x08` | `0x64d810` when `|w|≤thr` |
| `rlAVDCollisionSpinDamping` (str `@0xa93138`) | `+0x5bc` ×scale | `+0x0c` | `0x64d810` when `|w|>thr` |
| `rlAVDCollisionThreshold`   (str `@0xa93108`) | `+0x5c0`        | `+0x10` | `0x64d810` gate |

Loaded by `VehicleDb_LoadCloneBase @ 0x7f3020` (COM recordset). Prefix/upgrade adjust columns
`rlAVDNormalSpinDampeningAdjust` / `rlAVDCollisionSpinDampeningAdjust` (`@0xa8c574 / @0xa8c530`).
Havok reflection descriptor for the class lives at `.rdata 0x9e4a40..0x9e4b1c`
(members `normalSpinDamping @0x9e4af4`, `collisionSpinDamping @0x9e4adc`, class name
`hkAngularVelocityDamper @0x9e4b08`).

---

## 3. Collision-window recovery — `VehicleAction_airStabilization @ 0x598320`

Runs each tick from `applyAction`, AFTER `calcWheelTorque`. Gated by
`g_dwClientTickMs − entity+0x14 < 0x1900` (**6400 ms** since last collision).
This is a **separate** mechanism from the continuous damper (§1): it applies a one-shot corrective
impulse and, on window expiry, re-grounds the car. It does **not** set AVD rates.

### 3.1 In collision window (`ticks_since_collision < 0x1900`)
- Sets in-collision flag `VA+0x1c = 1`.
- If chassis speed `> _DAT_009d54a8` (~`1.19e-7`, i.e. moving at all):
  - Reads chassis angular velocity (`FUN_0053e0b0`) and transform (`FUN_00404c90` / `FUN_00404a20`).
  - Builds a corrective impulse and applies it via physics vtbl: `+0x3c` (apply angular impulse),
    `+0x40` (apply point impulse), through `CVOGPhysics_ApplyImpulseVector`.

### 3.2 Post-collision recovery (window just expired, `VA+0x1c != 0`)
- Clears `VA+0x1c = 0`.
- Resets **3 stabilizer slots** at `entity+0x260` (loop `0..0xC` step 4) via `FUN_0056a260`.
- Clears angular velocity: rigidbody vtbl `+0x50` then `+0x54` (with `DAT_00b04eb0` identity arg).
- Calls `VehicleEntity_SetDriveAxes(0)` (drive reset).
- **Re-grounds**: raises chassis Y by `10.0` then down-casts:
  `y = *(rb+0xb4) + DAT_00a110d8(10.0)`; `CVOGMap_CastTerrainHeight(rb+0xb0 [x], rb+0xb8 [z])`;
  writes result back to position (`rb+0xb0/b4/b8`) via vtbl `+0x40` (set position).

### 3.3 airStabilization constants
| Addr | Value | Role |
|------|------:|------|
| `0x1900` (immediate) | 6400 | collision-window length (ms) |
| `DAT_009d54a8` `@0x9d54a8` | `0x34000000` ≈ 1.19e-7 | "is moving" speed epsilon |
| `DAT_00a110d8` `@0xa110d8` | `0x41200000` = **10.0** | **re-ground Y raise** (not damping) |
| `DAT_00b04eb0` | identity vec | zero-angVel / reset arg |

---

## 4. Upright-restore impulse — `VehicleAction_applyAction @ 0x598650`

Lives in the **velocity-coupled steering** path (the `else` branch taken when movement-mode byte
`vehicleData+0x4ce != 0x02`). After building a desired-orientation basis from the chassis rotation
matrix (`rb+0x30..0x5c`) and world axes (`DAT_00af3390/94/98`, `DAT_00af33a0/a4/a8/ac`):

```
up_dot = fwd.x*af3394 + fwd.y*af3398 + fwd.z*af3390     // dot(bodyUp-ish, worldUp)
if (up_dot < DAT_00af3380 [0.7]) && (g_flMultiKillCountBlend < up_dot):   // tilted, not fully inverted
    // build corrective axis: desired = worldUp - proj(worldUp onto current), normalize
    proj  = -(qx*af3394 + qy*af3398 + qz*af3390)
    corr  = normalize(q*proj + worldUp)
    angle = acos(clamp(dot(corr, currentBasis), -1, 1))  // _CIacos; ±sign via axis compare
    // magnitude
    m     = (1/inertia) * dt * _DAT_00af3378 [0.8] * angle * throttleInput(param_2[1])
    impulse.xyzw = corr * m  −  steerTerm(rb+0x50/54/58/5c * (DAT_00af337c[0.1]*param_2[1]))
    if valid (FUN_005d6870): FUN_005994e0(impulse)       // apply angular impulse
    else: log "Illegal Impulse Detected: A/X/Y/Z"
```

- **Threshold `DAT_00af3380 = 0.7` `@0xaf3380`** (`0x3f333333`): when `dot(up,worldUp)` drops below
  0.7 (car tilted > ~45°) the righting impulse engages; the lower guard
  `g_flMultiKillCountBlend < up_dot` skips fully-inverted cars.
- `_DAT_00af3378 = 0.8` `@0xaf3378` (`0x3f4ccccd`): righting-impulse magnitude scale.
- `DAT_00af337c = 0.1` `@0xaf337c` (`0x3dcccccd`): steering-damp term scale.
- Impulse is scaled by `throttleInput` (`param_2[1]`) and `dt` — no throttle ⇒ no righting.

Separately, the **aerodynamic air block** (function tail) engages while airborne (`VA+0x2c==0`) with
boost timers, using `DAT_00af3374 = 0.5 @0xaf3374` and applying `CVOGPhysics_ApplyImpulseVector`
scaled by velocity — the "gain air but stay drivable" ramp lift.

---

## 5. Function / constant index

| Addr | Symbol | Role |
|-----:|--------|------|
| `0x64d810` | `hkAngularVelocityDamper_update` | **continuous AVD** per-step |
| `0x64d900` | `hkAngularVelocityDamper_ctor` | builds damper from clonebase AVD |
| `0x5fd390` | `Vehicle_buildHavokVehicleFramework` | wires damper into framework |
| `0x4fb660` | `Vehicle_createVehicleAction` | outer setup |
| `0x598320` | `VehicleAction_airStabilization` | collision-window impulse + re-ground |
| `0x598650` | `VehicleAction_applyAction` | upright-restore impulse (0.7 gate) |
| `0x4cfe60` | `CVOGMap_CastTerrainHeight` | re-ground down-cast |

| DAT | Addr | Value | Meaning |
|-----|------|------:|---------|
| `DAT_00a110d8` | `0xa110d8` | 10.0 | airStab re-ground Y raise |
| `DAT_009d54a8` | `0x9d54a8` | ~1.19e-7 | airStab moving-epsilon |
| `DAT_00af3380` | `0xaf3380` | 0.7 | upright-restore dot threshold |
| `_DAT_00af3378`| `0xaf3378` | 0.8 | righting-impulse magnitude |
| `DAT_00af337c` | `0xaf337c` | 0.1 | steering-damp term |
| `DAT_00af3374` | `0xaf3374` | 0.5 | aero/boost air block |
| collision window | imm | 6400 | `0x1900` ms |

---

## 6. Port recipe (server / JS reconstruction)

1. **Continuous damper** per physics step, per axis:
   `w *= max(0, 1 − (|w| <= AVDCollisionThreshold ? AVDNormalSpinDamping : AVDCollisionSpinDamping) * dt)`
   using the clonebase `rlAVD*` fields (scale the two damping rates by the vehicle mass/scale
   factor; leave the threshold unscaled). This alone reproduces "car stops tumbling after air".
2. **Collision window (6400 ms):** on a registered collision, for ~6.4 s apply a corrective
   angular/linear impulse each tick; on expiry, zero angular velocity and snap to terrain
   (`Y+10 → cast down`).
3. **Upright restore:** when `dot(bodyUp, worldUp) < 0.7` and throttle > 0, apply a righting angular
   impulse of magnitude `~0.8 * angle * throttle * dt / inertia` toward world-up. Skip if inverted.
