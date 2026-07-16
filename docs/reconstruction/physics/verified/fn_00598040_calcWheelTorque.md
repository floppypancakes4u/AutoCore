# `VehicleAction_calcWheelTorque` @ `0x598040` — verified

**Program:** `autoassault.exe` (image base `0x400000`)  
**Symbol:** `VehicleAction_calcWheelTorque`  
**Convention:** `__thiscall` (`ECX` = `VehicleAction* this`)  
**Verified:** 2026-07-15 — `decompile_function` + `disassemble_function` + `read_memory`  
**Related:** `VehicleEngine_torqueCurve2D` @ `0x4a9750`, `FUN_004f5550` (per-wheel friction), `FUN_004f5560` (wheel count), `FUN_004e8b60` (body up-axis extract), consumer `hkVehicleFramework_postTickApplyForces` @ `0x64bc70`

---

## 1. Role

AA’s **engine replacement** (no `hkDefaultEngine`). For each wheel in contact, writes a
non-negative **drive torque** into the per-wheel engine-torque array consumed later as drive
impulse by the friction solver.

| Item | Detail |
|---|---|
| Output | `wheelsDesc+0x28[i]` via `this+0x40 → +0xc → +0x28` (float array, index `i`) |
| Airborne flag | `this+0x2c` = 1 iff **no** wheel was in contact this pass (else 0 if any contact) |
| Clamp | `[0, 1000.0]` — floor 0 ⇒ path never emits retarding brake torque |

---

## 2. Constants (`read_memory`)

| Symbol / site | Address | Bytes (LE) | Value | Role |
|---|---|---|---|---|
| upright threshold | `0x00a0f698` | `cd cc 4c 3f` | **0.8** | if `\|dot\| < 0.8` apply pow falloff |
| low-speed threshold | `0x00aaa7a4` | `00 00 70 41` | **15.0** | `\|v\| < 15` → friction boost |
| low-speed slope | `0x00a0f70c` | `cd cc 4c 3e` | **0.2** | boost = `(15−\|v\|)×0.2 + 1` |
| torque clamp max | `0x00a0f520` | `00 00 7a 44` | **1000.0** | upper clamp |
| handbrake rear cut | `0x00a0f298` | `00 00 00 3f` | **0.5** | rear drive torque ×0.5 when `entity+0x61c≠0` |
| rear mod scale | `0x00a10e74` | `00 00 00 40` | **2.0** | when driver mod `<0` and rear: `mod *= 2` |
| identity / one | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | upright default; blend math |
| worldUp.x | `0x00af3390` | `00 00 00 00` | **0.0** | |
| worldUp.y | `0x00af3394` | `00 00 80 3f` | **1.0** | |
| worldUp.z | `0x00af3398` | `00 00 00 00` | **0.0** | worldUp = `(0,1,0)` (12 bytes @ `0xaf3390`) |
| upright pow exponent | `0x009d54e8` | `00 00 00 00 00 00 10 40` | **4.0** (float64) | `_CIpow` exponent |

---

## 3. Loop structure

```
wheelCount = (int)(signed char)FUN_004f5560()   // ECX = this+0x44 chain setup inside helper
allAirborne = 1

if wheelCount <= 0:
    this+0x2c = 1
    return

for i = 0 .. wheelCount-1:          // i in EDI; wheel stride EBX += 0xC0
    // power / anim gate
    if entityPowerFlag(+0xe4f8) == 0:
        continue                      // leave existing torque slot untouched

    wheel = wheelsArray + i*0xC0     // wheelsArray = *( *(this+0x40)+0xc ) + 0x80
    if wheel.inContact (+0x80) == 0:
        wheels+0x28[i] = 0
        continue

    allAirborne = 0
    // … per-wheel formula below …
    wheels+0x28[i] = clamp(torque, 0, 1000)

this+0x2c = allAirborne
```

**Power gate chain** (from decompile / asm `@0x598070`):

```
entity = this+0x44
flagPtr = *(*(entity+4)+4) + 0xa8 + entity
gate    = *(int*)(flagPtr + 0xe4f8)   // must be nonzero to apply torque
```

**Wheel contact / torqueCurve args:**

| Offset | Use |
|---|---|
| `wheel+0x80` | in-contact byte |
| `wheel+0x20` | first arg to `torqueCurve2D` (contact / curve X — see engine-torque-spec) |
| `wheel+0x28` | second arg to `torqueCurve2D` (contact / curve Y) |

---

## 4. Per-wheel formula (authoritative)

Assembly order at `0x5980b4–0x5982eb` (algebraically identical to the decompiler plate):

```
// 1) curve factor
t = VehicleEngine_torqueCurve2D(wheel+0x20, wheel+0x28)   // CALL 0x4a9750 → ST0 → stack

// 2) driver modifier m  (optional; null driver skips)
//    driver = *(*(entity+4)+4) + 0xb0 + entity
//    if driver != null:
//        obj = driver->vtbl[+0x214]()
//        m   = *(float*)(obj + 0x118)
if m > 0:
    t = 1.0 - (1.0 - m) * (1.0 - t)     // blend toward 1  (uses DAT_00a0f2a0)
else if m < 0:
    mm = m
    if isRear(i):
        mm = m * 2.0                    // DAT_00a10e74
    t = (mm + 1.0) * t
// m == 0 or no driver: t unchanged

// 3) upright factor
bodyUp = FUN_004e8b60(chassisUpSource)  // 3 floats; worldUp = (0,1,0)
dotAbs = | bodyUp · worldUp |
if dotAbs < 0.8:                        // DAT_00a0f698; asm FCOMIP + JC @ 0x5981e8
    upright = pow(dotAbs, 4.0)          // base=|dot| (float), exp=double @ 0x9d54e8
else:
    upright = 1.0                       // DAT_00a0f2a0

// 4) combine curve × upright early (asm @ 0x598211)
ut = upright * t

// 5) friction + low-speed boost
mu = FUN_004f5550(i)                    // thiscall, arg = wheel index
v  = length(chassisLinVel)              // sqrt(vx²+vy²+vz²) at rigidBody+0x40/44/48
                                        // rigidBody = *( *(entity+8) + 0x3c )
if v < 15.0:                            // DAT_00aaa7a4
    mu = ((15.0 - v) * 0.2 + 1.0) * mu  // DAT_00a0f70c, DAT_00a0f2a0

// 6) final torque + handbrake rear cut
torque = mu * ut
if (entity+0x61c != 0) && isRear(i):
    torque = torque * 0.5               // DAT_00a0f298

// 7) clamp [0, 1000]  (asm COMISS sequence @ 0x5982ca)
if torque < 0:      torque = 0
elif torque > 1000: torque = 1000.0     // DAT_00a0f520

wheels+0x28[i] = torque
```

### Compact identity

```
torque_i = clamp(  mu_i'  ·  upright  ·  t_i  ·  handbrakeRear_i  ,  0,  1000 )
```

where

| Term | Definition |
|---|---|
| `t_i` | `torqueCurve2D` after optional driver mod |
| `upright` | `1.0` if `\|û·ŷ\| ≥ 0.8`, else `\|û·ŷ\|⁴` |
| `mu_i'` | `FUN_004f5550(i)` × low-speed boost if `\|v\| < 15` |
| `handbrakeRear_i` | `0.5` if `entity+0x61c≠0` and rear, else `1` |

---

## 5. `isRear(i)` — rear wheels are `i > axle+0x4cc`

Both the **negative driver-mod ×2.0** path and the **handbrake ×0.5** path use the **same** test.

### Assembly (handbrake site `@0x5982a1`, identical pattern at `@0x598149`)

```
entity  = this+0x44
obj     = *(entity + 0x258)           // 0x258 == 600 decimal (decompiler “+600”)
base    = *(*(obj+4)+4) + 0xac + obj
vehData = *(base + 0x3c)
bound   = (uint8_t)*(vehData + 0x4cc)
// apply rear-only scale when:  bound < i   i.e.  i > bound
CMP bound, i
JGE skip_scale                        // if bound >= i → front/not-rear
```

### Semantics

| Condition | Meaning |
|---|---|
| `i > *(uint8*)(axleOrVehicleData + 0x4cc)` | **rear** wheel |
| `i ≤ bound` | front (or non-rear) — no ×2.0 mod, no handbrake cut |

The bound byte is a **per-vehicle rear-start index** (first rear wheel index minus one, as an
unsigned compare). Docs shorthand: **rear ⇔ `i > axle+0x4cc`**.

---

## 6. Handbrake rear cut (`entity+0x61c`)

| Item | Verified fact |
|---|---|
| Flag | `entity[+0x61c]` byte ≠ 0 (handbrake / burnout state; also pushed to controller by `PushDriveAxesToController` @ `0x4fbc10`) |
| Scope | **Rear wheels only** (`isRear` above) |
| Scale | `× DAT_00a0f298` = **0.5** |
| What it is **not** | Not `RearWheelFrictionScalar` (`vehicleData+0x740` scales rear friction table at **setup**) |
| What it is **not** | Not a service-brake torque; clamp floor is 0, so this path cannot produce negative torque |

Effect: halves rear **drive** torque under handbrake so rears break traction for slides/burnouts.

---

## 7. Upright pow — **GAP CLOSED**

Prior notes (`engine-torque-spec.md` §3) flagged `_CIpow` base/exp as opaque because the decompiler
emits bare `_CIpow()`. **Assembly recovers both operands:**

```
; |dot| already in [ESP+0x14]; threshold compare failed (|dot| < 0.8)
005981f8  FLD  float  ptr [ESP+0x14]     ; base  = |dot(bodyUp, worldUp)|
005981fc  FLD  double ptr [0x009d54e8]   ; exp   = 4.0
00598202  CALL 0x006a3e2c               ; _CIpow  (x87: ST1^ST0 style CRT helper)
00598207  FSTP float  ptr [ESP+0x14]     ; upright = (float)pow(|dot|, 4.0)
```

| Operand | Source | Value |
|---|---|---|
| base | `|bodyUp · worldUp|` (float, after `FABS`) | runtime ∈ [0,1] typical |
| exp | `*(double*)0x009d54e8` | **4.0** |

**Gate:** only when `|dot| < 0.8`; otherwise upright is forced to **1.0** (not `0.8⁴ ≈ 0.41`).
That is a **step** at the threshold: upright jumps from continuous `|d|⁴` below 0.8 to 1.0 at/above
0.8 — intentional per the branch at `0x5981ec`.

**bodyUp source:** `FUN_004e8b60` on chassis transform:

- if `entity+8 != 0`: src = `*(entity+8)+0x3c` + `0x30` (physics body orientation block)
- else: src = entity pose path `*( *(entity+4)+4 ) + entity + 0x94`

**worldUp:** static `(0, 1, 0)` at `0xaf3390..0xaf3398`.

---

## 8. Chassis speed for low-speed boost

```
body = *( *(entity+8) + 0x3c )     // rigid body / motion
v = sqrt( body[0x40]² + body[0x44]² + body[0x48]² )   // linVel xyz
```

Boost (only if `v < 15`):

```
mu *= (15.0 - v) * 0.2 + 1.0
```

At `v=0` → `mu *= 4.0`; at `v→15` → `mu *= 1.0` (continuous from below).

---

## 9. Downstream (context, not re-verified here)

`wheels+0x28[i]` is aggregated per axle in `hkVehicleFramework_postTickApplyForces` (`0x64bc70`)
as `(Σ torque · wheel+0x88) / axleWheelCount` and fed as drive impulse into
`hkVehicleFrictionSolver_solve` (`0x6c4450`). See `brake-spec.md` / `0.3-friction-solver.md`.

---

## 10. Evidence checklist

| Step | Result |
|---|---|
| `decompile_function(0x598040)` | Full loop, driver mod, upright gate, mu boost, handbrake, clamp |
| `disassemble_function(0x598040)` | Recovered `_CIpow` operands; confirmed `i > +0x4cc` compares; order of muls |
| `read_memory` listed DAT_* | Values in §2 match prior phase-0 tables; **new:** `0x9d54e8 = 4.0` double |
| Emulation | Not used (pointer-heavy thiscall + entity graph) |

---

## 11. Port notes (no invention)

- Implement `upright = (|dot| < 0.8) ? pow(|dot|, 4.0f) : 1.0f` — bit-exact exponent is **4.0**.
- Clamp order: handbrake cut **before** `[0, 1000]` clamp.
- Rear tests use **unsigned byte** bound vs signed wheel index (`MOVZX` + `CMP` + `JGE`).
- Do not treat `×0.5` as friction scalar or service brake.
- `torqueCurve2D` args and table population remain documented under `engine-torque-spec.md`
  (separate function `0x4a9750`); this file owns only the **assembly** of `μ · upright · t · cuts`.
)
