# Verified: `Vehicle_BuildSteeringDescriptor` @ `0x5fc710`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fc710` – `0x005fc831` |
| Symbol | `Vehicle_BuildSteeringDescriptor` |
| Convention | MSVC `__cdecl` (3 args on stack) |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` |
| Verified | Ghidra `decompile_function` (re-gate) |
| Constants | **none** — no `DAT_*` floats; pure VehSpec × entity mults + bitfield |

---

## Signature / args

```
void Vehicle_BuildSteeringDescriptor(
    int    param_1,   // entity / vehicle instance
    undef  param_2,   // unused by this body (caller still passes context)
    byte*  param_3    // out: steering descriptor (stack blob → ctor)
)
```

After this returns, the caller constructs either:

- `hkDefaultSteering_ctor` @ `0x64fac0` (size `0x38`), **or**
- `TankSteering_ctor` @ `0x64fc80` if `VehSpec+0x4c0 == 4`

Selection is **in the caller**, not in this builder. Both consume the same descriptor.

---

## VehSpec access chains

### Standard (maxAngle, fullSpeedLimit, doesSteer flags)

```
cloneSlot = *(int*)(param_1 + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec   = *(int*)( *(int*)(param_1 + slotOff + 0xac) + 0x3c )
```

Matches the shared setup convention in `setup-field-mapping.md`.

### Front-axle count (`+0x4cc`) only

```
related   = *(int*)(param_1 + 600)          // decimal 600 = 0x258
cloneSlot = *(int*)(related + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec2  = *(int*)( *(int*)(related + slotOff + 0xac) + 0x3c )
frontN    = *(char*)(VehSpec2 + 0x4cc)
```

Same shape, base object is `entity+0x258` rather than `entity`. For normal vehicles this is
expected to resolve to the same `VehicleSpecific`; port should preserve the **logical** field
(`axle0WheelCount` / front count) regardless of which pointer the client used.

### Wheel count helper — `FUN_004f5560` @ `0x4f5560` (`__fastcall`, `this`=entity)

```
return *(byte*)( *(int*)(entity + 600) + 0xb0 );   // wheel count
```

Called three times in the builder (capacity check, store count byte, loop bound). Same result each time.

---

## Descriptor layout (`param_3`)

| Off | Type | Written | Meaning |
|---|---|---|---|
| `+0x00` | `byte` | yes | wheel count (copy of `FUN_004f5560`) |
| `+0x04` | `float32` | yes | **maxSteeringAngle** (radians after mult) |
| `+0x08` | `float32` | yes | **maxSpeedFullSteeringAngle** (`fullSpeedLimit`) |
| `+0x0c` | `ptr` | grow via `FUN_005b3300` | `doesWheelSteer[i]` byte array base |
| `+0x10` | `int32` | yes | wheel count |
| `+0x14` | `uint32` | capacity | high bit `0x80000000` = unowned/sentinel; usable cap = `val & 0x7fffffff` |

### Grow rule (before filling flags)

```
n = (int)(char)FUN_004f5560()          // wheel count
cap = *(uint*)(desc + 0x14) & 0x7fffffff
if (cap < n):
    newCap = cap * 2
    if (newCap <= n): newCap = n
    FUN_005b3300(desc + 0xc, newCap, 1)   // element size 1
*(int*)(desc + 0x10) = n
*desc = (byte)FUN_004f5560()
```

---

## Exact algorithm (from decompile)

```
// --- scalars ---
VehSpec = VehicleSpecific_from(entity)          // chain above

// maxAngle
*(float*)(desc + 4) =
    *(float*)(VehSpec + 0x594)                  // rlSteeringMaxAngle
  * *(float*)(entity + 0x208);                  // runtime steer-angle mult

// fullSpeedLimit
*(float*)(desc + 8) =
    *(float*)(VehSpec + 0x598)                  // rlSteeringFullSpeedLimit
  * *(float*)(entity + 0x20c);                  // runtime full-speed-limit mult

// --- per-wheel doesSteer ---
frontN = *(char*)(VehSpec_via_entity_plus_600 + 0x4cc)   // axle-0 wheel count
flags  = *(byte*)(VehSpec + 0x5f0)

for (i = 0; i < wheelCount; i++):
    if (i < frontN):                             // FRONT axle
        doesWheelSteer[i] = (flags >> 2) & 1     // bit 2
    else:                                        // REAR axle
        doesWheelSteer[i] = (flags >> 3) & 1     // bit 3
```

Where `doesWheelSteer[i] = *(byte*)(*(int*)(desc + 0xc) + i)`.

### Critical details

1. **Multiplies are float × float**, no clamp, no deg→rad conversion in this function.
   Degrees vs radians is a DB/content concern; the builder stores the product as-is.
2. **Runtime multipliers** live on the **entity instance**:
   - `entity+0x208` → scales max angle
   - `entity+0x20c` → scales full-speed limit  
   These are prefix/upgrade adjust registers (see `setup-field-mapping.md` “Runtime multiplier registers”).
3. **Axle split is index-based**: wheels `[0 .. frontN)` are front; `[frontN .. n)` are rear.
   There is **no** per-wheel bit in the DB for steering — only front/rear axle flags.
4. **`+0x5f0` bit map (steering-relevant)**:
   | Bit | Mask | Role in this builder |
   |---|---|---|
   | 2 | `0x04` | front axle `doesWheelSteer` |
   | 3 | `0x08` | rear axle `doesWheelSteer` |
   Bits 0/1 are **handbrake** flags (consumed by brake builder `FUN_005fcb00`), not steering.
5. Configurations:
   - front-steer only: bit2=1, bit3=0 (standard cars)
   - rear-steer only: bit2=0, bit3=1
   - 4WS: bit2=1, bit3=1
   - no steer: both clear (flags array all 0)

---

## Handoff into `hkDefaultSteering` (`FUN_0064f920` @ `0x64f920`)

Called from `hkDefaultSteering_ctor` with the descriptor:

| Desc off | Steering object off | Field |
|---|---|---|
| `+0x04` | `+0x24` | maxSteeringAngle |
| `+0x08` | `+0x28` | maxSpeedFullSteeringAngle |
| `+0x0c` → copy | `+0x2c` | `doesWheelSteer[]` (bytes) |
| `+0x10` | `+0x30` (and `+0x20`) | wheel count |

Runtime angle math is **not** in this builder — see `hkDefaultSteering_update` @ `0x64f840`
(`steering-spec.md`): quadratic inverse-speed falloff above `fullSpeedLimit`.

---

## Conflicts vs Phase-0 evidence

| Item | `steering-spec.md` / `setup-field-mapping.md` | This re-verify | Verdict |
|---|---|---|---|
| `VehSpec+0x594 × entity+0x208` → maxAngle | yes (`desc+0x04`) | yes (`param_3+4`) | **match** |
| `VehSpec+0x598 × entity+0x20c` → fullSpeedLimit | yes (`desc+0x08`) | yes (`param_3+8`) | **match** |
| Front `doesSteer` = `(flags>>2)&1` | yes | yes | **match** |
| Rear `doesSteer` = `(flags>>3)&1` | yes | yes | **match** |
| Split at `VehSpec+0x4cc` | yes | yes (via `entity+600` chain) | **match** (chain nuance noted) |
| Wheel count from helper / `desc+0x10` | yes | yes (`FUN_004f5560` → `+0xb0`) | **match** (helper details filled) |
| Tank path when `+0x4c0==4` | caller, not builder | confirmed in `0x5fd390` | **match** |

**No algorithm conflict.** Binary confirms Phase-0 steering setup mapping.

### Nuances not in Phase-0 prose (additive only)

- Capacity grow: `max(cap*2, n)` element-size-1 array at `desc+0xc`.
- `desc+0` stores wheel count as a **byte** in addition to `desc+0x10` int.
- Front-count read walks `entity+0x258`, not `entity` — document if pointer aliasing matters in tests.

---

## Port checklist (descriptor → `HkVehicleData` / steering component)

```
maxSteeringAngle          = rlSteeringMaxAngle      * steerAngleMult      // entity+0x208
maxSpeedFullSteeringAngle = rlSteeringFullSpeedLimit * fullSpeedLimitMult // entity+0x20c
for i in 0..wheelCount-1:
    doesWheelSteer[i] = (i < axle0Count)
        ? ((flags0x5f0 >> 2) & 1)
        : ((flags0x5f0 >> 3) & 1)
```

No constants to `read_memory`. Emulation skipped (pointer-heavy VehSpec walk + dynamic array).
Goldens for this builder: synthetic `VehicleSpecific` + mults → expected desc floats/flags.
Runtime falloff goldens belong to `hkDefaultSteering_update` (`0x64f840`), not this function.
