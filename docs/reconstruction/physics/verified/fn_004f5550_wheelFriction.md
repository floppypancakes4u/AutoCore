# VERIFIED — `FUN_004f5550` (per-wheel base friction) @ `0x4f5550`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x4f5550`, `0x5a6f20`, `0x4f5560`, `0x598040`, `0x5fcce0`;  
`disassemble_function` @ `0x4f5550` / `0x5a6f20`; `read_memory` @ `0xaaa668`, jump table `0x5a6f70`;  
`get_xrefs_to` @ `0x4f5550`.

**Scope of this file:** the accessor itself and its return value.  
**Not this file:** low-speed μ boost, upright falloff, handbrake ×0.5, or solver μmax (those live in callers).

---

## 1. Identity & call graph

| Item | Value |
|------|------:|
| Entry | `0x4f5550` |
| Symbol (Ghidra) | `FUN_004f5550` |
| Body size | 2 instructions (`0x4f5550`–`0x4f555a`) — pure thunk |
| Convention | `__thiscall` + one stack arg (wheel index) |
| `this` (`ECX`) | Object that owns **`+0x258`** → wheelset / friction table |
| Stack arg | wheel index `i` (byte / small int; cases **0..5**) |
| Return | **x87 ST0** (`float10` in decompiler) = **μ_base[i]** |
| Tail target | `FUN_005a6f20` @ `0x5a6f20` (actual table read) |
| Sibling | `FUN_004f5560` @ `0x4f5560` — wheel **count** from same object (`*(this+0x258)+0xb0`) |

### Callers (complete xrefs)

| Address | Function | Role of return |
|--------:|----------|----------------|
| `0x598221` | `VehicleAction_calcWheelTorque` | unscaled **μ** in drive-torque product |
| `0x5fcf42` | `FUN_005fcce0` (friction-table setup) | seed **μ0**; rear then × `vehicleData+0x740` |

No other xrefs.

---

## 2. Exact machine path

### `FUN_004f5550` (full body)

```
004f5550  MOV  ECX, dword ptr [ECX + 0x258]   ; this → wheelset
004f5556  JMP  0x005a6f20                    ; tail into table switch
```

Decompiler collapses this to `FUN_005a6f20(); return;` — **use the two-instruction body**, not the empty-looking pseudocode.

### `FUN_005a6f20` (return implementation)

```
// __thiscall: this = wheelset (already rebased by thunk)
// stack arg:  wheelIndex (byte, sign-extended)

MOVSX EAX, byte ptr [ESP+4]
CMP   EAX, 5
JA    default                     // → FLD DAT_00aaa668; RET 4

JMP   [EAX*4 + 0x5a6f70]          // jump table

// cases (verified via jump table + FLD targets):
case 0:  FLD float ptr [ECX + 0xb4];  RET 4
case 1:  FLD float ptr [ECX + 0xb8];  RET 4
case 2:  FLD float ptr [ECX + 0xbc];  RET 4
case 3:  FLD float ptr [ECX + 0xc0];  RET 4
case 4:  FLD float ptr [ECX + 0xc4];  RET 4
case 5:  FLD float ptr [ECX + 0xc8];  RET 4
default: FLD float ptr [DAT_00aaa668]; RET 4
```

Equivalent closed form:

```
if (0 <= i && i <= 5)
    return *(float*)(wheelset + 0xb4 + i * 4);   // μ_base[i]
else
    return -1.0f;                                 // DAT_00aaa668
```

`RET 4` pops the stack index (MSVC thiscall with one stack parameter).

---

## 3. Return value (document focus)

| Property | Value |
|----------|------:|
| Type | **float32** on **x87 ST0** (Ghidra: `float10`) |
| Meaning | **Base per-wheel friction coefficient** μ_base for wheel `i` |
| Source | Contiguous `float[6]` at **wheelset `+0xb4`** |
| Valid indices | **0..5** only (max 6 wheels) |
| OOR / invalid index | **`−1.0`** (`DAT_00aaa668` = `bf 80 00 00`) |
| Scaling in this fn | **None** — raw table entry only |

### What this function does **not** return / apply

| Not applied here | Where it actually happens |
|------------------|---------------------------|
| `RearWheelFrictionScalar` (`vehicleData+0x740`) | **only** in `FUN_005fcce0` after the call |
| μmax = μ0 × 1.5 (`DAT_00aaa68c`) | `FUN_005fcce0` setup |
| low-speed boost (`\|v\| < 15` ×0.2+1) | `VehicleAction_calcWheelTorque` after the call |
| upright falloff / torqueCurve2D / handbrake ×0.5 | `calcWheelTorque` combine path |

**Critical for porting drive torque:** `calcWheelTorque` multiplies by the **raw** return of this function. Rear friction scalar is **not** baked into μ for the engine path — it only affects the solver’s parallel friction arrays built at setup.

---

## 4. Wheelset layout (object behind `this+0x258`)

Confirmed by this thunk + `FUN_004f5560` + `FUN_005a6f20`:

| Off | Type | Meaning |
|----:|------|---------|
| `+0xb0` | `u8` | **wheel count** (`FUN_004f5560` returns this byte) |
| `+0xb4` | `f32` | μ_base[0] |
| `+0xb8` | `f32` | μ_base[1] |
| `+0xbc` | `f32` | μ_base[2] |
| `+0xc0` | `f32` | μ_base[3] |
| `+0xc4` | `f32` | μ_base[4] |
| `+0xc8` | `f32` | μ_base[5] |

### `this` chain (callers)

Both live callers pass a **VehicleAction-like** object as `this`:

```
// calcWheelTorque @ 0x598040 — this = VehicleAction (param_1)
mu = FUN_004f5550(i);     // ECX = VA; thunk loads *(VA+0x258)

// FUN_004f5560 sibling (same object):
count = *(u8*)(*(VA + 0x258) + 0xb0);   // decompile shows +600 == +0x258
```

**Ambiguity retained (unchanged from 0.5 notes):** which higher-level type *owns* slot `+0x258` is clear for VehicleAction call sites; the exact RTTI/name of the pointed-to wheelset object is not required for the return formula.

---

## 5. Caller usage of the return

### 5.1 `VehicleAction_calcWheelTorque` @ `0x598040` (xref `0x598221`)

```
// per in-contact wheel i (abridged — product terms only):
t       = torqueCurve2D(...);          // possibly driver-modulated
upright = 1.0 or pow(...);             // if |bodyUp·worldUp| < 0.8
mu      = FUN_004f5550(i);             // ← THIS return (unscaled)
if (|v| < 15.0)
    mu *= (15.0 - |v|) * 0.2 + 1.0;
torque  = mu * upright * t;
if (handbrakeFlag && rear) torque *= 0.5;
clamp(torque, 0, 1000) → wheels+0x28[i];
```

### 5.2 `FUN_005fcce0` friction setup @ `0x5fcce0` (xref `0x5fcf42`)

```
mu0 = FUN_004f5550(i);                 // seed μ0 → (param_3+0x28)[i]
if (i >= firstRear)                    // firstRear = vehicleData+0x4cc
    mu0 *= *(vehicleData + 0x740);     // RearWheelFrictionScalar
muMax = mu0 * 1.5;                     // DAT_00aaa68c → (param_3+0x40)[i]
```

So the **same** accessor feeds two pipelines; only the setup path multiplies rear μ.

---

## 6. Constants

| Symbol | Addr | LE bytes | float32 | Role |
|--------|-----:|----------|--------:|------|
| `DAT_00aaa668` | `0xaaa668` | `00 00 80 bf` | **−1.0** | default return for `i > 5` |

Jump table base: `0x5a6f70` — six dwords → cases 0..5 (read_memory verified targets `0x5a6f43`, `0x5a6f55`, `0x5a6f4c`, `0x5a6f31`, `0x5a6f3a`, `0x5a6f5e`).

---

## 7. Pseudocode (portable)

```
// wheelset = *(component + 0x258)
// component is VehicleAction when called from calcWheelTorque / setup

float GetWheelBaseFriction(void* component, int wheelIndex)
{
    void* wheelset = *(void**)((char*)component + 0x258);

    if (wheelIndex < 0 || wheelIndex > 5)
        return -1.0f;

    return ((float*)((char*)wheelset + 0xb4))[wheelIndex];
}

// companion count accessor FUN_004f5560:
// return *(uint8_t*)( *(component+0x258) + 0xb0 );
```

---

## 8. Corrections vs prior notes

| Claim | Source | Verdict |
|-------|--------|---------|
| Thunk: `MOV ECX,[ECX+0x258]; JMP 0x5a6f20` | `0.5-wheel-collide.md` | **CONFIRMED** (disasm) |
| Cases 0..5 → `+0xb4 + i*4`; default −1.0 | same | **CONFIRMED** |
| `calcWheelTorque` uses **un-scaled** μ | same / engine-torque-spec | **CONFIRMED** |
| Ghidra decompile of `0x4f5550` alone is sufficient | — | **INSUFFICIENT** — empty call/return; must follow thunk |

---

## 9. Verification checklist

- [x] Disasm `0x4f5550` — 2-insn thunk to `0x5a6f20`
- [x] Disasm/decompile `0x5a6f20` — switch 0..5 + default
- [x] Jump table @ `0x5a6f70` matches FLD offsets `0xb4..0xc8`
- [x] `read_memory` `0xaaa668` → `bf800000` = −1.0
- [x] Xrefs: only `calcWheelTorque` + `FUN_005fcce0`
- [x] Confirmed **return = raw μ_base[i]** (no rear scalar inside this fn)
- [x] Sibling `FUN_004f5560` shares `+0x258` → `+0xb0` count
)
