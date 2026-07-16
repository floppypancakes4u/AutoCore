# NPC Vehicle Driving — Client Reverse-Engineering & Reconstruction Reference

**Target:** `autoassault.exe` (retail Auto Assault client), image base `0x00400000`
**Tooling:** Ghidra project *AA-decode* via `ghidra-mcp` (26,238 functions).
**Compiled:** 2026-07-15. Consolidates + re-verifies `NPC_DRIVING_FIX_RE.md`, `NPC_VEHICLE_DRIVE_RE.md`,
`MOTION_CLIENT_RE.md`, `nullWheels.md`, `NPC.md`. Every address / decompile in §3–§7 was re-pulled
fresh from the loaded binary for this document.

---

## 0. Answer to the driving question

> *"Does the client contain the code for how NPCs actually drive vehicles — taking ramps, gaining
> air, turning wheels — or is it all server scripting?"*

**Yes. The full NPC vehicle driving stack lives in the client, and it is a real vehicle simulation,
not path scripting.** Retail NPC cars are driven by the *same* control + physics code path as the
local player's car. An AI brain chooses an **aim point**, a controller converts that to
**throttle / steering / handbrake** axes, and **Havok 2.3's vehicle SDK** (suspension, wheel
friction, aerodynamics, air stabilization) turns those axes into motion. Ramps, air time, wheel
spin, and slope pitch/roll are *emergent from the physics*, never authored.

This is why the retail game looked alive and AutoCore does not: **AutoCore authors the final chassis
pose on the server and streams it as truth**, so the client's physics never gets to run for foreign
NPCs. The reconstruction path (§8) is to have the server produce the *inputs* (drive axes +
curvature-limited speed + terrain-aligned pose) that the retail controller would have produced, and
stream those so the client's Havok layer can animate between corrections.

---

## 1. The four layers (retail pipeline)

```
┌─ AI BRAIN ────────────────────────────────────────────────────────────────┐
│ CVOGHBAIDriver::DoLogic                      005d7750  (state machine)      │
│   state 0 idle/patrol · state 1 engage · state 2 combat (owner+0x26c)       │
│   ├─ ReturnToNormalLocation                  005d6e80  (path vs pursue gate) │
│   │    └─ CVOGWaypoint::UpdateState           005d6300                        │
│   │         state0 → FUN_005d5750  (MapPath follow + corner-slow)  005d5750  │
│   │              └─ CVOGMapPath::AdvanceAndSteer 005df950                     │
│   │                   • accept radius, ReactionCoid, reverse-wrap            │
│   │                   • aim point, curvature radius (prev/cur/next circle)   │
│   └─ CVOGHBAICreatureBase::DoVehiclePursue    (combat chase, when no path)   │
│   └─ CVOGHBAIFollowVehicle::FireWeapons       (always, independent of drive) │
└─────────────────────────────────────────────────────────────────────────────┘
                 │ aim point → vehicle+0x190..0x198
                 ▼
┌─ DRIVE CONTROLLER (produces axes) ─────────────────────────────────────────┐
│ CVOGVehicle::MoveToTarget3DPoint             004fc650                        │
│   writes  throttle vehicle+0x614 · steer +0x618 · sharp/handbrake +0x61c    │
│   └─ VehicleEntity_PushDriveAxesToController  004fbc10                        │
│        pushes axes into VehicleAction (vehicle+0x1a0); NO-OP if action null  │
└─────────────────────────────────────────────────────────────────────────────┘
                 │ ctrl+0x20 throttle · ctrl+0x24 handbrake · ctrl+0x28 steer
                 ▼
┌─ HAVOK VEHICLE PHYSICS (per-tick) ─────────────────────────────────────────┐
│ VehicleAction::applyAction                   00598650                        │
│   • throttle ramp (rate 2.0/s) → steering scaled by min(|v|/0.6,1)          │
│   • VehicleAction::calcWheelTorque           00598040  (per-wheel drive)     │
│   • VehicleAction::airStabilization          00598320  (AVD + collision)     │
│   • hkVehicleFramework: suspension · friction solver · aerodynamics · lift   │
│   → chassis pos/rot/vel/angVel via Havok integration                        │
└─────────────────────────────────────────────────────────────────────────────┘
                 │
                 ▼
┌─ NETWORK APPLY (foreign vehicles) ─────────────────────────────────────────┐
│ Vehicle_setDrivingInputs                     00504c70  (net entry)          │
│   unpacks thr/steer/sharp (same +0x614/618/61c) then:                        │
│   FUN_0053eec0                               0053eec0  (soft-buffer / hard)  │
│     physics NOT fully ready → buffer pos/rot/vel/angVel, teleport if err>15  │
│     physics fully ready OR no shell → HARD overwrite entity +0x84/+0x94      │
└─────────────────────────────────────────────────────────────────────────────┘
```

Two distinct owners of a vehicle:
- **Local AI** (client owns the NPC) → runs the *entire* stack, Havok included. Ramps/air/wheels
  all real.
- **Foreign NPC** (another client / server owns it) → only the **network apply** runs; the client
  reproduces motion from streamed pose + drive axes, integrating with Havok *between* packets iff a
  `VehicleAction` exists on that foreign vehicle.

---

## 2. Function anchor index

| Address | Symbol / role | Layer |
|--------:|---------------|-------|
| `005d7750` | `CVOGHBAIDriver::DoLogic` — AI tick, 3-state machine | AI |
| `005d6e80` | `CVOGHBAIDriver::ReturnToNormalLocation` — path-vs-pursue gate | AI |
| `005d6300` | `CVOGWaypoint::UpdateState` — waypoint state machine | AI |
| `005d5750` | MapPath follow state0 — advance + corner speed-scale | AI |
| `005df950` | `CVOGMapPath::AdvanceAndSteer` — index/aim/curvature radius | AI |
| `005ce990` | Path heartbeat teleport — local-AI ground-clamp step | AI |
| `004fc650` | `CVOGVehicle::MoveToTarget3DPoint` — **the drive math** (thr/steer/sharp) | Controller |
| `004fbc10` | `VehicleEntity_PushDriveAxesToController` — axes → VehicleAction | Controller |
| `00504c70` | `Vehicle_setDrivingInputs` — network drive-axis entry | Net |
| `0053eec0` | Network pose apply — soft 15u buffer / hard write | Net |
| `00598650` | `VehicleAction::applyAction` — per-tick Havok vehicle driver | Physics |
| `00598040` | `VehicleAction::calcWheelTorque` — per-wheel engine torque | Physics |
| `00598320` | `VehicleAction::airStabilization` — AVD + collision recovery | Physics |
| `004a9750` | `VehicleEngine::torqueCurve2D` — 2D torque LUT | Physics |
| `0064bc70` | `hkVehicleFramework::postTickApplyForces` — axle aggregation | Havok |
| `006c4450` | `hkVehicleFrictionSolver::solve` — friction/drive impulse | Havok |
| `004e8a40`/`4e8ad0`/`4e8b60` | basis extractors (forward/right/up from transform) | Math |
| `004c6100` | `CVOGCreature::FindTerrainHeight` — Y snap (+foot) | Terrain |
| `004cfe60` | `CVOGMap::CastTerrainHeight` — down-cast ray height | Terrain |
| `004cd220` | terrain heightfield sample (used by heartbeat) | Terrain |

---

## 3. AI brain — `CVOGHBAIDriver::DoLogic` (`005d7750`)

Client-side simulation. State on **`owner+0x26c`**: `0` idle/patrol, `1` engage (wind-up), `2` combat.

- **State 0 (patrol/idle):** casts idle skills, then `ReturnToNormalLocation`. If that returns
  false (no active path / off-path) it steers toward `vtbl+0x1a0(1)` fallback target via
  `vtbl+0x4c` → drive controller. This is the plain patrol driving path.
- **State 1 (engage):** timed wind-up (`this[0x2d]` = engage start tick; compares elapsed against
  `waypoint[6]` ratio and `piVar4[6]`), casts skill-set 1, transitions to 0 or 2.
- **State 2 (combat):** casts skill-set 2, rolls two `CVOGReaction_RandomUnitScalar()` gates for
  reaction/skill firing, then `ReturnToNormalLocation`; **only if the path returns false and a
  target exists (`this[6]+0xa0`)** does it call `CVOGHBAICreatureBase::DoVehiclePursue(this)`.
- **Always**, at the end: `CVOGHBAIFollowVehicle::FireWeapons`. Weapon fire is *decoupled* from
  drive — an NPC keeps shooting while path-driving.

**Key implication:** an NPC on an active MapPath **never pursues** — path following wins over combat
lunge. Combat only steers the car when there's no path. This matches AutoCore's "path COID → no
combat lunge, fire only" design.

---

## 4. MapPath following — `005d5750` + `AdvanceAndSteer` (`005df950`)

### 4.1 State0 follower (`005d5750`)
1. `RTDynamicCast` the stored path COID (`+0x40`/`+0x44`) to `CVOGMapPath`. Null → clears path
   (`+0x50 = 2`, COIDs → `0xffffffff`) and returns.
2. Reads chassis position (`obj+0x3c → +0xb0`), calls path vtbl `+0x2cc`
   (**= `AdvanceAndSteer`**) with: pos, index ptr (`+0x48`), aim out (`+0x20`), reverse flag
   (`+0x51`), radius out (`local_24`). Stores accept result at `+0x52`.
3. **Wait nodes:** if a waypoint carries a dwell (`node+0x18`), sets `+0x54 = tick + dwell`; while
   `tick < +0x54` it **freezes the aim** (zeroes `+0x20..+0x2c`) → the car brakes and holds.
4. **Corner speed-scale** at `+0x58`:
   - If curvature radius `+0x58 ≥ 30` (`DAT_00a0f694`) **or** aim distance `≥ 30` → scale `= 0`
     (no extra slow; cruise).
   - Else `scale = (30 − radius) * 0.05` clamped to `[0,1]`, then `× (30 − dist) × (1/30)`
     (`_DAT_00aaab14`). Tight corners → positive slowdown factor.
5. Sets a "near target" bool at `+0x53` when aim distance `< +0x4c` (arrival gate).

### 4.2 `AdvanceAndSteer` (`005df950`) — index, aim, curvature
Point stride **`0x20`** bytes; array `[path+0x65 .. path+0x66)`. Point layout:
`+0x00 xyz`, `+0x0c accept-radius`, `+0x10/+0x14 ReactionCoid (I64)`, `+0x18 dwell(ms)`.

1. **Invalid index (`0xffffffff`)** → linear scan for nearest point (full 3-D squared distance),
   set index.
2. **Outside accept radius** (`d² > r²`) → keep index, fill aim = current point (`vtbl+0x2c4`).
3. **Inside accept radius** → resolve `ReactionCoid`, if it casts to `CVOGClonedObjectBase` fire
   `vtbl+0x114(reaction)`; then advance `index ± 1` per reverse flag; wrap at ends (loop path →
   `0`; ping-pong path (`+0x68`) → flip reverse flag and clamp to `len-2`).
4. Load **prev / current / next** point positions.
5. **Turn radius** = circumradius of the circle through those three points
   (standard determinant form; degenerate/near-collinear → sentinel `DAT_00a0f520 = large`).
   Written to `*param_8`. Aim heading angle → `*param_7` via `FUN_00788400`.

So the retail AI already has **look-ahead geometry** (3-point curvature) and **corner slowdown** — it
does **not** teleport the chassis node-to-node; it produces an **aim point + a speed cap**.

---

## 5. Drive controller — `CVOGVehicle::MoveToTarget3DPoint` (`004fc650`)

The heart. Reads chassis transform + the AI aim point at `vehicle+0x190..0x198`; writes three axes.
Signature (recovered): `(this, float acceptDist param_2, float cruiseScale param_3, aim,
char allowReverse param_5)`.

**Outputs**
| Offset | Axis | Meaning |
|-------:|------|---------|
| `+0x614` | **throttle** | forward/reverse magnitude (reverse floor `−1.0`) |
| `+0x618` | **steering** | lateral aim error vs chassis right vector, clamped `[−1,+1]` |
| `+0x61c` | **sharp/handbrake** | `1` when speed high **and** heading error large |

**Math (recovered from decompile)**
```
toAim   = aim − chassisPos                       // +0x190..+0x198 minus obj +0xb0
distXZ  = sqrt(dx*dx + dz*dz)                     // planar distance (param_2 = accept)
inv     = 1 / |toAim|                             // full 3-D normalize
right   = FUN_004e8ad0(basis)                     // chassis +X (right)
fwd     = FUN_004e8a40(basis)                     // chassis +Z (forward)
lateral = right · (toAim*inv)                     // steer signal   (fVar9)
forward = fwd   · (toAim*inv)                     // thr alignment  (fVar10)
speed   = |chassis linear velocity|              // obj+0x40..+0x48

if allowReverse==0 OR forward >= 0:   revSign = +1   else revSign = −1   // DAT_00aaa668=-1

// STEERING
if |lateral| >= 0.01 (DAT_00a0f718):             // outside deadband
    steer = clamp( revSign * lateral * base, −1, +1 )   // → +0x618 (unless flag-blocked)
else:                                            // inside deadband
    if forward >= 0:  SetSteerInput(0)
    else:             SetSteerInput( lateral<=0 ? −1 : +1 )   // reverse-align spin

// THROTTLE
thr = revSign
if speed > 0 (DAT_00aaa688):
    if distXZ < 30 (DAT_00a0f694) and thr < distXZ:  thr *= distXZ * (1/30)  // ease near target
    if cruiseScale (param_3) valid:
        if forward < thr:  cruiseScale *= −1     // face-away → reverse cruise
        thr *= cruiseScale
+0x614 = thr

// SHARP / HANDBRAKE-ASSIST
sharp = (speed > 15.0  &&  |lateral| > 0.70) ? 1 : 0     // +0x61c
```
**No position write anywhere in this function.** It is pure input generation; Havok moves the car.

**Constants (verified addresses)**
| Addr | Value | Role |
|------|------:|------|
| `00a0f718` | 0.01 | steer deadband |
| `00a0f710` | 0.70 | lateral threshold for sharp |
| `00aaa7a4` | 15.0 | speed gate for sharp / low-speed traction |
| `00a0f694` | 30.0 | near-target distance / curvature reference |
| `00aaab14` | 1/30 | curvature/near-target ease scale |
| `00a10e78` | 0.05 | steer/curve ramp step |
| `00aaa668` | −1.0 | reverse / clamp floor |
| `00a0f298` | 0.5 | rear-traction cut under `+0x61c` |

### 5.1 Axes → controller — `PushDriveAxesToController` (`004fbc10`)
Requires `entity+0x101 == 0` (not disabled) and **`entity+0x1a0 != 0` (VehicleAction present)**.
`ctrl = *(entity+0x1a0)+8`.
- `ctrl+0x20 = entity+0x614` (throttle). Clamp to `0.9` (`DAT_00a0f734`) if `ctrl+0x19` set.
- `entity+0x109` (forced stop) → `ctrl+0x20 = 0`, `ctrl+0x24 = 1`.
- `ctrl+0x24 = entity+0x61c` (handbrake byte).
- **Speed cap gate:** derives a max speed from driver/vehicle (`+0x634` cap field, `+0x10c`
  requested), and if current speed exceeds it while throttle *opposes* forward, zeroes throttle.

**Critical:** if `entity+0x1a0` (VehicleAction) is null, this whole function is a no-op → **throttle
never reaches Havok → wheels never spin.** This is the `nullWheels` failure class.

---

## 6. Havok physics layer

### 6.1 `VehicleAction::applyAction` (`00598650`)
Custom AA layer over **Havok 2.3 `hkVehicleFramework`**. Asserted by strings
`"VehicleAction::havok code"` @ `0x9d5534`, `"VehicleAction::applyAction"` @ `0x9d5550`.
`param_2 = {dt, throttleInput}`.

Instance layout:
| Off | Field |
|----:|-------|
| `+0x20` | current throttle |
| `+0x24` | current brake/handbrake |
| `+0x28` | current steer angle |
| `+0x2c` | all-wheels-airborne flag (set by calcWheelTorque) |
| `+0x30` | boost timer · `+0x34` boost cooldown |
| `+0x3c` | Havok vehicle instance (`+0x5c` = steer-enable byte) |
| `+0x40` | wheel container (`+0xc` count, `+0x80` array, `+0x28` output torque array) |
| `+0x44` | Vehicle entity (chassis + driver refs) |

Per tick:
1. **Stuck/aggro watchdog:** if not driven for `> 0x77a1` ms and no target → clears drive and bails.
2. **Suspension min-compression check:** scans wheels for most-compressed (`+0xb0` array), if
   negative (penetrating) nudges chassis up by `−minCompression` in Y (anti-sink).
3. **Throttle ramp:** `VA+0x24` ramps toward `entity+0x618` at rate `entity+0x20 × dt ×` sign
   (rate base `DAT_00a10e74 = 2.0/s`), clamped `[−1,+1]`. Writes wheels-desc `+0x1c`.
4. **Steering** (movement mode byte `entity...+0x4ce`):
   - `0x02` (analog, common): `speedFactor = min(|v|/0.6, 1)` (`DAT_00af3388=0.6`);
     `targetSteer = wheelDesc+0x1c × speedFactor`; ramp `VA+0x28` by `±0.05` (`DAT_00a10e78`) per
     tick toward target, clamp `[−1,+1]`; `hkpVehicleSteering::setSteeringAngle`.
   - else (velocity-coupled): the large quaternion block — builds desired angular velocity from
     chassis orientation and forward vector and applies a steering impulse (`FUN_005994e0`), with an
     "Illegal Impulse Detected" guard.
5. `VehicleAction::calcWheelTorque` — per-wheel drive torque (§6.2).
6. `VehicleAction::airStabilization` — AVD + collision (§6.3).
7. **Aerodynamic downforce / lift** (tail block): while airborne (`+0x2c`) and boost active, applies
   `CVOGPhysics::ApplyImpulseVector` scaled by velocity — this is the "gain air but stay drivable"
   behavior when taking ramps. Upright threshold `DAT_00af3380 = 0.7` (dot(up,worldUp) below → air
   handling).

### 6.2 `calcWheelTorque` (`00598040`) — per-wheel engine
There is **no `hkDefaultEngine`** — this is AA's engine replacement. Output → `wheels+0x28[i]`, which
`hkVehicleFramework::postTickApplyForces` (`0x64bc70`) aggregates per axle
(`× wheel+0x88 ÷ axleWheelCount`) as the **drive impulse** into `hkVehicleFrictionSolver::solve`
(`0x6c4450`).

Per wheel (only if **in contact** `wheel+0x80`, and power gate `entity..+0xe4f8 != 0`):
```
t   = VehicleEngine::torqueCurve2D(wheel+0x20, wheel+0x28)     // 2D LUT (rpm/gear × load)
driverMod = driver..+0x118  (default 0)
  mod > 0:  t = 1 − (1−mod)(1−t)                    // blend toward 1 (boost)
  mod < 0:  rear wheels ×2.0 first, then t ×= (1+mod)
upright = 1.0, unless |dot(bodyUp, worldUp)| < 0.8 (DAT_00a0f698) → pow() falloff
μ   = wheelsetFriction[i]                            // FUN_004f5550
lowSpeed: |v| < 15.0 → μ ×= (15−|v|)×0.2 + 1         // DAT_00a0f70c=0.2, low-speed grip
torque = μ × upright × t
if (entity+0x61c handbrake && rear wheel):  torque ×= 0.5   // DAT_00a0f298 rear traction cut
clamp [0, 1000.0]  →  wheels+0x28[i]                 // DAT_00a0f520=1000
```
`VA+0x2c` (all-airborne) is set when no wheel was in contact this pass. This is where **wheel spin
and traction** come from — driven by `torqueCurve2D`, not by pose.

### 6.3 `airStabilization` (`00598320`) — AVD + collision
Angular-velocity damping and collision recovery. Collision timer:
`g_dwClientTickMs − entity+0x14 < 0x1900` (~6400 ticks / ~1.07s @ 60Hz).
- **In collision window:** sets in-collision flag `VA+0x1c=1`; if moving, applies corrective
  angular/linear impulse (physics vtbl `+0x3c` angular, `+0x40` point impulse). Additive damping
  `DAT_00a110d8 = 10.0` on `angVel.y`, scaled per-vehicle by **AVDCollisionSpinDamping**.
- **Post-collision (window expired):** resets 3 stabilizer slots (`entity+0x260`, `FUN_0056a260`),
  clears angular velocity (vtbl `+0x50`/`+0x54`), re-grounds via `CVOGMap::CastTerrainHeight`.
- Acts on **all three angular axes** (roll/yaw/pitch). **AVDNormalSpinDamping** is applied
  continuously as `chassisBody.angularDamping` (set before world step).

This keeps a landing/ramp/collision car from tumbling forever — the "recover after air" behavior.

---

## 7. Network apply — foreign NPC vehicles

### 7.1 `Vehicle_setDrivingInputs` (`00504c70`)
Network entry when a *foreign* vehicle's ghost updates. Writes the **same** `+0x614/618/61c` axes
from the wire, then calls `PushDriveAxesToController` (needs VehicleAction) and `FUN_0053eec0` for
pose.

### 7.2 `FUN_0053eec0` — soft-buffer vs hard-write
`(this, pos[4], rot[4], vel[4], angVel[4], integrateDt)`.

- **Soft path** — physics object exists but **not fully ready** (`obj+0x40==0` or `obj+8==0`) OR a
  usable physics shell exists: buffer target pos/rot into `param_1[10]` (the dead-reckon buffer),
  gate velocity write on `|vel| ≥ 0.01` (`DAT_00a0f718`) else zero it, store angVel, and **teleport
  only if positional error `> DAT_009d000c` (~15u)**. Then if `integrateDt != 0`, calls
  `FUN_0053eb90(0, dt)` to integrate the buffer forward (uses buffered vel + **angVel**).
- **Hard path** — fully ready OR no usable shell, and `|pos| > _DAT_009d0010`: **overwrites** entity
  `+0x84` (position quaternion slot) and `+0x94` (rotation) directly. This *is* the visible chassis
  when Havok is active — physics `setRotation` is skipped, so **whatever rotation is on the wire is
  what the player sees**.

Integrate `dt` on the client = `ghostObj+0xBC × 0.001` (ms → s). Zero `dt` ⇒ no soft integration
between packets (car snaps at each pack). Buffer target `DAT_00b04610..1c` is the identity rotation
fallback when `|vel|` is sub-threshold.

**Consequence for reconstruction:** when physics is fully ready, the client **hard-writes the wire
rotation onto the entity quaternion**. So pitch/roll for slopes/ramps **must be on the streamed
rotation** — the server cannot delegate that to client suspension for a foreign car.

### 7.3 Local-AI ground clamp — `005ce990`
Client-only helper when local AI owns a path/reaction: steps along the aim, clamps Y to terrain
height (`FUN_004cd220` heightfield/cast) **+ 1.0 clearance**, then `CVOGReaction::TeleportTarget`.
Y-only ground clamp — not a multi-corner stance sampler.

---

## 8. Reconstruction — what the server must produce & stream

AutoCore cannot call these client functions; it must **emit the inputs/pose the retail controller
would have produced** so the client's Havok layer animates plausibly between packets.

### 8.1 Navigation vs movement split
| Component | Responsibility | Must NOT |
|-----------|----------------|----------|
| **Navigator** (`NpcPathFollower`) | index, AcceptDistance, dwell, reactions, reverse, look-ahead **aim**, curvature **radius** (port §4.2 circumradius), corner **speed cap** (§4.1) | write final chassis rotation from "face next node" |
| **Movement controller** | aim → speed(accel/brake) → throttle/steer/sharp (port §5) → **facing-aligned velocity** → **terrain-aligned rotation** | teleport along the chord while facing elsewhere |

### 8.2 Movement controller per tick (`dt ≈ 0.05–0.10s`)
1. **Aim** = look-ahead along polyline (~16–28u).
2. **Radius** = circumradius(prev,cur,next); **cruise** = clonebaseSpeed × cornerScale
   (`R≥30 → 1`, tighter → `(30−R)×0.05` ease, per §4.1).
3. **Longitudinal:** approach cruise with accel/brake limits; brake to 0 at dwell nodes.
4. **Heading:** desired yaw = `atan2` toward **aim**; clamp yaw-rate; **velocity must equal
   facing × speed** (bicycle model, small slip) — never chord-velocity while yaw lags.
5. **Position:** integrate `pos += facing × speed × dt`; arrival via **closest-point-on-segment /
   along-track**, not "distance to node while drifting beside it" (avoids AcceptDistance orbit).
6. **Terrain stance:** sample heightfield at chassis center + front/back (+ optional left/right)
   under clonebase length/width; average contact plane → **pitch/roll quaternion**; clamp
   pitch/roll rate; enforce clearance (~1.0, per §7.3); **do not exceed ~40° tilt** (client
   `airStabilization` will fight it; upright threshold 0.7).
7. **Drive axes always:** compute throttle/steer/sharp every moving tick via the §5 model — do
   **not** derive steer from `dYaw` of an already-aligned pose (steer≈0 → wheels look locked). Sharp
   when `speed>15 && |lat|>0.7`. **Do not set the Handbrake bit** for sharp on the wire.

### 8.3 Ghost fields to stream (PositionMask block)
| Field | Client consumer | Required value |
|-------|-----------------|----------------|
| Position XYZ | `0053eec0` hard/soft target | grounded; XZ from controller; Y from terrain plane |
| Rotation XYZW | entity `+0x94` / hard-write | **yaw + pitch + roll** (slope stance), not yaw-only |
| Velocity XYZ | soft buffer / dead-reckon | **facing × speed** (not waypoint chord) |
| AngularVelocity XYZW | `FUN_0053eb90` integrate | full `ω` incl. pitch/roll rates, not yaw-only |
| Acceleration (=thr) | `entity+0x614` | ~1 cruising, ease/brake on corners, reverse only re-orienting |
| Steering | `entity+0x618` | look-ahead lateral error |
| VehicleFlags/sharp | `entity+0x61c` path | sharp assist when warranted; **not** permanent handbrake |
| Integrate dt | `ghost+0xBC` | **non-zero** (ms) so soft buffer integrates between packs |

### 8.4 Client prerequisites (must be true, not packets)
1. Foreign vehicle **activated with a VehicleAction** (`+0x1a0`) so `PushDriveAxesToController`
   isn't a no-op → wheels spin, Havok runs between packs. (See `nullWheels.md` owner/wheel race.)
2. Owner/driver create order preserved (HUD + activate).
3. **Dense pose** (~50ms effective) so corrections stay well under the 15u soft-teleport threshold.

We **cannot stream suspension**; we stream **pose that already sits on the surface** + drive axes so
wheels animate. When physics is fully ready the hard pose *is* the chassis, so orientation **must**
carry terrain pitch/roll on the wire.

### 8.5 Current AutoCore implementation (branch `fix/npcDriving`, lever-gated)
| Lever | Default | Env |
|-------|---------|-----|
| `EnableNpcVehicleDriveController` | **false** (legacy) | `AUTOCORE_WIRE_NPC_VEHICLE_DRIVE` |

Ticker priority (vehicles only): drive-controller ON → `NpcVehicleDriveController.Apply`; else
soft ON → `SoftNpcPathMotion.Apply`; else hard `NpcPathFollower`. Foot creatures never drive.
Files: `src/AutoCore.Game/Npc/NpcVehicleDriveController.cs`, `PathCurvature.cs`,
`TerrainContactPlane.cs`; ghost packing in `GhostVehicle.cs` / `Vehicle.ApplyServerMove`.

**Open v1 items:** enable lever by default after live sign-off; dedicated sharp-turn wire nibble
(never the Handbrake bit); live path check (e.g. 5092 Skiddoo) — verify no orbit, no lateral slide,
wheels turn, slopes pitch, ramps produce air.

---

## 9. Verdict

The reconstruction data **exists in the client and is fully mapped**. This is not a
"code-doesn't-exist, invent physics" situation: AA ships a complete AI→controller→Havok vehicle sim,
and every stage is anchored above with addresses and recovered math. The server-side task is
faithful **input reproduction + surface-correct pose streaming**, not physics invention — the
client already owns the physics. The one genuinely open reconstruction guess is the
SpeedLimiter/AbsoluteTopSpeed application site (the `applyAction` plate flags it UNCONFIRMED); a
soft-taper approximation is acceptable until a runtime differential pins it.

### Cross-references
`NPC_DRIVING_FIX_RE.md` (fix plan) · `NPC_VEHICLE_DRIVE_RE.md` (first client RE) ·
`MOTION_CLIENT_RE.md` (pose apply) · `nullWheels.md` (VehicleAction/owner race) · `NPC.md` §15.7
(path/terrain) · `docs/reconstruction/` (evidence tree). Referenced but **not on disk** in this
tree: `docs/vehicle-physics-port.md`, `tools/model-viewer/vehicle/controller.js` (cited in Ghidra
plate comments; likely on another branch — see memory note on lost NPC.md revisions).
