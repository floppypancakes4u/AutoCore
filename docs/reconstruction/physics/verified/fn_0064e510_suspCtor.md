# Verified: `hkDefaultSuspension_ctor` @ `0x64e510`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkDefaultSuspension_ctor` |
| Address | `0x0064e510` |
| Body | `0x0064e510` – `0x0064e554` |
| Convention | MSVC `__thiscall` (`this` = ECX; descriptor on stack) |
| Heap size | **`0x68`** (written as `uint16` at `object+4` by allocator site) |
| Vtable | `PTR_FUN_009e4c00` @ `0x009e4c00` |
| Role | Construct stock Havok 2.3 default suspension component from descriptor |
| RE tools | `decompile_function` @ `0x64e510`, callees `0x65e070` / `0x64df10` / `0x65d930`; `read_memory` vtable + `DAT_00aaa668`; xrefs |
| Status | **Verified** (re-read) |

Related (not this function):

- Descriptor fill: `Vehicle_BuildSuspensionDescriptor` @ `0x5fcff0`
- Owner setup: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (alloc `0x68` → ctor → `FUN_0064dda0` descriptor teardown)
- Runtime force: `hkDefaultSuspension_update` @ `0x64de50` (see `0.4-suspension.md`)
- Map evidence: `0.4-suspension.md` §5–6, `setup-field-mapping.md` Suspension section

No C# in this file (RE evidence only).

---

## 1. Decompile (authoritative)

```c
undefined4 * __thiscall hkDefaultSuspension_ctor(undefined4 *param_1, undefined4 param_2)
{
  // param_1 = this (hkDefaultSuspension*, size 0x68)
  // param_2 = suspension descriptor* (built by Vehicle_BuildSuspensionDescriptor)

  FUN_0065e070(param_2);                 // base hkVehicleSuspension ctor + copy hardpoint/dir/restLen
  *param_1 = &PTR_FUN_009e4c00;          // install default-suspension vtable

  // empty hkArray strength  @ +0x44 / +0x48 / +0x4c
  param_1[0x11] = 0;                     // +0x44 ptr
  param_1[0x12] = 0;                     // +0x48 size
  param_1[0x13] = 0x80000000;            // +0x4c capacity (unowned flag)

  // empty hkArray compressionDamp @ +0x50 / +0x54 / +0x58
  param_1[0x14] = 0;                     // +0x50 ptr
  param_1[0x15] = 0;                     // +0x54 size
  param_1[0x16] = 0x80000000;            // +0x58 capacity

  // empty hkArray extensionDamp @ +0x5c / +0x60 / +0x64
  param_1[0x17] = 0;                     // +0x5c ptr
  param_1[0x18] = 0;                     // +0x60 size
  param_1[0x19] = 0x80000000;            // +0x64 capacity

  FUN_0064df10(param_2);                 // copy strength / compDamp / extDamp from descriptor
  return param_1;
}
```

Disassembly matches (body ends `RET 0x4` — one stack arg):

```
0064e510  PUSH ESI / PUSH EDI
0064e516  PUSH EDI                  ; descriptor
0064e517  MOV ESI, ECX              ; this
0064e519  CALL FUN_0065e070
0064e51e  MOV dword ptr [ESI], 0x9e4c00
0064e524  XOR EAX, EAX
0064e526  MOV [ESI+0x44], EAX      ; strength ptr/size = 0
0064e529  MOV [ESI+0x48], EAX
0064e52c  MOV ECX, 0x80000000
0064e531  MOV [ESI+0x4c], ECX      ; strength cap
0064e534  MOV [ESI+0x58], ECX      ; compDamp cap
0064e537  MOV [ESI+0x50], EAX
0064e53a  MOV [ESI+0x54], EAX
0064e53d  MOV [ESI+0x64], ECX      ; extDamp cap
0064e540  PUSH EDI
0064e541  MOV ECX, ESI
0064e543  MOV [ESI+0x5c], EAX
0064e546  MOV [ESI+0x60], EAX
0064e549  CALL FUN_0064df10
0064e54e  POP EDI / MOV EAX, ESI / POP ESI / RET 4
```

---

## 2. Construction sequence

```
Vehicle_buildHavokVehicleFramework (0x5fd390)
  Vehicle_BuildSuspensionDescriptor(entity, ?, stackDesc)   // 0x5fcff0
  heap = alloc(0x68);  *(uint16*)(heap+4) = 0x68
  hkDefaultSuspension_ctor(heap, stackDesc)                 // 0x64e510
    ├─ FUN_0065e070(desc)                                   // base class
    │    ├─ vtable = PTR_FUN_009e70d0 (base)
    │    ├─ *(uint16*)(this+6) = 1
    │    ├─ init 4 empty hkArrays @ +0x10 / +0x1c / +0x28 / +0x34
    │    └─ FUN_0065d930(desc)                              // copy base fields
    ├─ vtable = PTR_FUN_009e4c00 (default suspension)
    ├─ init 3 empty hkArrays @ +0x44 / +0x50 / +0x5c
    └─ FUN_0064df10(desc)                                   // copy subclass arrays
  FUN_0064dda0(...)                                         // post: stack-descriptor teardown (not component)
```

### Callers of `0x64e510`

| Site | Role |
|---|---|
| `Vehicle_buildHavokVehicleFramework` @ `0x005fd6b7` | Primary production path |
| Factory thunk @ `0x0064e65c` | `alloc(0x68)` + set size word + call ctor + `ret` (secondary) |

`get_function_callees`: `FUN_0065e070`, `FUN_0064df10` only.

---

## 3. Base ctor `FUN_0065e070` @ `0x65e070`

```c
undefined4 * __thiscall FUN_0065e070(undefined4 *param_1, undefined4 param_2)
{
  *(undefined2 *)((int)param_1 + 6) = 1;
  *param_1 = &PTR_FUN_009e70d0;          // base suspension vtable (overwritten by default ctor)

  // hardpointCS hkArray (vec4) @ +0x10
  param_1[4] = 0;  param_1[5] = 0;  param_1[6] = 0x80000000;
  // directionCS hkArray (vec4) @ +0x1c
  param_1[7] = 0;  param_1[8] = 0;  param_1[9] = 0x80000000;
  // restLength  hkArray (f32)  @ +0x28
  param_1[10] = 0; param_1[0xb] = 0; param_1[0xc] = 0x80000000;
  // forceOut    hkArray (f32)  @ +0x34
  param_1[0xd] = 0; param_1[0xe] = 0; param_1[0xf] = 0x80000000;

  FUN_0065d930(param_2);                 // grow/copy from descriptor
  return param_1;
}
```

### Base copy `FUN_0065d930` @ `0x65d930` (summary)

| Step | Component dest | Descriptor source | Element |
|---|---|---|---|
| 1 | `this+0x0c = desc.size` (hardpoint count) | `desc+0x04` (`param_2[1]`) | scalar count |
| 2 | forceOut `@+0x34` grow to count, **zero-fill** | same count | f32 |
| 3 | hardpointCS `@+0x10` realloc+memcpy | `desc+0x00` ptr / `desc+0x04` size | **vec4** (stride `0x10`) |
| 4 | directionCS `@+0x1c` realloc+memcpy | `desc+0x0c` ptr / `desc+0x10` size | **vec4** (stride `0x10`) |
| 5 | restLength `@+0x28` realloc+memcpy | `desc+0x18` ptr / `desc+0x1c` size | **f32** (stride `4`) |

Allocator path uses `DAT_00b05060` vtable `+0x10` (alloc) / `+0x14` (free), same heap as framework builders. `0x80000000` capacity bit = “not owned / empty sentinel” (Havok `hkArray` convention).

---

## 4. Subclass copy `FUN_0064df10` @ `0x64df10`

```c
void __thiscall FUN_0064df10(int this, int desc)
{
  // wheel count (subclass field)
  *(this + 0x40) = *(desc + 0x28);           // = strength array size

  // strength[]: desc+0x24 → this+0x44  (count = desc+0x28)
  // compressionDamp[]: desc+0x30 → this+0x50  (count = desc+0x34)
  // extensionDamp[]:   desc+0x3c → this+0x5c  (count = desc+0x40)
  // each: ensure capacity, set size, dword-copy source→dest
}
```

Confirmed mapping (matches `0.4-suspension.md` / setup map):

| Component | Array | Desc source |
|---|---|---|
| `this+0x40` | wheel count (i32) | `desc+0x28` (strength size) |
| `this+0x44` | spring strength[] | `desc+0x24` / size `desc+0x28` |
| `this+0x50` | compression damping[] | `desc+0x30` / size `desc+0x34` |
| `this+0x5c` | extension damping[] | `desc+0x3c` / size `desc+0x40` |

---

## 5. Object layout after ctor (`size 0x68`)

| Offset | Type | Meaning | Who sets |
|---|---|---|---|
| `+0x00` | ptr | vtable `0x009e4c00` | ctor |
| `+0x04` | u16 | heap object size `0x68` | allocator site (before ctor) |
| `+0x06` | u16 | `1` (base flag) | `FUN_0065e070` |
| `+0x08` | ptr | framework back-ptr | **later** (framework assembly; not this ctor) |
| `+0x0c` | i32 | wheel count (base) | `FUN_0065d930` ← hardpoint count |
| `+0x10` | hkArray&lt;vec4&gt; | hardpoint CS (ShockAttachPoints) | base copy |
| `+0x1c` | hkArray&lt;vec4&gt; | suspension direction CS | base copy |
| `+0x28` | hkArray&lt;f32&gt; | rest length[] | base copy |
| `+0x34` | hkArray&lt;f32&gt; | **output force[]** (zeroed; filled by update) | base copy (zeros) |
| `+0x40` | i32 | wheel count (subclass; used by update) | `FUN_0064df10` |
| `+0x44` | hkArray&lt;f32&gt; | spring strength[] | subclass copy |
| `+0x50` | hkArray&lt;f32&gt; | compression damping[] | subclass copy |
| `+0x5c` | hkArray&lt;f32&gt; | extension damping[] | subclass copy |
| `+0x68` | — | end | — |

`hkArray` layout at each base: `{ void* data; int size; int capacity }` (12 bytes). Capacity high bit `0x80000000` when empty/unowned.

### Runtime consumers (cross-check, not re-implemented here)

`hkDefaultSuspension_update` @ `0x64de50` reads:

- `this+0x08` → framework → wheels / chassis RB
- `this+0x28[i]` rest length, `this+0x34[i]` force out
- `this+0x40` wheel count
- `this+0x44[i]` strength
- `this+0x50` / `this+0x5c` damp arrays selected by `wheel+0xB4` sign

---

## 6. Descriptor layout (input to ctor)

Filled by `Vehicle_BuildSuspensionDescriptor` @ `0x5fcff0`. Six `hkArray`s on the stack desc:

| Desc dwords | Byte off | Element | VehSpec / source |
|---|---|---|---|
| `[0..2]` | `+0x00` | hardpoint **vec4** | `VehSpec+0x514` (vec3/wheel, w pad); front then rear |
| `[3..5]` | `+0x0c` | direction **vec4** | fixed `(0, DAT_00aaa668, 0)` = **`(0, -1, 0)`** |
| `[6..8]` | `+0x18` | rest length f32 | Front `+0x55c` / Rear `+0x560` (`rlSuspensionLength*`) |
| `[9..0xb]` | `+0x24` | strength f32 | Front `+0x564` / Rear `+0x568` (`rlSuspensionStrength*`) |
| `[0xc..0xe]` | `+0x30` | compression damp f32 | Front `+0x56c` / Rear `+0x570` |
| `[0xf..0x11]` | `+0x3c` | extension damp f32 | Front `+0x574` / Rear `+0x578` |

Front/rear split: index `< (byte)VehSpec+0x4cc` (`NumFrontWheels`) uses Front; remainder Rear.

### Constant (raw `read_memory`)

| Address | Hex (4 B) | Float | Role |
|---|---|---|---|
| `DAT_00aaa668` @ `0x00aaa668` | `00 00 80 bf` | **−1.0** | Suspension direction Y in descriptor (down axis) |

No other float `DAT_*` loads inside `0x64e510` itself.

---

## 7. Vtable `PTR_FUN_009e4c00` @ `0x009e4c00`

`read_memory` 28 bytes LE:

| Slot | Address | Notes |
|---:|---|---|
| 0 | `0x0064e560` | scalar deleting dtor → body free at `FUN_0064e590` (frees three subclass arrays then base) |
| 1 | `0x005ffd80` | shared base slot (`hkAnalogDI_vtbl1` name in DB — shared stub) |
| 2 | `0x005ffdb0` | shared base slot |
| 3 | `0x0064e630` | small RTTI/type helper region (near factory) |
| 4 | `0x005ffc80` | shared stub |
| 5 | **`0x0064de50`** | **`hkDefaultSuspension_update`** (component tick slot `vtbl+0x14`) |
| 6 | `0x009e4c94` | type-info / trailing meta |

---

## 8. Port notes / non-goals

- **Ctor only** — no spring/damper math here. Force formula is exclusively in `0x64de50`.
- Ports that materialize a suspension component must:
  1. Size object **`0x68`**.
  2. Own **seven** arrays: hardpoint, direction, restLength, forceOut, strength, compDamp, extDamp.
  3. Zero forceOut at init; keep restLength / strength / both damps parallel with wheel count.
  4. Direction defaults to **world-down in chassis space** `(0,-1,0)` unless descriptor differs (AA always writes that).
  5. Set framework back-ptr (`+0x08`) when wiring into `hkVehicleFramework` (not in this function).
- `FUN_0064dda0` after the framework call is **descriptor teardown**, not part of component state.
- Emulation skipped: pointer/heap-heavy; goldens for force math belong under the update function, not the ctor.
- No C# in this file.
