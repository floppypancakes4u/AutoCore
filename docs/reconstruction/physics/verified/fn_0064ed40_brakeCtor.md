# Verified: `hkDefaultBrake_ctor` @ `0x64ed40`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkDefaultBrake_ctor` |
| Address | `0x0064ed40` |
| Convention | MSVC `__thiscall` (`this` = ECX; descriptor on stack) |
| Heap size | **`0x54`** (written as `uint16` at `object+4` by allocator site) |
| Vtable | `PTR_FUN_009e4cb8` @ `0x009e4cb8` |
| Role | Construct stock Havok 2.3 default brake component from stack descriptor |
| RE tools | `decompile_function` @ `0x64ed40`, callees `0x65e2d0` / `0x65e1e0` / `0x64e840` / `0x5b3300`; `read_memory` vtable + reflection strings `0x9e4d28+`; xrefs |
| Status | **Verified** (re-read) |

Related (not this function):

- Descriptor fill: `FUN_005fcb00` @ `0x5fcb00` — see `fn_005fcb00_brakeBuilder.md`
- Descriptor zero-init: `FUN_0064ef20` @ `0x64ef20` (before builder)
- Owner setup: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (alloc `0x54` → ctor)
- Runtime: `hkDefaultBrake_update` @ `0x64e6f0` (vtbl slot `+0x14`; plate `WI-MOV-005`)
- Map evidence: `setup-field-mapping.md` Brake section, `brake-spec.md`

No C# in this file (RE evidence only).

---

## 1. Decompile (authoritative)

```c
undefined4 * __thiscall hkDefaultBrake_ctor(undefined4 *param_1, char *param_2)
{
  // param_1 = this (hkDefaultBrake*, size 0x54)
  // param_2 = BrakeDescriptor* (first byte = wheel count; three hkArrays + minTime scalar)

  FUN_0065e2d0(param_2);                 // base hkBrakeComponent ctor + size base out-arrays
  *param_1 = &PTR_FUN_009e4cb8;          // install default-brake vtable

  // empty hkArray maxBrakingTorque  @ +0x28 / +0x2c / +0x30  (elem size 4)
  param_1[10] = 0;
  param_1[0xb] = 0;
  param_1[0xc] = 0x80000000;

  // empty hkArray minPedalInputToBlock @ +0x34 / +0x38 / +0x3c  (elem size 4)
  param_1[0xd] = 0;
  param_1[0xe] = 0;
  param_1[0xf] = 0x80000000;

  // empty hkArray isConnectedToHandbrake @ +0x40 / +0x44 / +0x48  (elem size 1)
  param_1[0x10] = 0;
  param_1[0x11] = 0;
  param_1[0x12] = 0x80000000;

  // grow each subclass array to *desc (wheel count as signed char)
  int n = (int)*param_2;
  // maxTorque: ensure cap, then size = n  (FUN_005b3300(param_1+10, newCap, 4))
  // minPedal:  ensure cap, then size = n  (FUN_005b3300(param_1+0xd, newCap, 4))
  // handbrake: ensure cap, then size = n  (FUN_005b3300(param_1+0x10, newCap, 1))
  // grow rule: if (cap & 0x7fffffff) < n → newCap = max(cap*2, n)

  FUN_0064e840(param_2);                 // deep-copy desc arrays + minTime scalar into this
  return param_1;
}
```

`get_function_callees`: `FUN_0065e2d0`, `FUN_005b3300`, `FUN_0064e840`.

---

## 2. Construction sequence

```
Vehicle_buildHavokVehicleFramework (0x5fd390)
  FUN_0064ef20(brakeDesc)                    // zero-init three hkArrays + minTime=0
  FUN_005fcb00(entity, ?, brakeDesc)         // 0x5fcb00 — fill front/rear fan-out
  heap = alloc(0x54);  *(uint16*)(heap+4) = 0x54
  hkDefaultBrake_ctor(heap, brakeDesc)       // 0x64ed40
    ├─ FUN_0065e2d0(desc)                    // base class
    │    ├─ vtable = PTR_FUN_009e7180 (base)
    │    ├─ *(uint16*)(this+6) = 1
    │    ├─ init empty hkArrays @ +0x10 (f32 out-torque), +0x1c (byte isBlocked)
    │    └─ FUN_0065e1e0(desc)               // size both to *desc; zero-fill; this+0xc = n
    ├─ vtable = PTR_FUN_009e4cb8 (default brake)
    ├─ init 3 empty hkArrays @ +0x28 / +0x34 / +0x40
    ├─ grow each to *desc (elem 4 / 4 / 1)
    └─ FUN_0064e840(desc)                    // copy subclass fields from desc
  // next: FUN_0064e670 zeros the SUSPENSION stack-desc (not brake teardown)
```

### Callers of `0x64ed40`

| Site | Role |
|---|---|
| `Vehicle_buildHavokVehicleFramework` @ `0x005fd654` | Primary production path |
| Factory thunk @ `0x0064ef0c` | Secondary `alloc` + ctor path (same pattern as other components) |

---

## 3. Base ctor `FUN_0065e2d0` @ `0x65e2d0`

```c
undefined4 * __thiscall FUN_0065e2d0(undefined4 *param_1, undefined4 param_2)
{
  *(undefined2 *)((int)param_1 + 6) = 1;
  *param_1 = &PTR_FUN_009e7180;          // base brake vtable (overwritten by default ctor)

  // outBrakeTorque hkArray (f32) @ +0x10
  param_1[4] = 0;  param_1[5] = 0;  param_1[6] = 0x80000000;
  // isBlocked      hkArray (u8)  @ +0x1c
  param_1[7] = 0;  param_1[8] = 0;  param_1[9] = 0x80000000;

  FUN_0065e1e0(param_2);
  return param_1;
}
```

### Base size/zero `FUN_0065e1e0` @ `0x65e1e0`

```c
void __thiscall FUN_0065e1e0(int this, char *desc)
{
  int n = (int)*desc;
  *(this + 0xc) = n;                     // wheel count

  // grow outBrakeTorque @ +0x10 to n (elem 4); zero-fill new slots
  // set size at +0x14 = n

  // grow isBlocked @ +0x1c to n (elem 1); zero-fill new slots
  // set size at +0x20 = n
}
```

So after the base stage:

| Offset | Value |
|---|---|
| `+0x0c` | wheel count `n = *desc` |
| `+0x10..` | `n` zeros (f32 brake-torque **output**, filled later by update) |
| `+0x1c..` | `n` zeros (byte **isBlocked** flags, filled later by update) |

---

## 4. Subclass copy `FUN_0064e840` @ `0x64e840`

Deep-copy helper (uses `DAT_00b05060` heap alloc/free at vtbl `+0x10` / `+0x14` when re-sizing owned buffers).

| Step | Component dest | Descriptor source | Element |
|---|---|---|---|
| 1 | `this+0x28` maxBrakingTorque[] | `desc+0x04` / size `desc+0x08` | **f32** (stride 4); unrolled ×4 copy |
| 2 | size/cap `@+0x2c/+0x30` | from `desc+0x08` | |
| 3 | `this+0x34` minPedalInputToBlock[] | `desc+0x10` / size `desc+0x14` | **f32** (stride 4) |
| 4 | size/cap `@+0x38/+0x3c` | from `desc+0x14` | |
| 5 | `this+0x40` isConnectedToHandbrake[] | `desc+0x20` / size `desc+0x24` | **byte** (stride 1) |
| 6 | size/cap `@+0x44/+0x48` | from `desc+0x24` | |
| 7 | **`this+0x4c = this+0x50 = desc+0x1c`** | scalar `wheelsMinTimeToBlock` | **f32** |

Critical: the scalar min-time is written to **both** `+0x4c` (configured min) and `+0x50` (remaining block timer) at construct time. After the AA builder path both are **0** (`desc+0x1c` forced 0 in `FUN_005fcb00`).

`hkArray` capacity high bit `0x80000000` = empty/unowned sentinel (same convention as suspension/steering).

---

## 5. Object layout after ctor (`size 0x54`)

| Offset | Type | Meaning | Who sets |
|---|---|---|---|
| `+0x00` | ptr | vtable `0x009e4cb8` | ctor |
| `+0x04` | u16 | heap object size `0x54` | allocator site (before ctor) |
| `+0x06` | u16 | `1` (base flag) | `FUN_0065e2d0` |
| `+0x08` | ptr | framework back-ptr | **later** (framework assembly; not this ctor) |
| `+0x0c` | i32 | wheel count (loop bound in update) | `FUN_0065e1e0` ← `*desc` |
| `+0x10` | hkArray&lt;f32&gt; | **output** per-wheel brake torque | base: zeroed; update writes |
| `+0x1c` | hkArray&lt;u8&gt; | **output** per-wheel isBlocked / lock flags | base: zeroed; update writes |
| `+0x28` | hkArray&lt;f32&gt; | `wheelsMaxBrakingTorque[]` | `FUN_0064e840` ← desc |
| `+0x34` | hkArray&lt;f32&gt; | `wheelsMinPedalInputToBlock[]` | `FUN_0064e840` ← desc |
| `+0x40` | hkArray&lt;u8&gt; | `wheelsIsConnectedToHandbrake[]` | `FUN_0064e840` ← desc |
| `+0x4c` | f32 | `wheelsMinTimeToBlock` (configured) | `FUN_0064e840` ← `desc+0x1c` |
| `+0x50` | f32 | remaining block-timer (runtime) | init = same as `+0x4c`; update mutates |
| `+0x54` | — | end | — |

`hkArray` layout at each base: `{ void* data; int size; int capacity }` (12 bytes).

### Havok reflection name strings (raw `read_memory`)

| Address | C-string |
|---|---|
| `0x009e4d28` | `wheelsIsConnectedToHandbrake` |
| `0x009e4d48` | `wheelsMinTimeToBlock` |
| `0x009e4d60` | `wheelsMinPedalInputToBlock` |
| `0x009e4d7c` | `wheelsMaxBrakingTorque` |
| `0x009e4d94` | `hkDefaultBrake` |

---

## 6. Descriptor layout (input to ctor)

Filled by `FUN_005fcb00` after `FUN_0064ef20` zero-init. Full fan-out math is in `fn_005fcb00_brakeBuilder.md`.

| Desc off | Type | Meaning |
|---|---|---|
| `+0x00` | `u8` | wheel count (`*param_2` for grow/size) |
| `+0x04` | `float*` / size / cap | `wheelsMaxBrakingTorque[]` |
| `+0x10` | `float*` / size / cap | `wheelsMinPedalInputToBlock[]` |
| `+0x1c` | `f32` | `wheelsMinTimeToBlock` (**always 0** from AA builder) |
| `+0x20` | `byte*` / size / cap | `wheelsIsConnectedToHandbrake[]` |

Source → desc (builder, not ctor):

| Array | Front `i < VehSpec+0x4cc` | Rear |
|---|---|---|
| maxTorque | `VehSpec+0x57c * entity+0x200` | `VehSpec+0x580 * entity+0x204` |
| minPedal | `VehSpec+0x58c` | `VehSpec+0x590` |
| handbrake connect | `VehSpec+0x5f0 & 1` | `(VehSpec+0x5f0 >> 1) & 1` |

---

## 7. Vtable `PTR_FUN_009e4cb8` @ `0x009e4cb8`

`read_memory` 28 bytes LE:

| Slot | Address | Notes |
|---:|---|---|
| 0 | `0x0064ee20` | scalar deleting dtor → body free `FUN_0064ee50` (frees three subclass arrays then base) |
| 1 | `0x005ffd80` | shared base slot |
| 2 | `0x005ffdb0` | shared base slot |
| 3 | `0x0064eee0` | type / factory neighbor region |
| 4 | `0x005ffc80` | shared empty stub |
| 5 | **`0x0064e6f0`** | **`hkDefaultBrake_update`** (component tick slot `vtbl+0x14`) |
| 6 | `0x009e4d7c` | reflection / member-name table anchor (`wheelsMaxBrakingTorque` string) |

### Dtor body `FUN_0064ee50` (ownership confirmation)

Frees in reverse subclass order when capacity high-bit clear:

1. handbrake array `@+0x40` (bytes; free size = cap)
2. minPedal array `@+0x34` (f32; free size = cap×4)
3. maxTorque array `@+0x28` (f32; free size = cap×4)
4. `FUN_0064ecd0` — base array teardown

Confirms ctor owns the three subclass buffers at `+0x28/+0x34/+0x40`.

---

## 8. Runtime consumer (context only — not this function)

`hkDefaultBrake_update` @ `0x64e6f0` (comment plate `WI-MOV-005`):

```
status     = *( *(this+8) + 0x14 )     // framework status/input
brakePedal = status[+0x10]             // f32
handbrake  = status[+0x18]             // byte
dt         = param_2[0]
// param_2[1] used in coast-spin torque path

for i in 0 .. this[+0xc]:
  isBlocked[i] = (handbrakeConnected[i] && handbrake)
  peak = brakePedal * maxBrakingTorque[i]
  // clamp opposing wheel-spin torque into ±peak → outBrakeTorque[i] @ +0x10
  if minPedal[i] <= brakePedal: arm block-timer path
// if any wheel armed:
//   if remaining(+0x50) > 0: remaining -= dt; return
//   else force isBlocked[i]=1 for wheels with minPedal <= pedal
// else: remaining = configured minTime (+0x4c)
```

With AA builder forcing `minTime=0`, block eligibility is immediate once pedal ≥ minPedal on an armed wheel.

**Retail custom-path caveat** (from `brake-spec.md` / builder doc): `VehicleAction` may never feed a non-zero service-brake pedal into status `+0x10`. Ctor still installs a fully valid component; whether `update` produces non-zero torque depends on the input/status path, not on this constructor.

---

## 9. Port notes / non-goals

- **Ctor only** — no torque math. Peak / lock / handbrake application lives in `0x64e6f0`.
- Ports that materialize a brake component must:
  1. Size object **`0x54`**.
  2. Own **five** arrays: outTorque, isBlocked, maxTorque, minPedal, handbrakeConnect.
  3. Zero outTorque / isBlocked at init; keep config arrays parallel with wheel count.
  4. Init `minTime` (`+0x4c`) and remaining timer (`+0x50`) from the same scalar (AA: **0**).
  5. Set framework back-ptr (`+0x08`) when wiring into `hkVehicleFramework` (not in this function).
- Descriptor content is entirely the builder’s job; this ctor is a pure structural copy.
- No float `DAT_*` loads inside `0x64ed40` itself.
- Emulation skipped: pointer/heap-heavy; goldens for brake force belong under the update function / input path, not the ctor.
- No C# in this file.

---

## 10. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x64ed40` | OK — base + three arrays + copy |
| `decompile_function` `0x65e2d0` / `0x65e1e0` | OK — base out-arrays sized to `*desc`, zero-filled; `+0xc` = n |
| `decompile_function` `0x64e840` | OK — three arrays + dual write of minTime to `+0x4c/+0x50` |
| `decompile_function` `0x64e6f0` | OK — runtime meaning of built fields |
| `decompile_function` `0x64ee50` | OK — free order confirms ownership |
| `read_memory` `0x9e4cb8` | OK — vtable → update `0x64e6f0` |
| `read_memory` `0x9e4d28+` | OK — Havok member / class name strings |
| Callers | production `0x5fd654`; factory `0x64ef0c` |
| Conflict vs prior | None; aligns with `fn_005fcb00_brakeBuilder.md` §5 and `setup-field-mapping.md` |
| Emulation | Skipped — heap/`hkArray` graph |

---

## 11. Confidence

| Claim | Confidence |
|---|---|
| Object size `0x54`, vtable `0x9e4cb8` | **High** |
| Base out-arrays at `+0x10` / `+0x1c`; count at `+0xc` | **High** |
| Config arrays at `+0x28` / `+0x34` / `+0x40` | **High** (copy helper + reflection names + update loads) |
| `+0x4c`/`+0x50` dual-init from `desc+0x1c` | **High** |
| Update slot `vtbl+0x14` = `0x64e6f0` | **High** |
| Framework back-ptr not set here | **High** (same pattern as other component ctors) |
