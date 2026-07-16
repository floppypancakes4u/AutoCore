# Verified: `hkVehicleFrictionSolver_circleProjection` @ `0x006c3f90`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Parent solver: `hkVehicleFrictionSolver_solve` @ `0x006c4450` (see `fn_006c4450_frictionSolve.md`).
Scale-table builder: `FUN_006c4150` @ `0x006c4150` (called from `hkVehicleFramework_initFromDescriptor`).

| Item | Value |
|------|--------|
| Address | `0x006c3f90` … `0x006c4143` |
| Ghidra name | `hkVehicleFrictionSolver_circleProjection` |
| Size | 124 instructions, 7 basic blocks, **no callees** (leaf; uses `FSQRT` only) |
| Role | Final friction-circle / traction-ellipse projection for **one axle** after the solver’s coarse `1/mag²` pre-scale |
| Sole caller | `hkVehicleFrictionSolver_solve` @ call site `0x006c4e7c` (loop ×2, once per axle) |

**Verdict:** circle projection is a **separate function**, not inlined into `solve`.

---

## Tools used (this verification)

1. **`decompile_function`** `0x6c3f90` program=`autoassault.exe`
2. **`disassemble_function`** `0x6c3f90` (needed: decompiler dropped `ESI` / mis-typed signature)
3. **`get_xrefs_to`** / **`get_assembly_context`** call site `0x6c4e7c` in `solve`
4. **`disassemble_bytes`** tiny window `0x6c4e20`–`0x6c4eb0` (call-site register setup only)
5. **`decompile_function`** + **`disassemble_function`** `0x6c4150` (per-axle scale table)
6. **`read_memory`** `0xa0f2a0`, `0xa14000`, `0xa0d300`

Did **not** use broad `disassemble_bytes` as primary analysis; FPU body reconstructed from full-function disassembly of the leaf helper.

---

## Calling convention (assembly-authoritative)

Ghidra currently types this as `void circleProjection(void)` / a broken `__thiscall` with `unaff_ESI`. **Ignore that.** Live registers at the only call site:

```
// solve @ 0x6c4e52..0x6c4e7c  (axle index in EAX = 0 or 1)
ECX  = &stackWork[axle]          // LEA ECX, [ESP + axle*0xA0 + 0x50]
ESI  = &setup[axle] + 8          // LEA ESI, [setup + axle*0x64 + 8]
                                 //   setup = solve param_2 = framework+0x1fc
PUSH out + axle*0x1c             // out = solve param_4 = framework+0x2cc
CALL hkVehicleFrictionSolver_circleProjection   // 0x6c3f90
ADD  ESP, 4
```

| Slot | Meaning |
|------|---------|
| **ECX** | Per-axle **solver work block** on `solve`’s stack (stride **`0xA0`** bytes) |
| **ESI** | Per-axle **projection scale table** at `setup + axle*0x64 + 8` |
| **`[esp+4]`** (after `CALL`, `[esp+0x1c]` inside helper after `SUB ESP,0x18`) | Per-axle **output** slot `out + axle*7` floats (`0x1c` bytes) |

Pseudo-signature for ports:

```
// custom: ECX=work, ESI=scales, stack=outAxle
void circleProjection(WorkAxle *work, const float *scales, float *outAxle);
```

Before the call, `solve` also seeds one work field:

```
work[+0x84] = -(work[+0x70] * work[+0x68]);
```

After return, `solve` couples axles:

```
otherAxleRhs += coupleScale * work[+0x84];   // work[+0x84] is post-projection long component
```

and runs the helper **twice**, processing the axle with larger normalized `mag²` first, then the other (see solve Phase D).

---

## Constants (`read_memory`)

### Used inside `circleProjection`

| Symbol | Address | LE bytes | float32 | Role |
|--------|---------|----------|---------|------|
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **`1.0`** | Unit-circle test; residual `sqrt(mag²)−1`; `1/(r_cur−r_prev)` |

No other globals. No heap. No vcalls.

### Used when **building** the ESI table (`FUN_006c4150`)

| Symbol | Address | LE bytes | Value | Role |
|--------|---------|----------|-------|------|
| `DAT_00a14000` | `0x00a14000` | `00 00 80 3d` | **`0.0625`** (`1/16`) | Initial geometric parameter `x0 = product * 0.0625` |
| double `@0xa0d300` | `0x00a0d300` | `00 00 00 20 11 11 b1 3f` | **`≈ 0.06666667`** (`1/15`) | Exponent for ratio `(1/x0)^(1/15)` |
| `g_flOne` | `0x00a0f2a0` | … | `1.0` | Table end marker; `1 − pow(...)` |

---

## Work-block fields touched (ECX-relative)

All offsets in **bytes** from the per-axle work base.

| Off | Access | Role in this helper |
|-----|--------|---------------------|
| `+0x60` | R | Jacobian / metric component (with `+0x68`) for residual magnitude |
| `+0x68` | R | Pair of `+0x60` in `denom = w60² + w68²` |
| `+0x74` | R | Scale on **long** residual when forming `out[+8]` |
| `+0x7c` | R | Scale on **lat** residual when forming `out[+8]` |
| `+0x80` | R/W | Lateral impulse candidate (pre: free solve; post: projected) |
| `+0x84` | R/W | Longitudinal impulse candidate (pre: free/`-K` seed; post: projected) |
| `+0x88` | R | Lateral friction limit `Fmax_lat` (un-normalize) |
| `+0x8c` | R | Longitudinal friction limit `Fmax_long` (un-normalize) |
| `+0x90` | R | `invLim_lat` (`1/(Fmax+eps)` from Phase D) |
| `+0x94` | R | `invLim_long` |
| `+0x98` | W | Lateral residual `impLat_old − impLat_proj` (0 if inside circle) |

Output axle slot (`outAxle`):

| Off | Access | Role |
|-----|--------|------|
| `outAxle[+8]` | W | Scalar residual magnitude written **only on the outside-circle path** |

---

## Scale table at `ESI` (`setup + axle*0x64 + 8`)

Built once per vehicle by `FUN_006c4150` into `framework+0x1fc` (per-axle stride `0x64`).

Let `product` be the per-axle scalar written to `setup_axle[+0x58]` (effective-mass × wheel term from contact geometry — exact product formula is in `0x6c4150`, not re-derived here). Then:

```
x0    = product * 0.0625                    // = product / 16
ratio = (1 / x0) ^ (1/15)                   // = (16/product)^(1/15)
        // stored at setup_axle float[0x12]  ↔  ESI[0x10]

setup_axle[+0x4c] = x0                      // float[0x13]  ↔  ESI[0x11]
setup_axle[+0x48] = ratio                   // float[0x12]  ↔  ESI[0x10]
setup_axle[+0x44] = 1.0                     // float[0x11]  ↔  ESI[15] (last lat sample)

// 15 lat-table entries at setup_axle+0x08 .. +0x40  ↔  ESI[0]..ESI[14]
x = x0
for i in 0 .. 14:
    table[i] = 1.0 - (1.0 - x) ^ (1.0 / product)   // _CIpow, ST1^ST0
    x = x * ratio
// After 15 multiplies, x → 1 exactly (geometric identity).
```

**ESI layout used by the helper:**

| ESI index | Byte off from ESI | Content |
|-----------|-------------------|---------|
| `0 .. 14` | `0x00 .. 0x38` | `table[0..14]` (lat search samples) |
| `15` | `0x3c` | `1.0` (final lat sample) |
| `0x10` (16) | `0x40` | `ratio` (long geometric multiplier) |
| `0x11` (17) | `0x44` | `x0 = product/16` (initial long scale) |

Search path in **normalized** impulse space is **not** a pure radial scale of `(nLong, nLat)`:

```
sLong_i = nLong * x_i                 // x geometric: x0 → 1
sLat_i  = nLat  * table[i]            // table = nonlinear warp of x by product
```

That anisotropy is how Havok folds axle / wheel effective-mass coupling into the ellipse projection.

---

## Algorithm (instruction order)

### 0. Normalize free impulse into unit-limit space

```
nLong = work[+0x94] * work[+0x84]     // invLim_long * imp_long
nLat  = work[+0x90] * work[+0x80]     // invLim_lat  * imp_lat
mag2  = nLong² + nLat²
```

### 1. Early out — already inside the unit circle

```
if mag2 < 1.0:                        // FCOMP g_flOne; fallthrough when ST < 1
    work[+0x98] = 0
    return                            // does NOT write outAxle[+8]
```

### 2. Outside — geometric search (max 16 samples)

```
sLong = nLong * scales[0x11]          // * x0
sLat  = nLat  * scales[0]             // * table[0]
mag2  = sLong² + sLat²
prevLong = 0; prevLat = 0; prevMag2 = 0
iter = 0

// If first sample already outside (mag2 > 1), skip loop; prev stays 0
// → lerp against origin = pure radial project of first sample (see §3).

while mag2 <= 1.0 and ++iter < 16:
    prevLong = sLong
    prevLat  = sLat
    prevMag2 = mag2
    sLong = sLong * scales[0x10]      // *= ratio
    sLat  = nLat  * scales[iter]      // table[iter] or 1.0 at iter==15
    mag2  = sLong² + sLat²
// Exit: first sample with mag2 > 1, or iter cap 16
```

### 3. Bracket interpolation onto the unit circle

Residuals use **`sqrt(mag²) − 1`**, then linear weights that sum to 1:

```
r_cur  = sqrt(mag2)     - 1.0         // > 0 when outside
r_prev = sqrt(prevMag2) - 1.0         // < 0 when prev was inside; −1 if prev was 0

t      = 1.0 / (r_cur - r_prev)
w_cur  = -t * r_prev                  // weight on current (outside) sample
w_prev =  t * r_cur                   // weight on previous (inside) sample
// w_cur + w_prev == 1

// Interpolate in scaled space, then un-normalize by Fmax:
impLong' = (w_cur * sLong + w_prev * prevLong) * work[+0x8c]
impLat'  = (w_cur * sLat  + w_prev * prevLat ) * work[+0x88]
```

Special case `prevMag2 == 0` (first sample already outside):

```
r_prev = -1
w_cur  = 1 / sqrt(mag2)
imp'   = (s / sqrt(mag2)) * Fmax      // standard radial clamp of the first sample
```

### 4. Write projected impulses + residual channels

```
oldLong = work[+0x84]
oldLat  = work[+0x80]

work[+0x84] = impLong'
work[+0x80] = impLat'
work[+0x98] = oldLat - impLat'        // lateral correction delta

dLong = oldLong - impLong'
dLat  = work[+0x98]

a = work[+0x7c] * dLat  * work[+0x88]
b = work[+0x74] * dLong * work[+0x8c]
denom = work[+0x68]² + work[+0x60]²

outAxle[+8] = sqrt( (a² + b²) * (1.0 / denom) )
```

---

## How `solve` uses this (caller contract)

Phase D of `0x6c4450` (see `fn_006c4450_frictionSolve.md`) already:

1. Builds free impulses from the coupled 2×2 `Minv2 · rhs`.
2. Forms per-axle `mag² = (imp·invLim)²` sums.
3. Optionally zeros some channels when `|local_f0|` vs `cb+0xa0` trips.
4. If **either** axle `mag² > 1`:
   - Pre-scales selected `out[]` channels by **`1/mag²`** (not `1/mag` — map `0.3` was wrong).
   - Calls **`circleProjection` twice** (dominant axle first, then the other).
   - Feeds `work[+0x84]` back into the **other** axle’s RHS (`couple * projected long`).

If **both** inside: zeros static-friction outputs; **does not** call this helper.

So the full clamp is:

```
coarse:   scale selected channels by 1/mag²          // in solve
fine:     circleProjection per axle                  // this function
          — geometric search + sqrt-residual lerp
          — anisotropic via product-warped lat table
```

Bit-exact ports need **both** stages plus the `FUN_006c4150` table.

---

## Reconstruction-ready summary

```
// Normalized free impulse
n = (imp_long * invLim_long, imp_lat * invLim_lat)
if |n| < 1:
    residualLat = 0; return

// Grow along Havok search path until outside unit disk (≤16 samples)
//   s_i = (n_long * x_i,  n_lat * table(x_i, product))
// Bracket [inside, outside], lerp with weights from (sqrt(m)-1) residuals
// Un-normalize: imp' = s_proj * Fmax
// out[+8] = |metric · Δimp|
```

| Value | Source | Use |
|------:|--------|-----|
| `1.0` | `0xa0f2a0` | unit circle / residual base |
| `0.0625` | `0xa14000` | table `x0 = product/16` |
| `1/15` | `0xa0d300` (double) | table geometric ratio exponent |
| `16` | immediate | max search iterations (`CMP EDX,0x10`) |
| `15` | immediate | table fill loop in `0x6c4150` |

---

## Open items (do not invent)

1. **`product` closed form** — `FUN_006c4150` sets `product = local_5c[axle] * setup[+0x50]` after an inertia/contact walk; exact semantic name (Havok `wheelToChassisEffectiveMass` etc.) not required for implementing the **helper**, but is required when building `fw+0x1fc` from vehicle data.
2. **Work `+0x60/+0x68/+0x74/+0x7c` identity** — residual magnitude metric; filled earlier in `solve` Phase A/D. Treat as “jacobian metric already in work” until a full work-block map is written.
3. **`outAxle[+8]` consumer** — written here; confirm which post-solve / debug path reads it (not used for the `1/mag²` pre-scale, which runs *before* the call).

None of these change the helper’s **shape**: separate leaf @ `0x6c3f90`, unit test in invLim space, 16-step anisotropic geometric search, `sqrt(mag)−1` bracket lerp, Fmax un-normalize.

---

## Diff vs prior map notes

| Source | Claim | This verify |
|--------|-------|-------------|
| `0.3-friction-solver.md` open item 3 | `circleProjection` @ `0x6c????`, pass `out+axle*7` only | **Address `0x6c3f90`**; real args = **ECX work + ESI table + stack out** |
| `0.3` Phase D | “scale by `1/mag` then project” | Caller scales by **`1/mag²`**; helper does **search+lerp**, not `imp *= 1/|n|` alone |
| `fn_006c4450_frictionSolve.md` open item 1 | helper not expanded | **Resolved** by this file |
| Ghidra decompile of helper | `unaff_ESI`, bogus `__thiscall` | **Assembly wins** — ESI is live-in scale table |

Binary wins. Port the assembly algorithm above; do not trust the raw Ghidra prototype for this leaf.
