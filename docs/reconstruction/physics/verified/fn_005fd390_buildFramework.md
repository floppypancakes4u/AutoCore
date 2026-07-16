# Verified: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fd390` |
| Body | `0x005fd390` – `0x005fda48` |
| Symbol | `Vehicle_buildHavokVehicleFramework` |
| Sole caller | `Vehicle_createVehicleAction` @ `0x4fb660` (xref `0x4fb76e`) |
| Return | `int` — heap `hkVehicleFramework*` (`size 0x360`) |
| Verified | Ghidra `decompile_function` + `get_function_callees` + component-ctor peeks (re-gate) |

---

## Purpose

**Sole** vehicle-physics setup function: builds every `hkVehicle*` simulation component from
entity / `VehicleSpecific`, wires them into an `hkVehicleFramework`, then writes a gear-ratio
weighted speed-governor constant to `entity+0x110` (`param_1[0x44]`).

**No `hkDefaultEngine`** — AA replaced Havok's engine; torque is produced later by
`VehicleAction_calcWheelTorque` (`0x598040`) + `VehicleEngine_torqueCurve2D` (`0x4a9750`).

---

## Construction order (authoritative)

Each component follows the same pattern unless noted:

```
Build*Descriptor / FUN_*desc  →  heap alloc (DAT_00b05060+0x10, size N)  →  *ctor  →  post-helper
```

Heap objects store their size as a `uint16` at `object+4` immediately after alloc.

### Phase 0 — vehicle-data descriptor (not a component)

| # | Call | Address | Role |
|--:|---|---|---|
| 0a | `FUN_00650020` | `0x650020` | Default-init framework/vehicle-data desc (`+0x28..+0x58`; inertia slots `+0x44/+0x48/+0x4c` = 1.0; `+0x3c` = `DAT_00a110d8`) |
| 0b | `FUN_005fc620` | `0x5fc620` | Overlay desc from `VehicleSpecific` (incl. `rlRVInertia*` → chassis unit inertia) |
| 0c | `FUN_0064ff90` | `0x64ff90` | Thin wrapper → `FUN_00649e10` (desc container prep) |

### Phase 1 — components (alloc → ctor), in call order

| # | Builder (desc) | Addr | Heap size | Component ctor | Ctor addr | Post / notes |
|--:|---|---|---:|---|---|---|
| 1 | `FUN_005fcce0` | `0x5fcce0` | **`0x390`** | `hkDefaultWheels_ctor` | `0x64fee0` | then `FUN_0064fe40` |
| 2 | *(inline / vcall)* | — | **`0x40`** | `hkDefaultChassis_ctor` | `0x64fdf0` | Prior vcall `(*param_1+0x28)()` feeds chassis input; then `FUN_0064fd30`. Chassis holds `hkRigidBody*` at **`chassis+0x3c`** (body built earlier by clonebase loader, not here). |
| 3 | `Vehicle_BuildSteeringDescriptor` | `0x5fc710` | **`0x38`** | **branch** (see below) | — | then `FUN_005d6720` |
| 3a | — | — | `0x38` | `TankSteering_ctor` | `0x64fc80` | **iff** `VehSpec+0x4c0 == 4` |
| 3b | — | — | `0x38` | `hkDefaultSteering_ctor` | `0x64fac0` | **else** (default car steering) |
| 4 | `FUN_005fc3d0` | `0x5fc3d0` | **`0x3c`** | `FUN_005d6640` | `0x5d6640` | Raycast wheel-collide component; vtable `PTR_FUN_009dad44`. Copies 10 dwords from desc. then `FUN_0064f750` |
| 5 | `Vehicle_BuildTransmissionDescriptor` | `0x5fc840` | **`0x60`** | `hkDefaultTransmission_ctor` | `0x64f610` | then `FUN_0064ef20` |
| 6 | `FUN_005fcb00` | `0x5fcb00` | **`0x54`** | `hkDefaultBrake_ctor` | `0x64ed40` | then `FUN_0064e670` |
| 7 | `Vehicle_BuildSuspensionDescriptor` | `0x5fcff0` | **`0x68`** | `hkDefaultSuspension_ctor` | `0x64e510` | then `FUN_0064dda0` |
| 8 | `Vehicle_BuildAerodynamicsDescriptor` | `0x5fc4f0` | **`0x50`** | `hkDefaultAerodynamics_ctor` | `0x64da90` | then `FUN_0064d930` |
| 9 | *(inline, no Build\* helper)* | — | **`0x14`** | `hkAngularVelocityDamper_ctor` | `0x64d900` | 3 floats: `VehSpec+0x5b8/0x5bc` × `entity[0x84]`, and `VehSpec+0x5c0` (threshold). Ptr appended to damper array (`local_1d8` / `FUN_005b3370` grow). |
| 10 | *(framework owns all prior ptrs)* | — | **`0x360`** | `hkVehicleFramework_ctor` | `0x64cd30` | `framework+0x34 = param_2`. **This is the return value.** |

### Compact builder order (desc builders only)

```
FUN_005fcce0                         // wheels
(chassis: no separate Build* desc)
Vehicle_BuildSteeringDescriptor      // 0x5fc710
FUN_005fc3d0                         // wheel-collide
Vehicle_BuildTransmissionDescriptor  // 0x5fc840
FUN_005fcb00                         // brake
Vehicle_BuildSuspensionDescriptor    // 0x5fcff0
Vehicle_BuildAerodynamicsDescriptor  // 0x5fc4f0
(inline AVD params)
hkVehicleFramework_ctor              // final assembly
```

### Compact component construction order

```
Wheels → Chassis → Steering (Tank|Default) → WheelCollide → Transmission
      → Brake → Suspension → Aerodynamics → AngularVelocityDamper → Framework
```

---

## Phase 2 — speed-governor precompute (tail)

After `hkVehicleFramework_ctor`, before descriptor teardown:

```
VehSpec = *(*(entity + *(entity[1]+4) + 0xac) + 0x3c)

fVar8 = (VehSpec+0x6b4) / ( (VehSpec+0x6c4) * VehSpec+0x6d0[ lastGear ] ) * DAT_009dd348
        // lastGear index = (char)(VehSpec+0x699 /* tinNumberOfGears */) - 1

fVar10 = 0
for i in 0 .. wheelCount-1:          // wheelCount via FUN_004f5560
    axle0 = (byte)VehSpec+0x4cc
    if i < axle0:
        share = (VehSpec+0x5e8) / axle0          // front torque split
    else:
        share = (VehSpec+0x5ec) / (wheelCount - axle0)  // rear
    fVar10 += share * (VehSpec+0x600)[i] * fVar8

entity[0x44] = fVar10   // vehicle+0x110 — top-speed / governor constant
```

### Teardown (stack desc dtors / frees — not construction)

| Call | Address | When |
|---|---|---|
| `FUN_005fde60` | `0x5fde60` | after speed write |
| `FUN_005fdb80` | `0x5fdb80` | |
| `FUN_005fdaf0` | `0x5fdaf0` | |
| heap free `DAT_00b05060+0x14` | — | conditional |
| `FUN_005fdc30` | `0x5fdc30` | |
| free damper array | — | if `local_1d8 >= 0` |

---

## Steering branch (exact predicate)

```
VehSpec = *(*(param_1 + *(param_1[1]+4) + 0xac) + 0x3c)
if (*(char*)(VehSpec + 0x4c0) == 4)
    TankSteering_ctor      // tracked
else
    hkDefaultSteering_ctor // wheeled
```

Both paths allocate **`0x38`** and share the same steering descriptor from
`Vehicle_BuildSteeringDescriptor`.

---

## AVD inline inputs (component #9)

| Input | Source | Transform |
|---|---|---|
| normalSpinDamping | `VehSpec+0x5b8` (`rlAVDNormalSpinDamping`) | × `param_1[0x84]` |
| collisionSpinDamping | `VehSpec+0x5bc` (`rlAVDCollisionSpinDamping`) | × `param_1[0x84]` |
| collisionThreshold | `VehSpec+0x5c0` | as-is (decompiler shows as pointer-typed; float payload) |

---

## Callees inventory (from `get_function_callees`)

### Descriptor builders
`FUN_005fcce0`, `Vehicle_BuildSteeringDescriptor` (`0x5fc710`), `FUN_005fc3d0`,
`Vehicle_BuildTransmissionDescriptor` (`0x5fc840`), `FUN_005fcb00`,
`Vehicle_BuildSuspensionDescriptor` (`0x5fcff0`), `Vehicle_BuildAerodynamicsDescriptor` (`0x5fc4f0`),
`FUN_005fc620`

### Component ctors
`hkDefaultWheels_ctor` (`0x64fee0`), `hkDefaultChassis_ctor` (`0x64fdf0`),
`TankSteering_ctor` (`0x64fc80`), `hkDefaultSteering_ctor` (`0x64fac0`),
`FUN_005d6640` (wheel-collide), `hkDefaultTransmission_ctor` (`0x64f610`),
`hkDefaultBrake_ctor` (`0x64ed40`), `hkDefaultSuspension_ctor` (`0x64e510`),
`hkDefaultAerodynamics_ctor` (`0x64da90`), `hkAngularVelocityDamper_ctor` (`0x64d900`),
`hkVehicleFramework_ctor` (`0x64cd30`)

### Supporting
`FUN_00650020`, `FUN_0064ff90`, post-helpers `FUN_0064fe40` / `0064fd30` / `005d6720` /
`0064f750` / `0064ef20` / `0064e670` / `0064dda0` / `0064d930`,
`FUN_004f5560` (wheel count), `FUN_005b3370` (array grow), teardown `FUN_005fde60` /
`005fdb80` / `005fdaf0` / `005fdc30`

**Absent:** any `hkDefaultEngine` / engine ctor.

---

## Conflicts vs existing evidence

| Item | `setup-field-mapping.md` / `0.2-mass-inertia.md` | This re-verify | Verdict |
|---|---|---|---|
| Builder order Wheels→Chassis→Steer→Collide→Xmit→Brake→Susp→Aero→AVD→Framework | yes | yes (decompile body) | **match** |
| Heap sizes 0x390 / 0x40 / 0x38 / 0x3c / 0x60 / 0x54 / 0x68 / 0x50 / 0x14 / 0x360 | yes | yes | **match** |
| Tank vs Default steering on `VehSpec+0x4c0 == 4` | yes | yes | **match** |
| No `hkDefaultEngine` | yes | yes | **match** |
| AVD from `+0x5b8/+0x5bc/+0x5c0` × `entity[0x84]` | yes | yes | **match** |
| Speed write to `entity[0x44]` / `vehicle+0x110` | yes | yes | **match** |
| Sole caller `Vehicle_createVehicleAction` | `0x4fb660` / `0x4fb6a0` (docs vary) | function entry **`0x4fb660`**, call site `0x4fb76e` | **match entry `0x4fb660`** |
| Chassis rigid body created here | no (asset loader) | confirmed — only wraps existing body at `chassis+0x3c` | **match** |

**No construction-order conflict.** Binary order matches `setup-field-mapping.md`.

### Decompiler caveats (do not treat as semantic)

- Stack temps (`uStack_1f0`, etc.) are **reused** across components; later assignments do not
  overwrite earlier component identities — Ghidra reuse only.
- Some post-helpers / desc-dtor names remain `FUN_*`; identity as “descriptor teardown /
  transfer helpers” is from call-site placement, not renamed symbols.
- Wheel-collide class string is still unconfirmed; vtable `0x9dad44` / size `0x3c` is solid.

---

## Porting notes (no C# in this file)

When implementing framework assembly on the server:

1. Preserve **this exact component construction order** if any step depends on prior state
   (retail builds descriptors independently then hands all pointers to `hkVehicleFramework_ctor`).
2. Branch steering on vehicle-type byte `VehSpec+0x4c0 == 4`.
3. Do **not** invent an engine component; torque stays AA-layer.
4. Chassis mass/inertia body remains the clonebase physics asset; this function only wraps it
   and fills unit-inertia slots via `FUN_005fc620`.
5. Speed-governor formula at the tail is part of setup, not a runtime tick subsystem.

---

## Related evidence

- `docs/reconstruction/physics/setup-field-mapping.md` — VehSpec offset → component field map
- `docs/reconstruction/physics/0.2-mass-inertia.md` — `FUN_005fc620` / chassis unit inertia
- `docs/reconstruction/physics/avd-airstab-spec.md` — AVD runtime + setup wiring
- Component Build\* functions (next re-gates as needed): `0x5fcce0`, `0x5fc710`, `0x5fc3d0`,
  `0x5fc840`, `0x5fcb00`, `0x5fcff0`, `0x5fc4f0`, `0x5fc620`
