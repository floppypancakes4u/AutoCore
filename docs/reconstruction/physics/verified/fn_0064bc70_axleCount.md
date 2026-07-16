# Verified: `axleCount` path in `hkVehicleFramework_postTickApplyForces` @ `0x0064bc70`

**Program:** `autoassault.exe` (image base `0x00400000`)  
**Ghidra:** project AA-decode · `decompile_function` re-pull 2026-07-15  
**Symbol:** `hkVehicleFramework_postTickApplyForces`  
**Scope:** confirm **`axleCount = *(int *)(wheels + 0x64)`** and the **axleCount = 2** retail loop shape  
**Sibling:** full pipeline in `fn_0064bc70_postTick.md` (suspension, drivePack, solve, writeback)

---

## 1. Object graph (axle-related only)

```text
fw      = this                                      // hkVehicleFramework*
wheels  = *(fw + 0x0c)                              // hkDefaultWheels / wheels collection
```

| Symbol | Expression | Type | Role |
|--------|------------|------|------|
| `wheelCount` | `*(int *)(wheels + 0x0c)` | int | per-wheel loop bound |
| **`axleCount`** | **`*(int *)(wheels + 0x64)`** | **int** | **per-axle loop bound** |
| `axleOf[i]` | `*(*(int *)(wheels + 0x58) + i*4)` | int | wheel → axle index |
| `axleWheelCount[ax]` | `*(*(int *)(wheels + 0x68) + ax*4)` | int | `N_ax` for `invN = 1/N_ax` |
| `WHEEL_i` | `*(int *)(wheels + 0x80) + i*0xC0` | — | wheel struct (stride `0xC0`) |

**Decompiler note:** Ghidra often prints `wheels + 100` instead of `wheels + 0x64` (`100 == 0x64`). Same field.

**Pointer rule:** `wheels+0x58` and `wheels+0x68` are **heap pointers** to int tables.  
`wheels+0x64` is an **inline int** (axle count), not a pointer.

---

## 2. Confirmed: `axleCount` lives at `wheels+0x64`

Fresh decompile loads the wheels pointer early:

```text
wheels = *(fw + 0x0c)     // decomp: fVar24 / afStack_410[0] after spill
```

Every **per-axle** control loop in this function bounds on:

```text
*(int *)(wheels + 0x64)     // decomp form: *(int *)((int)fVar24 + 100)
```

There is **no** other axle-count source in this function (not framework, not transmission, not a hard-coded constant for those later loops).

---

## 3. Three axle-related control structures

### 3a. Axle **pack zero** — counter hardcoded **2**

Before the wheel loop, stack axle packs are cleared with a countdown of **2** (not read from `wheels+0x64`):

```text
iVar17 = 2;
do {
    // zero ~0x14 floats + in-contact byte  (pack stride 0x50 bytes / 0x14 dwords)
    // decomp pointer step: pfVar13 += 0x14
    iVar17 = iVar17 - 1;
} while (iVar17 != 0);
```

| Fact | Value |
|------|-------|
| Loop count | **literal 2** |
| Pack stride | **`0x50`** bytes (`0x14` floats in decomp pointer arithmetic) |
| Stack bases (decomp) | `afStack_2cc` (jac rows) + `acStack_280` (drivePack / μ / contact flag) |

Retail vehicles use **2 axles**, so this matches `axleCount == 2`. The zero pass does **not** re-read `wheels+0x64`; it assumes the classic 2-axle stack layout.

### 3b. Per-wheel aggregation — index via `axleOf[i]`, average via `axleWheelCount`

```text
for i = 0 .. wheelCount-1:                    // bound: wheels+0x0c
    ax   = *(*(wheels + 0x58) + i*4)          // axle index for this wheel
    invN = g_flOne / (float)*(*(wheels + 0x68) + ax*4)

    // pack slot = ax * 0x50  (same stride as zero pass)
    drivePack[ax] += wheels+0x28[i] * WHEEL[+0x88] * invN
    // + averaged ang jac, lat frame, μ rows; summed susp; contact OR
```

This path does **not** loop `axleCount` times. It visits every wheel and **buckets** into axle packs `0 .. axleCount-1` (retail: `ax ∈ {0,1}`).

### 3c. Per-axle post-process — bound is **`wheels+0x64`**

After the wheel loop, decompile (decimal `+100` = hex `+0x64`):

```text
iStack_430 = 0;
if (0 < *(int *)(wheels + 0x64)) {
    do {
        // 1) re-normalize aggregated lat/long direction rows for this axle
        // 2) fill contact-body solver record for axle's tracked contact RB
        //    (or zero fixed/missing body pack)
        iStack_430 = iStack_430 + 1;
        // pack pointer advances by 0x14 floats / 0x50 bytes
        // contact-body workspace advances by 0x80
    } while (iStack_430 < *(int *)(wheels + 0x64));
}
```

**This is the primary runtime use of `axleCount`.**

### 3d. Post-solve contact RB writeback — again **`wheels+0x64`**

After `hkVehicleFrictionSolver_solve` and chassis velocity writeback:

```text
i = 0;
if (0 < *(int *)(wheels + 0x64)) {
    do {
        // if contact body for axle i is live + movable: vtbl+0x54 / +0x50 writeback
        i = i + 1;
    } while (i < *(int *)(wheels + 0x64));
}
```

Wheel **output** writeback (`WHEEL+0x94..+0xa0`) is a separate loop over **`wheelCount`** (`wheels+0x0c`), indexing the axle output row via `axleOf[i] * 0x1c` into `fw+0x2cc`.

---

## 4. Retail path: `axleCount == 2`

| Site | Bound | Retail value |
|------|-------|--------------|
| Pack zero | hardcoded | **2** |
| Axle normalize + contact-body fill | `wheels+0x64` | **2** |
| Post-solve contact RB writeback | `wheels+0x64` | **2** |
| Friction setup / output (consumer) | 2 axle rows at `fw+0x1fc` (stride `0x64`) / `fw+0x2cc` (stride `0x1c`) | **2** |
| Wheel loop | `wheels+0x0c` | typically **4** (2+2) |
| `axleOf[i]` | `wheels+0x58[i]` | **0** front, **1** rear |
| `axleWheelCount[ax]` | `wheels+0x68[ax]` | typically **2** per axle |

### Why “axleCount = 2 path”

1. Stack packs are dimensioned and zeroed for **exactly two** axles.
2. Runtime axle loops read **`*(wheels+0x64)`**, which for AA car/truck builds is **2** (front + rear).
3. Drive aggregation formula for a 4-wheel / 2-axle vehicle:

```text
drivePack[ax] = (1 / N_ax) * Σ_{i: axleOf[i]==ax}  ( driveTorque[i] * wheelScale88[i] )
// N_ax = axleWheelCount[ax]  (== 2 for a normal dual-wheel axle)
```

4. Solver is invoked once with the full multi-axle setup blob; phase maps document **2** constraint points (see `0.3-friction-solver.md`).

**Port implication:** implement **2-axle** aggregation + solve for retail NPC/player cars. Do not invent a free-N axle stack without also matching pack-zero / setup table sizes. If a vehicle ever had `wheels+0x64 != 2`, pack zero and solver layout would need separate verification (binary zero pass still hardcodes 2).

---

## 5. Offset cheat-sheet (axle fields only)

### Wheels collection

| Off | Kind | Field |
|----:|------|-------|
| `+0x0c` | int | `wheelCount` |
| `+0x28` | ptr→f32[] | drive torque from `calcWheelTorque` |
| `+0x58` | ptr→i32[] | `axleOf[i]` |
| **`+0x64`** | **int** | **`axleCount`** |
| `+0x68` | ptr→i32[] | `axleWheelCount[ax]` |
| `+0x80` | ptr | wheel struct base (`0xC0` stride) |

### Axle pack (stack, stride `0x50`)

| Relative | Role |
|----------|------|
| jac / lat rows | `afStack_2cc[ax*0x14 + …]` |
| `drivePack` | `acStack_280 + ax*0x50 − 0x14` ← Σ torque·scale·invN |
| μ / susp / extra | −0x10 / −0x0c / −0x08 / −0x04 |
| `inContact` | `acStack_280[ax*0x50]` (byte) |

### Framework (solver IO)

| Off | Stride | Role |
|----:|-------:|------|
| `fw+0x1fc` | `0x64` / axle | friction **setup** → solve arg2 |
| `fw+0x2cc` | `0x1c` / axle | friction **output** → solve arg4 / wheel writeback |

---

## 6. Reconciliation

| Claim | Verdict |
|-------|---------|
| `axleCount = *(int *)(wheels + 0x64)` | **Confirmed** (decomp `+100` ≡ `+0x64`) |
| Pack zero loops **2** times | **Confirmed** (`iVar17 = 2` countdown) |
| Post-wheel axle normalize uses `wheels+0x64` | **Confirmed** |
| Post-solve contact writeback uses `wheels+0x64` | **Confirmed** |
| Wheel loop uses `wheels+0x0c`, not axleCount | **Confirmed** |
| Phase-0 / `fn_0064bc70_postTick.md` “typically 2” | **Match** for retail |

**Binary wins:** decompiler decimal `+100` must be documented as hex **`+0x64`** so it is not confused with wheel-struct field `WHEEL+0x64` (lat-basis component on the `0xC0` wheel blob).

---

## 7. Verification provenance

| Step | Result |
|------|--------|
| `decompile_function` @ `0x64bc70`, program `autoassault.exe` | Full pseudocode; plate WI-MOV-004; axle loops at `*(wheels+100)` / pack zero `iVar17=2` |
| Cross-check | `fn_0064bc70_postTick.md` §1, §4, §7; `0.3-friction-solver.md` Part 2; `fn_offsets_wheel.md` container table |

**Emulation:** not run — axleCount is a runtime field on the wheels object; confirmation is structural (loads + loop bounds), not a pure float formula.

**Not in scope here:** full suspension impulse math, friction solver internals, C# port (see sibling postTick doc + Phase 0 maps).
