# Verified: `hkDefaultWheels_ctor` @ `0x64fee0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkDefaultWheels_ctor` |
| Address | `0x0064fee0` |
| Body | `0x0064fee0` – `0x0064fef8` |
| Convention | MSVC `__thiscall` (`this` = ECX; descriptor on stack) |
| Heap size | **`0x390`** (written as `uint16` at `object+4` by allocator site) |
| Vtable | `PTR_FUN_009e5010` @ `0x009e5010` |
| Role | Construct stock Havok 2.3 default wheels component from descriptor |
| RE tools | `decompile_function` @ `0x64fee0`, callees `0x5fbbb0` / `0x5fa9b0` / `0x5fa6e0` / `0x5fa830`; `read_memory` on DAT constants; callers / xrefs |
| Status | **Verified** (re-read) |

Related (not this function):

- Descriptor fill: `FUN_005fcce0` @ `0x5fcce0` → see [`fn_005fcce0_wheelsBuilder.md`](fn_005fcce0_wheelsBuilder.md)
- Owner setup: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (alloc `0x390` → ctor → `FUN_0064fe40` **stack-descriptor teardown**)
- Runtime consumers: wheel array at `*(wheels+0x80)` stride `0xC0`; friction / drive arrays at `+0x28` / `+0x34` / `+0x40`; axle map at `+0x58` / counts at `+0x68` / `+0x64` (see `0.3-friction-solver.md`, `0.4-suspension.md`)

No C# in this file (RE evidence only).

---

## 1. Decompile (authoritative)

```c
// this = hkDefaultWheels* (heap 0x390)
// param_2 = wheels descriptor* (stack; filled by FUN_005fcce0)
undefined4 * __thiscall hkDefaultWheels_ctor(undefined4 *param_1, undefined4 param_2)
{
  FUN_005fbbb0(param_2);           // base wheels ctor: empty hkArrays + desc copy
  *param_1 = &PTR_FUN_009e5010;    // install default-wheels vtable
  return param_1;
}
```

Body is a thin derived ctor (same pattern as suspension / steering): base class does all storage init + descriptor copy; this only swaps the vtable.

### Callers

| Site | Role |
|---|---|
| `Vehicle_buildHavokVehicleFramework` @ `0x005fd44d` | Primary production path |
| Factory thunk `FUN_0064ff60` @ `0x0064ff60` | `alloc(0x390)` + size word + call ctor (secondary) |

### Callees (direct + transitive for this gate)

| Addr | Symbol | Role |
|---|---|---|
| `0x005fbbb0` | `FUN_005fbbb0` | Base wheels ctor (vtable + empty arrays + call copy) |
| `0x005fa9b0` | `FUN_005fa9b0` | Descriptor → object array grow/copy + axle histogram |
| `0x005fa6e0` | `FUN_005fa6e0` | Per-wheel element default init (template for new slots) |
| `0x005fa830` | `FUN_005fa830` | Per-wheel element dword copy (construct from template) |
| `0x005b3300` | `FUN_005b3300` | `hkArray` grow helper (used by copy path) |

---

## 2. Construction sequence

```
Vehicle_buildHavokVehicleFramework (0x5fd390)
  FUN_005fcce0(entity, ?, stackDesc)                // fill wheels descriptor
  stackDesc[0] = 8                                  // header stamped by caller (not builder)
  heap = alloc(0x390);  *(uint16*)(heap+4) = 0x390
  hkDefaultWheels_ctor(heap, stackDesc)             // 0x64fee0
    ├─ FUN_005fbbb0(desc)                           // base
    │    ├─ vtable = PTR_FUN_009dd2b8 (base; overwritten below)
    │    ├─ *(uint16*)(this+6) = 1
    │    ├─ empty hkArrays @ +0x10 / +0x1c / +0x28 / +0x34 / +0x40 / +0x4c / +0x58 / +0x68
    │    ├─ wheel hkArray @ +0x80 → inline storage @ +0x90, capacity 4 (0x80000004)
    │    └─ FUN_005fa9b0(desc)                      // grow/copy + axle histogram
    └─ vtable = PTR_FUN_009e5010 (default wheels)
  FUN_0064fe40(stackDesc)                           // stack-descriptor teardown (NOT the heap object)
```

`FUN_0064fe40` → `FUN_0065eb10`: clears a byte at `*desc` and writes `DAT_00a0f520` (**1000.0f**) at `desc+4`. Same class of post-helper as `FUN_0064dda0` / `FUN_0064fd30` (teardown stack descriptors after component construction). **Does not mutate the heap wheels instance.**

---

## 3. Base ctor `FUN_005fbbb0` @ `0x5fbbb0`

```c
undefined4 * __thiscall FUN_005fbbb0(undefined4 *param_1, undefined4 param_2)
{
  *(undefined2 *)((int)param_1 + 6) = 1;
  *param_1 = &PTR_FUN_009dd2b8;          // base wheels vtable

  // empty hkArray float/uint @ +0x10, +0x1c, +0x28, +0x34, +0x40, +0x4c, +0x58
  // each: { ptr=0, size=0, capacity=0x80000000 }
  // empty hkArray axleCounts @ +0x68: same sentinel

  // wheel-struct array (inline buffer for up to 4 wheels)
  param_1[0x20] = param_1 + 0x24;        // +0x80 data → this+0x90
  param_1[0x21] = 0;                     // +0x84 size
  param_1[0x22] = 0x80000004;            // +0x88 capacity = 4 | HAVOK_DONT_DEALLOCATE

  FUN_005fa9b0(param_2);                 // copy from descriptor
  return param_1;
}
```

### Why size is `0x390`

| Region | Bytes |
|---|---:|
| Header through `+0x90` (wheel array data start) | `0x90` |
| Inline wheel storage: 4 × stride `0xC0` | `0x300` |
| **Total** | **`0x390`** |

If `wheelCount > 4`, `FUN_005fa9b0` reallocates the wheel array via `FUN_005b3300(this+0x80, n, 0xC0)` (heap-backed).

---

## 4. Descriptor → object copy `FUN_005fa9b0` @ `0x5fa9b0`

### Scalars first

| Dest | Source | Meaning |
|---|---|---|
| `this+0x08` | `*desc` (`desc[0]`) | Header / flags (caller stamps `8` before ctor) |
| `this+0x0c` | `desc+0x14` (`param_2[5]`) | **Wheel count** (size of radius array = all per-wheel arrays) |

### Array copy map

Each growable array is Havok `hkArray`: `{ void* data; int size; int capacity }` (capacity high bit `0x80000000` = empty/unowned). Grow path uses heap vtable `DAT_00b05060` slots `+0x10` (alloc) / `+0x14` (free), element size `4` for float/uint tables.

| Component array | Dest base | Desc source ptr / size | Element | Havok / port name |
|---|---|---|---|---|
| Radius | `this+0x10` | `desc+0x10` / `desc+0x14` | f32 | `wheelsRadius` |
| Width | `this+0x1c` | `desc+0x1c` / `desc+0x20` | f32 | `wheelsWidth` |
| Friction μ0 | `this+0x28` | `desc+0x28` / `desc+0x2c` | f32 | `wheelsFriction` |
| Viscosity friction | `this+0x34` | `desc+0x34` / `desc+0x38` | f32 | `wheelsViscosityFriction` |
| Max friction μmax | `this+0x40` | `desc+0x40` / `desc+0x44` | f32 | `wheelsMaxFriction` |
| Force-feedback mult | `this+0x4c` | `desc+0x4c` / `desc+0x50` | f32 | `wheelsForceFeedbackMultiplier` |
| **Axle index** | **`this+0x58`** | **`desc+0x04` / `desc+0x08`** | **uint** | **`wheelsAxle`** (remapped off) |

**Same-offset copies** for `+0x10..+0x4c`. **Remap:** descriptor axle table lives at `desc+0x04`; object axle table lands at `this+0x58`.

### Wheel structs (`this+0x80`, stride `0xC0`)

1. Ensure capacity ≥ wheel count (`FUN_005b3300` if needed).
2. For each new slot: default-init a stack/template via `FUN_005fa6e0`, then `FUN_005fa830` into `*(this+0x80) + i*0xC0` (full 0xC0 dword/byte copy).
3. Set `*(this+0x84) = wheelCount`.
4. **Overwrite drive scale:** for each wheel `i`:

```
wheel[i] + 0x84  =  *(desc + 0x58)[i]     // from builder: DAT_00aaa7a4 = 15.0
```

(Note: builder stores 15.0 in `desc+0x58[]`; after ctor that payload lives **only** on each wheel’s `+0x84`, **not** as an object-level float array at `+0x58` — `+0x58` is the axle-index array.)

### Axle histogram (object `+0x64` / `+0x68`)

```
// maxAxleExclusive = 1 + max(axleIndex[i]) over wheels
// grow this+0x68 float/int array to maxAxleExclusive; zero-fill
// for each wheel i:
//   axleCounts[ axleIndex[i] ] += 1
this+0x64 = this+0x6c;   // axleCount = histogram size
```

Matches runtime use: `axleCount = wheels+0x64`, `axleWheelCount[ax] = *(wheels+0x68)[ax]`, `axleIndex[i] = *(wheels+0x58)[i]` (`0.3-friction-solver.md`).

---

## 5. Per-wheel element defaults `FUN_005fa6e0` @ `0x5fa6e0`

Default-constructs one `0xC0` wheel slot before the 15.0 overwrite. Salient fields (dword indices ×4):

| Offset | Default | Later runtime meaning (from other gates) |
|---|---|---|
| `+0x00..+0x0c` | 0 | hardpoint base (CS) |
| `+0x10..+0x1c` | 0 | hardpoint working / ray end region |
| `+0x20..+0x2c` | 0 | contact point |
| `+0x30..+0x3c` | mostly 0; `+0x34 = 1.0` | contact normal frame |
| `+0x40..+0x4c` | `+0x44 = 1.0` else 0 | chassis basis / spin frame |
| `+0x50..+0x5c` | `+0x50 = 1.0` else 0 | suspension down axis seed |
| `+0x60..+0x7c` | mostly 0; `+0x68 = 1.0` | steer/roll basis working |
| `+0x80` (byte) | **0** | in-contact flag |
| `+0x84` | **1.0** then **overwritten → 15.0** | drive-torque axle scale (`postTick` / calcWheelTorque) |
| `+0x88..+0xB4` | 0 | spin, friction writeback, susp length/vel, etc. |

`FUN_005fa830` is a straight member-wise copy of that 0xC0 blob (including the trailing byte fields).

---

## 6. Object layout after ctor (`size 0x390`)

| Offset | Type | Meaning | Who sets |
|---|---|---|---|
| `+0x00` | ptr | vtable `0x009e5010` | ctor |
| `+0x04` | u16 | heap object size `0x390` | allocator site (before ctor) |
| `+0x06` | u16 | `1` (base flag) | `FUN_005fbbb0` |
| `+0x08` | i32/flags | from `desc[0]` (caller `8`) | copy |
| `+0x0c` | i32 | **wheel count** | copy ← `desc+0x14` |
| `+0x10` | hkArray&lt;f32&gt; | `wheelsRadius[]` | copy |
| `+0x1c` | hkArray&lt;f32&gt; | `wheelsWidth[]` | copy |
| `+0x28` | hkArray&lt;f32&gt; | `wheelsFriction[]` (μ0; also **runtime torque slot** written by calcWheelTorque) | copy |
| `+0x34` | hkArray&lt;f32&gt; | `wheelsViscosityFriction[]` | copy |
| `+0x40` | hkArray&lt;f32&gt; | `wheelsMaxFriction[]` (μmax) | copy |
| `+0x4c` | hkArray&lt;f32&gt; | `wheelsForceFeedbackMultiplier[]` | copy |
| `+0x58` | hkArray&lt;i32&gt; | **axle index per wheel** | copy ← `desc+0x04` |
| `+0x64` | i32 | **axle count** (= histogram size) | histogram tail |
| `+0x68` | hkArray&lt;i32&gt; | **wheels-per-axle counts** | histogram |
| `+0x80` | hkArray&lt;Wheel&gt; | wheel structs (stride `0xC0`); data often inline at `+0x90` | grow + default + mass/scale fill |
| `+0x90` | Wheel[4] | inline storage when capacity ≤ 4 | base ctor |
| `+0x390` | — | end | — |

`hkArray` layout at each base: `{ void* data; int size; int capacity }` (12 bytes).

### Wheel struct (stride `0xC0`) — ctor-relevant slots

Full runtime layout: `0.4-suspension.md` §4. Ctor-specific:

| Offset | After ctor | Source |
|---|---|---|
| most fields | zeros / unit-axis seeds | `FUN_005fa6e0` |
| `+0x80` | `0` (not in contact) | default |
| `+0x84` | **15.0** | `desc+0x58[i]` ← `DAT_00aaa7a4` (builder) |

---

## 7. Constants (`read_memory`, length 4)

| Symbol | Address | Raw LE | float32 | Role |
|---|---|---|---|---|
| `DAT_00aaa7a4` | `0x00aaa7a4` | `00 00 70 41` | **15.0** | Builder → `desc+0x58[i]` → **`wheel[i]+0x84`** |
| `DAT_00aaa68c` | `0x00aaa68c` | `00 00 c0 3f` | **1.5** | Builder μmax scale (desc only; already in friction arrays at ctor time) |
| `DAT_00a0f718` | `0x00a0f718` | `0a d7 23 3c` | **0.01** | Builder force-feedback mult (already in `+0x4c[]`) |
| `DAT_00a0f520` | `0x00a0f520` | `00 00 7a 44` | **1000.0** | Stack-desc teardown (`FUN_0065eb10`), not component data |

Ctor itself does **not** load these DATs; it only copies whatever the descriptor already holds. Values above re-verified because they land on the live object via the builder → copy path.

---

## 8. Port notes / non-goals

- **Ctor does no VehSpec math** — all DB/constant fusion is in `FUN_005fcce0`. This gate only installs storage layout and copies.
- **`this+0x28` dual use:** setup fills μ0; runtime `VehicleAction_calcWheelTorque` **overwrites** per-wheel slots with drive torque for `postTick`. Do not treat the array as immutable friction after first sim tick.
- **`wheel+0x84 = 15.0`** is the stock Havok “wheelsMass”-style drive scale for AA’s descriptor path (not body mass). Friction solver uses it as `driveTorque * wheelScale / axleWheelCount`.
- **Axle encoding from builder:** rear → `0`, front → `1` (inverted vs “front=0” intuition); histogram still yields `axleCount == 2` for a normal car.
- Emulation skipped: pointer-heavy `hkArray` grow + heap allocator (`DAT_00b05060`). Goldens should assert post-ctor layout from a known descriptor.

---

## Related addresses

| Addr | Role |
|---|---|
| `0x005fcce0` | Wheels descriptor builder |
| `0x005fd390` | Framework build (sole production caller) |
| `0x0064ff60` | Factory thunk (`alloc 0x390` + ctor) |
| `0x0064fe40` / `0x0065eb10` | Stack-descriptor teardown after ctor |
| `0x009e5010` | `hkDefaultWheels` vtable |
| `0x009dd2b8` | Base wheels vtable (overwritten by default ctor) |

---

## Emulation

Not practical: depends on descriptor heap arrays and allocator. Port verification: build a descriptor with known `wheelCount`, radii, μ tables, axle indices, and 15.0 mass/scale slots; after ctor assert object `+0x0c`, array contents at the offsets above, `wheel[i]+0x84 == 15.0`, and `axleCounts` histogram.
