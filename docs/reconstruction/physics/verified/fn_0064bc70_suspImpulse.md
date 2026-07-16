# Verified: suspension impulse / `applyPointImpulse` in `postTickApplyForces` @ `0x0064bc70`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Function | `hkVehicleFramework_postTickApplyForces` @ `0x0064bc70` |
| Focus of this file | **Per-wheel suspension impulse** → chassis (and optional contact-body) **`vtbl+0x60` applyPointImpulse** |
| Convention | MSVC `__thiscall` — framework in `ECX` (`param_1`); `param_2` = `float *dtVec` |
| RE tools | Ghidra MCP `decompile_function` @ `0x64bc70`; `read_memory` @ `0xa0f2a0` |
| Status | **Verified** (re-decompile 2026-07-15) |
| Sibling docs | Full postTick map: `fn_0064bc70_postTick.md`. Force producer: `0.4-suspension.md` / `hkDefaultSuspension::update` @ `0x64de50` |

> **Scope:** only the **immediate suspension / normal impulse** path at the head of the per-wheel loop.  
> Axle aggregation, friction solve, and writeback are out of scope (see sibling postTick doc).

---

## 1. Where this sits in the tick

```
hkDefaultSuspension::update (0x64de50)
  writes scalar suspForce[i] → *( *(fw+0x28)+0x34 + i*4 )

hkVehicleFramework_postTickApplyForces (0x64bc70)
  [pre: aero / chassis linear-angular impulses, force list]
  [zero 2 axle packs]
  per wheel i:                          ◄── THIS FILE
      I = dt · suspForce[i] · n̂_i
      chassisRB.vtbl[+0x60](&I, WHEEL)   // applyPointImpulse
      optional equal-and-opposite on dynamic contact body
      [then axle pack accumulate — not covered here]
  [friction solve + writeback]
```

Suspension is **not** a friction-solver unknown. It is converted to an **impulse and applied immediately**, before axle packing / `hkVehicleFrictionSolver_solve`.

---

## 2. Object graph (pointers used by this path)

| Symbol | Expression | Notes |
|--------|------------|-------|
| `fw` | `this` (`param_1`) | `hkVehicleFramework*` |
| `dt` | `*param_2` | substep timestep |
| `wheels` | `*(fw + 0x0c)` | wheels collection |
| `wheelCount` | `*(int *)(wheels + 0x0c)` | loop bound |
| `WHEEL_i` | `*(wheels + 0x80) + i * 0xC0` | wheel stride **`0xC0`** |
| `axleOf[i]` | `*(*(wheels + 0x58) + i*4)` | used after susp apply (contact-body tracking) |
| `susp` | `*(fw + 0x28)` | suspension component |
| `suspForce[i]` | `*(*(float **)(susp + 0x34) + i)` | table via **heap pointer** at `susp+0x34` |
| `chassisShell` | `*(fw + 0x30)` | |
| `chassisRB` | `*(chassisShell + 0x3c)` | rigid body; **vtbl slot `+0x60`** |
| `collisionScale[i]` | `*(*(wheels + 0x4c) + i*4)` | scales contact-body reaction |
| `contactBody` | `*(WHEEL + 0xa4)` | hit body from preUpdate cast; `0` if none |

**Pointer rule:** `susp+0x34` and `wheels+0x4c` are **pointers to float tables**, not inline floats. Notation `susp+0x34[i]` means `*(*(susp+0x34)+i*4)`.

---

## 3. Decompile — suspension impulse block (wheel-loop head)

Annotated from fresh `decompile_function` (variable names as emitted; types cleaned):

```c
// this = fw;  param_2 = dtVec
// fVar24 / wheels = *(fw + 0xc)
// iVar18 = wheel index i
// afStack_410[3] accumulates i * 0xC0 as the wheel-struct byte offset

iVar17 = *(int *)(*(int *)(wheels + 0x58) + i * 4);          // axleOf[i]
iVar19 = *(int *)(wheels + 0x80) + (int)wheelByteOff;         // WHEEL_i
afStack_410[1] = *(float *)(*(int *)(*(int *)(fw + 0x28) + 0x34) + i * 4);
                                                              // suspForce[i]
iVar15 = *(int *)(fw + 0x30);                                 // chassisShell

local_420 = *param_2 * afStack_410[1];                        // s = dt * suspForce
fStack_42c = *(float *)(iVar19 + 0x30) * local_420;           // I.x = n.x * s
fStack_428 = *(float *)(iVar19 + 0x34) * local_420;           // I.y
fStack_424 = *(float *)(iVar19 + 0x38) * local_420;           // I.z
local_420  = *(float *)(iVar19 + 0x3c) * local_420;           // I.w (pad lane)

// optional profiler probes (FUN_005070b0 / FUN_005070d0) — no math

// chassisRB = *(chassisShell + 0x3c)
// thiscall ECX=chassisRB; args = (&I, WHEEL)
(**(code **)(**(int **)(iVar15 + 0x3c) + 0x60))(&fStack_42c, iVar19);

// --- contact-body reaction (dynamic only) ---
iVar15 = *(int *)(iVar19 + 0xa4);                             // contactBody
if (iVar15 != 0
    && *(int *)(iVar15 + 0xc) != 0
    && *(int *)(*(int *)(iVar15 + 0xc) + 8) == 2) {           // type == 2 dynamic RB
    fVar20 = 0.0f - *(float *)(*(int *)(wheels + 0x4c) + i * 4);
                                                              // scale = -collisionScale[i]
    fStack_42c *= fVar20;
    fStack_428 *= fVar20;
    fStack_424 *= fVar20;
    local_420  *= fVar20;                                     // F2 = scale * I  (component-wise)

    (**(code **)(**(int **)(iVar15 + 0x3c) + 0x60))(&fStack_42c, iVar19);
                                                              // bodyRB.applyPointImpulse(F2, WHEEL)

    // track preferred contact body per axle into (&iStack_3fc)[axle]:
    // keep first, or replace if this body's invMass (RB+0x2c) is smaller
    ...
}
```

Plate comment on the function (binary annotation) confirms step 1 is **suspension/normal**, not engine drive:

> force along contact normal: `(fw+0x28)+0x34[i] * dt * wheel+0x30..` → chassis RB `vtbl+0x60`

---

## 4. Closed-form formula

```
suspForce[i] = *( *(fw+0x28)+0x34 + i )     // written by hkDefaultSuspension::update
n̂_i         = WHEEL_i[+0x30 .. +0x3c]      // contact normal (4-float)
dt           = *dtVec

I_susp = (dt * suspForce[i]) * n̂_i          // component-wise scale of normal

applyPointImpulse(chassisRB, I_susp, WHEEL_i)

if contactBody = WHEEL[+0xa4] is non-null
   and collidable graph at body+0xc is non-null
   and *(collidable+8) == 2:                 // dynamic rigid body
    scale = -collisionScale[i]               // collisionScale = *(wheels+0x4c)[i]
    applyPointImpulse(bodyRB, scale * I_susp, WHEEL_i)
```

### Port facts

| Fact | Detail |
|------|--------|
| **When** | Immediate, **before** friction solve |
| **Direction** | Wheel **contact normal** `+0x30..+0x3c` — not re-derived from spring rest length here |
| **Magnitude** | `dt * suspForce[i]`; airborne wheels have `suspForce=0` from `0x64de50` (no separate gate in this block) |
| **RB method** | Chassis / body vtbl **slot `+0x60`** = applyPointImpulse |
| **Point argument** | Binary passes **`WHEEL` base** (`iVar19`), not `WHEEL+0x20` explicitly. As an `hkVector4*`, that is the first 16 bytes of the wheel = **hardpoint origin** (`+0x00..+0x0c`, world after preUpdate). Sibling maps that say “at contact `+0x20`” describe the contact point used for **axle angular jacobian** (`+0x20..+0x2c` averaged later); the **applyPointImpulse** call site uses the wheel base / hardpoint. |
| **Contact reaction** | Only type **`2`** (dynamic). Scale is **negative** collisionScale (equal-and-opposite when scale≈1). |
| **No drive here** | Engine/drive is separate: `wheels+0x28[i] * wheel+0x88 / N_ax` into axle pack → friction solver |

---

## 5. Wheel / framework offsets (this path only)

### Wheel (stride `0xC0`)

| Off | Use |
|----:|-----|
| `+0x00..+0x0c` | Hardpoint origin (world) — **point arg** as passed to `vtbl+0x60` |
| `+0x30..+0x3c` | **Contact normal** — impulse direction |
| `+0xa4` | Contact body pointer |

### Framework / collections

| Off | Use |
|----:|-----|
| `fw+0x0c` | wheels |
| `fw+0x28` | susp component → `+0x34` force table ptr |
| `fw+0x30` | chassis shell → RB @ shell `+0x3c` |
| `wheels+0x0c` | wheelCount |
| `wheels+0x4c` | collisionScale[] ptr |
| `wheels+0x58` | axle index[] ptr |
| `wheels+0x80` | wheel struct base |

### Rigid body vtbl

| Slot | Role |
|-----:|------|
| `+0x60` | **applyPointImpulse** (this path; also used for contact reaction) |

---

## 6. Contact-body side effects (beyond impulse)

After a successful dynamic-body apply, the function may store that body into a **per-axle stack slot** `(&iStack_3fc)[axle]`:

- If slot empty → take this body.
- Else keep the body whose RB `+0x2c` (inv-mass / mass scalar) is **smaller** (lighter preferred), using a `<=` compare with equality-guard.

That selection feeds later axle contact-body solver packing (out of scope). It does **not** change the suspension impulse formula.

---

## 7. Upstream force (not re-derived here)

`suspForce[i]` is produced by `hkDefaultSuspension::update` @ `0x64de50`:

```
if !inContact:  suspForce = 0
else:
  damp = (closingSpeed >= 0) ? extensionDamp : compressionDamp
  suspForce = ( (restLen - currentLen) * strength * scaleFactor
                - damp * closingSpeed ) * gScale
  gScale = (chassisRB[+0x2c] == 0) ? 0 : 1 / chassisRB[+0x2c]
```

Full spring/damper detail: `docs/reconstruction/physics/0.4-suspension.md`.

This postTick block **only multiplies by `dt` and the normal** and applies the impulse.

---

## 8. Reconciliation

| Topic | Verdict |
|-------|---------|
| `I = dt * susp+0x34[i] * n̂(+0x30)` | **Match** (fresh decomp) |
| Chassis `*(fw+0x30)+0x3c` → `vtbl+0x60` | **Match** |
| Contact body type `== 2`, scale `-(wheels+0x4c)[i]` | **Match** |
| Impulse applied before axle pack / solve | **Match** |
| Airborne handling | **No local gate**; relies on susp update writing `0` |
| Apply point | **Binary:** pass `WHEEL` base → hardpoint `+0x00`. Maps saying “contact +0x20” refer to jacobian / contact geometry, not this call’s second arg. **Binary wins** for the call. |
| `g_flOne` | Used later for axle `invN`; not in the susp multiply itself. `read_memory 0xa0f2a0` = `00 00 80 3f` → **1.0** |

---

## 9. Port-facing snippet (no C# — pseudocode only)

```
for i in 0 .. wheelCount-1:
    W    = wheels.base + i * 0xC0
    s    = dt * suspForce[i]
    I    = (W.n.x * s, W.n.y * s, W.n.z * s, W.n.w * s)   // n @ +0x30
    chassis.applyPointImpulse(I, point=W.hardpoint)        // W base / +0x00

    body = W.contactBody                                   // +0xa4
    if body is dynamic (type 2):
        body.applyPointImpulse(-collisionScale[i] * I, point=W.hardpoint)
```

---

## 10. Verification provenance

| Step | Result |
|------|--------|
| `decompile_function` @ `0x64bc70`, program `autoassault.exe` | Full body; plate WI-MOV-004; susp path at wheel-loop head |
| `read_memory` `0x00a0f2a0` len 4 | `00 00 80 3f` → 1.0 (`g_flOne`) |
| Cross-check | `0.4-suspension.md` §2; `fn_0064bc70_postTick.md` §3; `fn_offsets_wheel.md` |

**Emulation:** not run — pointer-heavy (RB vtbl, multi-object graph). Goldens for a port: hand-derive `I = dt·F·n` and apply at hardpoint; optional live capture of chassis linear/angular velocity delta after this call.

**Out of scope here:** drivePack, friction solve, writeback (`fn_0064bc70_postTick.md`); spring formula (`0x64de50` / `0.4-suspension.md`).
