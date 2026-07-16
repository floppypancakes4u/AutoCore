# Verified: `hkVehicleFrictionSolver_solve` @ `0x006c4450`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Reconciled against map evidence `docs/reconstruction/physics/0.3-friction-solver.md`.

| Item | Value |
|------|--------|
| Address | `0x006c4450` … `0x006c500d` |
| Ghidra name | `hkVehicleFrictionSolver_solve` |
| Role | 2-axle coupled friction constraint solve: effective mass, warm-start apply, drive/slip impulse, μ(slip)·N·dt friction-circle clamp |
| Callee | `hkVehicleFrictionSolver_circleProjection` @ `0x006c3f90` |
| Caller | `hkVehicleFramework_postTickApplyForces` @ `0x0064bc70` |

## Tools used (this verification)

1. **`decompile_function`** `0x6c4450` program=`autoassault.exe` (full body)
2. **`read_memory`** `0xa0d2f4`, `0xa0f298`, `0xa0f704`, `0xa0f2a0` length=4
3. **`get_function_by_address`** / **`get_function_callees`** (bounds + `circleProjection`)
4. Cross-check caller decompile `0x64bc70` for **drive pack** aggregation only (not re-implemented here)

Did **not** use `disassemble_bytes`. Emulation skipped (pointer-heavy multi-body graph).

---

## Constants (`read_memory`, length 4)

| Symbol | Address | LE bytes | u32 | float32 | Role in this function |
|--------|---------|----------|-----|---------|------------------------|
| `_DAT_00a0d2f4` | `0x00a0d2f4` | `00 00 00 34` | `0x34000000` | **`1.1920929e-7`** (`2^-23`) | Denominator epsilon: `Keff += eps`; `1/(Fmax + eps)` |
| `DAT_00a0f298` | `0x00a0f298` | `00 00 00 3f` | `0x3f000000` | **`0.5`** | Drive/slip blend: twice in `driveTarget = … · 0.5 · 0.5` |
| `DAT_00a0f704` | `0x00a0f704` | `00 00 80 3e` | `0x3e800000` | **`0.25`** | Weight on prior residual in **second-row** relative velocity |
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | `0x3f800000` | **`1.0`** | Reciprocals `1/Keff`, circle test `mag > 1`, scale `1/mag` |

Also used (named globals; `g_flZero` re-read this pass @ `0x00a0f518` = `0.0`):

- `g_flZero` — comparisons, zeroing disabled-drive / inside-circle outputs

**Loop counter note:** decompiler shows `local_184 = 2.8026e-45`. That is **integer `2`** bit-cast to float (`0x00000002`), then decremented as int each axle iteration — **not** the epsilon at `0xa0d2f4`.

---

## Signature / buffers

```
void hkVehicleFrictionSolver_solve(
    float *dt2,     // param_1: dt2[0] = dt2[1] = substep dt
    AxleSetup *setup, // param_2: framework+0x1fc, per-axle stride 0x64 (0x19 floats)
    ChassisBody *cb,  // param_3: solver body + per-axle jacobian/friction pack (from postTick stack)
    float *out        // param_4: framework+0x2cc, per-axle stride 0x1c (7 floats)
)
```

Hard-coded **2 axles** (both major loops run exactly twice).

### Important strides

| Region | Base | Stride | Notes |
|--------|------|--------|-------|
| Axle jacobian block | `cb+0x20` | `0x50` (0x14 floats) | `jLin`, contact pos, body-B ptr, … |
| Axle friction cursor | `cb+0x48` | `0x50` | μ / drive-scale / gate fields (see Drive pack) |
| Setup softness | `setup+0x54` | `0x64` (0x19 floats) | Regularization added to row-0 `Keff` |
| Output | `out+0` | `0x1c` (7 floats) | Impulse + status writeback |

### Chassis body fields used

| Offset | Use |
|--------|-----|
| `cb+0xbc` (char) | Chassis “fixed / infinite mass” — skip `Iinv` transform when set |
| `cb+0xf0..0xfc` | Chassis COM (contact arm `rA = pContact − COM`) |
| `cb+0xe0..0xec` | Chassis linear inv-mass diagonal (4 floats; w used with eps) |
| `cb+0x100..0x128` | Chassis world inv-inertia 3×3 (applied to `r×n` when not fixed) |
| `cb+0xc0..0xcc` | Accumulated **linear** impulse (read/write) |
| `cb+0xd0..0xdc` | Accumulated **angular** impulse (read/write) |
| `cb+0x140..0x158` | Chassis linear/angular velocity channels used in `J·v` |
| `cb+0x30`, `cb+0x80` | Body-B pointers per axle (same-pointer ⇒ extra coupling terms) |
| `cb+0x44`, `cb+0x94` | **Drive packs** axle0 / axle1 (filled by postTick) |
| `cb+0xa0` | Lateral stability cutoff threshold vs `|local_f0|` |

Body B (ground / other RB) mirrors: inv-mass `@+0x30..0x3c`, inv-inertia `@+0x50..0x7c`, pos `@+0x40`, vel `@+0x10/+0x20`, fixed-flag `@+0xc`.

---

## Drive pack (caller → solver)

Filled in `postTickApplyForces` **before** this function runs (verified via caller plate + decompile):

```
invN = 1 / axleWheelCount[ax]

drivePack[ax] += wheels+0x28[i] * wheel[i]+0x88 * invN
// wheels+0x28[i] = calcWheelTorque output, clamped [0, 1000] upstream
// wheel+0x88     = per-wheel drive scale
```

Closed form:

```
drivePack[ax] = (1/N_ax) * Σ_{i on axle} ( torque_i · wheelScale_i )
```

In the solver body these live at **`cb+0x44` (ax0)** and **`cb+0x94` (ax1)** (stride `0x50`).

Phase D **header** mixes them with setup gains:

```
d0 = cb[0x44]
d1 = cb[0x94]
sumD = d0 + d1

// setup offsets are byte offsets into param_2 (framework+0x1fc)
mix0 = d0 * setup[0x64] + sumD * setup[0x68]   // → local_e8[1]
mix1 = d1 * setup[0xc8] + sumD * setup[0xcc]   // → local_44
```

Per-axle slip branch also consumes the **output slot** `out[ax*7 + 6]` (see Phase D): after Phase A that slot holds accumulated row-0 `J·v`; the ×0.5×0.5 blend treats it as the drive/slip source term.

---

## Four phases (decompile order)

All axle loops: **`for ax in {0,1}`**.

---

### Phase A — Effective mass + free `J·v` (loop ×2)

For each axle, two constraint rows are built from the jacobian block at `cb+0x20 + ax*0x50`:

**Row 0** uses `jLin = J[0..3]`; **Row 1** uses `jLin = J[-4..-1]` (second direction — lateral / side).

#### A1. Contact arms

```
p  = contact position from jacobian block (J[-8..-5])
rA = p − chassisCOM          // cb+0xf0..
rB = p − bodyB.pos           // bodyB+0x40..
```

#### A2. Angular jacobians

```
jAngA_raw = rA × jLin          // standard cross
jAngB_raw = jLin × rB          // opposite cross order on B (reaction)

if chassis not fixed (cb+0xbc == 0):
    jAngA = Iinv_A · jAngA_raw   // 3×3 at cb+0x100..
else:
    jAngA = jAngA_raw             // infinite-mass path keeps raw cross

if bodyB not fixed (bodyB+0xc == 0):
    jAngB = Iinv_B · jAngB_raw
else:
    jAngB = jAngB_raw
```

#### A3. Scalar effective mass (diagonal metric)

```
eps = 1.1920929e-7               // _DAT_00a0d2f4

// linear inv-mass diagonals MinvA.xyz, MinvB.xyz; .w channels added with eps
Keff = MinvA.w + MinvB.w + eps
     + Σ_{k∈x,y,z} ( jAngA[k]² · MinvA[k] + jAngB[k]² · MinvB[k] )

invKeff     = 1 / Keff
Keff_reg    = Keff + setup_softness[ax]    // * (setup+0x54 + ax*0x19)
invKeff_reg = 1 / Keff_reg
```

Row 1 repeats the same form → `Keff_lat`, `invKeff_lat` (no extra setup softness on the lat row in this decompile).

Workspace also caches `jLin`, `jAngA`, `jAngB` for later apply (stride `0x28` floats of stack state per axle).

#### A4. Free relative velocity accumulation into `out`

Using chassis vel at `cb+0x140..` minus bodyB vel, dotted with both rows’ jacobians:

```
out[ax*7 + 5] += J_row1 · v_rel     // side / second row
out[ax*7 + 6] += J_row0 · v_rel     // forward / first row
```

(Exact `out` index pairing follows `param_4+6` cursor with `±1` writes; treat as **per-axle pair of slip-velocity accumulators**.)

#### A5. Cross-axle / cross-row coupling scalar (after loop)

If both axles share the same body-B pointer (`cb+0x30 == cb+0x80`), extra products of the two axles’ jacobians with inv-mass are included; else chassis-only terms:

```
b = Σ (jAng_ax0 · Minv · jAng_ax1) + …   // → coupling “local_180”
```

---

### Phase B — Coupled 2×2 inverse (between the two primary rows)

```
a = Keff_primary_0      // decompile: local_3c
d = Keff_primary_1      // decompile: local_e8[3]
b = coupling            // decompile: local_180  (may be 0 if axles decoupled)

det = a*d − b*b
if (det*det >= 0):      // NaN/denormal guard; always true for real det
    inv = 1 / det
    // Minv2 stored as:
    M00 = a * inv       // local_160
    M01 = −b * inv      // local_15c
    M10 = M01           // local_158
    M11 = d * inv       // local_154
```

**Implementable solve** (used in Phase D after slip recompute):

```
imp0 = −( rhs0 * M00 + rhs1 * M01 )
imp1 = −( rhs0 * M10 + rhs1 * M11 )
```

> **Binding note:** decompiler stack names make it ambiguous whether `(a,d)` are long/lat of one contact or the two axles’ primary rows. The **algebra** (symmetric 2×2, `det = ad−b²`, impulses `−Minv·rhs`) is solid. Live single-axle drive tests should confirm which RHS component lands in `out[3]` vs `out[10]`.

**Note on inverse form:** the decompile multiplies `a/det` and `d/det` on the diagonal (not the textbook `d/det`, `a/det`). Port **exactly this form** with the same `a,d` sources the binary uses; do not “fix” the matrix inverse independently.

---

### Phase C — Warm-start / prior impulse apply (loop ×2)

For each axle, if `out` prior impulse scalar `λ_prev != 0`:

```
sA = λ_prev * MinvA.w     // scales linear apply on chassis
sB = λ_prev * MinvB.w     // scales linear apply on body B

chassis.linImpulse  += sA * jLin
bodyB.linImpulse    −= sB * jLin
chassis.angImpulse  += λ_prev * MinvA.xyz * jAngA   // per-component
bodyB.angImpulse    += λ_prev * MinvB.xyz * jAngB
```

Signs: **action on chassis (+lin along jLin)**, **reaction on B (−lin)**. Angular B uses `+` with the B angular jacobian (already opposite-handed from Phase A).

---

### Phase D — Drive/slip, friction limit, friction circle (loop ×2 + joint clamp)

#### D0. Drive-pack header mix

See **Drive pack** above (`mix0` / `mix1`). Also zeroes `out[2]`, `out[9]` before the axle loop.

#### D1. Recompute relative velocity with current impulses

```
// linear relative velocity at contact from impulse accumulators
vLinRel = chassis.linImpulse.xyz − bodyB.linImpulse.xyz
// (+ angular channels from cb+0xd0 and bodyB+0x20)

Jv0 = jAngA0·ωA + jAngB0·ωB + jLin0·vLinRel     // first row (forward)

// second row adds residual weight:
// f_prior = out-slot residual (pfVar6[-1] / prior accumulator)
Jv1 = f_prior * 0.25 + jAngA1·ωA + jAngB1·ωB + jLin1·vLinRel
//                         ^^^^ DAT_00a0f704
```

#### D2. Longitudinal / drive impulse branch

Gate: `driveOrContactGate = *(char*)&frictionCursor[1]` (decompile: `*(char*)(pfVar8+1)`).

```
Nload = |workspace.normalLoad|     // ABS(pfVar9[0xb]) — axle normal/susp load
dt    = dt2[0]

if gate == 0:
    out_driveSlot = 0
    lambda = carriedPrior            // pfVar6[-6]
else:
    // driveSlot = out[ax*7+6] after Phase A (Jv accum / drive source)
    driveTarget =
        invKeff_reg * invKeff_lat
        * (driveSlot * 0.5 + Jv0) * 0.5
    // both 0.5 factors are DAT_00a0f298

    driveMax = muScale * invKeff_lat * Nload * dt
    // muScale = frictionCursor[-4]  (same slot family as μ0; see open items)

    driveClamped = clamp(driveTarget, −driveMax, +driveMax)
    // decompile clamp: if |driveTarget| > driveMax → sign(driveTarget)*driveMax

    lambda = −Jv0 − driveClamped
```

#### D3. Slip-dependent friction limit (per axle)

```
mu0   = frictionCursor[-4]
slope = frictionCursor[-3]
muMax = frictionCursor[-2]

mu = mu0
if slope != 0:
    slip = sqrt(lambda² + Jv1²)      // combined long residual + lat Jv
    mu   = mu0 + slip * slope
    mu   = min(mu, muMax)
    mu   = max(mu, 0)

Fmax = mu * Nload * dt               // friction impulse budget this substep
out_limit[ax]     = Fmax             // stored twice (pair of channels)
out_invLimit[ax]  = 1 / (Fmax + eps) // eps = 1.1920929e-7
```

Also writes a blended impulse preview:

```
preview = gateTerm * dt + invK_lat * lambda
```

(`gateTerm` is 0 when drive branch taken, else `*frictionCursor`.)

#### D4. Coupled impulses + friction-circle test

```
// RHS from the two primary slip channels after D1/D2
rhs0 = axle0_sideOrPrimary_Jv     // local_e8[0]
rhs1 = axle1_sideOrPrimary_Jv     // local_48

imp0 = −(rhs0 * M00 + rhs1 * M01)  // local_cc[0]
imp1 = −(rhs0 * M10 + rhs1 * M11)  // local_2c

// ellipse / circle magnitudes using inv-limits (local_bc, local_d0, …):
mag0² = (imp0 * invLim0_a)² + (imp_aux0 * invLim0_b)²
mag1² = (imp1 * invLim1_a)² + (imp_aux1 * invLim1_b)²
```

**Stability cutoff** (before circle):

```
if |local_f0| >= cb+0xa0:
    out[5] = out[6] = out[12] = out[13] = 0
```

**Friction circle / traction ellipse clamp:**

```
if mag0² > 1 OR mag1² > 1:
    if mag0² > 1:
        s0 = 1 / mag0²
        out[5] *= s0
        out[6] *= s0
    if mag1² > 1:
        s1 = 1 / mag1²
        out[12] *= s1
        out[13] *= s1

    // then twice, alternating which axle is “dominant”:
    for k in 0..1:
        hkVehicleFrictionSolver_circleProjection(out + axleSel*7)
        // updates residual RHS with coupling * projected state
else:
    // inside circle: zero static-friction outputs
    out[0] = out[1] = out[7] = 0
    rhs1 = 0
```

> Scale uses **`1/mag²`** on the pre-projection channels (decompile: `g_flOne / fVar3` where `fVar3` is already a sum of squares). Final renormalization is inside `circleProjection` (`0x6c3f90`) — decompile that helper before claiming bit-exact ellipse projection.

#### D5. Write scaled impulses + final body apply (loop ×2)

```
out[3]  = imp0 * dt2[1]     // primary impulse × dt  (ax0 slot family)
out[10] = imp1 * dt2[1]     // primary impulse × dt  (ax1 slot family)
// plus companion channels out[4], out[8], out[11], …
```

Final apply to chassis / body B is the same pattern as Phase C, using the **clamped** friction impulse scalars and the cached jacobians.

---

## Reconstruction-ready equations (per substep)

```
// --- inputs (from postTick) ---
drivePack[ax] = mean_i (torque_i * wheelScale_i)
// jacobians, Minv, Iinv, contact loads N[ax], μ table, setup softness

// --- Phase A ---
for ax in 0..1:
  for row in {fwd, side}:
    jAngA = maybe_IinvA(rA × jLin)
    jAngB = maybe_IinvB(jLin × rB)
    Keff[ax,row] = jAngAᵀ diag(MinvA) jAngA
                 + jAngBᵀ diag(MinvB) jAngB
                 + MinvA.w + MinvB.w + 2^-23
    invK[ax,row] = 1 / Keff[ax,row]
  Kreg[ax] = Keff[ax,fwd] + softness[ax]
  invKreg[ax] = 1 / Kreg[ax]
  Jv_free[ax] += J · v_rel

// --- Phase B ---
det = a*d - b*b
Minv2 = [[a, -b], [-b, d]] / det     // as decompiled (see note)

// --- Phase C ---
apply λ_prev through J to chassis (+) and bodyB (−lin)

// --- Phase D ---
for ax in 0..1:
  Jv0 = J_fwd · v_imp
  Jv1 = 0.25 * residual + J_side · v_imp

  if gate:
    u = invKreg * invK_side * (driveSlot*0.5 + Jv0) * 0.5
    u = clamp(u, ± muScale * invK_side * |N| * dt)
    λ = -Jv0 - u
  else:
    λ = λ_carried

  mu = mu0
  if slope != 0:
    mu = clamp(mu0 + sqrt(λ² + Jv1²)*slope, 0, muMax)
  Fmax = mu * |N| * dt
  invLim = 1 / (Fmax + 2^-23)

(imp0, imp1) = -Minv2 · (rhs0, rhs1)
if outside unit circle in invLim-metric:
  scale channels by 1/mag²; circleProjection(out+ax*7)
apply clamped impulses; out_impulse = imp * dt
```

### Constants to hard-code in a port

| Value | Source | Use |
|------:|--------|-----|
| `1.1920929e-7` | `0xa0d2f4` | `Keff` and `1/Fmax` eps |
| `0.5` | `0xa0f298` | drive blend (×2) |
| `0.25` | `0xa0f704` | second-row residual weight |
| `1.0` | `0xa0f2a0` | reciprocals / circle unit test |
| `2` | immediate | axle count (both loops) |

---

## Output → wheel writeback (caller)

After return, `postTickApplyForces` maps each wheel’s axle slot:

```
OUT = framework+0x2cc + axleIndex[i] * 0x1c
// wheel longitudinal / lateral impulse fields at wheel+0x94 / +0xa0 etc.
// (exact float index → wheel field mapping: see 0.3-friction-solver.md §2c;
//  this function’s definitive writes are out[3], out[10] = imp*dt)
```

---

## Open items (do not invent)

1. **`circleProjection` @ `0x6c3f90`** — final ellipse renormalization not expanded here; required for bit-exact clamp when `mag² > 1`.
2. **2×2 row binding** — confirm with a one-axle-driving debugger capture which RHS is long vs lat / ax0 vs ax1.
3. **`frictionCursor[-4]` dual use** — appears as both `mu0` and `driveMax` scale; confirm against postTick field at axle `+0x38` vs μ table (`wheels+0x34/+0x40`) with a live dump.
4. **Gate byte** — decompile reads `*(char*)(pfVar8+1)`; confirm whether this is contact, drive-enable, or low byte of a float param.
5. **Softness / setup `+0x64..+0xcc` gains** — populated by friction-setup builder (`fw+0x1fc`); needed for exact drive mix0/mix1.

None of these change the **shape**: 2-axle solve, `Keff` from `J Minv Jᵀ` + eps, drive blend with **0.5×0.5**, μ(slip)·N·dt limit, circle clamp with **0.25** residual weight on the second row.

---

## Diff vs map `0.3-friction-solver.md`

| Topic | Map | This re-verify |
|-------|-----|----------------|
| Epsilon `0xa0d2f4` | `2^-23` | **Confirmed** LE `00 00 00 34` |
| Drive blend `0xa0f298` | 0.5 | **Confirmed** |
| Residual weight `0xa0f704` | 0.25 on lateral Jv | **Confirmed** (`Jv1 = 0.25*prior + …`) |
| `g_flOne` `0xa0f2a0` | 1.0 | **Confirmed** |
| Circle scale | `1/mag` | Decompile scales by **`1/mag²`** then calls `circleProjection` |
| Minv2 diagonal | textbook `[[d,-b],[-b,a]]/det` | Decompile stores **`[[a,-b],[-b,d]]/det`** — port as-is with same `a,d` |
| Drive pack location | AX−0x14 narrative | **`cb+0x44` / `cb+0x94`** + setup mix; slip blend uses `out` slot |

Binary wins; update C# against this file + `circleProjection` when implementing.
