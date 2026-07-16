# Verified: Wheel runtime state — stride `0xC0` field map

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Struct | Per-wheel runtime state (`hkDefaultWheels` array element) |
| Array base | `*(wheels + 0x80)` where `wheels = *(framework + 0xc)` |
| Count | `*(wheels + 0xc)` |
| Stride | **`0xC0`** (192 bytes) — postTick loop adds `0xc0` per wheel |
| Status | **Verified** from suspension / preUpdate / calcWheelTorque / postTick decompiles |

## Source functions (authoritative readers/writers)

| Symbol | Addr | Role vs wheel fields |
|---|---|---|
| `hkVehicleFramework_preUpdate` | `0x64cf20` | Raycast path; writes contact frame, spin, susp scalars |
| `hkVehicleWheelCollide::collide` (fw vtbl+0x20) | `0x64bbd0` | Packs ray from `wheel+0x00..0x1c` → phantom cast |
| `hkDefaultSuspension::update` | `0x64de50` | Spring+damper: reads `+0x80`, `+0xAC`, `+0xB0`, `+0xB4` |
| `VehicleAction_calcWheelTorque` | `0x598040` | Drive: gates `+0x80`; `torqueCurve2D(+0x20, +0x28)` |
| `hkVehicleFramework_postTickApplyForces` | `0x64bc70` | Impulse at `+0x20`; normal `+0x30`; scale `+0x88`; writeback `+0x94..+0xA0` |
| `VehicleAction_applyAction` | `0x598650` | Scans `+0xB0` for most-compressed (landing / air logic) |

Related (not this struct): per-wheel **engine torque output** lives in a **parallel float array**
`*(wheels + 0x28) + i*4` (calcWheelTorque → postTick drive pack). That is **not** inside the
`0xC0` wheel element.

Cross-refs: `0.3-friction-solver.md`, `0.4-suspension.md`, `0.5-wheel-collide.md`,
`0.7-transmission.md`, `0.8-struct-offsets.md`.

---

## Access pattern

```
wheels = *(framework + 0x0c)          // hkDefaultWheels / wheel container
wheel  = *(wheels + 0x80) + i * 0xC0  // i ∈ [0, wheelCount)
```

postTick confirms stride:

```c
// iVar19 = *(wheels+0x80) + offset; then offset += 0xc0 each iteration
afStack_410[3] = (float)((int)afStack_410[3] + 0xc0);
```

All `vec4` entries are 4×`float32` (xyzw), 16-byte aligned in practice.

---

## Full field map (stride `0xC0`)

| Off | Size | Type | Name | Meaning | Primary evidence |
|----:|-----:|------|------|---------|------------------|
| `+0x00` | 16 | vec4 | **hardpoint origin** | Wheel attach / ray **start** (world after transform). Cast copies `+0x00..0x0c` | preUpdate; collide `0x64bbd0` |
| `+0x10` | 16 | vec4 | **ray end / hardpoint work** | Ray **end** (`+0x10..0x1c`); also hardpoint working copy in preUpdate | collide packs 8 floats start+end |
| `+0x20` | 16 | vec4 | **contact / hardpoint point** | World contact point. Suspension impulse applied **here**. Axle jacobian averages `+0x20..0x2c`. preUpdate builds: `hardpoint(+0x00) + downAxis(+0x50)×len` | preUpdate; postTick applyPointImpulse; 0.7 |
| `+0x20` | 4 | f32 | *(alias)* torqueCurve2D **X** | `calcWheelTorque` passes `*(float*)(wheel+0x20)` as arg1 — **contact.x**, not RPM | `0x598040` → `0x4a9750` |
| `+0x24` | 4 | f32 | contact.y | Y of contact point | preUpdate write |
| `+0x28` | 4 | f32 | *(alias)* torqueCurve2D **Y** | `calcWheelTorque` passes `*(float*)(wheel+0x28)` as arg2 — **contact.z**, not throttle | `0x598040` |
| `+0x2c` | 4 | f32 | contact.w | Pad / fourth lane of contact vec | preUpdate / postTick |
| `+0x30` | 16 | vec4 | **contact normal** | Suspension force direction; airborne = `−downAxis(+0x50)`. postTick: `F = suspForce·dt · (+0x30..)` | preUpdate; susp update; postTick |
| `+0x40` | 16 | vec4 | **chassis basis row** | Spin-axis frame (copied from chassis RB) | preUpdate (0.4) |
| `+0x50` | 16 | vec4 | **suspension down axis** | Hardpoint direction / cast direction unit | preUpdate; descriptor default `(0,−1,0)` |
| `+0x60` | 16 | vec4 | **lateral / steer basis** | Crossed with normal in postTick for axle lateral jacobian rows (`+0x60/+0x64/+0x68` × `+0x30..`) | postTick decompile |
| `+0x70` | 16 | vec4 | **steer/roll basis** | Normalized basis written near end of preUpdate | preUpdate (0.4) |
| `+0x80` | 1 | u8 | **in-contact flag** | `1` = grounded, `0` = airborne. Gates susp force, calcWheelTorque, friction writeback | preUpdate; `0x64de50`; `0x598040`; postTick |
| `+0x81..0x87` | 7 | — | *(pad / unknown)* | Between contact byte and next float; not named by the three primary decompiles | — |
| `+0x88` | 4 | f32 | **drive-torque axle scale** | Drive pack: `Σ (wheels+0x28[i] × wheel+0x88) / axleWheelCount` | postTick `*(wheel+0x88)` |
| `+0x8C` | 4 | f32 | **wheel spin ω** | `spin = (wheel+0x9C + longContactVel) / radius[i]` | preUpdate |
| `+0x90` | 4 | f32 | **spin angle** | `+= dt · wheel+0x8C` (visual / integrate) | preUpdate |
| `+0x94` | 4 | f32 | **friction writeback [0]** | postTick: if in-contact **and** `+0xA8`: set from axle OUT; else **0**. Longitudinal/drive impulse lane (OUT[2] in 0.3 map) | postTick writeback |
| `+0x98` | 4 | f32 | **friction writeback [1]** | Always written from OUT[3] | postTick |
| `+0x9C` | 4 | f32 | **longitudinal contact vel / OUT[0]** | Used next preUpdate for spin; overwritten by solver writeback `*OUT` | preUpdate spin; postTick |
| `+0xA0` | 4 | f32 | **lateral impulse / OUT[1]** | Friction writeback | postTick |
| `+0xA4` | 4 | ptr | **contact rigid body** | Hit body from cast (`result+0x20`); `0` on miss. postTick may apply equal-and-opposite impulse to movable body type==2 | preUpdate; postTick `*(wheel+0xa4)` |
| `+0xA8` | 1 | u8 | **contact usable / collidable flag** | Gates writeback of `+0x94` with `+0x80` (`if (+0x80==0 \|\| +0xA8==0)` → clear +0x94 path) | postTick |
| `+0xA9..0xAB` | 3 | — | *(pad)* | Align to `+0xAC` | — |
| `+0xAC` | 4 | f32 | **suspension scaling factor** | Spring mult. Miss: `1.0`. Contact shallow: `1/maxSuspLen`. Deep hit: `−1/hitDist` (`DAT_00aaa668 = −1.0` numerator) | preUpdate; `0x64de50` |
| `+0xB0` | 4 | f32 | **suspension current length** | Compression input: `restLength[i] − wheel+0xB0`. applyAction scans min for lift. Miss → rest length | preUpdate; susp; applyAction |
| `+0xB4` | 4 | f32 | **closing speed** | Damper velocity. `< 0` → compression damp (`comp+0x50`); `≥ 0` → extension damp (`comp+0x5C`). Miss → `0` | preUpdate; `0x64de50` |
| `+0xB8..0xBF` | 8 | — | *(tail / unused in these paths)* | End of `0xC0` element; not referenced by the listed force pipeline | — |

---

## Highlighted fields (task offsets)

### `+0x20` / `+0x28` — contact point X/Z (and torqueCurve2D args)

**Writer:** `preUpdate` second loop (after cast result):

```
wheel[+0x20..+0x2c] = wheel[+0x00..+0x0c]           // hardpoint origin
                    + wheel[+0x50..+0x5c] * len      // + downAxis × suspension length
```

**Readers:**

1. **postTick** — `applyPointImpulse(suspImpulse, wheel)` uses the contact point; axle angular jacobian averages `+0x20..+0x2c`.
2. **calcWheelTorque** — `torqueCurve2D(wheel+0x20, wheel+0x28)` = **contact.x, contact.z**.

**Port note (from 0.7):** these are **world coordinates**, not RPM×throttle. The 2D LUT bins almost always fall **out of range** → factor `engine.factors[0]`. Do not invent an RPM writer into `+0x20/+0x28`.

### `+0x80` — in-contact byte

| Path | Behavior |
|---|---|
| preUpdate miss | `*(u8*)(wheel+0x80) = 0` |
| preUpdate hit | `= 1` |
| `hkDefaultSuspension::update` | if `0` → `suspForce[i] = 0` |
| `calcWheelTorque` | if `0` → `wheels+0x28[i] = 0` (no drive) |
| postTick writeback | with `+0xA8`: if either false → zero `+0x94` path |

### `+0x88` — drive scale into friction axle pack

```
drivePack[axle] += wheels[+0x28][i] * wheel[+0x88] * (1 / axleWheelCount)
```

Confirmed postTick decompile: `fVar9 = wheels+0x28[i]`, `fVar10 = *(wheel+0x88)`, accumulate `fVar9 * fVar10 * invN`.

### `+0xAC` — suspension scaling factor

| Condition | Value written by preUpdate |
|---|---|
| No contact | `1.0` |
| Contact, hit within max susp clamp (`framework+0x304`) | `1 / maxSuspLen` |
| Contact, deep / alternate branch | `−1 / hitDist` (`DAT_00aaa668`) |

Consumed by spring term:

```
springForce = (restLength[i] − wheel[+0xB0]) * strength[i] * wheel[+0xAC]
```

### `+0xB0` — current suspension length

preUpdate (contact; `shockScale` defaults `1.0`):

```
// 0.5 form (hit fraction along ray):
wheel[+0xB0] = (wheelRadius[i] + suspRestLen[i]) * hitFraction − wheelRadius[i]

// 0.4 alternate wording (hit length / shockScale path):
//   (restLen + hitLen)*shockScale − restLen
// Miss: wheel[+0xB0] = restLen
```

`hkDefaultSuspension::update`:

```
compression = restLength[i] − wheel[+0xB0]
```

`applyAction` scans all wheels' `+0xB0` for most-compressed (air/landing).

### `+0xB4` — closing / damper speed

| Condition | Value |
|---|---|
| No contact | `0` |
| Contact, shallow clamp branch | `0` (with `+0xAC = 1/maxSuspLen`) |
| Contact, rate branch | `(−1/hitDist) * dot(normal, hardpoint − contactPoint)` |

Damper selection in `0x64de50`:

```
dampCoef = (wheel[+0xB4] >= 0) ? extensionDamp[i] : compressionDamp[i]
damperForce = dampCoef * wheel[+0xB4]
suspForce[i] = (springForce − damperForce) * gScale   // gScale = 1/chassisRB[+0x2c]
```

---

## Pipeline order (who touches the struct)

```
preUpdate 0x64cf20
  ├─ cast (ray from +0x00 / +0x10)
  ├─ write +0x30 normal, +0x80 contact, +0xA4 body, +0xA8 flag
  ├─ write +0xAC / +0xB0 / +0xB4
  ├─ write +0x20..+0x2c contact hardpoint
  └─ write +0x8C spin, integrate +0x90

component updates (vtbl+0x14)
  └─ hkDefaultSuspension::update 0x64de50
       reads +0x80, +0xAC, +0xB0, +0xB4 → susp component force[]  (not into wheel)

VehicleAction_calcWheelTorque 0x598040
  ├─ gate +0x80
  ├─ torqueCurve2D(+0x20, +0x28)
  └─ write parallel array wheels+0x28[i]  (NOT wheel+0x28)

postTickApplyForces 0x64bc70
  ├─ impulse along +0x30 at point +0x20
  ├─ drive pack × +0x88
  ├─ lateral basis from +0x30 × +0x60
  └─ writeback +0x94 / +0x98 / +0x9C / +0xA0  (gate +0x80 && +0xA8 for +0x94)
```

---

## Parallel arrays on the wheel container (not in stride `0xC0`)

| Container off | Meaning |
|---|---|
| `wheels+0x0c` | wheel count |
| `wheels+0x10` | `wheelRadius[]` (preUpdate spin `/radius[i]`) |
| `wheels+0x28` | **per-wheel drive torque out[]** — calcWheelTorque clamps `[0, 1000]` (`DAT_00a0f520`) |
| `wheels+0x34` / `+0x40` | friction μ table entries (setup; rear already × `RearWheelFrictionScalar`) |
| `wheels+0x58` | `axleIndex[i]` (i32) |
| `wheels+0x64` | axle count |
| `wheels+0x68` | `axleWheelCount[ax]` |
| `wheels+0x80` | **base pointer** of `0xC0` runtime array |

---

## Constants touched via these fields

| Symbol | Addr | Value | Role |
|---|---|---|---|
| `DAT_00aaa668` | `0x00aaa668` | **−1.0** | `+0xAC` / `+0xB4` numerator; down-axis Y in descriptor |
| `g_flOne` | `0x00a0f2a0` | **1.0** | default `+0xAC` on miss; inv-length numerators |
| `DAT_00a0f520` | `0x00a0f520` | **1000.0** | clamp on `wheels+0x28[i]` (not a wheel field) |
| `DAT_00a0f298` | `0x00a0f298` | **0.5** | rear handbrake cut in calcWheelTorque (not stored on wheel) |
| `framework+0x304` | runtime | max susp length | preUpdate clamp for `+0xAC`/`+0xB4` branch |

---

## Corrections / pitfalls

1. **`wheel+0x20` / `+0x28` are not RPM/throttle.** They are contact hardpoint X/Z. torqueCurve2D still *calls* them as if they were curve axes (see `fn_004a9750_torqueCurve2D.md`).
2. **`wheels+0x28[i]` ≠ `wheel+0x28`.** Engine torque is a **sibling array** on the container; the wheel element's `+0x28` is contact.z.
3. **`+0x9C` dual use:** long contact speed into next spin integrate, then solver writeback overwrites it each postTick.
4. **`+0xB0` formula** — 0.4 and 0.5 word the hit-length path slightly differently; both agree miss = rest length and spring uses `rest − current`. Prefer cast `hitFraction` form when implementing collide:  
   `(radius + restLen) * fraction − radius`.
5. **Stride is `0xC0`, not `0x80`.** Chassis RB and other Havok objects use different strides; only this wheel array is `0xC0`.

---

## Status

| Item | State |
|---|---|
| Stride `0xC0`, base `wheels+0x80` | **Verified** (postTick add `0xc0`) |
| `+0x20/+0x28` contact + torqueCurve2D args | **Verified** (preUpdate write + calcWheelTorque read) |
| `+0x80` in-contact | **Verified** (preUpdate / susp / torque / writeback) |
| `+0x88` drive scale | **Verified** (postTick multiply) |
| `+0xAC / +0xB0 / +0xB4` susp triple | **Verified** (preUpdate write + `0x64de50` read) |
| `+0x94..+0xA0` friction writeback | **Verified** (postTick after solve) |
| `+0x81..0x87`, `+0xB8..0xBF` | **Unnamed** — unused by the force pipeline above |
| Live `read_memory` of a sample car wheel block | **Not done this pass** (layout from decompiles only) |
