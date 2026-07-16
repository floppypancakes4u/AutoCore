# Verified: `hkDefaultSteering_ctor` @ `0x64fac0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkDefaultSteering_ctor` |
| Address | `0x0064fac0` |
| Body | `0x0064fac0` – `0x0064faf1` |
| Convention | MSVC `__thiscall` (`this` = ECX; steering descriptor on stack) |
| Heap size | **`0x38`** (written as `uint16` at `object+4` by allocator in `Vehicle_buildHavokVehicleFramework`) |
| Vtable | `PTR_FUN_009e4ee4` @ `0x009e4ee4` |
| Role | Construct stock Havok 2.3 **default wheeled steering** component from stack descriptor |
| RE tools | `decompile_function` @ `0x64fac0`, callees `0x65e5f0` / `0x65e530` / `0x64f920`; `read_memory` / `inspect_memory_content` on vtable + reflection strings `0x9e4ee4+`; `get_xrefs_to`; cross-check `hkDefaultSteering_update` `0x64f840`, builder `0x5fc710`, `TankSteering_ctor` `0x64fc80` |
| Status | **Verified** (re-read) |

Related (not this function):

- Descriptor fill: `Vehicle_BuildSteeringDescriptor` @ `0x5fc710` — see `fn_005fc710_steeringBuilder.md`
- Owner setup: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (alloc `0x38` → this ctor, unless tank branch)
- Runtime angle math: `hkDefaultSteering_update` @ `0x64f840` — see `fn_0064f840_steering.md`
- Tank override: `TankSteering_ctor` @ `0x64fc80` calls this then swaps vtable
- Map evidence: `setup-field-mapping.md` Steering section, `steering-spec.md`

No C# in this file (RE evidence only). No `DAT_*` formula constants in this function.

---

## 1. Decompile (authoritative)

```c
undefined4 * __thiscall hkDefaultSteering_ctor(undefined4 *param_1, undefined4 param_2)
{
  // param_1 = this (hkDefaultSteering*, size 0x38)
  // param_2 = SteeringDescriptor* (from Vehicle_BuildSteeringDescriptor)

  FUN_0065e5f0(param_2);                 // base hkSteeringComponent ctor (ECX=this; desc stack arg)
  *param_1 = &PTR_FUN_009e4ee4;          // install hkDefaultSteering vtable

  // empty hkArray doesWheelSteer @ +0x2c / +0x30 / +0x34  (elem size 1)
  param_1[0xb] = 0;                      // +0x2c data = null
  param_1[0xc] = 0;                      // +0x30 size = 0
  param_1[0xd] = 0x80000000;             // +0x34 capacity = unowned sentinel

  FUN_0064f920(param_2);                 // copy desc → maxAngle / fullSpeedLimit / flags
  return param_1;
}
```

`get_function_callees`: `FUN_0065e5f0`, `FUN_0064f920`.

### Ghidra `thiscall` note

Same pattern as other component ctors (`hkDefaultAerodynamics_ctor`, `hkDefaultBrake_ctor`): Ghidra may render the base call as `FUN_0065e5f0(param_2)`, but the base is a `__thiscall` that operates on **ECX = this**. The descriptor remains a stack argument that the base sizing helper (`FUN_0065e530`) consumes. Treating the base as “constructing the descriptor” is wrong.

---

## 2. Construction sequence

```
Vehicle_buildHavokVehicleFramework (0x5fd390)
  Vehicle_BuildSteeringDescriptor(entity, ?, steerDesc)   // 0x5fc710
  if (VehSpec+0x4c0 == 4):
      heap = alloc(0x38); *(uint16*)(heap+4) = 0x38
      TankSteering_ctor(heap, steerDesc)                  // 0x64fc80 → calls this, then tank vtable
  else:
      heap = alloc(0x38); *(uint16*)(heap+4) = 0x38
      hkDefaultSteering_ctor(heap, steerDesc)             // 0x64fac0  ← this function
        ├─ FUN_0065e5f0(desc)                             // base class
        │    ├─ vtable = PTR_FUN_009e7238 (base; overwritten)
        │    ├─ *(uint16*)(this+6) = 1
        │    ├─ init empty hkArray wheelsSteeringAngle @ +0x14 / +0x18 / +0x1c
        │    └─ FUN_0065e530(desc)                        // size out-array to *desc; this+0xc = n
        ├─ vtable = PTR_FUN_009e4ee4 (default steering)
        ├─ init empty hkArray doesWheelSteer @ +0x2c / +0x30 / +0x34
        └─ FUN_0064f920(desc)                             // subclass fields from descriptor
  // framework wire later: hkVehicleFramework_wireComponents sets this+0x08 = framework*
```

### Callers of `0x64fac0`

| Site | Role |
|---|---|
| `Vehicle_buildHavokVehicleFramework` @ `0x005fd536` | Primary production path (wheeled vehicles) |
| `TankSteering_ctor` @ `0x0064fc88` | Tank path reuses this then overwrites vtable `PTR_FUN_009e4f1c` |
| Secondary factory thunk @ `0x0064fd1c` | Same alloc+ctor pattern as other components |

---

## 3. Base ctor `FUN_0065e5f0` @ `0x65e5f0` (`hkSteeringComponent`)

```c
undefined4 * __thiscall FUN_0065e5f0(undefined4 *param_1, undefined4 param_2)
{
  *(undefined2 *)((int)param_1 + 6) = 1;
  *param_1 = &PTR_FUN_009e7238;          // base steering vtable (name string: "hkSteeringComponent")

  // wheelsSteeringAngle hkArray (f32) @ +0x14
  param_1[5] = 0;                        // +0x14 data
  param_1[6] = 0;                        // +0x18 size
  param_1[7] = 0x80000000;               // +0x1c capacity (unowned)

  FUN_0065e530(param_2);                 // size array to wheel count from desc
  return param_1;
}
```

### Base size/zero `FUN_0065e530` @ `0x65e530`

```c
void __thiscall FUN_0065e530(int this, char *desc)
{
  int n = (int)*desc;                    // wheel count byte (desc+0x00)
  *(this + 0xc) = n;                     // store wheel count at +0x0c

  // grow wheelsSteeringAngle @ +0x14 to n (elem size 4) via FUN_005b3300 if needed
  // zero-fill new float slots
  // set size at +0x18 = n
}
```

So after the base stage:

| Offset | Value |
|---|---|
| `+0x00` | base vtable (soon overwritten) |
| `+0x06` | `uint16 = 1` |
| `+0x0c` | wheel count `n = *desc` |
| `+0x14` | `n` zeros (f32 **per-wheel out angle**, filled later by update) |
| `+0x18` | size = `n` |
| `+0x1c` | capacity (owned after grow) |

---

## 4. Subclass copy `FUN_0064f920` @ `0x64f920`

Deep-copy helper from the steering descriptor into the **derived** half of the object.
Uses `DAT_00b05060` heap alloc/free (vtbl `+0x10` / `+0x14`) when resizing the owned `doesWheelSteer` buffer (alloc tag `0x12`).

```c
void __thiscall FUN_0064f920(int this, int desc)
{
  *(this + 0x24) = *(desc + 0x04);       // maxSteeringAngle (f32)
  *(this + 0x28) = *(desc + 0x08);       // maxSpeedFullSteeringAngle (f32)

  // ensure doesWheelSteer[] capacity >= desc+0x10 (wheel count)
  // if re-alloc: free old if owned (cap high bit clear), alloc n bytes (elem size 1)
  // set capacity at +0x34 = n (owned, high bit clear)

  *(this + 0x30) = *(desc + 0x10);       // size = wheel count
  // byte-copy doesWheelSteer[i] from *(desc+0x0c) → *(this+0x2c), i in [0..n)
  *(this + 0x20) = *(this + 0x30);       // mirror wheel count at +0x20
}
```

### Descriptor → component map

| Desc off | Type | Component off | Field |
|---|---|---|---|
| `+0x00` | `byte` | via base `+0x0c` | wheel count (byte) |
| `+0x04` | `f32` | **`+0x24`** | **maxSteeringAngle** |
| `+0x08` | `f32` | **`+0x28`** | **maxSpeedFullSteeringAngle** (`fullSpeedLimit`) |
| `+0x0c` | `u8*` | **`+0x2c`** (deep copy) | **wheelsDoesSteer[]** / `doesWheelSteer[]` |
| `+0x10` | `i32` | **`+0x30`** (and **`+0x20`**) | wheel count |

Descriptor source values (builder, not this function):

| Concept | Builder source |
|---|---|
| maxSteeringAngle | `VehSpec+0x594 × entity+0x208` |
| maxSpeedFullSteeringAngle | `VehSpec+0x598 × entity+0x20c` |
| doesWheelSteer[i] | axle split at `VehSpec+0x4cc`; flags `VehSpec+0x5f0` bits 2 (front) / 3 (rear) |

---

## 5. Full object layout (size `0x38`)

Total heap allocation: **`0x38` bytes** (offsets `0x00` … `0x37`).

| Offset | Size | Type | Init source | Meaning |
|---|---:|---|---|---|
| `+0x00` | 4 | ptr | ctor | **vtable** `PTR_FUN_009e4ee4` |
| `+0x04` | 2 | u16 | allocator site | object size word **`0x38`** |
| `+0x06` | 2 | u16 | base ctor | flag / ref style field = **`1`** |
| `+0x08` | 4 | ptr | **framework wire** (not this ctor) | parent `hkVehicleFramework*` (`wireComponents` writes component `+8`) |
| `+0x0c` | 4 | i32 | base `FUN_0065e530` | wheel count `n` (from `desc+0`) |
| `+0x10` | 4 | f32 | *(runtime update)* | **main / computed steering angle** (`hkDefaultSteering_update` writes) |
| `+0x14` | 4 | ptr | base array | **`wheelsSteeringAngle[]` data** (f32 × n) — update output |
| `+0x18` | 4 | i32 | base array | `wheelsSteeringAngle` size (= n) |
| `+0x1c` | 4 | u32 | base array | `wheelsSteeringAngle` capacity (`0x80000000` = unowned until grown) |
| `+0x20` | 4 | i32 | `FUN_0064f920` | wheel count mirror (= `+0x30`) |
| `+0x24` | 4 | f32 | `FUN_0064f920` ← `desc+0x04` | **`maxSteeringAngle`** (radians after mult) |
| `+0x28` | 4 | f32 | `FUN_0064f920` ← `desc+0x08` | **`maxSpeedFullSteeringAngle`** / full-speed limit |
| `+0x2c` | 4 | ptr | `FUN_0064f920` | **`doesWheelSteer[]` / `wheelsDoesSteer[]` data** (u8 × n) |
| `+0x30` | 4 | i32 | `FUN_0064f920` ← `desc+0x10` | `doesWheelSteer` size (= n) |
| `+0x34` | 4 | u32 | ctor then grow | `doesWheelSteer` capacity (`0x80000000` unowned → owned after copy) |

### End of object

```
0x00                                                    0x38
├─ vtbl / hdr ─┬─ base out-angle array ─┬─ derived params + flags array ─┤
0x00        0x10                       0x20                             0x38
```

### Reflection names (`.rdata` after vtable `0x009e4ee4`)

Member name strings present for this class:

| String VA | Name |
|---|---|
| `0x009e4fa8` | `maxSteeringAngle` |
| `0x009e4f8c` | `maxSpeedFullSteeringAngle` |
| `0x009e4f7c` | `wheelsDoesSteer` |
| (class) | `hkDefaultSteering` |

Base class name at reflection near `0x009e7238`: **`hkSteeringComponent`**.

### Vtable note (update slot)

Within `PTR_FUN_009e4ee4`, slot **`+0x14`** holds `hkDefaultSteering_update` @ **`0x64f840`** (per-tick angle math). Not installed by this function beyond the whole-vtable pointer write.

---

## 6. Runtime consumers of ctor fields

From `hkDefaultSteering_update` @ `0x64f840` (see `fn_0064f840_steering.md`):

| Load | Role in angle math |
|---|---|
| `this+0x08` | parent framework |
| `this+0x24` | multiplied into main angle (stored at ctor as **maxSteeringAngle**) |
| `this+0x28` | full-speed limit gate / quadratic falloff |
| `this+0x2c` / `+0x30` | per-wheel does-steer flags + count |
| `this+0x10` | **written**: computed main angle |
| `this+0x14` | **written**: per-wheel out angles (`flag ? main : 0`) |

Also used:

```
*( *(framework + 0x14) + 0x14 ) * *(this + 0x24)
```

as the pre-falloff product. **Ctor + reflection name `this+0x24` as `maxSteeringAngle`.**  
Some earlier port notes labeled `this+0x24` as “normalized steer input”; that conflicts with this ctor handoff and the Havok member name. Binary ctor wins for the **post-construction** identity of `+0x24`: it is the **max angle from the descriptor**. The other multiply operand is **not** filled by this ctor (comes from the framework graph at runtime).

`hkpVehicleSteering_setSteeringAngle` @ `0x636410` writes **`target+0x50`**. This object is only **`0x38`** bytes, so that setter does **not** target `hkDefaultSteering` itself.

Parent link `this+0x08` is set later by `hkVehicleFramework_wireComponents` @ `0x636940` (all ticked components get `component+8 = framework`).

---

## 7. hkArray conventions used here

| Pattern | Meaning |
|---|---|
| `capacity == 0x80000000` | empty / **unowned** (no heap free on replace) |
| `capacity & 0x7fffffff` | usable capacity when owned |
| grow | `newCap = max(oldCap*2, needed)` via `FUN_005b3300` (base out-array) or direct heap (doesSteer) |

---

## 8. Conflicts vs prior docs

| Item | Prior note | This re-verify | Verdict |
|---|---|---|---|
| Heap size `0x38` | `setup-field-mapping.md`, `fn_005fd390_buildFramework.md` | yes | **match** |
| Vtable `0x9e4ee4` | buildFramework plate | yes | **match** |
| `desc+0x04 → +0x24`, `desc+0x08 → +0x28` | `fn_005fc710_steeringBuilder.md` handoff | yes via `FUN_0064f920` | **match** |
| `doesWheelSteer` at `+0x2c`, count `+0x30` | `fn_0064f840_steering.md` | yes | **match** |
| `+0x24` semantic | some notes: “normalized steer input” | ctor + reflection: **`maxSteeringAngle`** | **binary/ctor wins** for post-ctor identity |
| Tank shares size `0x38` | buildFramework | `TankSteering_ctor` calls this then swaps vtable | **match** |

---

## 9. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x64fac0` | OK — base + vtable + empty doesSteer array + `FUN_0064f920` |
| `decompile_function` `0x65e5f0` / `0x65e530` | OK — base `hkSteeringComponent` out-angle array |
| `decompile_function` `0x64f920` | OK — maxAngle / fullSpeedLimit / flags copy |
| `read_memory` / inspect `0x9e4ee4+` | OK — class `hkDefaultSteering`; fields `maxSteeringAngle`, `maxSpeedFullSteeringAngle`, `wheelsDoesSteer` |
| `get_xrefs_to` `0x64fac0` | buildFramework + TankSteering + factory thunk |
| Constants / emulation | N/A (no `DAT_*` math; pointer-heavy array grow) |

---

## 10. Port notes / non-goals

- Port needs a **`0x38`-byte** steering component with the layout in §5.
- Seed from descriptor: **`+0x24`**, **`+0x28`**, **`+0x2c[]`**, counts at **`+0x0c` / `+0x20` / `+0x30`**, zeroed out-angle array at **`+0x14`**.
- Angle formula, ramp, and speed factor are **not** in this function — see `fn_0064f840_steering.md` and `fn_00598650_steerRamp.md`.
- Emulation skipped (heap grow + descriptor pointer walk). Goldens for ctor: synthetic descriptor → expected floats/flags at the offsets above.
