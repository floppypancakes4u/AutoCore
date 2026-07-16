# Verified: `RearWheelFrictionScalar` (`VehSpec+0x740`) in `FUN_005fcce0` @ `0x5fcce0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Function | `FUN_005fcce0` — wheels descriptor builder |
| Body | `0x005fcce0` – `0x005fcfec` |
| Focus | **`VehSpec+0x740` → rear `wheelsFriction` (μ0) / derived μmax only** |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` |
| Downstream | `hkDefaultWheels_ctor` @ `0x0064fee0` (heap size `0x390`) |
| Verified | Ghidra `decompile_function` + `read_memory` + call-site hex around `0x5fcf40` |
| Sibling (full builder) | [`fn_005fcce0_wheelsBuilder.md`](fn_005fcce0_wheelsBuilder.md) |
| μ_base accessor | [`fn_004f5550_wheelFriction.md`](fn_004f5550_wheelFriction.md) |

**Scope:** how `fRearWheelFrictionScalar` is applied to the **solver friction table** at setup.  
**Not this file:** full eight-array grow/fill of the wheels descriptor (see wheelsBuilder); runtime friction solve (`0x6c4450`); drive-torque path (`0x598040`).

---

## One-line result

At **setup only**, for every wheel with index **`i >= firstRear`** (`firstRear = *(char*)(VehSpec+0x4cc)`):

```
desc.wheelsFriction[i]  *=  *(float*)(VehSpec + 0x740)   // fRearWheelFrictionScalar
desc.wheelsMaxFriction[i] = desc.wheelsFriction[i] * 1.5 // after the scale
```

Front wheels (`i < firstRear`) keep raw μ from `FUN_004f5550(i)`.  
`+0x740` is **never** applied to radius (`+0x600`), width (`+0x618`), or engine torque.

---

## DB / layout identity

| Item | Evidence |
|---|---|
| SQL column | **`fRearWheelFrictionScalar`** (last select column of `tVehicle` load) |
| String VA | `0x00a93d40` — `… fSkirtExtentsZ, fRearWheelFrictionScalar From tVehicle WHERE IDCloneBase = ?` |
| VehSpec offset | **`+0x740`** (float32) — decompile load `movss xmm0, dword ptr [edx+0x740]` at multiply site |
| Informal name | `RearWheelFrictionScalar` (phase-0 docs) |

---

## Access paths used by the multiply

### VehicleSpecific for `+0x740` (and `+0x600` / `+0x618`)

Standard clone-template chain on the builder’s entity `param_1`:

```
VehSpec = *(*( *( *(param_1 + 4) + 4 ) + 0xac + param_1 ) + 0x3c)
scalar  = *(float*)(VehSpec + 0x740)
```

### `firstRear` (rear/front split) — entity `+0x258` chain

```
ws = *(param_1 + 0x258)                                    // decimal 600
VehSpec' = *(*( *( *(ws + 4) + 4 ) + 0xac + ws ) + 0x3c)
firstRear = *(char*)(VehSpec' + 0x4cc)                      // tinNumWheelsAxle0 / axle-0 count
```

### Base μ (not from VehSpec)

```
// ECX = param_1 (entity / VehicleAction-like); arg = wheel index i
mu0 = FUN_004f5550(i)
// thunk: ECX = *(ECX+0x258); JMP FUN_005a6f20
// table: *(float*)(wheelset + 0xb4 + i*4)  for i in 0..5; else −1.0
```

---

## Exact decompile fragment (rear scale only)

From `FUN_005fcce0` wheel loop (`piVar1 = param_3 + 0x28` = `wheelsFriction` array base):

```
// cVar3 = firstRear (byte from VehSpec'+0x4cc)
// cVar7 = i
// local_1c low byte = i (passed to FUN_004f5550)

iVar8 = i * 4;
iVar6 = *piVar1;                              // desc+0x28 array ptr
bVar2 = (i < firstRear);                      // isFront
fVar9 = FUN_004f5550(i);
*(float*)(iVar6 + iVar8) = (float)fVar9;      // μ0 seed

if (!bVar2) {                                 // REAR: i >= firstRear
    *(float*)(iVar8 + *piVar1) =
        *(float*)(VehSpec + 0x740) * *(float*)(iVar8 + *piVar1);
}

*(float*)(iVar8 + *(int*)(param_3 + 0x40)) =
    *(float*)(iVar8 + *piVar1) * DAT_00aaa68c;  // μmax = μ0 * 1.5  (AFTER scale)
*(uint*)(iVar8 + *(int*)(param_3 + 4)) = (uint)bVar2;  // axle flag (front=1, rear=0)
```

### Machine multiply (around `0x5fcf40`, after `FUN_004f5550` store)

```
test  al, al              ; AL = isFront (bVar2)
jnz   skip_scale          ; front → leave μ0
...
movss xmm0, [edx+0x740]   ; VehSpec.fRearWheelFrictionScalar
mulss xmm0, [ecx]         ; × current wheelsFriction[i]
movss [ecx], xmm0         ; write back μ0
```

(`F3 0F 10 82 40 07 00 00` / `F3 0F 59 01` / `F3 0F 11 01` at the scale site.)

---

## Friction-table slots touched by `+0x740`

Descriptor arrays are growable `{ptr, count, cap}` triples. Only these friction-related slots carry the scalar:

| Desc array ptr | Havok / port name | Front | Rear |
|---|---|---|---|
| `param_3+0x28` | **`wheelsFriction` (μ0)** | `FUN_004f5550(i)` | `FUN_004f5550(i) * *(VehSpec+0x740)` |
| `param_3+0x40` | **`wheelsMaxFriction` (μmax)** | `μ0 * 1.5` | **same formula on already-scaled μ0** |

Not scaled by `+0x740`:

| Desc array ptr | Source | Role |
|---|---|---|
| `+0x10` | `VehSpec+0x600[i]` | radius (reflection: `wheelsRadius`) — **copy only** |
| `+0x1c` | `VehSpec+0x618[i]` | width (reflection: `wheelsWidth`) — **copy only** |
| `+0x34` | `g_flMsToSeconds` @ `0xa0f72c` = **0.001** | viscosity friction |
| `+0x4c` | `DAT_00a0f718` = **0.01** | force-feedback mult |
| `+0x58` | `DAT_00aaa7a4` = **15.0** | later per-wheel `+0x84` payload |
| `+0x04` | `(uint)(i < firstRear)` | axle index encoding |

---

## Constants involved at / after the scale

| Symbol | Addr | LE bytes | float32 | Role |
|---|---|---|---:|---|
| `DAT_00aaa68c` | `0x00aaa68c` | `00 00 c0 3f` | **1.5** | `μmax = μ0 × 1.5` (μ0 already rear-scaled) |
| `DAT_00a0f718` | `0x00a0f718` | `0a d7 23 3c` | **0.01** | same loop; not × `+0x740` |
| `DAT_00aaa7a4` | `0x00aaa7a4` | `00 00 70 41` | **15.0** | same loop; not × `+0x740` |
| `g_flMsToSeconds_Inferred` | `0x00a0f72c` | `6f 12 83 3a` | **0.001** | viscosity; not × `+0x740` |
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **−1.0** | OOR default of `FUN_005a6f20` (not used when `i` valid) |

All four DAT floats re-read via `read_memory` length 4.

---

## Boundary / ordering rules (must-match for ports)

1. **Rear predicate is `i >= firstRear`**, not `i > firstRear`.  
   `isFront = (i < firstRear)`; scale when `!isFront`.
2. **Scale then derive μmax.** Rear μmax carries `× fRearWheelFrictionScalar × 1.5`.
3. **Baked once at framework build** (`0x5fd390` → this builder → ctor). Runtime postTick / friction solver consume already-scaled table entries (see `0.3-friction-solver.md`).
4. **Does not affect drive torque.** `VehicleAction_calcWheelTorque` (`0x598040`) multiplies `FUN_004f5550(i)` **raw** (plus upright / low-speed boost / handbrake ×0.5). Handbrake `×0.5` (`DAT_00a0f298`) is **not** this field.
5. **Does not scale torque ratio.** `VehSpec+0x600[i]` is copied to `desc+0x10` with no `+0x740` factor.

---

## Worked example

Assume 4 wheels, `firstRear = 2`, μ_base = `{1.0, 1.0, 1.0, 1.0}`, `fRearWheelFrictionScalar = 0.85`:

| i | axle | μ0 after setup | μmax |
|--:|:----:|---------------:|-----:|
| 0 | front | 1.0 | 1.5 |
| 1 | front | 1.0 | 1.5 |
| 2 | rear | 0.85 | 1.275 |
| 3 | rear | 0.85 | 1.275 |

---

## Conflicts vs prior notes

| Claim | Source | Verdict vs this re-gate |
|---|---|---|
| `+0x740` scales solver rear μ0 / μmax at setup | `0.5-wheel-collide.md`, `0.3-friction-solver.md` | **match** |
| `+0x740` multiplies rear `wheelsTorqueRatio` / `+0x600` | `setup-field-mapping.md` | **false** — binary multiplies **`wheelsFriction` only** |
| Scalar applies at runtime in calcWheelTorque | — | **false** — setup-only in this function |
| Handbrake rear ×0.5 is `RearWheelFrictionScalar` | — | **false** — separate `DAT_00a0f298` path |

**No C# in this gate** — documentation only.

---

## Related addresses

| Addr | Role |
|---|---|
| `0x005fcce0` | this builder (full wheels desc fill + rear μ scale) |
| `0x005fd390` | sole caller; alloc `0x390` → this → `hkDefaultWheels_ctor` |
| `0x004f5550` / `0x005a6f20` | raw μ_base table read |
| `0x004f5560` | wheel count (`*(entity+0x258)+0xb0`) |
| `0x00598040` | uses **unscaled** `FUN_004f5550` for drive torque |
| `0x0064bc70` / `0x006c4450` | postTick aggregation + friction solver (consume baked μ) |
| `0x00a93d40` | SQL string containing `fRearWheelFrictionScalar` |
