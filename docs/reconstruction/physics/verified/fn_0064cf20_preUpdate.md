# Verified: `hkVehicleFramework_preUpdate` @ `0x64cf20`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x0064cf20` |
| Body | `0x0064cf20` – `0x0064d7cc` |
| Symbol | `hkVehicleFramework_preUpdate` |
| Convention | MSVC `__thiscall` — `this` = framework; `param_2` = step info (`param_2[0]` = `dt`) |
| Vtable slot | Framework vtbl **`+0x14`** (table near `0x9e4a40`: dword `+0x14` = `0x64cf20`) |
| Caller | `VehicleAction_tickSubsystems` @ `0x636a60` (before component `vtbl+0x14` updates) |
| RE tools | Ghidra `decompile_function` + `force_decompile` re-gate; `read_memory` on `DAT_00aaa668`, `g_flOne`, `DAT_00a0f298` |
| Status | **Verified** (re-read) |

Related phase maps: `0.4-suspension.md`, `0.5-wheel-collide.md`, `0.7-transmission.md`,
`fn_0064bbd0_wheelCollide.md`, `fn_offsets_wheel.md`.

---

## Role

Per-tick **pre-component** pass over every wheel:

1. **Build world rays** (hardpoint → hardpoint + downAxis·(radius+restLen)).
2. Optional phantom / filter setup (`vtbl+0x1c`, one-time alloc path).
3. **Cast** via `vtbl+0x20` (`0x64bbd0` → `TtPhantom::castRay`).
4. Write **contact frame**, **in-contact**, **compression length**, **spring scale**, **closing/rate term**.
5. Integrate **wheel spin** (`+0x8c` / `+0x90`) and refresh steer/spin basis (`+0x70`, `+0x40`).

Does **not** apply suspension force (that is `hkDefaultSuspension::update` @ `0x64de50` after this).

---

## Anchors

| Location | Meaning |
|---|---|
| `framework + 0x0c` (`param_1[3]`) | Wheels container |
| `*(wheels + 0x80) + i·0xC0` | Per-wheel runtime slot (`pfVar7` / `puVar6`) |
| `*(wheels + 0x0c)` | Wheel count |
| `*(wheels + 0x10)[i]` | Wheel **radius** |
| `framework + 0x28` (`param_1[10]`) | Suspension component |
| `*(susp + 0x28)[i]` | Suspension **rest length** |
| `framework + 0x304` (`param_1[0xc1]`) | Max-susp / scale clamp (`maxSuspLen`) |
| `framework + 0x1f8` | TtPhantom\* (used inside collide) |
| `param_2[0]` | Substep **dt** |

**Stride:** `0xC0`. **Base:** `*(wheels+0x80)`.

Float-index ↔ byte offset (wheel base = `pfVar7`):

| `pfVar7[k]` | Byte off | Field |
|---:|---|---|
| `[0..3]` | `+0x00` | hardpoint / ray start |
| `[4..7]` | `+0x10` | ray end |
| `[8..0xb]` | `+0x20` | contact point |
| `[0xc..0xf]` | `+0x30` | contact normal |
| `[0x14..0x17]` | `+0x50` | suspension down axis |
| byte at `pfVar7+0x20` | **`+0x80`** | **in-contact** |
| `[0x22]` | `+0x88` | (also written; drive-scale slot) |
| word at `+0x8c` / `+0x90` | **`+0x8c` / `+0x90`** | **spin ω / angle** |
| `[0x29]` | `+0xa4` | contact body |
| byte at `pfVar7+0x2a` | `+0xa8` | body flag |
| **`[0x2b]`** | **`+0xac`** | **suspension scale** |
| **`[0x2c]`** | **`+0xb0`** | **current susp length** |
| **`[0x2d]`** | **`+0xb4`** | **closing / rate term** |

---

## Pipeline (three wheel loops)

```
Loop 1 — ray geometry
  for i in wheels:
    transform hardpoint / down-axis into world (FUN_005d6ae0 / FUN_005d68f0)
    rayLen = radius[i] + restLen[i]
    wheel.end = wheel.hardpoint + downAxis * rayLen     // +0x10..+0x1c

vtbl[+0x1c]();   // framework prep
// phantom setup if framework[0xe]==0, else FUN_00580dd0

Loop 2 — cast + contact / compression / scale / rate
  for i in wheels:
    seed fraction=1.0, body=0
    vtbl[+0x20](i, &result)            // 0x64bbd0
    if miss:  write airborne state
    else:     write contact, +0xb0, +0xac, +0xb4, contact point

Loop 3 — spin + steer basis
  chassisLongVel = dot(chassisBasisRow, linVel)
  for i in wheels:
    if unlockChar[i]==0:
      ω = (wheel[+0x9c] + chassisLongVel) / radius[i]
      wheel[+0x8c] = ω
      wheel[+0x90] += dt * ω
    else:
      wheel[+0x8c] = 0
    // also writes +0x70 basis, copies chassis row to +0x40, …
```

Decompiler reuses stack slots (`local_d0` / `local_d8` / `local_cc`) heavily after Loop 1; treat
**float-index stores to `pfVar7[...]`** and the **compression algebra** as authoritative, not the
reused pointer names in Loop 2’s loads.

---

## Cast result (Loop 2 input)

```
fStack_94 = g_flOne;          // hit fraction default 1.0
iStack_88 = 0;                // hit collidable
framework->vtbl[+0x20](i, &fStack_a8);
```

| Result off | Stack name | Meaning |
|---|---|---|
| `+0x00..0x0c` | `fStack_a8..fStack_9c` | contact **normal** (world) |
| `+0x14` | `fStack_94` | hit **fraction** along ray |
| `+0x20` | `iStack_88` | hit **collidable** (`0` = miss) |

Miss ⇔ `iStack_88 == 0`. Full cast path: `fn_0064bbd0_wheelCollide.md`.

---

## Highlighted fields (task focus)

### `wheel+0x80` — in-contact (byte)

| Branch | Write |
|---|---|
| Miss | `*(u8*)(wheel+0x80) = 0` |
| Hit | `= 1` |

Gates suspension force (`0x64de50`) and drive (`0x598040`). **Not** the spin-integrate gate
(see spin section — separate char array).

### `wheel+0xb0` — current suspension length (compression input)

**Authoritative hit formula** (decompile):

```
radius  = (*(wheels + 0x10))[i]          // Loop 1 source; confirmed
restLen = (*(susp   + 0x28))[i]          // Loop 1 source; confirmed
frac    = result[+0x14]                  // fStack_94 after cast

// fVar9 = radius, fVar10 = restLen (roles fixed by miss path + algebra)
travel      = (radius + restLen) * frac
wheel[+0xb0] = travel - radius
// i.e.  wheel[+0xb0] = (radius + restLen) * hitFraction - radius
```

**Miss:**

```
wheel[+0xb0] = restLen                  // fully extended
```

| `hitFraction` | `+0xb0` |
|---|---|
| `0` | `−radius` |
| `radius/(radius+restLen)` | `0` |
| `1` (hit at ray end) | `restLen` |
| miss | forced `restLen` |

Spring later uses `compression = restLen[i] − wheel[+0xb0]` (`0x64de50`).

**Correction vs `0.4-suspension.md` wording:** Phase 0.4 wrote
`(restLen + hitLen)*shockScale − restLen`. Re-decompile matches the **`0.5` / wheelCollide** form
`(radius + restLen)·frac − radius` with `frac` defaulting to `1.0` (`g_flOne`). Same numeric family
when `frac` is the ray hit fraction and radius/restLen are not swapped.

Contact **point** on hit (same travel):

```
wheel[+0x20..+0x2c] = hardpoint[+0x00] + downAxis[+0x50] * travel
// travel = (radius + restLen) * frac
```

On miss, contact point is copied from ray **end** (`+0x10`), normal = `−downAxis`.

### `wheel+0xac` — suspension scaling factor

| Branch | Value |
|---|---|
| Miss | `1.0` (`g_flOne`) |
| Hit, `hitDist >= −maxSuspLen` | `1.0 / maxSuspLen` |
| Hit, `hitDist < −maxSuspLen` | `DAT_00aaa668 / hitDist` = **`−1.0 / hitDist`** |

`maxSuspLen = (float)framework[+0x304]` (`param_1[0xc1]`).

Consumed by spring:

```
springForce = (restLen − wheel[+0xb0]) * strength[i] * wheel[+0xac]
```

### `wheel+0xb4` — closing / rate term (damper input)

| Branch | Value |
|---|---|
| Miss | `0` |
| Hit, `hitDist >= −maxSuspLen` (**shallow / normal clamp**) | **`0`** |
| Hit, `hitDist < −maxSuspLen` (**deep branch**) | `(−1/hitDist) * dot(normal, Δ)` |

Deep-branch decompile (verbatim structure):

```
scale = DAT_00aaa668 / fStack_ac;          // −1 / hitDist
wheel[+0xb4] = scale * (
    normal.z * (a.z - b.z) +
    normal.y * (a.y - b.y) +
    normal.x * (a.x - b.x)
);
wheel[+0xac] = scale;
```

`Δ = vecA − vecB` comes from stack vectors filled around the post-cast helper
(`vtbl+0x24`) and two `vtbl+0x58` point-velocity-style calls on chassis / contact body.
Phase maps call this `dot(normal, hardpoint − contactPoint)`; decompiler stack names are
heavily aliased — treat as **normal · relative vector** with denominator **`hitDist`**, not
`framework+0x304`.

**Important vs `0.5-wheel-collide.md` ambiguity #3:** denominator of the rate term is
**`fStack_ac` (hitDist)**, not `fw+0x304`. `fw+0x304` is only the **branch threshold** and the
numerator for the shallow scale `1/maxSuspLen`.

**Damper implication:** on the shallow branch (typical grounded hit with `hitDist ≥ −maxSuspLen`),
preUpdate writes **`+0xb4 = 0`**, so `hkDefaultSuspension::update` sees zero closing speed and
the damper term is inactive unless the deep branch fires. Spring still uses `+0xac = 1/maxSuspLen`.

### `hitDist` (`fStack_ac`)

Stack scalar compared as:

```
if (−maxSuspLen <= fStack_ac) {  /* shallow */ }
else                          {  /* deep: scale = −1/fStack_ac */ }
```

No clean single assignment to `fStack_ac` appears in the high-level decompile between cast return
and this compare (likely filled by `vtbl+0x24` / velocity helper path, or fused with length).
Documented as **signed hit-distance / length term** driving the `+0xac`/`+0xb4` branch; not the
same stack reuse as Loop 3’s later `fStack_ac = chassisLongVel` (that overwrite is **after** Loop 2).

---

## Miss branch (full write set)

```
wheel[+0xb4] = 0
wheel[+0x80] = 0                    // airborne
wheel[+0xa8] = 1
wheel[+0xa4] = 0
wheel[+0xb0] = restLen
wheel[+0x20..+0x2c] = ray end       // copy +0x10
wheel[+0x30..+0x3c] = −downAxis     // synthetic normal
wheel[+0x88] = 0                    // pfVar7[0x22]
wheel[+0xac] = 1.0
```

---

## Hit branch (full write set, ordered)

```
wheel[+0x30..+0x3c] = result normal
// body pointer path: if body+0x18==1 then body+0x20 else 0 → +0xa4
wheel[+0xa8] = *(u8*)(bodyish + 0x40)   // flag from body path
wheel[+0x80] = 1
wheel[+0xb0] = (radius + restLen)*frac − radius
wheel[+0x20..+0x2c] = hardpoint + downAxis * ((radius+restLen)*frac)
vtbl[+0x24](wheel, &result, …)         // post-contact helper
// N·downAxis into fStack_b8 (computed; not stored on wheel)
// two point-velocity calls
if (hitDist >= −maxSuspLen):
    wheel[+0xb4] = 0
    wheel[+0xac] = 1.0 / maxSuspLen
else:
    scale = −1.0 / hitDist
    wheel[+0xb4] = scale * dot(normal, Δ)
    wheel[+0xac] = scale
```

---

## Wheel spin (Loop 3)

Before the loop, chassis longitudinal scalar:

```
// iVar4 = *(framework[+0x0c] + 0x3c)  → chassis-related body
// (fStack_78, fStack_74, fStack_70) = linear velocity vector (from prior xform path)
chassisLongVel = body[+0x40]*vx + body[+0x44]*vy + body[+0x48]*vz
// stored in fStack_ac for the spin loop only
```

Per wheel:

```
radius = (*(wheels + 0x10))[i]
dt     = param_2[0]

// Gate: byte at *(framework[9] /* fw+0x24 */ + 0x1c) + i
// (char array; 0 = integrate spin, nonzero = force ω=0)
if (unlockChar[i] == 0) {
    ω = (wheel[+0x9c] + chassisLongVel) / radius
    wheel[+0x8c] = ω
    wheel[+0x90] = wheel[+0x90] + dt * ω
} else {
    wheel[+0x8c] = 0
    // +0x90 left unchanged
}
```

| Offset | Meaning |
|---|---|
| `+0x9c` | Longitudinal contact / friction writeback from **previous** postTick |
| `+0x8c` | Wheel spin rate ω (rad/s analogue) |
| `+0x90` | Integrated spin angle |

**Corrections:**

1. Spin gate is the **char array** at `*(fw+0x24 + 0x1c) + i`, **not** `wheel+0x80` (0.5’s
   “grounded only” is incomplete).
2. `wheel+0x8c` is **never** fed into `torqueCurve2D` (0.7 still holds): curve args remain
   contact hardpoint X/Z at `+0x20` / `+0x28`.

Loop 3 also builds a normalized steer/roll basis at `+0x70..+0x7c` (uses `DAT_00a0f298 = 0.5`
as a scale on an input vector) and copies a chassis basis row into `+0x40` — secondary to the
contact/compression/spin verification target.

---

## Constants (`read_memory`)

| Symbol | Address | Raw LE | float32 | Role in this function |
|---|---|---|---|---|
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Default hit fraction; miss `+0xac`; inv numerators |
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **−1.0** | Deep-branch numerator for `+0xac` / `+0xb4` |
| `DAT_00a0f298` | `0x00a0f298` | `00 00 00 3f` | **0.5** | Loop 3 steer-basis scale (not contact math) |

Runtime (not image constants): `framework+0x304` (`maxSuspLen`), per-wheel `radius[]` / `restLen[]`,
`param_2[0]` (`dt`).

---

## Conflicts / reconciliations

| Item | Prior map | This re-verify | Verdict |
|---|---|---|---|
| Compression `(r+L)·frac − r` | `0.5`, `fn_0064bbd0` | yes | **match** |
| Miss → `+0xb0=restLen`, `+0x80=0`, `+0xac=1`, `+0xb4=0` | `0.4` / `0.5` | yes | **match** |
| `+0xac` shallow = `1/maxSuspLen` | `0.4`, `fn_offsets_wheel` | yes | **match** |
| `+0xb4` shallow = `0` | `fn_offsets_wheel` | yes | **match** |
| Deep `+0xac/+0xb4` use `−1/hitDist` | `0.4` | yes (`DAT_00aaa668/fStack_ac`) | **match** |
| Rate-term **den** = `fw+0x304` | `0.5` ambiguity #3 | **den = hitDist**; `fw+0x304` is threshold only | **0.5 wrong** |
| `0.4` compression wording `(rest+hitLen)·scale−rest` | `0.4` §3 | prefer `(r+L)·frac−r` | **prefer 0.5 form** |
| Spin only when grounded (`+0x80`) | `0.5` | gate is char array @ `*(fw+0x24+0x1c)+i` | **0.5 incomplete** |
| Spin formula `(+0x9c + longVel)/radius`, integrate `+0x90` | `0.7` | yes | **match** |
| Stride `0xC0`, base `wheels+0x80` | all maps | yes | **match** |

---

## Residual ambiguities

1. **Exact producer of `hitDist` (`fStack_ac`)** before the shallow/deep compare — not a single
   obvious store in pseudocode; may be `vtbl+0x24` (`0x51e900`, not defined as a function in the
   DB) or fused length/velocity math. Behavior of the branch is still bit-specified above.
2. **Identity of `Δ` in deep `+0xb4`** — hardpoint−contact vs chassis−ground velocity difference;
   two `+0x58` calls sit immediately above the formula. Does not change the scale `−1/hitDist`.
3. **Loop 2 decompiler pointer names** (`local_d0+0x28` vs susp rest array) — algebra + Loop 1
   sources fix `radius` / `restLen` roles; do not trust Loop 2’s recycled pointer locals.
4. **Char array at `*(fw+0x24)+0x1c`** — spin lock / fixed-wheel flags; owner component not fully
   named here.

---

## Status

| Item | State |
|---|---|
| Decompile `0x64cf20` | **Verified** (`force_decompile` refresh) |
| `+0x80` in-contact | **Verified** |
| `+0xb0` compression formula | **Verified** `(r+L)·frac − r` / miss `restLen` |
| `+0xac` scale branches | **Verified** |
| `+0xb4` rate branches | **Verified** (shallow 0; deep `−1/hitDist·dot`) |
| Wheel spin `+0x8c`/`+0x90` | **Verified** |
| Constants `g_flOne`, `DAT_00aaa668`, `DAT_00a0f298` | **`read_memory` confirmed** |
| Live sample wheel block | **Not done** (static RE only) |
