# Verified: `hkVehicleFramework_postTickApplyForces` @ `0x0064bc70`

**Program:** `autoassault.exe` (image base `0x00400000`)  
**Ghidra:** project AA-decode · tool `ghidra-mcp` · `decompile_function` re-pull 2026-07-15  
**Symbol:** `hkVehicleFramework_postTickApplyForces`  
**Related:** solver `hkVehicleFrictionSolver_solve` @ `0x006c4450`; upstream torque `VehicleAction_calcWheelTorque` @ `0x00598040`; susp force writer `hkDefaultSuspension::update` @ `0x0064de50`  
**Constants:** `g_flOne` @ `0x00a0f2a0` = `00 00 80 3f` = **1.0** (raw `read_memory`)

> Scope of this file: **suspension impulse apply**, **axle aggregation / drivePack**, **solver call**, **writeback offsets**.  
> Pre-wheel aero/chassis force packing and contact-body solver-record fill are summarized only as call-order context.

---

## 1. Signature & object graph

```c
// __thiscall  (decompiler types param_1 as float; it is the this ptr)
void hkVehicleFramework_postTickApplyForces(hkVehicleFramework *this, float *dtVec);
// dt = *dtVec  (substep timestep; dtVec[1] also used as second dt for the solver pair)
```

| Symbol | Expression | Notes |
|--------|------------|-------|
| `fw` | `this` | `hkVehicleFramework` |
| `wheels` | `*(fw + 0x0c)` | wheels collection |
| `wheelCount` | `*(int *)(wheels + 0x0c)` | loop bound |
| `axleCount` | `*(int *)(wheels + 0x64)` | typically **2** |
| `axleOf[i]` | `*(*(int *)(wheels + 0x58) + i*4)` | per-wheel axle index |
| `WHEEL_i` | `*(int *)(wheels + 0x80) + i*0xC0` | wheel struct stride **`0xC0`** |
| `axleWheelCount[ax]` | `*(*(int *)(wheels + 0x68) + ax*4)` | **int**; double-indirect via ptr at `wheels+0x68` |
| `driveTorque[i]` | `*(*(float **)(wheels + 0x28) + i)` | calcWheelTorque out (clamped `[0,1000]` upstream) |
| `susp` | `*(fw + 0x28)` | suspension component |
| `suspForce[i]` | `*(*(float **)(susp + 0x34) + i)` | written by suspension update |
| `chassisShell` | `*(fw + 0x30)` | |
| `chassisRB` | `*(chassisShell + 0x3c)` | rigid body; vtbl methods below |

**Pointer rule (binary):** arrays on `wheels` (`+0x28`, `+0x34`, `+0x40`, `+0x4c`, `+0x58`, `+0x68`) and `susp+0x34` are **heap pointers to contiguous tables**, not inline floats at that offset. Phase-0 shorthand `wheels+0x28[i]` means `*(*(wheels+0x28)+i*4)`.

---

## 2. High-level pipeline (call order)

1. **Pre-forces (not expanded here):** scale aero/extra gravity × `dt`, apply chassis linear/angular impulses (`vtbl+0x6c`, `+0x5c`, `+0x64`), run `fw+0x330` force list (`count @ fw+0x334`, vtbl `+0x14`).
2. **Zero 2 axle packs** (stride `0x50` = `0x14` floats; loop counter hardcoded **2**).
3. **Per-wheel loop** (`i = 0 .. wheelCount-1`):
   - Apply **suspension/normal impulse** immediately to chassis (and optionally contact body).
   - Accumulate **axle jacobians + drivePack + friction params + contact flag**.
4. **Per-axle normalize** lat/long dir vectors; fill contact-body solver records when needed.
5. **Chassis solver body** prep (`FUN_0064b0b0`, optional `fw+0x348` scaled forward term).
6. **`hkVehicleFrictionSolver_solve`** (see §5).
7. Finite-check + `FUN_0064b200`; write velocities back to chassis / contact RBs (`vtbl+0x54` / `+0x50`).
8. **Writeback** solver outputs onto each wheel (§6).

Drive torque is **not** read from transmission/`hkDefaultEngine`. Engine path is AA’s `calcWheelTorque` → `wheels+0x28[i]`.

---

## 3. Suspension impulse apply (immediate, pre-solver)

Verified decompile (wheel loop head):

```text
suspForce = *(*(susp + 0x34) + i*4)          // susp = *(fw+0x28)
s         = dt * suspForce                   // dt = *dtVec
F.x = *(WHEEL + 0x30) * s
F.y = *(WHEEL + 0x34) * s
F.z = *(WHEEL + 0x38) * s
F.w = *(WHEEL + 0x3c) * s                    // contact-normal basis × (force·dt)

// chassisRB = *(*(fw+0x30) + 0x3c)
chassisRB.vtbl[+0x60](&F, WHEEL)             // applyPointImpulse(F, contactPoint≈WHEEL)
```

**Contact-body reaction** (only if movable RB):

```text
body = *(WHEEL + 0xa4)
if body != 0
   && *(body + 0xc) != 0
   && *(*(body + 0xc) + 8) == 2:             // type == 2 (dynamic rigid body)
    scale = - *(*(wheels + 0x4c) + i*4)      // per-wheel collisionScale
    F2 = scale * F                           // component-wise
    bodyRB = *(body + 0x3c)
    bodyRB.vtbl[+0x60](&F2, WHEEL)
    // also tracks lightest/first contact body per axle into stack slot (&iStack_3fc)[ax]
```

### Port formula

```
I_susp = (dt * suspForce[i]) * n̂_i          // n̂ = WHEEL[0x30..0x3c]
applyPointImpulse(chassis, I_susp, WHEEL)
if contact body is dynamic RB:
    applyPointImpulse(body, -collisionScale[i] * I_susp, WHEEL)
```

**Facts for the port:**

- Suspension is an **impulse applied now**, not a friction-solver unknown.
- Direction is the **wheel contact normal** already stored on the wheel struct (`+0x30..+0x3c`), not re-derived from spring rest length here.
- `suspForce` itself is produced earlier by `hkDefaultSuspension::update` (`0x64de50`) into `susp+0x34[i]` (see `0.4-suspension.md`).

---

## 4. Axle aggregation & `drivePack`

### 4a. Axle pack layout (stack)

Two packs zeroed, then filled. Useful fields (relative to pack base for axle `ax`; decompiler names `afStack_2cc` / `acStack_280`):

| Field | Stack expression (decomp) | Role |
|-------|---------------------------|------|
| `jAng[0..3]` | `afStack_2cc[ax*0x14 + 0..3]` | angular jacobian row, **× invN** |
| lat cross terms | `afStack_2cc[ax*0x14 + 4..9]` (+ related) | lateral frame from `n × t` style products |
| **`drivePack`** | `*(acStack_280 + ax*0x50 − 0x14)` | **Σ (driveTorque·wheelScale·invN)** |
| `fricA` | `acStack_280 + ax*0x50 − 0x10` | `*(wheels+0x34)[i] * invN` (μ table) |
| `fricB` | `acStack_280 + ax*0x50 − 0x0c` | `*(wheels+0x40)[i] * invN` |
| `sumSusp` | `acStack_280 + ax*0x50 − 0x08` | **sum** `suspForce` (**no** invN) |
| `extra` | `acStack_280 + ax*0x50 − 0x04` | `local_3ec` sum (see below; **no** invN) |
| `inContact` | `acStack_280[ax*0x50]` | byte; OR of wheel grounded flags |

`invN = g_flOne / (float)axleWheelCount[ax]` with `g_flOne = 1.0`.

Plate / asm note (function comment): drive accumulate at **`0064c17e..0064c263`** → axle field **`+0x38`** (matches `drivePack` relative packing into the friction axle blob).

### 4b. Lateral unit from contact frame

```text
// cross-ish products of WHEEL normal (+0x30..) and WHEEL +0x60.. (+0x64 = 100 decimal)
v0 = W[+0x34]*W[+0x68] - W[+0x64]*W[+0x38]
v1 = W[+0x60]*W[+0x38] - W[+0x30]*W[+0x68]
v2 = W[+0x64]*W[+0x30] - W[+0x34]*W[+0x60]
invLen = (len2==0) ? 0 : 1/sqrt(len2)
v̂ = v * invLen
// then accumulate into axle lat rows (with additional cross products vs n̂)
```

After the wheel loop, each axle’s direction rows are **re-normalized** (two `1/sqrt` passes on the aggregated vectors).

### 4c. Drive impulse pack (engine → friction)

```text
driveTorque = *(*(wheels + 0x28) + i*4)     // calcWheelTorque output
wheelScale  = *(WHEEL + 0x88)               // per-wheel drive scale
invN        = 1.0f / (float)axleWheelCount[ax]

drivePack[ax] += driveTorque * wheelScale * invN
```

**Closed form:**

```
drivePack[ax] = (1 / N_ax) * Σ_{i on ax}  ( wheels+0x28[i] * WHEEL_i[+0x88] )
```

where `N_ax = axleWheelCount[ax]` (usually 2 for a standard 4-wheel / 2-axle setup).

### 4d. Other per-wheel accumulators (same loop)

```text
// friction params — averaged
*(AX-0x10) += *(*(wheels+0x34)+i*4) * invN
*(AX-0x0c) += *(*(wheels+0x40)+i*4) * invN

// suspension magnitude — summed (not averaged)
*(AX-0x08) += suspForce

// extra scalar (not invN):
//   ( *(fw+0x24)+0x10[i]  +  *(fw+0x20)+0x20[i] ) / *(wheels+0x10)[i]
*(AX-0x04) += that

// angular jac — averaged
AX.jAng[0..3] += WHEEL[+0x20..+0x2c] * invN

// in-contact byte:
//   set if pack already true OR *( *(fw+0x24)+0x1c + i ) != 0
//   (decomp: OR of acStack_280 flag and grounded table under fw+0x24)
```

**Architectural fact:** the friction solver runs on **axles** (2 constraint points), not per wheel. All drive/friction inputs are bucketed by `axleOf[i]` before `solve`.

---

## 5. Solver call

After chassis/contact body records are prepared on the stack (`auStack_2d8` region is the chassis+contact solver body blob fed as arg 3):

```c
fStack_3f8 = dtVec[0];   // dt
fStack_3f4 = dtVec[1];   // second dt component (same substep value in normal path)

hkVehicleFrictionSolver_solve(
    &fStack_3f8,              // param_1: {dt, dt}
    (int)fw + 0x1fc,          // param_2: per-axle friction SETUP  (stride 0x64)
    auStack_2d8,              // param_3: chassis + contact jacobian/Minv body data
    (int)fw + 0x2cc           // param_4: OUTPUT impulse array     (stride 0x1c)
);
```

| Arg | Address / base | Meaning |
|-----|----------------|---------|
| 1 | stack `{dt,dt}` | timestep pair |
| 2 | `fw + 0x1fc` | axle friction **setup** (μ, softness, etc.; filled at vehicle build + live updates) |
| 3 | stack `auStack_2d8` | solver body / jacobian workspace built in this function |
| 4 | `fw + 0x2cc` | **output** impulses, **7 floats (`0x1c` bytes) per axle** |

Solver implementation is **out of scope** for this file (see `0.3-friction-solver.md` / future `fn_006c4450_*.md`). This function only supplies setup + body packs and consumes `fw+0x2cc`.

---

## 6. Writeback offsets (post-solve)

Per wheel `i` after solve:

```text
ax      = *(*(wheels + 0x58) + i*4)
OUT     = (float *)( fw + 0x2cc + ax * 0x1c )   // 7 floats / axle
WHEEL   = *(wheels + 0x80) + i * 0xC0

if (*(char *)(WHEEL + 0x80) == 0 || *(char *)(WHEEL + 0xa8) == 0)
    *(WHEEL + 0x94) = 0
else
    *(WHEEL + 0x94) = OUT[2]     // longitudinal / drive-brake impulse

*(WHEEL + 0x98) = OUT[3]
*(WHEEL + 0xa0) = OUT[1]         // lateral impulse
*(WHEEL + 0x9c) = OUT[0]
```

### Writeback map

| Wheel offset | Source | Condition |
|-------------:|--------|-----------|
| `+0x94` | `OUT[2]` | only if `WHEEL+0x80` (in contact) **and** `WHEEL+0xa8`; else **0** |
| `+0x98` | `OUT[3]` | always |
| `+0x9c` | `OUT[0]` | always |
| `+0xa0` | `OUT[1]` | always |

**Note:** only the longitudinal slot (`+0x94`) is gated on contact flags. The other three outputs are written unconditionally from the axle’s output row (airborne wheels still receive `OUT[0/1/3]` for their axle).

All wheels on the same axle share the same `OUT` row (`ax * 0x1c`).

---

## 7. Key offsets cheat-sheet

### `hkVehicleFramework` (`fw`)

| Off | Use in this function |
|----:|----------------------|
| `+0x0c` | `wheels` ptr |
| `+0x10` / `+0x18` | basis / scalar for optional `fw+0x348` term |
| `+0x20` / `+0x24` | components contributing `local_3ec` / grounded table |
| `+0x28` | suspension component (`suspForce` via `susp+0x34`) |
| `+0x2c` / `+0x34` | aero / extra-force sources (pre-loop) |
| `+0x30` | chassis shell → RB @ `+0x3c` |
| `+0x1fc` | friction **setup** base → solver arg2 |
| `+0x2cc` | friction **output** base → solver arg4 / writeback |
| `+0x320..+0x32c` | (as `800` / `0x324` / …) direction scale into chassis pack |
| `+0x330` / `+0x334` | external force list / count |
| `+0x348` | optional extra longitudinal scale |
| `+0x34c` | copied into stack before axle body fill |

### Wheels collection

| Off | Type | Use |
|----:|------|-----|
| `+0x0c` | int | `wheelCount` |
| `+0x10` | ptr→float[] | divisor in `local_3ec` |
| `+0x28` | ptr→float[] | **driveTorque[i]** (calcWheelTorque) |
| `+0x34` | ptr→float[] | friction param A |
| `+0x40` | ptr→float[] | friction param B |
| `+0x4c` | ptr→float[] | collisionScale (contact body reaction) |
| `+0x58` | ptr→int[] | axle index per wheel |
| `+0x64` | int | `axleCount` |
| `+0x68` | ptr→int[] | wheels-per-axle counts |
| `+0x80` | ptr | wheel struct base |

### Wheel struct (stride `0xC0`)

| Off | Use |
|----:|-----|
| `+0x20..+0x2c` | ang jacobian contribution |
| `+0x30..+0x3c` | contact normal (suspension impulse direction) |
| `+0x60..+0x68` | second frame vector for lat basis |
| `+0x80` | in-contact flag (writeback gate) |
| `+0x88` | **drive scale** into `drivePack` |
| `+0x94` | writeback long impulse |
| `+0x98` | writeback OUT[3] |
| `+0x9c` | writeback OUT[0] |
| `+0xa0` | writeback lat impulse (OUT[1]) |
| `+0xa4` | contact body ptr |
| `+0xa8` | second writeback gate flag |

### Rigid body vtbl slots used

| Slot | Role |
|-----:|------|
| `+0x0c` | build/integrate solver body from RB |
| `+0x18` | body type query (`2` / `4` branches) |
| `+0x50` / `+0x54` | post-solve velocity writeback |
| `+0x5c` / `+0x64` / `+0x6c` | pre-loop chassis impulse apply |
| `+0x60` | **applyPointImpulse** (suspension + contact reaction) |

---

## 8. Reconciliation vs Phase-0 map (`0.3-friction-solver.md`)

| Topic | Verdict |
|-------|---------|
| Susp impulse = `dt * susp+0x34[i] * n̂`, chassis `vtbl+0x60` | **Match** (fresh decomp) |
| Contact body type `==2`, scale `-(wheels+0x4c)[i]` | **Match** |
| `drivePack = Σ torque·(+0x88) / N_ax` | **Match** |
| Solver `(dt2, fw+0x1fc, bodyStack, fw+0x2cc)` | **Match** |
| Writeback `+0x94←OUT[2]` gated; `+0x98←OUT[3]`, `+0xa0←OUT[1]`, `+0x9c←OUT[0]` | **Match** |
| Axle loop count hardcoded 2 for pack zero; `axleCount` from `wheels+0x64` for later loops | **Match** |
| Notation `wheels+0x28[i]` | **Clarify:** binary is `*(*(wheels+0x28)+i*4)` (pointer table) |
| Notation `wheels+0x68+ax*4` | **Clarify:** binary is `*(*(wheels+0x68)+ax*4)` |
| `sumSusp` / `local_3ec` not always ×invN | **Confirmed** (drive/friction jac use invN; susp sum and `local_3ec` do not) |

**Binary wins** on the pointer-indirection clarifications; math formulas in Phase 0 remain correct.

---

## 9. Port-facing summary

```
for each wheel i:
    apply I_susp = dt * suspForce[i] * normal_i   to chassis (+ equal/opposite*scale to dynamic contact)

    ax = axleOf[i]
    invN = 1 / N_wheels_on_axle[ax]
    drivePack[ax] += driveTorque[i] * wheel[i].scale88 * invN
    // + averaged jac / μ rows, summed susp, contact OR

normalize axle direction rows
prepare chassis + contact solver bodies
hkVehicleFrictionSolver_solve({dt,dt}, fw+0x1fc, bodyPack, fw+0x2cc)

for each wheel i:
    OUT = fw+0x2cc + axleOf[i]*0x1c
    wheel.long  (+0x94) = (inContact && flagA8) ? OUT[2] : 0
    wheel.f98   (+0x98) = OUT[3]
    wheel.lat   (+0xa0) = OUT[1]
    wheel.f9c   (+0x9c) = OUT[0]
```

No transmission/`ctrl` throttle read in this function. Drive input is **only** `wheels+0x28[i] × wheel+0x88`, axle-averaged, into the friction solver.

---

## 10. Verification provenance

| Step | Result |
|------|--------|
| `decompile_function` @ `0x64bc70`, program `autoassault.exe` | Full pseudocode (plate WI-MOV-004); symbols `hkVehicleFramework_postTickApplyForces`, call to `hkVehicleFrictionSolver_solve` |
| `read_memory` `0x00a0f2a0` len 4 | `00 00 80 3f` → 1.0 (`g_flOne`) |
| Cross-check | `docs/reconstruction/physics/0.3-friction-solver.md` Part 2; `0.4-suspension.md` §2 |

**Emulation:** not run — function is pointer-heavy (RB vtbl, multi-object graph). Goldens for a C# port should be hand-derived from §3–§6 formulas or captured live from a debugger on `fw+0x2cc` / wheel `+0x94..+0xa0`.
