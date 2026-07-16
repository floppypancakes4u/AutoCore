# VERIFIED — `VehicleAction_applyAction` @ `0x598650`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Symbol:** `VehicleAction_applyAction`  
**Convention:** `__thiscall` (`ECX` = Havok `VehicleAction* this`)  
**Body:** `0x598650` – `0x5994c4`  
**Verified:** 2026-07-15 — `decompile_function` @ `0x598650`, `get_function_callees`,  
`audit_globals_in_function`, `read_memory` on all formula DATs, `decompile_function` of  
`VehicleAction_tickSubsystems` @ `0x636a60`.

**Identity strings (xrefs from this fn):**
- `"VehicleAction::havok code"` @ `0x9d5534`
- `"VehicleAction::applyAction"` @ `0x9d5550`

**Related verified notes:**
- `fn_00598040_calcWheelTorque.md` — per-wheel drive torque
- `fn_00598320_airStab.md` — collision-window recovery
- `fn_entity_driveAxes_offsets.md` — entity `+0x614/+0x618/+0x61c`
- `fn_0064f840_steering.md` / `steering-spec.md` — physical wheel angles (downstream)
- `0.1-step-rate.md` — who passes `param_2[0] = substep_dt`

---

## 1. Role

Per-substep **AA custom vehicle driver** layered over Havok 2.3 `hkVehicleFramework`. Invoked from
the island action list as vtable slot **`+0x14`** of the VehicleAction vtable (`0x009d54c4`, set by
ctor). It does **not** integrate the rigid body itself; it:

1. Early-outs idle / bad body state  
2. Ticks the Havok framework + children (`tickSubsystems`)  
3. Anti-sink chassis lift from suspension penetration  
4. Ramps steer input and applies either analog wheel-steer or velocity-coupled upright impulse  
5. Writes per-wheel drive torque (`calcWheelTorque`)  
6. Runs collision-window air-stab (`airStabilization`)  
7. Grounded helpers + optional dead/disabled boost/air impulse  

**Scope of this file:** orchestration order and the math that lives **inline** in `applyAction`.  
Detailed formulas for callees belong in their own verified notes.

---

## 2. Signature / parameters

| Arg | Meaning |
|-----|---------|
| `this` (`param_1`) | Havok `VehicleAction` instance |
| `param_2` | `float*` block: **`param_2[0] = substep_dt`**, **`param_2[1] = throttle input`** (used by upright-restore + boost path) |

Upstream: `CVOGSectorMap::StepTo` (`0x4d6c80`) builds `substep_dt = frameDt/N` and the island step
hands that into `applyAction` (see `0.1-step-rate.md` / `fn_004d6c80_stepTo.md`).

### Key `this` offsets used here

| Off | Field | Use in this fn |
|----:|-------|----------------|
| `+0x1c` | in-collision flag | written by `airStabilization` (not here) |
| `+0x20` | throttle / ramp-rate partner | **multiplies stage-1 steer ramp** (`* dt * sign_factor`) |
| `+0x24` | **steering-ramp stage 1** | ramps toward `entity+0x618`; mirror → wheelsDesc `+0x1c` |
| `+0x28` | **steering final** (mode `0x02`) | ramps toward speed-scaled target; `setSteeringAngle` |
| `+0x2c` | all-wheels-airborne (1 = none in contact) | set by `calcWheelTorque`; gates grounded helpers / boost |
| `+0x30` | boost timer | dead/disabled air block |
| `+0x34` | boost cooldown timer | dead/disabled air block |
| `+0x3c` | Havok vehicle/steering instance | `+0x5c` steer-enable byte (mode `0x02`) |
| `+0x40` | framework / wheel container (`this` of `tickSubsystems`) | `+0xc` wheelsDesc, `+0x14` steering desc |
| `+0x44` | vehicle entity back-ptr | axes, flags, rigid body |

> **Correction vs older plate comments:** `VA+0x24` is **not** a brake float. It is the
> steer-axis ramp stage. There is **no service-brake torque** in this function
> (see `brake-spec.md` / `fn_00598040_calcWheelTorque.md`).

### Entity offsets used here (`entity = *(this+0x44)`)

| Off | Field | Use |
|----:|-------|-----|
| `+0x14` | last-collision / last-drive tick (ms) | idle watchdog vs `g_dwClientTickMs` |
| `+0x102` | steering-suppress flag | mode `0x02`: skip `setSteeringAngle` when ≠ 0 |
| `+0x103` | forced-stop / dead | early-out gate; boost block gate |
| `+0x618` | **raw steer axis** | stage-1 ramp target |
| data `+0x4ce` | movement-mode byte | `0x02` = analog wheel steer; else velocity-coupled |
| data `+0x7d` | animation-suspend | skip anim vtbl `+0x110` when set |
| data `+0x7e` | fully-disabled | early-out / boost block |

Movement-mode path (decompile):

```
vehicleData = *(*( *(*(entity+600)+4)+4 ) + 0xac + *(entity+600) ) + 0x3c
mode = *(u8*)(vehicleData + 0x4ce)
```

---

## 3. Orchestration order (authoritative)

Exact control-flow order from the decompile. Port / sim reconstruction **must** preserve this
sequence per substep.

```
VehicleAction_applyAction(this, {dt, throttleInput}):

  ── 0. Setup ──────────────────────────────────────────────────────────────
  entity = this+0x44
  base   = *(*(entity+4)+4) + entity          // shared data / flag base

  ── 1. Idle / stuck early-out ─────────────────────────────────────────────
  if  data[+0x7e] == 0                        // not fully-disabled
  and entity[+0x103] == 0                     // not dead
  and bit1 of *(base+0x180) == 0
  and (g_dwClientTickMs - entity[+0x14]) > 0x77A1   // > 30625 ms idle
  and *(base+0x18) == 0:                      // no target / aggro
      log / clear drive (FUN_007a4480, zero slot, FUN_004d4790)
      return

  ── 2. Body-state early-out ───────────────────────────────────────────────
  if entity[+0x8] != 0:
      st = (*(rb vtbl + 0x18))(rb)            // rb = *( *(entity+8)+0x3c )
      if st == 6: return                      // inactive / non-simulating body

  ── 3. Profile / scope enter ──────────────────────────────────────────────
  FUN_0076cf00(); FUN_0076cf00();             // timing / scope (not physics math)

  ── 4. SUBSYSTEMS (Havok framework + children)  FIRST ─────────────────────
  VehicleAction_tickSubsystems(this+0x40, param_2)
      // ECX = framework at VA+0x40  (NOT VehicleAction)
      // fw+8 += dt
      // fw.vtbl+0x14  (preUpdate)          e.g. 0x64cf20
      // 7 children [+0x14..+0x2c].vtbl+0x14  (susp/steer/aero/AVD/… updates)
      // fw.vtbl+0x18  (postTick)           e.g. 0x64bc70  — friction/drive forces
  FUN_0076cef0();

  ── 5. Suspension anti-sink lift ──────────────────────────────────────────
  wheelsDesc = *( *(this+0x40) + 0xc )
  scan wheelsArray = wheelsDesc+0x80, count = wheelsDesc+0xc, stride 0xC0:
      minComp = min over wheel[+0xB0]         // suspension current length
  if minComp < 0:                             // penetrating
      pos = chassis world pos (rb+0xB0 or entity pose +0x84)
      pos.y -= minComp                        // raise by -minComp
      set position (vtbl +0x40) with dirty guards FUN_005070b0 / FUN_005070d0

  ── 6. STAGE-1 STEER RAMP  (entity+0x618 → VA+0x24 → wheelsDesc+0x1c) ────
  // Often mislabeled "throttle ramp" in older notes — target is STEER axis.
  // Rate uses VA+0x20 (throttle slot) * dt * sign_factor.
  delta = entity[+0x618] - this[+0x24]
  if delta != 0:
      sign_factor = 2.0  if (current off-zero and target inside open [-1,1] window)
                 else 1.0   // g_flLevelUpUiBase_Inferred @ 0xa10e74 = 2.0; g_flOne = 1.0
      step = this[+0x20] * dt * sign_factor
      step = min(|delta|, |step|)
      this[+0x24] += sign(delta) * step
      this[+0x24] = clamp(this[+0x24], -1.0, +1.0)
      *( *(this+0x40)+0x14 ) + 0x1c  = this[+0x24]   // wheelsDesc desired steer

  ── 7. STEER APPLY (movement-mode branch) ─────────────────────────────────
  if mode == 0x02:                            // analog / wheel-angle path (common)
      if entity[+0x102] == 0:
          *(this+0x3c + 0x5c) = 1             // steering active
          speed = |chassisLinVel|             // rb+0x40..0x48
          speedFactor = min(speed / 20.0, 1.0) // _DAT_00af3388 = 20.0  (NOT 0.6)
          target = wheelsDesc[+0x1c] * speedFactor
          // ramp this+0x28 toward target by ±0.05 per tick (DAT_00a10e78)
          this[+0x28] = clamp(this[+0x28], -1.0, +1.0)
          hkpVehicleSteering_setSteeringAngle(…, this[+0x28])   // 0x636410
      else:
          *(this+0x3c + 0x5c) = 0             // suppress steer
  else:
      // velocity-coupled orientation basis from chassis quat (rb+0x30..0x3c)
      // + world axes DAT_00af3390..ac
      // UPRIGHT-RESTORE angular impulse when:
      //   up_dot = bodyUp · worldUp  <  0.7   (DAT_00af3380)
      //   and g_flMultiKillCountBlend (0.1) < up_dot   // skip fully inverted
      // magnitude ∝ (1/inertia) * dt * 0.8 * angle * param_2[1]
      // damp term: angVel * (0.1 * param_2[1])
      // apply via FUN_005994e0 if FUN_005d6870 ok, else "Illegal Impulse Detected"

  ── 8. ENGINE / TORQUE ────────────────────────────────────────────────────
  VehicleAction_calcWheelTorque();            // 0x598040 → wheels+0x28[i]
                                              // also sets this+0x2c airborne flag

  ── 9. AIR STABILIZATION (collision window) ───────────────────────────────
  VehicleAction_airStabilization();           // 0x598320  — see fn_00598320_airStab.md
                                              // (continuous AVD is a child of tickSubsystems)

  ── 10. Grounded helpers ──────────────────────────────────────────────────
  if this[+0x2c] == 0:                        // at least one wheel in contact
      sample chassis pos
      FUN_0053e090(); FUN_004f3680();         // pose / helper (not torque math)

  ── 11. Animation tick (if not suspended) ─────────────────────────────────
  if data[+0x7d] == 0:
      (entity vtbl + 0x110)()

  ── 12. Dead / disabled boost + air impulse (tail) ────────────────────────
  if entity[+0x103] != 0  OR  data[+0x7e] != 0:
      // boost timers this+0x30 / +0x34; DAT_00af3374 = 0.5 threshold
      // optional CVOGPhysics_ApplyImpulseVector when airborne + velocity gates
      // (DAT_00af3364..0x70 cluster)
  else:
      // normal live drive: this block skipped

  ── 13. Scope exit ────────────────────────────────────────────────────────
  FUN_0076cef0();
  return
```

### Order summary (one line)

```
early-outs → tickSubsystems (preUpdate + children + postTick)
          → anti-sink lift
          → stage-1 steer ramp (VA+0x24)
          → mode-0x02 setSteeringAngle  OR  velocity-coupled upright impulse
          → calcWheelTorque
          → airStabilization
          → grounded helpers / anim / dead-boost tail
```

### Corrections to older docs

| Claim (old) | Verified |
|-------------|----------|
| Subsystems run after torque / late in tick | **No** — `tickSubsystems` is **step 4**, before steer/torque/airStab |
| `VA+0x24` is brake | **No** — steer-ramp stage 1 |
| Stage-1 ramp is “throttle ramp” toward thr axis | **No** — ramps **`entity+0x618` (steer)**; rate uses **`VA+0x20`** |
| `DAT_00af3388 = 0.6` for speedFactor | **No** — raw bytes `00 00 A0 41` = **20.0** (`0.6` lives at neighbor `0xaf3384`, unused here) |
| Continuous AVD is inside `airStabilization` | **No** — continuous AVD is a **framework child** updated in `tickSubsystems`; `airStabilization` is the 6400 ms collision-window path |
| Upright 0.7 gate is on the aero tail | **No** — upright-restore is in the **mode ≠ 0x02** branch, **before** calcWheelTorque |

---

## 4. Constants (`read_memory`)

| Symbol / site | Address | Bytes (LE) | Value | Role in this fn |
|---|---|---|---:|---|
| idle watchdog | imm | — | **`0x77A1` (30625)** | ms since `entity+0x14` before idle clear |
| collision window (callee) | imm | — | **`0x1900` (6400)** | used inside `airStabilization`, not here |
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | clamp max; basis normalize; sign_factor default |
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **−1.0** | clamp min for `VA+0x24` / `+0x28` |
| `g_flLevelUpUiBase_Inferred` | `0x00a10e74` | `00 00 00 40` | **2.0** | stage-1 ramp `sign_factor` when off-zero |
| `DAT_00a10e78` | `0x00a10e78` | `cd cc 4c 3d` | **0.05** | mode-`0x02` per-tick ramp on `VA+0x28` |
| `_DAT_00af3388` | `0x00af3388` | `00 00 a0 41` | **20.0** | `speedFactor = min(\|v\|/20, 1)` |
| `DAT_00af3380` | `0x00af3380` | `33 33 33 3f` | **0.7** | upright-restore `up·worldUp` upper gate |
| `g_flMultiKillCountBlend` | `0x00a0f730` | (plate 0.1) | **0.1** | upright lower gate (skip inverted) |
| `_DAT_00af3378` | `0x00af3378` | `cd cc 4c 3f` | **0.8** | upright impulse magnitude scale |
| `DAT_00af337c` | `0x00af337c` | `cd cc cc 3d` | **0.1** | upright angVel damp × `param_2[1]` |
| `DAT_009d54a4` | `0x009d54a4` | `db 0f 49 40` | **π** | acos fallback when `|dot| ≥ 1` and negative |
| `DAT_00af3374` | `0x00af3374` | `00 00 00 3f` | **0.5** | boost timer threshold (dead/disabled block) |
| `DAT_00a0f70c` | `0x00a0f70c` | `cd cc 4c 3e` | **0.2** | boost-timer gate (`this+0x30 ≤ 0.2`) |
| `_DAT_00af3370` | `0x00af3370` | `00 00 c0 3f` | **1.5** | boost cooldown compare |
| `_DAT_00af336c` | `0x00af336c` | `9a 99 99 3e` | **0.3** | air impulse vertical vel gate |
| `_DAT_00af3368` | `0x00af3368` | `9a 99 99 3e` | **0.3** | air impulse scale factor |
| `DAT_00af3364` | `0x00af3364` | `00 00 20 41` | **10.0** | air impulse scale clamp |
| worldUp.x/y/z | `0x00af3390`..`98` | `00.. / 00 00 80 3f / 00..` | **(0,1,0)** | upright / basis |
| axis B | `0x00af33a0`..`ac` | `00 / 00 / 00 00 80 3f / 00` | **(0,0,1,0)** | second basis axis for mode≠2 |

**Neighbor (do not use as mode-0x02 divisor):** `DAT_00af3384` = **0.6** (`9a 99 19 3f`) — not referenced by the speedFactor line (`_DAT_00af3388`).

---

## 5. Stage-1 steer ramp (detail)

```
// entity+0x618 = raw steer; this+0x20 = rate partner (throttle slot); this+0x24 = stage-1
delta = entity[+0x618] - this[+0x24]
if delta == 0: skip

// decompiler: g_flLevelUpUiBase_Inferred when current is strictly non-zero and target
// is still inside the open clamp interval; else g_flOne
if (this[+0x24] < 0 && entity[+0x618] > -1) || (this[+0x24] > 0 && entity[+0x618] < +1):
    sign_factor = 2.0          // 0xa10e74
else:
    sign_factor = 1.0          // g_flOne

step = this[+0x20] * dt * sign_factor
if |delta| < step: step = |delta|

if delta > 0:  this[+0x24] = min(this[+0x24] + step, +1.0)
if delta < 0:  this[+0x24] = max(this[+0x24] - step, -1.0)

wheelsDesc = *( *(this+0x40) + 0x14 )     // steering-desc side of container
wheelsDesc[+0x1c] = this[+0x24]
```

There is **no** inline ramp of `entity+0x614` (throttle axis) inside this function. Longitudinal
command reaches the controller via `PushDriveAxesToController` (`0x4fbc10`); drive force is produced
in `calcWheelTorque` + framework `postTick`.

---

## 6. Mode `0x02` steer final (detail)

```
*(this+0x3c)+0x5c = 1
speed       = sqrt(vx²+vy²+vz²)                 // rigid body linear vel
speedFactor = min(speed / 20.0, 1.0)            // 0xaf3388
target      = wheelsDesc[+0x1c] * speedFactor

if target != this[+0x28]:
    this[+0x28] += (target > this[+0x28]) ? +0.05 : -0.05
    if |target - this[+0x28]| < 0.05: this[+0x28] = target

this[+0x28] = clamp(this[+0x28], -1.0, +1.0)
hkpVehicleSteering_setSteeringAngle(steeringObj, this[+0x28])  // stores into obj+0x50
```

Physical per-wheel angles: `hkDefaultSteering_update` @ `0x64f840` during **next** (or this)
framework update — quadratic inverse-speed falloff (see `fn_0064f840_steering.md`).

When `entity+0x102 != 0`: clear steer-enable byte and **skip** the ramp/setter entirely.

---

## 7. Mode ≠ `0x02` upright-restore (detail)

Builds orthonormal-ish basis vectors from chassis quaternion (`rb+0x30..0x3c`) and world axes,
then:

```
up_dot = bodyUp · worldUp                       // worldUp = (0,1,0)
if up_dot < 0.7  AND  0.1 < up_dot:
    // desired righting direction = normalize(worldUp - proj onto current basis)
    angle = acos(clamp(dot(desired, current), -1, 1))   // _CIacos; sign via axis majority
    invI  = (rb+0x2c == 0) ? 0 : 1/(rb+0x2c)
    m     = invI * dt * 0.8 * angle * param_2[1]
    damp  = 0.1 * param_2[1]
    impulse = desired * m  −  angVel * damp
    if FUN_005d6870(impulse) ok: FUN_005994e0(impulse)
    else: log "Illegal Impulse Detected: A/X/Y/Z"
```

**No throttle (`param_2[1] == 0`) ⇒ zero righting magnitude.** Fully inverted (`up_dot ≤ 0.1`) is
skipped by the lower gate.

---

## 8. `tickSubsystems` @ `0x636a60` (called here)

`this` at call site is **`VA+0x40` (framework)**, not VehicleAction.

```
fw[+0x8] += dt
fw.vtbl[+0x14](dt)                 // preUpdate  (0x64cf20 typical)
for child in fw[+0x14], [+0x18], …, [+0x2c]:   // 7 children
    child.vtbl[+0x14](dt)          // includes continuous AVD @ 0x64d810 when present
fw.vtbl[+0x18](dt)                 // postTick   (0x64bc70 — apply forces / friction solve)
```

So within a single `applyAction` substep, **friction/drive force application from the previous
torque write happens inside this early `postTick`**, and **this** tick’s `calcWheelTorque` output
feeds the **next** framework postTick (classic one-substep lag on the custom torque path). Port
authors should preserve that ordering unless intentionally changing latency.

---

## 9. Callees (from `get_function_callees`)

| Address | Symbol | When |
|--------:|--------|------|
| `0x636a60` | `VehicleAction_tickSubsystems` | always (after early-outs) |
| `0x636410` | `hkpVehicleSteering_setSteeringAngle` | mode `0x02` and not suppressed |
| `0x598040` | `VehicleAction_calcWheelTorque` | always (after steer branch) |
| `0x598320` | `VehicleAction_airStabilization` | always (after torque) |
| `0x5994e0` | `FUN_005994e0` | mode≠2 upright impulse apply |
| `0x5d6870` | `FUN_005d6870` | impulse validity check |
| `0x40d260` | `CVOGPhysics_ApplyImpulseVector` | dead/disabled air boost tail |
| `0x4e8a40` | `FUN_004e8a40` | basis extract for boost impulse |
| `0x53e090` / `0x4f3680` | helpers | when not all-airborne |
| `0x5070b0` / `0x5070d0` | dirty / write-enable | anti-sink position write |
| `0x76cf00` / `0x76cef0` | scope enter/exit | bookkeeping only |
| `0x6a3e26` | `_CIacos` | upright angle |
| `0x7a4480` | log | idle clear / illegal impulse |
| `0x4d4790` | clear-drive helper | idle early-out |

---

## 10. Port checklist

- [x] Decompile `0x598650` (this note)
- [x] Constants re-read (`read_memory`) for speedFactor **20.0**, upright **0.7/0.8/0.1**, ramp **0.05/2.0**
- [x] Orchestration: **subsystems before** steer/torque/airStab
- [x] Stage-1 ramps **steer** (`entity+0x618`), not thr as target
- [x] `VA+0x24` documented as steer-ramp (not brake)
- [ ] Server `VehicleActionSim` must call modules in §3 order with the same `dt`
- [ ] Continuous AVD port lives under framework child update, **not** as a substitute for `airStabilization`

### Open / out of scope here

- Exact body-state enum for `vtbl+0x18 == 6` (treated as non-simulating early-out only)
- Semantics of `FUN_0053e090` / `FUN_004f3680` (grounded helpers; not torque)
- SpeedLimiter / AbsoluteTopSpeed application site remains **unconfirmed** in this function
  (plate still marks OPEN; not present in decompile body)
- How `VA+0x20` is initially filled each session (PushDrive → controller path; copy into action
  may be elsewhere) — only consumption is verified here

---

## 11. Evidence index

| Tool | Target | Result |
|------|--------|--------|
| `list_open_programs` | — | `autoassault.exe` current |
| `decompile_function` | `0x598650` | full body; order as §3 |
| `decompile_function` | `0x636a60` | framework preUpdate → 7 children → postTick |
| `get_function_callees` | `0x598650` | torque, airStab, setSteeringAngle, … |
| `audit_globals_in_function` | `0x598650` | DAT list incl. `g_flLevelUpUiBase_Inferred@a10e74` |
| `read_memory` | `0xa10e74` | `00000040` → 2.0 |
| `read_memory` | `0xa10e78` | `cdcc4c3d` → 0.05 |
| `read_memory` | `0xaf3388` | `0000a041` → **20.0** |
| `read_memory` | `0xaf3380` | `3333333f` → 0.7 |
| `read_memory` | `0xaf3378` len 16 | 0.8, 0.1, 0.7, 0.6 |
| `read_memory` | `0xaf3364` len 24 | 10.0, 0.3, 0.3, 1.5, 0.5, 0.8 |
| `read_memory` | `0xa0f70c` | `cdcc4c3e` → 0.2 |
| `read_memory` | `0x9d54a4` | `db0f4940` → π |
| `read_memory` | `0xaaa668` | `000080bf` → −1.0 |
| `read_memory` | `0xaf3390` / `0xaf33a0` | worldUp (0,1,0); axis (0,0,1,0) |
