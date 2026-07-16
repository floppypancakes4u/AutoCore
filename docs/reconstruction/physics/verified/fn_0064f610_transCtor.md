# Verified: `hkDefaultTransmission_ctor` @ `0x64f610`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkDefaultTransmission_ctor` |
| Address | `0x0064f610` – `0x0064f661` |
| Convention | MSVC `__thiscall` (`this` = ECX = `param_1`, stack `param_2` = filled descriptor) |
| Heap size | **`0x60`** (allocated in `Vehicle_buildHavokVehicleFramework` @ `0x5fd390`) |
| Vtable | `PTR_FUN_009e4dac` |
| Caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (call site `0x5fd5f2`; second xref `0x64f73c`) |
| Upstream builder | `Vehicle_BuildTransmissionDescriptor` @ `0x5fc840` — see `fn_005fc840_transBuilder.md` |
| Verified | Ghidra `decompile_function` + `read_memory` (re-gate) |

---

## Tools used (this verification)

1. **`decompile_function`** `0x64f610` (`hkDefaultTransmission_ctor`)
2. **`decompile_function`** `0x65e460` (base transmission / array-prep ctor)
3. **`decompile_function`** `0x65e3b0` (wheel-count + zero `wheelsTransmittedTorque[]` capacity)
4. **`decompile_function`** `0x64f100` (descriptor → component field + array copy)
5. **`decompile_function`** `0x5fc840` (builder; desc slot meanings)
6. **`decompile_function`** `0x64f510` / `hkDefaultTransmission_calcRPM` (runtime field identity cross-check)
7. **`read_memory`** `0x9e4dac` (vtable), `0xaaa668` (`-1.0f`), `0x9e4da8` (`60/(2π)`)
8. **`get_function_by_address`** / **`get_function_callees`** / **`analyze_function_complete`**

Did **not** use `disassemble_bytes`. Emulation skipped (heap allocator + descriptor arrays).

---

## Role

Construct the **`hkDefaultTransmission`** component (`size 0x60`) from a stack descriptor produced by
`Vehicle_BuildTransmissionDescriptor`:

1. Base-class init (`FUN_0065e460`) — vtable stub, flags, empty `hkArray` for per-wheel **output** torque.
2. Override vtable to `0x9e4dac`, clear gear / clutch runtime state, init array headers for gear + torque-ratio tables.
3. **`FUN_0064f100`** — copy scalars + deep-copy `gearsRatio[]` and `wheelsTorqueRatio[]` from descriptor.

Does **not** wire `this+0x08` (framework parent) — that is filled when the framework attaches children.
Does **not** implement shift / RPM math (that is `hkDefaultTransmission_update` @ `0x64f510`).

---

## Call graph (ctor only)

```
hkDefaultTransmission_ctor(this, desc)
  ├─ FUN_0065e460(desc)                 // base
  │    ├─ vtable = PTR_FUN_009e71e0
  │    ├─ *(uint16*)(this+6) = 1
  │    ├─ empty hkArray at this+0x20 (ptr=0, n=0, cap=0x80000000)
  │    ├─ FUN_0065e3b0(desc)            // this+0xc = (int)desc[0] (wheel count byte);
  │    │                                // grow + zero this+0x20[0..n)
  │    └─ this+0x10..+0x1c = 0          // gear / reverse / rpm / torque outputs
  ├─ *this = PTR_FUN_009e4dac           // hkDefaultTransmission vtable
  ├─ empty gearsRatio hkArray           // +0x40 / +0x44 / +0x48
  ├─ empty wheelsTorqueRatio hkArray    // +0x4c / +0x50 / +0x54
  ├─ clear gear + reverse flag          // +0x10 / +0x14
  ├─ clutchDelay active = 0             // +0x58
  ├─ clutch timer seed = DAT_00aaa668   // +0x5c = -1.0f
  └─ FUN_0064f100(desc)                 // desc → component layout below
```

Return: `this` (`param_1`).

---

## Authoritative decompile (`0x64f610`)

```c
undefined4 * __thiscall hkDefaultTransmission_ctor(undefined4 *param_1, undefined4 param_2)
{
  undefined4 uVar1;

  FUN_0065e460(param_2);
  uVar1 = DAT_00aaa668;                 // -1.0f
  *param_1 = &PTR_FUN_009e4dac;
  param_1[0x10] = 0;                    // +0x40 gearsRatio.ptr
  param_1[0x11] = 0;                    // +0x44 gearsRatio.count
  param_1[0x12] = 0x80000000;           // +0x48 gearsRatio.capacity (unowned sentinel)
  param_1[0x15] = 0x80000000;           // +0x54 wheelsTorqueRatio.capacity
  param_1[0x13] = 0;                    // +0x4c wheelsTorqueRatio.ptr
  param_1[0x14] = 0;                    // +0x50 wheelsTorqueRatio.count
  *(undefined1 *)(param_1 + 5) = 0;     // +0x14 isReversing
  param_1[4] = 0;                       // +0x10 currentGear
  param_1[0x17] = uVar1;                // +0x5c clutch timer / delayed-since = -1.0
  *(undefined1 *)(param_1 + 0x16) = 0;  // +0x58 delayed / in-clutch flag
  FUN_0064f100(param_2);
  return param_1;
}
```

---

## Component layout (`size 0x60`)

Byte offsets. Types as used by ctor + `FUN_0064f100` + update/calcRPM identity.

| Off | Dword | Type | Init (ctor) | Meaning |
|---:|---:|---|---|---|
| `+0x00` | `[0]` | ptr | `0x9e4dac` | vtable |
| `+0x04` | `[1]` | u16 | heap size `0x60` (alloc) | object size stamp |
| `+0x06` | — | u16 | **`1`** (`FUN_0065e460`) | base flag word (see base ctor) |
| `+0x08` | `[2]` | ptr | *(not set here)* | **framework parent** (wired later) |
| `+0x0c` | `[3]` | i32 | `(int)(byte)desc[0]` | wheel count (from desc byte; base) |
| `+0x10` | `[4]` | i32 | **`0`** | **currentGear** |
| `+0x14` | `[5]` | u8 | **`0`** | **isReversing** (runtime; update rewrites) |
| `+0x15..+0x17` | — | pad | 0 | — |
| `+0x18` | `[6]` | f32 | **`0`** (base) | **engineRPM** (written by update via `calcRPM`) |
| `+0x1c` | `[7]` | f32 | **`0`** (base) | **mainTransmittedTorque** / torque factor out |
| `+0x20` | `[8]` | ptr | alloc/zero in base | **wheelsTransmittedTorque[i]** runtime out (`frac[i] * +0x1c`) |
| `+0x24` | `[9]` | i32 | wheel count | count for `+0x20` array |
| `+0x28` | `[10]` | u32 | capacity / `0x80000000` sentinel | capacity for `+0x20` |
| `+0x2c` | `[11]` | f32 | ← `desc+0x04` | **downshiftRPM** |
| `+0x30` | `[12]` | f32 | ← `desc+0x08` | **upshiftRPM** |
| `+0x34` | `[13]` | f32 | ← `desc+0x0c` | **primaryTransmissionRatio** |
| `+0x38` | `[14]` | f32 | ← `desc+0x10` | **clutchDelayTime** |
| `+0x3c` | `[15]` | f32 | ← `desc+0x14` | **reverseGearRatio** |
| `+0x40` | `[16]` | ptr | deep-copy from `desc+0x18` | **gearsRatio[]** |
| `+0x44` | `[17]` | i32 | ← `desc+0x1c` | **numGears** |
| `+0x48` | `[18]` | u32 | capacity after alloc | gearsRatio capacity |
| `+0x4c` | `[19]` | ptr | deep-copy from `desc+0x24` | **wheelsTorqueRatio[]** (input fractions) |
| `+0x50` | `[20]` | i32 | ← `desc+0x28` | wheel count (torque-ratio array) |
| `+0x54` | `[21]` | u32 | capacity after alloc | wheelsTorqueRatio capacity |
| `+0x58` | `[22]` | u8 | **`0`** | **isDelayed** / clutch-hold active |
| `+0x59..+0x5b` | — | pad | 0 | — |
| `+0x5c` | `[23]` | f32 | **`-1.0`** (`DAT_00aaa668`) | clutch delay timebase seed (`delayedSince`) |

End of object: **`+0x60`**.

### Two distinct torque arrays (do not conflate)

| Array | Off | Role |
|---|---|---|
| **Input** `wheelsTorqueRatio[]` | `+0x4c` / count `+0x50` | Setup axle-split fractions from builder (`F/nF`, `R/nR`) |
| **Output** `wheelsTransmittedTorque[]` | `+0x20` / count `+0x24` | Runtime `ratio[i] * mainTransmittedTorque`; zeroed at ctor |

`calcRPM` weights wheel spin (`wheel+0x8c`, stride `0xC0`) by **input** `+0x4c[i]`. Update writes **output** `+0x20[i]`.

### `hkArray` capacity convention

Capacity dwords use high bit **`0x80000000`** as “unowned / not heap-owned” sentinel (common AA/Havok pattern). Usable capacity = `cap & 0x7fffffff`. Ctor seeds empty arrays with `ptr=0`, `count=0`, `cap=0x80000000` before `FUN_0064f100` reallocates when desc counts require storage.

---

## Descriptor → component map (`FUN_0064f100` @ `0x64f100`)

| Descriptor off | Component off | Field |
|---|---|---|
| `+0x04` | `+0x2c` | downshiftRPM |
| `+0x08` | `+0x30` | upshiftRPM |
| `+0x0c` | `+0x34` | primaryTransmissionRatio |
| `+0x10` | `+0x38` | clutchDelayTime |
| `+0x14` | `+0x3c` | reverseGearRatio |
| `+0x18` (ptr) → memcpy | `+0x40` | gearsRatio[] |
| `+0x1c` | `+0x44` | numGears |
| `+0x24` (ptr) → memcpy | `+0x4c` | wheelsTorqueRatio[] |
| `+0x28` | `+0x50` | wheel count |

Notes:

- Desc **`+0x00`** (wheel-count **byte**) is **not** copied by `FUN_0064f100`. Base `FUN_0065e3b0` already set `this+0x0c` / output array from that byte; torque-ratio count lives at **`+0x50`**.
- Array copies allocate via heap vtable `DAT_00b05060+0x10` (element size 4, tag `0x12`) when existing capacity is insufficient; old buffers freed via `+0x14` when owned.
- Bulk copy is unrolled ×4 then scalar tail (bit-identical float copy).

Full desc build algorithm (VehSpec offsets, RPM × `entity+0x1fc`, axle split): **`fn_005fc840_transBuilder.md`**.

---

## Vtable `PTR_FUN_009e4dac` (raw `read_memory`)

| Slot | VA | Target | Role |
|---:|---|---|---|
| `+0x00` | `0x0064f670` | dtor wrapper → `FUN_0064f6a0` | free `+0x4c` then `+0x40` arrays, then base dtor path |
| `+0x04` | `0x005ffd80` | shared | (serialize / DI helper) |
| `+0x08` | `0x005ffdb0` | shared | (flag helper) |
| `+0x0c` | `0x0064f710` | (named region) | — |
| `+0x10` | `0x005ffc80` | empty ret | — |
| `+0x14` | `0x0064f510` | **`hkDefaultTransmission_update`** | per-step gear / RPM / torque factor |

Framework child tick uses slot **`+0x14`** (same convention as aero/steering).

---

## Runtime field identities (cross-check; not ctor logic)

From `hkDefaultTransmission_update` @ `0x64f510` + `hkDefaultTransmission_calcRPM`:

```
isReversing (+0x14)  ← (driver reverse byte at framework+0x14+0x19) && (currentGear < 1)

if !isDelayed(+0x58):
    gearRatio = isReversing ? -reverseGearRatio(+0x3c)
                            : gearsRatio[currentGear]   // +0x40[gear]
    mainTransmittedTorque(+0x1c) =
        primary(+0x34) * gearRatio * *(framework.engineLike + 0xc)
        // via *( *(this+8) + 0x1c ) + 0xc
else:
    mainTransmittedTorque = 0

engineRPM(+0x18) = calcRPM():
    weightedSpin = Σ  wheelsTorqueRatio[i](+0x4c) * wheelSpin_i(+0x8c) * DAT_009e4da8
    // DAT_009e4da8 = 9.549296… = 60/(2π)  (rad/s → RPM)
    if !isReversing:
        return gearsRatio[gear] * primary * weightedSpin
    else:
        return (-reverseGearRatio) * primary * weightedSpin

for i in 0..count(+0x50):
    wheelsTransmittedTorque[i](+0x20) = wheelsTorqueRatio[i] * mainTransmittedTorque

// clutch hold: on up/down shift, isDelayed=1, +0x5c = framework time (+8);
// clear delay when (time - +0x5c) > clutchDelayTime(+0x38)
// upshift if RPM > upshiftRPM(+0x30) and gear+1 < numGears(+0x44)
// downshift if RPM < downshiftRPM(+0x2c) and gear > 0
// no auto-shift while isReversing
```

Confirms ctor map: **`+0x38` = clutchDelayTime**, **`+0x3c` = reverseGearRatio** (matches builder reflection fix; Phase-0 setup map had those VehSpec **names** swapped — binary order wins; see builder doc).

---

## Constants (`read_memory`)

| Address | LE bytes | float32 | Role in ctor |
|---|---|---|---|
| `DAT_00aaa668` @ `0x00aaa668` | `00 00 80 bf` | **`-1.0`** | `this+0x5c` seed (clutch timebase “unset”) |
| `DAT_009e4da8` @ `0x009e4da8` | `eb c9 18 41` | **`9.549296`** | **Not loaded by ctor** — used only in `calcRPM` as rad/s→RPM (`60/(2π)`) |

No other formula immediates in the ctor body. Array sentinel `0x80000000` is an integer capacity flag, not a float.

---

## Construction context (framework)

Inside `Vehicle_buildHavokVehicleFramework` (`0x5fd390`), transmission is component **#5**:

```
FUN_0064f750(stackDesc)                          // zero desc scalars + array headers
Vehicle_BuildTransmissionDescriptor(entity, …, stackDesc)
heap alloc size 0x60  (size stamp → object+4)
hkDefaultTransmission_ctor(object, stackDesc)    // this function
// parent framework pointer (+0x08) set during later framework assembly
```

Desc pre-zero: `FUN_0064f750` clears `desc+0x04..+0x14` floats and both array triples (`+0x18/+0x1c/+0x20`, `+0x24/+0x28/+0x2c` with caps `0x80000000`).

---

## Port notes (layout only — no C#)

When allocating a server-side `HkDefaultTransmission` equivalent:

1. **Object size `0x60`** — keep field order above if any native interop/golden mem compares are planned; otherwise map by **names**.
2. **Scalars from builder** (already × `entity+0x1fc` for RPM):  
   `downshiftRPM`, `upshiftRPM`, `primaryTransmissionRatio`, `clutchDelayTime`, `reverseGearRatio`, `gearsRatio[0..numGears)`, `wheelsTorqueRatio[0..nWheels)`.
3. **Runtime defaults after construct:**  
   `currentGear = 0`, `isReversing = 0`, `engineRPM = 0`, `mainTransmittedTorque = 0`,  
   `isDelayed = 0`, `delayedSince = -1.0`,  
   `wheelsTransmittedTorque[i] = 0` for all wheels.
4. **Do not** treat `wheelsTorqueRatio` (setup) and `wheelsTransmittedTorque` (runtime) as one array.
5. **Parent framework** (`+0x08`) must be set before update/calcRPM (needs wheels container, driver reverse byte, time, residual engine-ish float at `fw+0x1c+0xc`).
6. AA drive torque path (`calcWheelTorque` / `torqueCurve2D`) does **not** consume `+0x1c` / axle torque array for friction drive (residual Havok transmission — see `0.7-transmission.md`). Port still needs this layout if framework child update runs for RPM/gear/HUD parity.

### VehSpec name correction (carry from builder verify)

| VehSpec | Authoritative DB | Desc / component field |
|---|---|---|
| `+0x6c8` | `rlClutchDelayTime` | clutchDelayTime → component `+0x38` |
| `+0x6cc` | `rlReverseGearRatio` | reverseGearRatio → component `+0x3c` |

---

## Conflicts vs existing evidence

| Item | Prior docs | This re-verify | Verdict |
|---|---|---|---|
| Heap size `0x60` | `fn_005fd390_buildFramework` / setup map | yes | **match** |
| Vtable `0x9e4dac` | builder verified note | yes | **match** |
| Desc→component map via `FUN_0064f100` | `fn_005fc840_transBuilder` §handoff | yes (re-decompiled) | **match** |
| `+0x38` clutch / `+0x3c` reverse | builder + update | yes (`calcRPM` uses `+0x3c` as reverse) | **match** |
| `+0x5c = -1.0` at ctor | not in Phase-0 | confirmed `DAT_00aaa668` | **additive** |
| Output array at `+0x20` vs input at `+0x4c` | update plate comments | yes | **match** |
| No engine component | `0.7` / buildFramework | ctor never touches engine | **match** |

**No layout conflict** with `fn_005fc840_transBuilder.md` handoff table.

---

## Emulation

Not useful for the ctor: depends on heap allocator + live descriptor buffers. Layout is fully determined by decompile store order + desc copy. Goldens: fill a synthetic desc → construct → assert component scalars/arrays and default runtime fields (`gear=0`, `+0x5c=-1`, empty transmitted torque).
