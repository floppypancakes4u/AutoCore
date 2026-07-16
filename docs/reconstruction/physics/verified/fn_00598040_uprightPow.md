# VERIFIED — upright `_CIpow` falloff in `VehicleAction_calcWheelTorque` @ `0x598040`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Scope:** only the upright traction scale (`upright`) that multiplies engine curve / µ  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x598040`; `get_function_pcode` (high); `get_assembly_context` @ call `0x598202`;  
`get_xrefs_to` @ `0x009d54e8` / `_CIpow` `0x006a3e2c`; `read_memory` @ gate / exp / one / worldUp  
**Not used:** `disassemble_bytes` (per project RE rules)

**Related full-function note:** `fn_00598040_calcWheelTorque.md`  
**Prior GAP (now closed):** `engine-torque-spec.md` §3 flagged bare `_CIpow()` with opaque x87 base/exp.

---

## 1. Result (bit-exact formula)

```
dotAbs  = | bodyUp · worldUp |          // worldUp = (0, 1, 0)
if (dotAbs < 0.8)                       // DAT_00a0f698
    upright = (float) pow(dotAbs, 4.0)  // base = |dot| (f32), exp = 4.0 (f64 @ 0x9d54e8)
else
    upright = 1.0                       // DAT_00a0f2a0 / g_flOne
// then: torque path multiplies upright × t (curve) × µ' × handbrakeRear …
```

| Operand | Source | Type | Value |
|---------|--------|------|------:|
| **base** | `|dot(bodyUp, worldUp)|` after `FABS` / P-code `FLOAT_ABS` | float32 (runtime) | typically ∈ [0, 1] for unit axes |
| **exp** | `*(double*)0x009d54e8` | float64 constant | **4.0** |
| **gate** | `*(float*)0x00a0f698` | float32 | **0.8** |
| **pass value** | `*(float*)0x00a0f2a0` | float32 | **1.0** |

**No UNKNOWN** for base/exp/gate on this path.

---

## 2. Why decompile alone is insufficient

Decompiler plate emits:

```c
fVar9 = g_flOne;
if (ABS(fStack_20 * DAT_00af3390 + fStack_1c * DAT_00af3394 + fStack_18 * DAT_00af3398)
    < DAT_00a0f698) {
    fVar8 = (float10)_CIpow();   // ← no visible args
    fVar9 = (float)fVar8;
}
// … later: torque = µ * fVar9 * local_40
```

MSVC CRT `_CIpow` takes **x87 stack** operands (`ST1` base, `ST0` exp), not stdcall stack args, so Ghidra’s C view collapses to `_CIpow()`. Recovery requires P-code control flow + the two `FLD`s immediately before the call (via assembly context / listing, not bulk `disassemble_bytes`).

---

## 3. Control flow & math (P-code, high)

Block `0x5981a8–0x5981ec` (after `FUN_004e8b60` fills body-up on stack):

| Seq | Op | Meaning |
|-----|----|---------|
| `0x5981b7…d6` | `FLOAT_MULT` / `FLOAT_ADD` | `dot = bodyUp.x*wu.x + bodyUp.y*wu.y + bodyUp.z*wu.z` |
| `0x5981d8` | `FLOAT_ABS` | `dotAbs = \|dot\|` |
| `0x5981e8` | `FLOAT_LESS` vs `ram:a0f698` | `dotAbs < 0.8` |
| `0x5981ec` | `COPY ram:a0f2a0` + `CBRANCH → 0x5981f8` | default upright **1.0**; branch into pow if less |

Block `0x5981f8–0x59820b` (pow path only):

| Seq | Op | Meaning |
|-----|----|---------|
| `0x598202` | `CALL ram:6a3e2c` | `_CIpow` → float10 result |
| `0x598207` | `FLOAT2FLOAT` | result → float32 upright |

Join `0x598211`: `MULTIEQUAL` of pow-result vs `1.0`, then `FLOAT_MULT` with curve `t` (`stack` slot / prior `local_40`).

---

## 4. Operand loads (assembly context @ `0x598202`)

`get_assembly_context` on the `_CIpow` call site (12 insns of context):

```
; |dot| already computed and stored
005981d8  FABS
005981da  FSTP  float ptr [ESP+0x14]     ; [ESP+0x14] = |dot|
005981de  FLD   float ptr [0x00a0f698]   ; ST0 = 0.8
005981e4  FLD   float ptr [ESP+0x14]     ; ST0 = |dot|, ST1 = 0.8
005981e8  FCOMIP ST0,ST1                ; compare |dot| ? 0.8
005981ea  FSTP  ST0
005981ec  JC    0x005981f8              ; CF ⇒ |dot| < 0.8 → pow path
005981ee  MOVSS XMM0, [0x00a0f2a0]      ; upright = 1.0
005981f6  JMP   0x00598211
; ---- pow path ----
005981f8  FLD   float  ptr [ESP+0x14]    ; base = |dot|
005981fc  FLD   double ptr [0x009d54e8]  ; exp  = 4.0
00598202  CALL  0x006a3e2c              ; _CIpow  (ST1^ST0 CRT helper)
00598207  FSTP  float ptr [ESP+0x14]     ; upright = (float)pow(|dot|, 4.0)
0059820b  MOVSS XMM0, [ESP+0x14]
00598211  MULSS XMM0, [ESP+0x10]        ; upright × t
```

**CRT stack convention (MSVC `_CIpow`):** after the two `FLD`s, **ST1 = base**, **ST0 = exp**; helper returns `base^exp` in ST0. Matches `pow(dotAbs, 4.0)`, not `pow(4.0, dotAbs)`.

---

## 5. Constants (`read_memory`)

| Address | Bytes (LE) | IEEE value | Role |
|---------|------------|------------|------|
| `0x00a0f698` | `cd cc 4c 3f` | **0.8** f32 | gate: enter pow iff `\|dot\| < 0.8` |
| `0x009d54e8` | `00 00 00 00 00 00 10 40` | **4.0** f64 | `_CIpow` exponent |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** f32 | upright when gate fails (fully upright enough) |
| `0x00af3390` | `00 00 00 00` | **0.0** | worldUp.x |
| `0x00af3394` | `00 00 80 3f` | **1.0** | worldUp.y |
| `0x00af3398` | `00 00 00 00` | **0.0** | worldUp.z |

Nearby bytes at `0x009d54e0` (`6e db 36 40 …`) are **not** referenced by this function’s pow site; sole data xref of `0x9d54e8` in this path is the `FLD double` at **`0x5981fc`**.

---

## 6. Geometry inputs

**bodyUp** — `FUN_004e8b60(src, &out3)`:

| Condition | `src` |
|-----------|--------|
| `entity+8 != 0` | `*(entity+8)+0x3c + 0x30` (physics body orientation) |
| else | `*( *(entity+4)+4 ) + entity + 0x94` (pose fallback) |

**worldUp** — static `(0, 1, 0)` at `DAT_00af3390..98`.

Absolute value means inverted chassis (`dot ≈ −1`) is treated like fully upright for the **gate input**, then raised to the 4th power only when `|dot| < 0.8`.

---

## 7. Threshold discontinuity (intentional)

| `\|dot\|` | `upright` |
|----------:|----------:|
| `≥ 0.8` | **1.0** (hard assign, not `0.8⁴`) |
| just below `0.8` | `≈ 0.8⁴ = 0.4096` |
| `0.5` | `0.0625` |
| `0.0` | `0.0` |

At the gate there is a **step jump**: continuous `|d|⁴` approaches ~0.41 from below, then snaps to 1.0 at/above 0.8. Confirmed by `JC` to pow vs `MOVSS 1.0` path — not a smooth blend across 0.8.

---

## 8. Interaction with rest of calcWheelTorque (context only)

`upright` is **not** the only scale on wheel drive torque. Compact product (see full verified fn note):

```
torque_i = clamp( µ_i' · upright · t_i · handbrakeRear_i , 0, 1000 )
```

This file does **not** re-verify µ boost, driver mod, handbrake, or clamp — only the upright factor.

---

## 9. Evidence checklist

| Step | Result |
|------|--------|
| `decompile_function(0x598040)` | Gate `ABS(dot) < DAT_00a0f698` → `_CIpow()` → multiply into torque |
| `get_function_pcode` high | `FLOAT_ABS` → `FLOAT_LESS a0f698` → branch; `CALL 6a3e2c`; `MULTIEQUAL` with `a0f2a0` |
| `get_assembly_context(0x598202)` | `FLD |dot|` then `FLD double [0x9d54e8]` then `CALL _CIpow` |
| `get_xrefs_to(0x9d54e8)` | **sole** read: `0x5981fc` in `VehicleAction_calcWheelTorque` |
| `read_memory` | `0.8`, `4.0` double, `1.0`, worldUp `(0,1,0)` |

---

## 10. Port notes (no invention)

- Implement exactly:  
  `upright = (fabsf(dot) < 0.8f) ? powf(fabsf(dot), 4.0f) : 1.0f`  
  (or `pow` with double exp 4.0 then cast — matches store-as-float after CRT).
- Do **not** use continuous `|d|⁴` above 0.8; do **not** invent other exponents (e.g. 2).
- Do **not** confuse with applyAction upright-**restore impulse** gate `0.7` (`DAT_00af3380`) — different system.
- Gap in `engine-torque-spec.md` / `PORTING_RULES.md` for this `_CIpow` pair is **closed** by this file.
