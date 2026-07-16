# Verified: Chassis rigid body — field map (`physicsObj+0x3c`)

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Struct | Live Havok chassis rigid body / motion state (not the solver `cb` record) |
| Pointer | `*(physicsObj + 0x3c)` where `physicsObj = entity+0x08`, or via framework `*( *(fw+0x30) + 0x3c )` |
| Status | **Verified** from vehicle component + VehicleAction decompiles |
| Related | [`../0.8-struct-offsets.md`](../0.8-struct-offsets.md) §3 · [`fn_0064dae0_aero.md`](fn_0064dae0_aero.md) · [`fn_00598320_airStab.md`](fn_00598320_airStab.md) |

This is the **live body** used by aero, suspension, steering, AVD, calcWheelTorque, airStab, and postTick.  
It is **not** the friction-solver body record (`cb` in `0.3-friction-solver.md`).

---

## Source functions (authoritative readers)

| Symbol | Addr | RB fields used |
|---|---|---|
| `hkDefaultAerodynamics_update` | `0x64dae0` | `+0x2c` invMass; `+0x40..48` linVel; `+0x80..a8` 3×3 basis |
| `hkAngularVelocityDamper_update` | `0x64d810` | `+0x50..5c` angVel (read → scale → vtbl `+0x54` set) |
| `hkDefaultSuspension_update` | `0x64de50` | `+0x2c` invMass → `mass = 1/invMass` force scale |
| `hkDefaultSteering_update` | `0x64f840` | `+0x40..48` linVel; `+0x80` transform helper input |
| `hkVehicleFramework_postTickApplyForces` | `0x64bc70` | `rb[0xb]` = float at `+0x2c` invMass |
| `VehicleAction_calcWheelTorque` | `0x598040` | `+0x30` quat → basis extract; `+0x40..48` speed |
| `VehicleAction_airStabilization` | `0x598320` | `+0x40` linVel; `+0x50..5c` angVel; `+0xb0..b8` pos |
| `FUN_00404c90` (pos getter) | `0x404c90` | returns `rb + 0xb0` (or entity fallback) |
| `FUN_00404a20` (quat getter) | `0x404a20` | returns `rb + 0x30` (or entity fallback) |
| `FUN_004e8b60` (quat → up) | `0x4e8b60` | consumes 4-float quat at `rb+0x30` |

Tools: **`batch_decompile` / `decompile_function`** only (no `disassemble_bytes`).

---

## Access patterns

### A. Havok vehicle component path (aero / susp / steer / AVD / postTick)

```
framework = *(component + 0x08)          // parent hkVehicleFramework*
chassis   = *(framework + 0x30)          // chassis / physics object wrapper
rb        = *(chassis + 0x3c)            // rigid body*
```

Decompile (`0x64dae0`):

```c
iVar16 = *(*(param_1 + 8) + 0x30);
iVar17 = *(iVar16 + 0x3c);               // chassis RB
```

AVD (`0x64d810`) uses the same chain from the framework arg:

```c
iVar1 = *(param_3 + 0x30);
iVar2 = *(iVar1 + 0x3c);                 // rb
// read angVel at iVar2+0x50..
```

### B. Entity / VehicleAction path (calcWheelTorque / airStab)

```
entity = *(VehicleAction + 0x44)
if (entity+0x08 != 0)
    rb = *(*(entity + 0x08) + 0x3c)       // physicsObj → RB
else
    // entity fallback slots (pos @ +0x84, rot @ +0x94 via clonebase)
```

Pose helpers (thin wrappers used by airStab):

```c
// FUN_00404c90(entity) — world position pointer
if (*(entity + 8) != 0)
    return *( *(entity+8) + 0x3c ) + 0xb0;   // rb+0xb0
return *( *(entity+4)+4 ) + 0x84 + entity;  // fallback

// FUN_00404a20(entity) — orientation quaternion pointer
if (*(entity + 8) != 0)
    return *( *(entity+8) + 0x3c ) + 0x30;   // rb+0x30
return *( *(entity+4)+4 ) + 0x94 + entity;  // fallback
```

---

## Field map (verified offsets)

All multi-component vectors are **4× float32** (xyzw), 16-byte stride in practice.  
Pad `.w` lanes are copied with the xyz triple in several paths but are not always semantically used.

| Off | Size | Type | Field | Evidence |
|----:|-----:|------|-------|----------|
| `+0x2c` | 4 | f32 | **inverse mass** (`1/mass`; `0` → infinite / fixed) | See §invMass |
| `+0x30` | 4 | f32 | **orientation quaternion .x** | `FUN_00404a20`; calcWheelTorque passes `rb+0x30` to `FUN_004e8b60` |
| `+0x34` | 4 | f32 | quaternion .y | " |
| `+0x38` | 4 | f32 | quaternion .z | " |
| `+0x3c` | 4 | f32 | quaternion .w | " |
| `+0x40` | 4 | f32 | **linear velocity X** | aero forward-speed; steering speed; `|v|` in calcWheelTorque; airStab pack |
| `+0x44` | 4 | f32 | linear velocity Y | " |
| `+0x48` | 4 | f32 | linear velocity Z | " |
| `+0x4c` | 4 | f32 | linear velocity W / pad | airStab 4-float copy from `rb+0x40` |
| `+0x50` | 4 | f32 | **angular velocity X** | AVD `0x64d810`; airStab |
| `+0x54` | 4 | f32 | angular velocity Y | " |
| `+0x58` | 4 | f32 | angular velocity Z | " |
| `+0x5c` | 4 | f32 | angular velocity W / pad | AVD multiplies and writes via vtbl `+0x54` |
| `+0x80` | 4 | f32 | **world rotation matrix** row/col 0.x | aero axis transform; steering `FUN_005d6ae0(rb+0x80, …)` |
| `+0x84` | 4 | f32 | matrix 0.y | aero |
| `+0x88` | 4 | f32 | matrix 0.z | aero |
| `+0x90` | 4 | f32 | matrix 1.x | aero |
| `+0x94` | 4 | f32 | matrix 1.y | aero |
| `+0x98` | 4 | f32 | matrix 1.z | aero |
| `+0xa0` | 4 | f32 | matrix 2.x | aero |
| `+0xa4` | 4 | f32 | matrix 2.y | aero |
| `+0xa8` | 4 | f32 | matrix 2.z | aero |
| `+0xb0` | 4 | f32 | **world position X** | `FUN_00404c90`; airStab terrain cast X |
| `+0xb4` | 4 | f32 | **world position Y** (height) | airStab re-ground: cast result / `rb+0xb4 + 10.0` |
| `+0xb8` | 4 | f32 | **world position Z** | airStab terrain cast Z |
| `+0xbc` | 4 | f32 | position W / pad | 4-float pose copies |

**Not fully mapped here:** inertia tensor, center-of-mass, damping scalars, vtable (body starts with vptr at `+0x00`). Those appear in loaders / other apply paths outside this gate.

---

## Per-field decompile proof

### `+0x2c` — inverse mass

**Aero** (`0x64dae0`) — extra-gravity force = `extraGravity * mass`, `mass = 1/invMass`:

```c
fVar1 = *(float *)(*(int *)(iVar16 + 0x3c) + 0x2c);
if (fVar1 != 0.0) {
    fVar20 = g_flOne / fVar1;           // mass
}
// force += extraGravity * mass  (param_1+0x40..4c)
```

**Suspension** (`0x64de50`) — global scale `gScale = 1/invMass` multiplies every wheel spring+damper force:

```c
fVar6 = *(float *)(*(int *)(*(int *)(*(int *)(param_1 + 8) + 0x30) + 0x3c) + 0x2c);
if (fVar6 == 0.0) {
    fVar6 = 0.0;
} else {
    fVar6 = g_flOne / fVar6;            // mass / force scale
}
// suspForce[i] = (spring - damper) * fVar6
```

**postTick** (`0x64bc70`) — same scalar as `piVar11[0xb]` on `int* rb` → byte offset `0xb * 4 = 0x2c`:

```c
piVar11 = *(int **)(*(int *)(fw + 0x30) + 0x3c);
if ((float)piVar11[0xb] == 0.0) {
    fVar23 = 0.0;
} else {
    fVar23 = g_flOne / (float)piVar11[0xb];
}
```

| Use | Formula |
|-----|---------|
| Mass | `mass = (invMass == 0) ? 0 : 1/invMass` |
| Force scale | aero extra-g · mass; susp force · mass; postTick force · mass · dt |

---

### `+0x40..+0x4c` — linear velocity

**Aero** — forward speed = `dot(linVel, worldFront)`:

```c
fVar21 = *(float *)(iVar17 + 0x48) * fVar23
       + *(float *)(iVar17 + 0x44) * fVar22
       + *(float *)(iVar17 + 0x40) * fVar18;
```

**calcWheelTorque** — speed magnitude for low-speed friction boost:

```c
iVar4 = *(int *)(*(int *)(entity + 8) + 0x3c);
fVar2 = SQRT( *(float*)(iVar4+0x48)*… + *(float*)(iVar4+0x44)*… + *(float*)(iVar4+0x40)*… );
```

**Steering** (`0x64f840`) — same `rb+0x40..48` dotted with transformed forward axis.

**airStab** — when physics present:

```c
puVar4 = (undefined4 *)(*(int *)(piVar1[2] + 0x3c) + 0x40);  // rb+0x40
// copy 4 floats → local linVel pack
```

---

### `+0x50..+0x5c` — angular velocity

**Continuous AVD** (`0x64d810`):

```c
iVar2 = *(int *)(iVar1 + 0x3c);
local_20 = *(float *)(iVar2 + 0x50);   // ωx
local_1c = *(float *)(iVar2 + 0x54);   // ωy
local_18 = *(float *)(iVar2 + 0x58);   // ωz
// |ω|²  vs  threshold² (this+0x10)
// scale = 1 − damp * dt   (clamp ≥ 0)
// write back via (**(rb_vtbl + 0x54))(&local_20)   // setAngularVelocity
local_14 = *(float *)(iVar2 + 0x5c) * local_14;  // ω.w scaled too
```

**airStab** — packs angVel from the same slots for the corrective-impulse vtbl call:

```c
iVar6 = *(int *)(piVar1[2] + 0x3c);
local_30 = *(iVar6 + 0x50);
local_2c = *(iVar6 + 0x54);
local_28 = *(iVar6 + 0x58);
local_24 = *(float *)(iVar6 + 0x5c);
```

Recovery path clears angVel via body vtbl **`+0x50` then `+0x54`** with zero vector `DAT_00b04eb0` (method slots, **not** field offsets).

---

### `+0xb0..+0xbc` — world position

**Getter** (`FUN_00404c90`) returns `rb + 0xb0` — canonical position slot.

**airStab** re-ground (post collision window):

```c
iVar6 = *( *( *(VehicleAction+0x44) + 8 ) + 0x3c );   // rb
// terrain height at (pos.x, pos.z):
CVOGMap_CastTerrainHeight( *(iVar6 + 0xb0), *(iVar6 + 0xb8) );
// rebuild pose:
local_28 = *(iVar6 + 0xb0);   // x
local_24 = (float)castY;      // y from terrain
local_20 = *(iVar6 + 0xb8);   // z
// also reads *(iVar6 + 0xb4) + DAT_00a110d8 (10.0) on stack before cast
// set transform via body vtbl +0x40
```

| Lane | Off | Use |
|------|----:|-----|
| X | `+0xb0` | cast / pose |
| Y | `+0xb4` | height; damping additive source |
| Z | `+0xb8` | cast / pose |
| W | `+0xbc` | pad in 4-float copies |

---

### `+0x30..+0x3c` quaternion **and** `+0x80..+0xa8` basis — both present

These are **two orientation representations** on the same body, not aliases.

#### Quaternion at `+0x30` (4 floats)

Fits cleanly **before** linVel at `+0x40`. A 3×3 matrix starting at `+0x30` would collide with linVel — ruled out.

**calcWheelTorque** upright check:

```c
// body quat pointer:
iVar5 = *( *(entity+8) + 0x3c ) + 0x30;   // rb+0x30
FUN_004e8b60(iVar5, &fStack_20);         // quat → body-up vector
// if |dot(bodyUp, worldUp)| < 0.8 → pow falloff on torque
```

**`FUN_004e8b60`** is a quaternion → basis-vector extractor (up axis). Pseudocode from decompile
(`param_1` = quat xyzw, `param_2` = out vec):

```c
// uses constant 2.0 (g_flLevelUpUiBase_Inferred) and 1.0
out.x = (q.x*q.y - q.z*q.w) * 2
out.y = 1 - (q.z*q.z + q.x*q.x) * 2
out.z = (q.z*q.y + q.x*q.w) * 2
out.w = 0
```

**airStab** packs orientation via `FUN_00404a20` → `rb+0x30` (4 floats) into the impulse call.

#### 3×3 world matrix at `+0x80..+0xa8`

**Aero** rotates vehicle-data front/up axes by body matrix entries:

```c
// worldFront components from R * frontAxis:
// R rows/cols at rb+0x80, +0x84, +0x88, +0x90, +0x94, +0x98, +0xa0, +0xa4, +0xa8
fVar18 = *(rb+0xa0)*fz + *(rb+0x90)*fy + *(rb+0x80)*fx;
fVar22 = *(rb+0x84)*fx + *(rb+0xa4)*fz + *(rb+0x94)*fy;
fVar23 = *(rb+0x88)*fx + *(rb+0xa8)*fz + *(rb+0x98)*fy;
```

**Steering** (`0x64f840`):

```c
FUN_005d6ae0( *( *(fw+0x30) + 0x3c ) + 0x80,  *(fw+0x10) + 0x10 );
// then dots rb linVel with transformed axis in local_20/1c/18
```

| Region | Layout | Consumers |
|--------|--------|-----------|
| `+0x30..+0x3c` | Quaternion (xyzw) | calcWheelTorque upright; airStab; applyAction quat→ω paths |
| `+0x80..+0xa8` | 3×3 world rotation (9 floats, stride 0x10 per column/row with 4-byte gaps unused or pad) | aero; steering transform helper |

---

## Layout sketch (byte offsets)

```
rb +0x00  vptr / Havok header …
     …
     +0x2c  invMass                 f32
     +0x30  quat.x  quat.y  quat.z  quat.w     (16 B)
     +0x40  linVel.x .y .z .w                  (16 B)
     +0x50  angVel.x .y .z .w                  (16 B)
     …
     +0x80  R[0..]  (3×3 world basis, 9×f32 across +0x80..+0xa8)
     …
     +0xb0  pos.x   pos.y   pos.z   pos.w      (16 B)
```

---

## Cross-checks / non-goals

| Claim | Verdict |
|-------|---------|
| `+0x2c` is inverse mass | **Confirmed** (three independent mass = 1/x sites) |
| `+0x40` linVel, `+0x50` angVel | **Confirmed** |
| `+0xb0` world position | **Confirmed** (getter + airStab cast) |
| `+0x30` is quaternion, not matrix start | **Confirmed** (size fits before linVel; `004e8b60` quat math) |
| Body also keeps matrix at `+0x80` | **Confirmed** (aero / steering) — “basis as seen” |
| Solver `cb` inv-mass at `+0xe0` | **Different struct** — do not conflate with live RB |
| Full Havok `hkpRigidBody` / motion-state dump | **Out of scope** — only offsets used by chassis vehicle path |

---

## Tools used (this verification)

1. `batch_decompile` — `0x64dae0`, `0x64d810`, `0x64de50`, `0x64f840`, `0x64bc70`, `0x598040`, `0x598320`, `0x404c90`, `0x404a20`, `0x4e8b60`
2. Cross-read prior verified notes: `fn_0064dae0_aero.md`, `fn_00598320_airStab.md`, `0.8-struct-offsets.md`

Did **not** use `disassemble_bytes`.
