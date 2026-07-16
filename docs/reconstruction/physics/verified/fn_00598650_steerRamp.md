# VERIFIED — Steer input ramp in `VehicleAction_applyAction` @ `0x598650`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x598650`; `decompile_function` @ `0x636410`  
(`hkpVehicleSteering_setSteeringAngle`); `read_memory` on DAT constants below;  
`get_assembly_context` / instruction walk @ `0x5988a3`–`0x598ad7` (mode-`0x02` path).

**Scope of this file:** two-stage steer **input ramp** only  
(`VA+0x24` → wheelsDesc, then `VA+0x28` ±0.05, then push to steering object).  
**Not this file:** non-mode-`0x02` upright-restore impulse block; `calcWheelTorque`;  
`airStabilization`; `hkDefaultSteering_update` angle math (`0x64f840` — separate verified note).

---

## 1. Identity

| Item | Value |
|------|------:|
| Entry | `0x598650` |
| Symbol (Ghidra) | `VehicleAction_applyAction` |
| Convention | `__thiscall` |
| `this` (`param_1` / `ESI`) | Havok `VehicleAction` |
| `param_2` | input block: `param_2[0]` = **dt**, `param_2[1]` = throttle-related |
| Assert strings | `"VehicleAction::havok code"` @ `0x9d5534`; `"VehicleAction::applyAction"` @ `0x9d5550` |

Steer-ramp region in the tick (after subsystem tick + suspension anti-sink, before torque/air):

```
… stage-1 ramp (always; VA+0x24) …
if movementMode == 0x02:
    if entity+0x102 == 0:
        … stage-2 ramp (VA+0x28 ±0.05) …
        hkpVehicleSteering_setSteeringAngle(steeringObj, VA+0x28 * 0.6)
    else:
        *(VA+0x3c)+0x5c = 0   // suppress
else:
    … upright / angular-impulse branch (not documented here) …
VehicleAction_calcWheelTorque();
VehicleAction_airStabilization();
…
```

---

## 2. Struct offsets (steer path)

### VehicleAction (`this` / `ESI`)

| Off | Type | Role in this path |
|----:|------|-------------------|
| `+0x20` | f32 | **Stage-1 rate base** (same field used elsewhere as “current throttle”) — multiplies into stage-1 step |
| `+0x24` | f32 | **Stage-1 steer** — ramps toward `entity+0x618`; mirrored to wheelsDesc `+0x1c` |
| `+0x28` | f32 | **Stage-2 steer** — ramps ±0.05/tick toward `wheelsDesc+0x1c × speedFactor` |
| `+0x3c` | ptr | Steering / vehicle instance — `+0x5c` enable byte; `this` for `setSteeringAngle` |
| `+0x40` | ptr | Wheel container → `+0x14` = desc used for `+0x1c` desired-steer write |
| `+0x44` | ptr | Entity back-ref |

⚠ **Plate mislabel:** Ghidra plate on this fn still says `+0x24 = brake`. Binary uses `+0x24` exclusively as **stage-1 steer** in this region. Handbrake lives on the **entity** / controller, not here.

### Entity (`*(VA+0x44)`)

| Off | Type | Role |
|----:|------|------|
| `+0x102` | u8 | Steering suppress — mode `0x02` skips stage-2 / setter when nonzero |
| `+0x258` | ptr | Instance chain → vehicle data → movement-mode byte |
| `+0x618` | f32 | **Raw steer axis** (written by drive / AI); stage-1 target |

Movement-mode chain (matches decompile / asm @ `0x59899c`…):

```
mode = *(*( *(*(entity+0x258)+4)+4 ) + 0xac + entity)+0x3c  →  byte at +0x4ce
```

Analog ramp path only when **`mode == 0x02`**.

### Wheels / steering desc

| Access | Meaning |
|--------|---------|
| `wheelsDesc = *(*(VA+0x40)+0x14)` | desc pointer used by stage-1 write |
| `wheelsDesc+0x1c` | desired / intermediate steer (copy of `VA+0x24` after stage-1) |
| chassis body linVel | `*(*(entity+8)+0x3c)+0x40..0x48` — speed for stage-2 factor |

---

## 3. Stage 1 — `VA+0x24` ramp toward `entity+0x618`

**Always runs** (not gated on mode `0x02`). Asm start: `0x5988a3`.

### 3.1 Pseudocode (bit-exact shape)

```
delta = entity[0x618] - VA[0x24]
if delta == 0:
    skip stage-1 writes

// rate factor: 2.0 in the "open" band, else 1.0
factor = 1.0
if (VA[0x24] < 0 && entity[0x618] > -1.0) ||
   (VA[0x24] > 0 && entity[0x618] < +1.0):
    factor = 2.0                    // DAT_00a10e74 / g_flLevelUpUiBase_Inferred

step = VA[0x20] * dt * factor
step = min(|delta|, step)

if delta < 0:
    VA[0x24] -= step
    if VA[0x24] < -1.0: VA[0x24] = -1.0
elif delta > 0:
    VA[0x24] += step
    if VA[0x24] > +1.0: VA[0x24] = +1.0

// mirror (also taken on clamp-to--1 early write path)
*( *(VA[0x40] + 0x14) + 0x1c ) = VA[0x24]
```

### 3.2 Notes

| Fact | Evidence |
|------|----------|
| Stage-1 is **dt-scaled** | `MULSS` of `VA+0x20`, `*param_2` (dt), and `factor` @ `0x598921`–`0x59892e` |
| Factor **2.0** is `DAT_00a10e74` | Loaded into `XMM5` @ `0x5988be`; assigned when open-band @ `0x598911` |
| Clamp uses **±1.0** | `g_flOne` @ `0xa0f2a0`, `DAT_00aaa668` @ `0xaaa668` |
| Sign of step is **add vs sub**, not a signed multiply | `ADDSS`/`SUBSS` on `VA+0x24` @ `0x59894a` / `0x598975` |

`VA+0x20` is the shared “current throttle / rate base” field: if it is 0, stage-1 does not move `VA+0x24` this tick (step = 0).

---

## 4. Stage 2 — mode `0x02` ramp of `VA+0x28` by **±0.05**

Asm gate: `CMP CL, 0x2` @ `0x5989b8`. Suppress: `entity+0x102 != 0` → `*(VA+0x3c)+0x5c = 0` and skip.

### 4.1 Speed factor

```
body  = *(*(entity+8)+0x3c)
speed = sqrt(vx²+vy²+vz²)     // body+0x40, +0x44, +0x48
speedFactor = min(speed / 20.0, 1.0)   // FDIV [0x00af3388]; clamp vs g_flOne
targetSteer = wheelsDesc[0x1c] * speedFactor
```

| Token | Address | `read_memory` float | Role |
|-------|---------|--------------------:|------|
| `_DAT_00af3388` | `0x00af3388` | **20.0** (`00 00 a0 41`) | speed divisor |
| Neighbor only | `0x00af3384` | **0.6** (`9a 99 19 3f`) | **not** the divisor — see §5 |

**Correction:** older notes / plate text that used **`speed / 0.6`** were reading the **wrong neighbor dword**. Binary divides by **20.0**.

### 4.2 Fixed step toward `targetSteer` — `DAT_00a10e78 = 0.05`

```
if targetSteer != VA[0x28]:
    if targetSteer > VA[0x28]:
        VA[0x28] += 0.05          // ADDSS [0x00a10e78] @ 0x598a6f
    else:
        VA[0x28] -= 0.05          // SUBSS [0x00a10e78] @ 0x598a79
    if |targetSteer - VA[0x28]| < 0.05:
        VA[0x28] = targetSteer    // snap @ 0x598a9b

// clamp to [-1, +1]
if VA[0x28] >  1.0: VA[0x28] =  1.0
if VA[0x28] < -1.0: VA[0x28] = -1.0
```

### 4.3 Critical properties of the 0.05 step

| Property | Verified behavior |
|----------|-------------------|
| Magnitude | **Exactly** `DAT_00a10e78` = **0.05** (`cd cc 4c 3d`) |
| dt coupling | **None** — step is **per applyAction tick**, not `× dt` |
| Overshoot | Snap when remaining error **&lt;** 0.05 (after the ± step write) |
| Equality skip | If `targetSteer == VA+0x28`, no add/sub this tick |
| Clamps | Same ±1.0 constants as stage-1 |

Time-to-full-authority (from 0 → 1 with constant target 1 and `speedFactor=1`):  
`ceil(1/0.05) = 20` ticks (less if snap fires on the last partial).

---

## 5. Push to steering object — **hidden ×0.6**

Decompiler output for the call site **omits** the multiply. Asm is authoritative:

```
00598abc  MOVSS  XMM0, [ESI+0x28]          ; VA+0x28
00598ac1  MULSS  XMM0, dword [0x00af3384]  ; × 0.6
00598aca  MOV    ECX, [ESI+0x3c]           ; steering object
00598acd  MOVSS  [ESP], XMM0
00598ad2  CALL   hkpVehicleSteering_setSteeringAngle  ; 0x636410
```

Setter body (`0x636410`):

```
*(steeringObj + 0x50) = arg;   // trivial float store
```

So the value stored is:

```
steeringObj[+0x50] = clamp(VA[+0x28], -1, +1) * 0.6
```

| Constant | Address | Value | Use |
|----------|---------|------:|-----|
| `DAT_00af3384` | `0x00af3384` | **0.6** | **Final scale** into `setSteeringAngle` |
| `DAT_00af3388` | `0x00af3388` | **20.0** | Speed-factor **divisor only** |

Do **not** conflate these two neighbors. Ports that feed `VA+0x28` straight into wheel-angle math without ×0.6 will over-steer vs retail for the same max-angle descriptor.

Physical wheel angle after this store is still produced by `hkDefaultSteering_update` (`0x64f840`):  
`angle = MaxAngle × steerInput × (speed≤FSL ? 1 : (FSL/speed)²)` — see `fn_0064f840_steering.md`.

---

## 6. Constants — `read_memory` verified

| Address | Bytes (LE) | Float32 | Role in steer ramp |
|---------|------------|--------:|--------------------|
| `0x00a10e78` | `cd cc 4c 3d` | **0.05** | Stage-2 step ± per tick (`VA+0x28`) |
| `0x00a10e74` | `00 00 00 40` | **2.0** | Stage-1 open-band rate factor |
| `0x00af3388` | `00 00 a0 41` | **20.0** | `speedFactor = min(speed/20, 1)` |
| `0x00af3384` | `9a 99 19 3f` | **0.6** | Final `setSteeringAngle` scale |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Clamp max / speedFactor cap (`g_flOne`) |
| `0x00aaa668` | `00 00 80 bf` | **−1.0** | Clamp min |

Xrefs of `0xa10e78` inside this fn: **`0x598a6f`**, **`0x598a79`**, **`0x598a8f`** (add / sub / snap compare). Shared constant (many xrefs outside applyAction).

---

## 7. Closed-form pipeline (mode `0x02`, not suppressed)

```
// Stage 1 (every tick)
VA24 ← ramp(entity.steerAxis@0x618, rate = VA20·dt·{1|2}, clamp ±1)
wheelsDesc.desiredSteer@0x1c ← VA24

// Stage 2 (mode 0x02 && entity+0x102 == 0)
speedFactor ← min(|v| / 20.0, 1.0)
target     ← wheelsDesc.desiredSteer * speedFactor
VA28       ← step_toward(VA28, target, step=0.05); clamp ±1

// Push
setSteeringAngle(steeringObj, VA28 * 0.6)   // stores steeringObj+0x50
```

---

## 8. Misread registry

| Claim | Origin | Verdict |
|-------|--------|---------|
| `VA+0x24` is brake / handbrake | Ghidra plate; some NPCDriving table rows | **FALSE** — stage-1 **steer** |
| Speed factor divisor is **0.6** | plate; older NPCDriving §6.1; `0.7-transmission.md` | **FALSE** — divisor is **20.0** at `0xaf3388`; **0.6** is final scale |
| Stage-2 step is `× dt` | assumption from stage-1 shape | **FALSE** — fixed **0.05 / tick** |
| `setSteeringAngle` gets raw `VA+0x28` | decompile alone | **FALSE** — asm multiplies by **0.6** first |
| Stage-1 rate is only `DAT_00a10e74` | shorthand notes | **Incomplete** — rate is `VA+0x20 * dt * {1 or 2}`; 2.0 is the open-band factor |

Aligned prior write-up: `steering-spec.md` stage-1/2 shape is mostly right on **±0.05** and **20.0**, but must be updated for the **×0.6** push. Prefer **this file** for the applyAction ramp.

---

## 9. Port recipe (behavior only — no C# in this pass)

1. Maintain two floats on the vehicle action: `steerStage1` (`+0x24`), `steerStage2` (`+0x28`).
2. Each physics substep / apply tick:
   - Ramp `steerStage1` toward raw steer axis with `rateBase * dt * factor` (factor 2 when not at the open-side extreme; else 1); clamp ±1; copy to wheels desired-steer.
   - If movement mode is analog (`0x02`) and not suppress-flagged:
     - `target = desiredSteer * min(|v|/20, 1)`
     - Move `steerStage2` by **±0.05** toward `target` (snap if within 0.05); clamp ±1
     - Publish `steerStage2 * 0.6` into the Havok steering input slot
3. Do **not** implement quadratic falloff here — that is `hkDefaultSteering_update`.
4. Non-`0x02` modes use a different control law (impulse); leave out unless porting that branch.

---

## 10. Verification checklist

- [x] Decompile `0x598650` (fresh) — stage-1 / stage-2 structure
- [x] Asm walk `0x5988a3`–`0x598ad7` — factor, ±0.05, ×0.6, call
- [x] `read_memory` `0xa10e78` → **0.05**
- [x] `read_memory` `0xa10e74` → **2.0**
- [x] `read_memory` `0xaf3388` → **20.0**
- [x] `read_memory` `0xaf3384` → **0.6**
- [x] `read_memory` `0xa0f2a0` / `0xaaa668` → **±1.0**
- [x] `hkpVehicleSteering_setSteeringAngle` @ `0x636410` = store to `+0x50`
- [x] Decompiler omission of `MULSS [0xaf3384]` documented
- [x] No C# in this pass
