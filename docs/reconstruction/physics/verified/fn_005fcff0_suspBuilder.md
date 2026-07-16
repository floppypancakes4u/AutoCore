# Verified: `Vehicle_BuildSuspensionDescriptor` @ `0x5fcff0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fcff0` – `0x005fd383` |
| Symbol | `Vehicle_BuildSuspensionDescriptor` |
| Convention | MSVC `__cdecl` (3 args on stack) |
| Callees | `FUN_004f5560` @ `0x4f5560` (wheel count), `FUN_005b3300` @ `0x5b3300` (array grow) |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` → `hkDefaultSuspension_ctor` @ `0x64e510` (size `0x68`) then `FUN_0064dda0` |
| Verified | Ghidra `decompile_function` + `read_memory` (`DAT_00aaa668`) |

RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.

---

## Role

Build the **stack suspension descriptor** consumed by `hkDefaultSuspension_ctor`. Per-wheel
arrays are grown to `wheelCount`, then filled by **front/rear fan-out**:

- Indices `[0 .. frontN)` get **Front** rest / strength / comp-damp / ext-damp scalars
  (each scalar is **broadcast** — same float on every front wheel).
- Indices `[frontN .. n)` get the matching **Rear** scalars (same broadcast pattern).
- **Hardpoint CS** is the only truly per-wheel VehSpec payload (`+0x514`, stride `0xC` vec3 → desc vec4).
- **Direction CS** is fixed `(0, -1, 0, pad)` for every wheel.

No entity-prefix multipliers, no clamps, no unit conversion in this function.

---

## Signature / args

```
void Vehicle_BuildSuspensionDescriptor(
    int    param_1,   // entity / vehicle instance
    undef  param_2,   // unused by this body
    int*   param_3    // out: suspension descriptor (stack blob → ctor)
)
```

`param_3` is treated as a base of **dword slots** (`int*`). Byte offsets = index × 4.

---

## VehSpec / front-count access chains

### Standard VehSpec (rest / strength / damp / hardpoints)

```
cloneSlot = *(int*)(param_1 + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec   = *(int*)( *(int*)(param_1 + slotOff + 0xac) + 0x3c )
```

Used for all float pair loads (`+0x55c`…`+0x578`) and hardpoint base `+0x514`.

### Front-axle count (`+0x4cc`) only

```
related   = *(int*)(param_1 + 600)          // decimal 600 = 0x258
cloneSlot = *(int*)(related + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec2  = *(int*)( *(int*)(related + slotOff + 0xac) + 0x3c )
frontN    = *(byte*)(VehSpec2 + 0x4cc)      // axle-0 wheel count
```

Same shape as steering builder; base object is `entity+0x258` rather than `entity`. For normal
vehicles this is expected to resolve to the same `VehicleSpecific`. Port should preserve the
**logical** field (`axle0WheelCount` / front count).

### Wheel count — `FUN_004f5560` @ `0x4f5560` (`__fastcall`, `this`=entity)

```
return *(byte*)( *(int*)(entity + 600) + 0xb0 );   // wheel count
```

Called many times (capacity checks + loop bounds). Same result each time for a stable entity.

---

## Descriptor layout (`param_3`)

Six parallel **hkArray**-style triples: `{ ptr, count, capacity }` where capacity high bit
`0x80000000` is the unowned/sentinel flag; usable cap = `val & 0x7fffffff`.

| Dword idx | Byte off | Type | Grow elem size | Meaning |
|---:|---:|---|---:|---|
| `0` | `+0x00` | `ptr` | `0x10` | **hardpointCS[i]** vec4 base |
| `1` | `+0x04` | `int32` | — | hardpoint count (= n) |
| `2` | `+0x08` | `uint32` | — | hardpoint capacity |
| `3` | `+0x0c` | `ptr` | `0x10` | **directionCS[i]** vec4 base |
| `4` | `+0x10` | `int32` | — | direction count (= n) |
| `5` | `+0x14` | `uint32` | — | direction capacity |
| `6` | `+0x18` | `ptr` | `4` | **restLength[i]** float base |
| `7` | `+0x1c` | `int32` | — | restLength count (= n) |
| `8` | `+0x20` | `uint32` | — | restLength capacity |
| `9` | `+0x24` | `ptr` | `4` | **strength[i]** float base |
| `10` | `+0x28` | `int32` | — | strength count (= n) |
| `11` | `+0x2c` | `uint32` | — | strength capacity |
| `0xC` | `+0x30` | `ptr` | `4` | **compDamp[i]** float base |
| `0xD` | `+0x34` | `int32` | — | compDamp count (= n) |
| `0xE` | `+0x38` | `uint32` | — | compDamp capacity |
| `0xF` | `+0x3c` | `ptr` | `4` | **extDamp[i]** float base |
| `0x10` | `+0x40` | `int32` | — | extDamp count (= n) |
| `0x11` | `+0x44` | `uint32` | — | extDamp capacity |

Descriptor span used by this body: **`0x00`…`0x44`** (18 dwords). Component size `0x68` is the
**`hkDefaultSuspension` object**, not this stack blob.

### Grow rule (identical shape for each array)

```
n   = (int)(char)FUN_004f5560()
cap = *(uint*)(desc + capOff) & 0x7fffffff
if (cap < n):
    newCap = cap * 2
    if (newCap <= n): newCap = n
    FUN_005b3300(desc + ptrOff, newCap, elemSize)
*(int*)(desc + countOff) = n
```

Grow order in the body (before fills):

1. strength (`param_3+9`, elem 4)
2. compDamp (`param_3+0xc`, elem 4)
3. extDamp (`param_3+0xf`, elem 4)
4. hardpoint (`param_3+0`, elem `0x10`)
5. direction (`param_3+3`, elem `0x10`)
6. restLength (`param_3+6`, elem 4)

---

## Rest / strength / damp front–rear fan-out (primary map)

### VehSpec scalar pairs → descriptor arrays

| Semantic | Front VehSpec | Rear VehSpec | Desc ptr slot | Desc byte | DB / reflection |
|---|---|---|---|---|---|
| **rest length** | **`+0x55c`** | **`+0x560`** | `param_3[6]` | `+0x18` | `rlSuspensionLengthFront/Rear` |
| **spring strength** | **`+0x564`** | **`+0x568`** | `param_3[9]` | `+0x24` | `rlSuspensionStrengthFront/Rear` |
| **compression damp** | **`+0x56c`** | **`+0x570`** | `param_3[0xC]` | `+0x30` | `rlSuspensionDampeningCoefficientCompressionFront/Rear` |
| **extension damp** | **`+0x574`** | **`+0x578`** | `param_3[0xF]` | `+0x3c` | `rlSuspensionDampeningCoefficientExtensionFront/Rear` |

### Index split

```
frontN = *(byte*)(VehSpec_via_entity_plus_600 + 0x4cc)
n      = FUN_004f5560()   // total wheels

// FRONT: i = 0 .. frontN-1   (only if frontN > 0)
rest[i]     = *(float*)(VehSpec + 0x55c)   // SAME float every front wheel
strength[i] = *(float*)(VehSpec + 0x564)
compDamp[i] = *(float*)(VehSpec + 0x56c)
extDamp[i]  = *(float*)(VehSpec + 0x574)

// REAR:  i = frontN .. n-1   (only if frontN < n)
rest[i]     = *(float*)(VehSpec + 0x560)   // SAME float every rear wheel
strength[i] = *(float*)(VehSpec + 0x568)
compDamp[i] = *(float*)(VehSpec + 0x570)
extDamp[i]  = *(float*)(VehSpec + 0x578)
```

Where e.g. `rest[i] = *(float*)(param_3[6] + i*4)`.

### Critical fan-out rules

1. **Two axle values only.** DB stores Front and Rear once each; the builder **replicates** them
   across all wheels of that axle. There is no per-wheel rest/strength/damp table on VehSpec.
2. **Split is pure index:** wheel `i < frontN` → front pair; else rear pair. No bitfield.
3. **Copies are verbatim float32** (`undefined4` loads/stores). No `× entity[…]` mults (unlike
   steering angle / brake torque builders).
4. **Empty axle:** if `frontN == 0`, the front loop is skipped entirely; if `frontN >= n`, the
   rear loop is skipped. Both loops can run for a normal 2+2 split (`frontN=2`, `n=4`).
5. Front loop iterates with a countdown from `frontN` and sequential byte offsets into each
   float array (`+0,+4,+8…`); rear loop indexes by wheel id `i` with `base + i*4`.

---

## Hardpoint CS + direction CS (companion fills)

### Hardpoint (per-wheel vec3 → desc vec4)

```
// VehSpec hardpoint table starts at +0x514, stride 0xC (x,y,z float)
// desc hardpointCS[i] is 0x10-aligned (x,y,z,w)

hardpointCS[i].xyz = *(vec3*)(VehSpec + 0x514 + i*0xC)
hardpointCS[i].w   = local_14   // UNINITIALIZED stack float
```

- Front loop: walks hardpoints with `local_28 += 0xC` starting at 0 (same as `i*0xC` for `i=0..frontN-1`).
- Rear loop: uses `i * 0xC` for the same table (wheel index continues past `frontN`).
- **Not** front/rear paired — every wheel has its own hardpoint in the VehSpec array.

### Direction (fixed for all wheels)

```
directionCS[i].x = 0
directionCS[i].y = DAT_00aaa668   // -1.0
directionCS[i].z = 0
directionCS[i].w = local_14      // UNINITIALIZED
```

| Symbol | Addr | LE bytes | float32 |
|---|---|---|---|
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **−1.0** |

Suspension travel direction in chassis space is **down the −Y axis**. Confirmed via
`read_memory` on `autoassault.exe`.

---

## Exact algorithm (from decompile)

```
// param_1 = entity
// param_3 = descriptor (int* / hkArray triples)

n = FUN_004f5560()
// grow + set count for: strength, compDamp, extDamp, hardpoint, direction, restLength
// (see grow order above; each count = n)

frontN = *(byte*)(VehSpec_from(entity + 0x258) + 0x4cc)
VehSpec = VehSpec_from(entity)     // standard chain
i = 0

// --- FRONT fan-out ---
if (frontN > 0):
    for i = 0 .. frontN-1:
        *(float*)(param_3[6]  + i*4)  = *(float*)(VehSpec + 0x55c)  // rest front
        *(float*)(param_3[9]  + i*4)  = *(float*)(VehSpec + 0x564)  // strength front
        *(float*)(param_3[0xc] + i*4) = *(float*)(VehSpec + 0x56c)  // compDamp front
        *(float*)(param_3[0xf] + i*4) = *(float*)(VehSpec + 0x574)  // extDamp front

        hp = (float*)(param_3[0] + i*0x10)
        hp[0] = *(float*)(VehSpec + 0x514 + i*0xc)
        hp[1] = *(float*)(VehSpec + 0x518 + i*0xc)
        hp[2] = *(float*)(VehSpec + 0x51c + i*0xc)
        hp[3] = local_14   // garbage pad

        dir = (float*)(param_3[3] + i*0x10)
        dir[0] = 0
        dir[1] = DAT_00aaa668   // -1.0
        dir[2] = 0
        dir[3] = local_14

// --- REAR fan-out ---
i = frontN
while (i < FUN_004f5560()):
    *(float*)(param_3[6]  + i*4)  = *(float*)(VehSpec + 0x560)  // rest rear
    *(float*)(param_3[9]  + i*4)  = *(float*)(VehSpec + 0x568)  // strength rear
    *(float*)(param_3[0xc] + i*4) = *(float*)(VehSpec + 0x570)  // compDamp rear
    *(float*)(param_3[0xf] + i*4) = *(float*)(VehSpec + 0x578)  // extDamp rear

    hp = (float*)(param_3[0] + i*0x10)
    hp[0] = *(float*)(VehSpec + 0x514 + i*0xc)
    hp[1] = *(float*)(VehSpec + 0x518 + i*0xc)
    hp[2] = *(float*)(VehSpec + 0x51c + i*0xc)
    hp[3] = local_14

    dir = (float*)(param_3[3] + i*0x10)
    dir[0] = 0
    dir[1] = DAT_00aaa668
    dir[2] = 0
    dir[3] = local_14

    i++
```

### Critical details

1. **No runtime mults** on rest/strength/damp — pure VehSpec → desc array fan-out.
2. **Vec4 `.w` pad is stack garbage** (`local_14` never written). C# ports may zero-pad; retail
   does not. Downstream force math uses length/strength/damp floats and direction xyz, not w.
3. **Hardpoint table is dense per wheel index** across both axles (one contiguous `+0x514` array).
4. **Direction is not data-driven** — always `(0, -1, 0)` via `DAT_00aaa668`.
5. Return: void-style fallthrough (no meaningful return used by caller).

---

## Handoff into `hkDefaultSuspension`

Caller (`0x5fd390`) constructs component size **`0x68`** via `hkDefaultSuspension_ctor` @
`0x64e510`, then `FUN_0064dda0`. Phase-0 component map (`0.4-suspension.md`):

| Component off | Source from descriptor | Meaning |
|---|---|---|
| `+0x28` | restLength array (`desc+0x18`) | rest length[] |
| `+0x44` | strength array (`desc+0x24`) | spring strength[] |
| `+0x50` | compDamp array (`desc+0x30`) | compression damping[] |
| `+0x5C` | extDamp array (`desc+0x3c`) | extension (rebound) damping[] |
| (base ctor path) | hardpoint / direction vec4 arrays | attach point + travel dir |

Runtime force: `hkDefaultSuspension_update` @ `0x64de50` (not this function) — see
`0.4-suspension.md`.

---

## Constants

| Symbol | Addr | Value | Role |
|---|---|---|---|
| `DAT_00aaa668` | `0x00aaa668` | **−1.0** (`00 00 80 bf`) | directionCS.y for every wheel |

No other `DAT_*` formula constants. Offsets (`0x55c`…`0x578`, `0x514`, `0x4cc`, pointer chain
`+4` / `+0xac` / `+0x3c` / `+600`) are address arithmetic only.

Emulation skipped: pointer-heavy VehSpec walk + dynamic `FUN_005b3300` arrays.

---

## Conflicts vs Phase-0 evidence

| Item | `0.4-suspension.md` / `setup-field-mapping.md` | This re-verify | Verdict |
|---|---|---|---|
| rest Front/Rear `+0x55c`/`+0x560` → `param_3[6]` | yes | yes | **match** |
| strength Front/Rear `+0x564`/`+0x568` → `param_3[9]` | yes | yes | **match** |
| compDamp Front/Rear `+0x56c`/`+0x570` → `param_3[0xC]` | yes | yes | **match** |
| extDamp Front/Rear `+0x574`/`+0x578` → `param_3[0xF]` | yes | yes | **match** |
| Split at `VehSpec+0x4cc` | yes | yes (via `entity+0x258` chain) | **match** (chain nuance) |
| Hardpoint `+0x514` stride `0xC` → `param_3[0]` | yes | yes | **match** |
| Direction fixed `(0,-1,0)` via `DAT_00aaa668` | yes | yes (`read_memory` = −1.0) | **match** |
| Verbatim float copy (no entity mult) | implied | confirmed | **match** |
| Broadcast Front/Rear to all wheels on axle | yes (fan-out prose) | confirmed in both loops | **match** |

**No algorithm conflict.** Binary confirms Phase-0 suspension setup mapping.

### Nuances not always spelled out in Phase-0

- Capacity grow: `max(cap*2, n)` for six arrays; elem size `4` for float arrays, `0x10` for vec4s.
- Grow order is strength → compDamp → extDamp → hardpoint → direction → restLength (fill order
  still front-then-rear across all slots).
- Front-count read walks `entity+0x258`, not `entity` — same as steering builder.
- Desc vec4 `.w` components are uninitialized stack (`local_14`).

---

## Port checklist (descriptor fill)

```
n      = wheelCount
frontN = axle0WheelCount   // VehSpec+0x4cc

// grow six arrays to n; set counts = n

for i in 0..n-1:
    if i < frontN:
        rest[i]     = rlSuspensionLengthFront          // VehSpec+0x55c
        strength[i] = rlSuspensionStrengthFront        // +0x564
        compDamp[i] = rlSuspensionDampCompressionFront // +0x56c
        extDamp[i]  = rlSuspensionDampExtensionFront   // +0x574
    else:
        rest[i]     = rlSuspensionLengthRear           // +0x560
        strength[i] = rlSuspensionStrengthRear         // +0x568
        compDamp[i] = rlSuspensionDampCompressionRear  // +0x570
        extDamp[i]  = rlSuspensionDampExtensionRear    // +0x578

    hardpointCS[i].xyz = ShockAttachPoint[i]           // VehSpec+0x514 + i*12
    hardpointCS[i].w   = 0                             // retail = stack garbage; zero OK for port
    directionCS[i]     = (0, -1, 0, 0)
```

Goldens for this builder: synthetic VehSpec (frontN, n, four Front/Rear pairs, hardpoint table)
→ expected per-wheel rest/strength/comp/ext arrays + hardpoints + directions.
Runtime spring/damper force goldens belong to `hkDefaultSuspension_update` (`0x64de50`), not this function.
