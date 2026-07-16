# Verified: `Vehicle_createVehicleAction` @ `0x4fb660`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x004fb660` |
| Body | `0x004fb660` – `0x004fb847` |
| Symbol | `Vehicle_createVehicleAction` |
| Convention | MSVC `__fastcall` — vehicle entity `this` in `ECX` (`param_1`) |
| Role | **Entry path** to Havok vehicle framework build + VehicleAction registration |
| Callers | `Vehicle_ActivateEnterWorld` @ `0x503f30` (site `0x504176`); `Vehicle_TryActivatePhysics` @ `0x501420` (site `0x5014d1`) |
| Verified | Ghidra `decompile_function` + `read_memory` + callee peeks (re-gate) |

---

## Purpose

Outer setup for live vehicle physics:

1. Guard against a second action holder on the same entity.
2. Allocate the **action/controller holder** at `entity+0x1a0` (size `0xC`).
3. Construct **`hkDefaultAnalogDriverInput`** and store it at `holder+8`.
4. Call **`Vehicle_buildHavokVehicleFramework`** (`0x5fd390`) — sole component assembler — store result at `holder+4`.
5. If `entity+0x258 != 0`, construct **`VehicleAction`** (size `0x48`) and store at `holder+0`.
6. Register the action into the world/island action list (`FUN_0055fe50` → `FUN_006292a0`).
7. Post-create mode helper `FUN_005d4050(1)`.

This is the **framework-build entry**, not a tick function. Per-step sim is later:
`VehicleAction_applyAction` (`0x598650`) → framework preUpdate / friction, etc.

---

## Tools used (this verification)

1. **`decompile_function`** `0x4fb660` program=`autoassault.exe`
2. **`get_function_by_address`** / **`analyze_function_complete`** / xrefs
3. **`decompile_function`** callees: `0x5fe5c0` (holder zero), `0x5fe020` (`hkDefaultAnalogDriverInput_ctor`), `0x55fe50` (register), `0x5d4050` (post mode), `0x5fd390` (framework — cross-check args)
4. **`read_memory`** `0xa0f710`, `0x9cd0d8`, `0xa0f2a0` (driver-input seed floats / `g_flOne`)

Did **not** use `disassemble_bytes`. Emulation skipped (alloc + pointer graph).

---

## Constants (`read_memory`)

| Symbol | Address | Raw LE bytes | float32 | Role |
|---|---|---|---|---|
| `DAT_00a0f710` | `0x00a0f710` | `33 33 33 3f` | **0.7** | Analog driver-input seed slot 0 → input `+0x28` |
| `DAT_009cd0d8` | `0x009cd0d8` | `00 00 00 3f` | **0.5** | Analog driver-input seed slot 1 → input `+0x2c` |
| `g_flOne` / `DAT_00a0f2a0` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Used inside `hkDefaultAnalogDriverInput_ctor` ramp math |

Stack seed block for driver-input ctor (this function, locals):

```
local_1c = DAT_00a0f710   // 0.7
local_18 = DAT_009cd0d8   // 0.5
local_14 = 0
local_10 = 1              // byte flag
```

---

## Entity / holder layout (from this fn)

### Vehicle entity (`param_1`)

| Offset | Type | Role in this function |
|---|---|---|
| `+0x04` | ptr | RTTI / base-offset header (`*(entity+4)+4` → vbase delta) |
| `+0x08` | ptr | **Physics object** (chassis wrapper; required non-null to proceed) |
| `+0x1a0` | ptr | **Action holder** (`0xC` heap). Gate for duplicate create; ActivateEnterWorld also gates on this == 0 |
| `+0x258` (`600`) | ptr | **Wheelset / vehicle-instance object** used for movement-mode byte and VehicleAction_ctor gate |

RTTI clonebase chain (same pattern as other vehicle RE):

```
base = entity + *( *(entity+4) + 4 )
cloneOrMap = *(base + 0xac)
// TFID / id used only in duplicate-error log: *(cloneOrMap+0x34), *(base+0x164/0x168)
```

World/list pointer passed into framework build:

```
*( *(base + 0xa8) + 0xe4a4 )   // → buildFramework param_2 → framework+0x34
```

### Holder at `entity+0x1a0` (size `0xC`)

Ghidra plate + body:

| Holder off | After success | Source |
|---|---|---|
| `+0x00` | `VehicleAction*` | `VehicleAction_ctor` **only if** `entity+0x258 != 0` |
| `+0x04` | `hkVehicleFramework*` | `Vehicle_buildHavokVehicleFramework` |
| `+0x08` | `hkDefaultAnalogDriverInput*` | `hkDefaultAnalogDriverInput_ctor` |

Zeroed via `FUN_005fe5c0` immediately after `operator_new(0xc)` (three dwords cleared before fill-in).

PushDrive and other input writers later use **`[entity+0x1a0]+8`** as the live input controller (see `0.8-struct-offsets.md`).

---

## Call graph (this function)

```
Vehicle_createVehicleAction @ 0x4fb660
  ├─ [if entity+0x1a0 != 0]
  │     FUN_007a4480("Would have duplicate vehicle actions for %d, %I64d", ...)
  │     FUN_004f7d60()                          // hard error / stop path
  │
  └─ [if entity+0x08 != 0]                      // need physics object
        operator_new(0xC) → entity+0x1a0
        FUN_005fe5c0()                          // zero holder
        heap(0x40) → hkDefaultAnalogDriverInput_ctor(seed) → holder+8
        Vehicle_buildHavokVehicleFramework(...) → holder+4
        [if entity+0x258 != 0]
           mode2 = (VehSpec_via_+0x258 + 0x4ce == 2)
           heap(0x48) → VehicleAction_ctor(entity, entity+8, framework, mode2)
                        → holder+0
        FUN_0055fe50( *holder )                 // register action → world list
        FUN_005d4050(1)                         // post-create mode = 1
```

Callees (from `get_function_callees` / decompile):
`operator_new`, `FUN_005fe5c0`, `hkDefaultAnalogDriverInput_ctor` (`0x5fe020`),
`Vehicle_buildHavokVehicleFramework` (`0x5fd390`), `VehicleAction_ctor`,
`FUN_0055fe50`, `FUN_005d4050`, `FUN_007a4480`, `FUN_004f7d60`.

---

## Exact algorithm (decompile order)

```
// this = vehicle entity (ECX)

if (*(entity + 0x1a0) != 0) {
    // Duplicate action holder — log TFID / type and abort path
    FUN_007a4480(-1,
        "Would have duplicate vehicle actions for %d, %I64d",
        cloneId, tfid_lo, tfid_hi);
    FUN_004f7d60();
    // (does not soft-return; error path)
}

if (*(entity + 0x08) == 0)
    return;                                    // no chassis physics object → no-op

// --- holder ---
holder = operator_new(0xC);
*(entity + 0x1a0) = holder;
FUN_005fe5c0(holder);                          // *0,*1,*2 = 0

// --- analog driver input (Havok heap 0x40, tag 10; size field +4 = 0x40) ---
seed = { 0.7f, 0.5f, 0.0f, flag=1 };
driverInput = hkDefaultAnalogDriverInput_ctor(seed);
*(holder + 8) = driverInput;

// --- framework (sole setup) ---
worldSlot = *(*(entity + *( *(entity+4)+4 ) + 0xa8) + 0xe4a4);

framework = Vehicle_buildHavokVehicleFramework(
    entity,          // param_1 — vehicle
    worldSlot,       // param_2 — stored at framework+0x34
    0,               // param_3 — forwarded into Build* descriptors
    driverInput,     // param_4 — input component for framework
    *(entity + 8)    // param_5 — physics / chassis wrapper
    // call site also pushes a trailing 0 (stack; not a 6th named formal in callee sig)
);
*(holder + 4) = framework;

// --- VehicleAction (requires +0x258) ---
obj258 = *(entity + 0x258);                    // decimal 600 in decompile
if (obj258 != 0) {
    // Same clonebase chain as other vehicle-data walks, rooted at +0x258 object:
    //   VehSpec = *(*(obj258 + *( *(obj258+4)+4 ) + 0xac) + 0x3c)
    mode2 = ( *(char*)(VehSpec + 0x4ce) == 2 ); // movement-mode: analog ramp

    // Heap size 0x48 both branches; secondary alloc tag differs (0x24 when mode2)
    action = VehicleAction_ctor(
        entity,
        *(entity + 8),                         // physics object
        framework,                             // *(holder+4)
        mode2);
    *(holder + 0) = action;
}

// Register *holder (VehicleAction*) into island/world action list
FUN_0055fe50( **(entity + 0x1a0) );            // uses vtable +0x18 collect, FUN_006292a0
FUN_005d4050(1);                               // set entity-side mode helper to case 1
return;
```

---

## `hkDefaultAnalogDriverInput_ctor` seed → fields (`0x5fe020`)

Ctor body (verified this pass) maps the 16-byte seed:

| Ctor write | Source | Value from createVehicleAction |
|---|---|---|
| vtable | `PTR_FUN_009dd368` | — |
| `+0x28` (`param_1[10]`) | seed[0] | **0.7** |
| `+0x2c` (`param_1[11]`) | seed[1] | **0.5** |
| `+0x38` (`param_1[14]`) | seed[2] | **0.0** |
| `+0x3c` (byte) | seed[3] | **1** |
| `+0x34` | `(seed1) * (seed0 - seed2)` | `0.5 * 0.7 = 0.35` |
| `+0x30` | `(1 - that) / ((1 - seed2) - (seed0 - seed2))` | ramp scale using `g_flOne` |

Also zeros throttle/steer-related slots (`+0x0c..+0x20` region) and sets small status bytes (`+0x18`, `+0x19`, `+0x24`, `+0x25`, halfword at `+0x06 = 1`).

Runtime input slots used by PushDrive remain at driver-input `+0x20` (throttle float) / `+0x24` (handbrake byte) — distinct from the seed ramp constants above.

---

## Movement-mode / VehicleAction branch

```
mode2 = ( *(char*)(VehSpec_from_entity+0x258 + 0x4ce) == 2 )
```

| `+0x4ce` | Meaning (cross-map) | Alloc path in this fn |
|---|---|---|
| `== 2` | Analog ramp movement mode | heap `(0x48, 0x24)` then `VehicleAction_ctor(..., true)` |
| else | Non-analog | heap size still tagged `0x48`, `VehicleAction_ctor(..., false)` |

If **`entity+0x258 == 0`**:
- Framework **is still built** and stored at `holder+4`.
- Driver input **is still built** at `holder+8`.
- **`VehicleAction_ctor` is skipped** → `holder+0` stays `0`.
- `FUN_0055fe50(*holder)` still runs with a **null** action pointer → unsafe.

Separately, **`Vehicle_buildHavokVehicleFramework`** itself calls `FUN_004f5560` using `entity+0x258` (`param_1[0x96]`) for wheel count during the speed-governor tail — **null `+0x258` AVs inside framework build** (`FUN_004F5560` / related), matching `docs/nullWheels.md` and `OWNER_WHEEL_RACE_RE.md`.

---

## Registration: `FUN_0055fe50` (`0x55fe50`)

```
// thiscall context + VehicleAction*
// bumps action ref halfword at +6
// vcall action+0x18 → collect related bodies
// FUN_006292a0(action)  — add to world/island action list (see 0.1-step-rate.md)
// FUN_0062a3d0(...)
```

This is how `VehicleAction_applyAction` (`0x598650`, vtable slot `+0x14`) later receives island substeps.

---

## Callers (gates into this entry)

### `Vehicle_ActivateEnterWorld` @ `0x503f30` (xref `0x504176`)

Decompile gate (typed field alias resolves to **`entity+0x1a0`**):

```
if (entity+0x1a0 == 0)
    Vehicle_createVehicleAction();
```

No check of `entity+0x258` before call — owner-present activate can reach create with null wheelset (race).

### `Vehicle_TryActivatePhysics` @ `0x501420` (xref `0x5014d1`)

Second production entry; same create function.

---

## Downstream: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390`

**Sole** callee that constructs Havok vehicle components. Authoritative order (already verified in `fn_005fd390_buildFramework.md`):

```
Wheels → Chassis → Steering (Tank|Default) → WheelCollide → Transmission
      → Brake → Suspension → Aerodynamics → AngularVelocityDamper → Framework
```

**No `hkDefaultEngine`.** Engine torque is AA-layer (`VehicleAction_calcWheelTorque` + `VehicleEngine_torqueCurve2D`).

Args from this entry (mapped):

| Formal | Value from createVehicleAction |
|---|---|
| `param_1` | vehicle entity |
| `param_2` | `*(map+0xe4a4)` → written to `framework+0x34` |
| `param_3` | `0` |
| `param_4` | `hkDefaultAnalogDriverInput*` |
| `param_5` | `entity+0x08` physics object |

Return: `hkVehicleFramework*` size `0x360` → `holder+4`.

---

## Conflicts vs existing evidence

| Item | Prior docs | This re-verify | Verdict |
|---|---|---|---|
| Entry address | `0x4fb660` (setup-field-mapping, avd-airstab, 0.1-step-rate); **`0x4fb6a0` in 0.2-mass-inertia** | Function entry **`0x004fb660`**, body ends `0x004fb847` | **Binary: `0x4fb660`.** `0x4fb6a0` is mid-body, not entry |
| Sole framework builder | `0x5fd390` | call site in this fn | **match** |
| Holder `entity+0x1a0` = `{action, framework, driverInput}` | plate comment + 0.8-struct-offsets (`[+0x1a0]+8` = controller) | yes | **match** |
| Registration `FUN_0055fe50` → `FUN_006292a0` | 0.1-step-rate | yes | **match** |
| Mode byte `+0x4ce == 2` | 0.8 / steering-spec | `VehicleAction_ctor` 4th arg | **match** |
| Null `+0x258` race | nullWheels / OWNER_WHEEL_RACE | create still builds framework; action ctor gated; `FUN_004f5560` inside build uses `+0x258` | **match (crash surface)** |
| Driver-input seeds 0.7 / 0.5 | not previously tabulated | `read_memory` confirmed | **new verified constants** |

---

## Porting notes (no C# in this file)

When implementing the server “create vehicle physics” entry:

1. **One holder per entity** — refuse duplicate create if action/framework already present.
2. **Require chassis physics object** (`entity+0x08` analogue) before build.
3. **Require wheelset / `+0x258` analogue before framework build** — retail AVs otherwise; do not port the null race.
4. Build order: **driver input → framework (`0x5fd390` path) → VehicleAction (mode from `VehSpec+0x4ce`) → register for tick**.
5. Preserve seed constants **0.7 / 0.5 / 0 / flag 1** if the analog input ramp is shared with client.
6. Do not invent `hkDefaultEngine` at this layer; wire AA torque later via action tick.

---

## Related evidence

| Doc | Relation |
|---|---|
| [`fn_005fd390_buildFramework.md`](fn_005fd390_buildFramework.md) | Sole framework builder; component order |
| [`../setup-field-mapping.md`](../setup-field-mapping.md) | VehSpec → Havok field map (call path root) |
| [`../0.1-step-rate.md`](../0.1-step-rate.md) | Action registration / substep path |
| [`../0.8-struct-offsets.md`](../0.8-struct-offsets.md) | `+0x1a0`, `+0x258`, `+0x4ce`, controller slots |
| [`../../nullWheels.md`](../../nullWheels.md) | Null `+0x258` AV during create nest |
| [`../../debugger-hits/OWNER_WHEEL_RACE_RE.md`](../../debugger-hits/OWNER_WHEEL_RACE_RE.md) | Owner activate → create without wheelset |
