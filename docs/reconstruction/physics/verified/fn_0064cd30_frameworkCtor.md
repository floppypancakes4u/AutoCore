# Verified: `hkVehicleFramework_ctor` @ `0x0064cd30`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Size/layout notes for the server port (no production C# in this file).

| Item | Value |
|------|--------|
| Address | `0x0064cd30` |
| Body | `0x0064cd30` – `0x0064ce17` |
| Ghidra name | `hkVehicleFramework_ctor` |
| Calling convention | MSVC `__thiscall` (`this` = framework object; stack arg = setup descriptor) |
| Role | Allocate-time construct of the vehicle framework instance: base header, component wire-up, scalar/geometry precompute, action list copy |
| Object size | **`0x360` (864 bytes)** — confirmed by allocator write of `*(u16*)(obj+4) = 0x360` in `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` |
| Primary vtable | `PTR_FUN_009e4a40` @ `0x009e4a40` |
| Secondary vtable | `PTR_FUN_009e4a38` @ `0x009e4a38` installed at `this+0x1f0` |

## Tools used (this verification)

1. **`decompile_function`** `0x64cd30` program=`autoassault.exe`
2. **`decompile_function`** callees: `FUN_00636b30` (`0x636b30`), `hkVehicleFramework_wireComponents` (`0x636940`), `hkVehicleFramework_initFromDescriptor` (`0x64b2b0`), `FUN_005b3300` (`0x5b3300`)
3. **`decompile_function`** caller / tick: `Vehicle_buildHavokVehicleFramework` (`0x5fd390`), `VehicleAction_tickSubsystems` (`0x636a60`)
4. **`read_memory`** vtable `0x009e4a40` length=64; secondary `0x009e4a38` length=16; `g_flOne` `0x00a0f2a0`
5. **`get_function_by_address`** / **`get_function_callees`** for body bounds and call graph

Did **not** use `disassemble_bytes`. Emulation skipped (pointer-heavy construction graph).

---

## 1. Decompile (authoritative)

```c
undefined4 * __thiscall hkVehicleFramework_ctor(undefined4 *param_1, int param_2)
{
  int *piVar1;
  int iVar2;
  int iVar3;

  // Base / wire: header + component pointer fan-out from descriptor
  FUN_00636b30(param_2);                 // thiscall: this=param_1, arg=descriptor
  *param_1 = &PTR_FUN_009e4a40;          // primary vtable (overrides base)

  param_1[0xe] = 0;                     // +0x38
  param_1[0x7d] = 0;                    // +0x1f4
  param_1[0x7c] = &PTR_FUN_009e4a38;     // +0x1f0 secondary vtable (MI subobject)

  // Selective zero of friction-out / tail region (see §3)
  param_1[0xb7] = 0;
  param_1[0xb3] = 0;                    // +0x2cc friction OUT base
  param_1[0xb4] = 0;
  param_1[0xb5] = 0;
  param_1[0xb8] = 0;
  param_1[0xb9] = 0;
  param_1[0xbe] = 0;
  param_1[0xba] = 0;
  param_1[0xbb] = 0;
  param_1[0xbc] = 0;
  param_1[0xbf] = 0;
  param_1[0xc0] = 0;

  // hkArray-like action / force list at +0x330
  piVar1 = param_1 + 0xcc;              // +0x330
  *piVar1 = 0;                          // data*
  param_1[0xcd] = 0;                    // size
  param_1[0xce] = 0x80000000;           // capacity (Havok empty flag in high bit)

  // Precompute inertia / axle contact geometry into fw+0x1fc..+0x35c
  hkVehicleFramework_initFromDescriptor(param_2);

  // Copy descriptor action list desc+0x50[count=desc+0x54] → fw+0x330
  iVar2 = *(int *)(param_2 + 0x54);
  if ((int)(param_1[0xce] & 0x7fffffff) < iVar2) {
    iVar3 = (param_1[0xce] & 0x7fffffff) * 2;
    if (iVar3 <= iVar2) {
      iVar3 = iVar2;
    }
    FUN_005b3300(piVar1, iVar3, 4);     // grow array (element size 4)
  }
  iVar3 = 0;
  param_1[0xcd] = iVar2;
  if (0 < *(int *)(param_2 + 0x54)) {
    do {
      *(undefined4 *)(*piVar1 + iVar3 * 4) =
          *(undefined4 *)(*(int *)(param_2 + 0x50) + iVar3 * 4);
      iVar3 = iVar3 + 1;
    } while (iVar3 < *(int *)(param_2 + 0x54));
  }

  param_1[0x7e] = *(undefined4 *)(*(int *)(param_2 + 4) + 8);  // +0x1f8 = *(desc[1]+8)
  *(short *)(param_1[0xc] + 6) =
      *(short *)(param_1[0xc] + 6) + 1;  // wheels refcount++
  return param_1;
}
```

### Base ctor `FUN_00636b30` @ `0x636b30`

```c
// this = framework; param_2 = setup descriptor
*(undefined2 *)((int)this + 6) = 1;     // refcount
*this = &PTR_FUN_009e3b44;              // temporary base vtable (overwritten)
this[2] = 0;                            // +0x08 accum / link
hkVehicleFramework_wireComponents(param_2);
```

---

## 2. Allocation & object header

From `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (sole constructor site):

```
obj = allocator(0x360, 10)
*(u16*)(obj + 4) = 0x360                 // stored size
fw  = hkVehicleFramework_ctor(obj, &setupDesc)
*(fw + 0x34) = build_param_2             // written AFTER ctor returns
return fw
```

| Offset | Type | Init | Meaning |
|-------:|------|------|---------|
| `+0x00` | ptr | `0x009e4a40` | Primary vtable |
| `+0x04` | u16 | `0x360` | Object size (allocator metadata) |
| `+0x06` | u16 | `1` then kept | Refcount (base ctor; wheels also bumped at end) |
| `+0x08` | f32/int | `0` | Tick accumulator: `tickSubsystems` does `fw+0x08 += dt` |
| `+0x0c`… | | | Component pointers (see §4) |

**Port note:** treat logical framework size as **`0x360`**. Last known scalar is `fw+0x35c` (friction equalizer); `+0x35c+4 = +0x360`.

---

## 3. Ctor-local zero map (dword index → offset)

`param_1` is `undefined4*`; index `n` → byte offset `n*4`.

| Index | Offset | Init value | Port meaning |
|------:|-------:|------------|--------------|
| `[0xe]` | `+0x38` | `0` | Slot after builder-written `+0x34` |
| `[0x7c]` | `+0x1f0` | secondary vtbl | MI subobject start (before friction setup) |
| `[0x7d]` | `+0x1f4` | `0` | Pad / subobject field |
| `[0x7e]` | `+0x1f8` | `*(desc[1]+8)` | Written later (not zero) |
| `[0xb3]` | `+0x2cc` | `0` | Friction solver **OUT** base (stride `0x1c` / axle) |
| `[0xb4]` | `+0x2d0` | `0` | OUT continuation |
| `[0xb5]` | `+0x2d4` | `0` | OUT continuation |
| `[0xb7]` | `+0x2dc` | `0` | OUT continuation |
| `[0xb8..0xbc]` | `+0x2e0..+0x2f0` | `0` | OUT continuation |
| `[0xbe..0xc0]` | `+0x2f8..+0x300` | `0` | OUT tail toward `+0x304` |
| `[0xcc]` | `+0x330` | `0` | Action/force list data* |
| `[0xcd]` | `+0x334` | `0` → count | List size |
| `[0xce]` | `+0x338` | `0x80000000` | Capacity with empty flag |

**Gaps not written by ctor zero loop:** `+0x2d8` (`[0xb6]`), `+0x2f4` (`[0xbd]`). Do not assume full-region zero from this function alone; `initFromDescriptor` and first `postTick` fill live state. Safe port: zero **entire** `0x2cc..0x303` (2 axles × `0x1c`) plus the rest of the object before init.

---

## 4. Component pointer layout (`wireComponents` @ `0x636940`)

Descriptor is an array of component / data pointers. Wiring:

| `fw` off | `desc[i]` | Ticked? | Runtime role (cross-refs) |
|---------:|----------:|:-------:|---------------------------|
| `+0x10` | `[0]` | no | `hkVehicleData*` (axes, mass scalars) — used by aero/init |
| `+0x0c` | `[1]` | no | `hkDefaultWheels*` (`wheelCount@+0xc`, axle map`+0x58`, wheels`+0x80` stride `0xC0`) |
| `+0x14` | `[2]` | **1st** | Component update `vtbl+0x14` (steering / driver-input side of stack) |
| `+0x18` | `[3]` | **2nd** | Ticked component |
| `+0x1c` | `[4]` | **3rd** | Ticked component |
| `+0x20` | `[5]` | **4th** | Ticked component |
| `+0x24` | `[6]` | **5th** | Ticked component |
| `+0x28` | `[8]` | **6th** | **`hkDefaultSuspension*`** (note: desc index **8**, not 7) |
| `+0x2c` | `[7]` | **7th** | Ticked component (aero / damper side of stack) |
| `+0x30` | `[9]` | no | **`hkDefaultChassis*`** → rigid body at `chassis+0x3c` |

Back-pointer: each of the **seven** ticked components gets `comp+0x08 = framework`.

`tickSubsystems` @ `0x636a60` (this = framework):

```
fw+0x08 += dt
fw.vtbl+0x14(dt)                 // preUpdate 0x64cf20
for comp in {+0x14,+0x18,+0x1c,+0x20,+0x24,+0x28,+0x2c}:
    comp.vtbl+0x14(dt)
fw.vtbl+0x18(dt)                 // postTickApplyForces 0x64bc70
```

### Build order → components (from `0x5fd390`)

Allocation sizes written at `obj+4` before each ctor:

| Size | Ctor | Notes |
|-----:|------|-------|
| `0x390` | `hkDefaultWheels_ctor` `0x64fee0` | |
| `0x40` | `hkDefaultChassis_ctor` `0x64fdf0` | RB @ `+0x3c` |
| `0x38` | steering (default `0x64fac0` or tank `0x64fc80` if VehSpec`+0x4c0==4`) | |
| `0x3c` | wheel-collide `FUN_005d6640` | |
| `0x60` | `hkDefaultTransmission_ctor` `0x64f610` | |
| `0x54` | `hkDefaultBrake_ctor` `0x64ed40` | |
| `0x68` | `hkDefaultSuspension_ctor` `0x64e510` | |
| `0x50` | `hkDefaultAerodynamics_ctor` `0x64da90` | |
| `0x14` | `hkAngularVelocityDamper_ctor` `0x64d900` | Pushed into **desc action list** (`desc+0x50`), **not** a ticked `+0x14..+0x2c` slot |
| **`0x360`** | **`hkVehicleFramework_ctor` `0x64cd30`** | Returned instance |

**No `hkDefaultEngine`.** Engine torque is AA-layer (`VehicleAction_calcWheelTorque` / `torqueCurve2D`).

---

## 5. Fields filled by `initFromDescriptor` @ `0x64b2b0` (layout critical)

Called mid-ctor after zeros + empty action array. Writes the **simulation constants** the friction solver and preUpdate consume.

### 5a. Raw descriptor scalars → framework

| `fw` off | Source (`desc` dword) | Meaning (Phase-0 names) |
|---------:|----------------------:|-------------------------|
| `+0x33c` | `[0xb]` @ desc`+0x2c` | Spin-torque / RVSpinTorque axis 0 |
| `+0x340` | `[0xc]` @ desc`+0x30` | Spin-torque axis 1 |
| `+0x344` | `[0xd]` @ desc`+0x34` | Spin-torque axis 2 |
| `+0x350` | `[0x11]` @ desc`+0x44` | Unit inertia axis 0 (raw store) |
| `+0x354` | `[0x12]` @ desc`+0x48` | Unit inertia axis 1 |
| `+0x358` | `[0x13]` @ desc`+0x4c` | Unit inertia axis 2 |
| `+0x304` | `[0x10]` @ desc`+0x40` | Max susp / normal-clip threshold (`DAT_00af4614` default **0.2** via `FUN_00650020`) |
| `+0x348` | `[0xe]` @ desc`+0x38` | Extra torque factor |
| `+0x34c` | `[0xf]` @ desc`+0x3c` | Companion scalar (default **10.0** `DAT_00a110d8`) |
| `+0x35c` | `[0xa]` @ desc`+0x28` | **RVFrictionEqualizer** |

### 5b. Derived inverse-inertia blocks

Using chassis mass `m = vehicleData+0x0c` and local axes at vehicleData `+0x10/+0x20/+0x30`, with unit inertias from desc:

```
// "Real" inverse inertia (per world axis blend of unit inertia × mass)
fw+0x310 = 1 / I_axis0
fw+0x314 = 1 / I_axis1
fw+0x318 = 1 / I_axis2
fw+0x31c = 0

// Solver-facing inverse inertia (RVSpinTorque / RVInertia ratios) — postTick hands THIS to the solver
fw+0x320 / +0x324 / +0x328 / +0x32c
```

**Port significance:** friction solver authority is **not** the physical chassis inertia. Fleet data with small roll/pitch spin-torque ratios is the anti-rollover mechanism (see plate comment on `initFromDescriptor`). Keep both blocks if matching retail handling.

### 5c. Axle contact setup block

Per-wheel loop aggregates suspension hardpoint + direction×restLength into chassis-local rest contacts, then:

```
FUN_006c4150(axleContactScratch, fw + 0x1fc)
```

| Region | Size intent | Role |
|--------|-------------|------|
| `fw+0x1fc` | friction **setup** (per-axle stride `0x64` in solver) | Solver arg2 from `postTick` |
| `fw+0x2cc` | friction **OUT** (per-axle stride `0x1c`) | Solver arg4 / wheel writeback |

Exact setup field packing is owned by `FUN_006c4150` / `postTick` — see `fn_0064bc70_postTick.md` and `0.3-friction-solver.md`.

---

## 6. Action / external-force list (`+0x330`)

Havok-style array:

| Off | Field |
|----:|-------|
| `+0x330` | `T** data` |
| `+0x334` | `int size` |
| `+0x338` | `int capacity` (`0x80000000` = empty flag when high bit set) |

Ctor copies `desc+0x50[0 .. desc+0x54)`. Build pushes **`hkAngularVelocityDamper`** into that descriptor list before the framework ctor. `postTick` walks `fw+0x330` count `fw+0x334` and calls each entry’s `vtbl+0x14` (force integration path).

---

## 7. Primary vtable (`0x009e4a40`) — slots used by tick

| Slot | Address | Symbol / role |
|-----:|---------|---------------|
| `+0x00` | `0x0064cef0` | Scalar deleting dtor wrapper |
| `+0x14` | `0x0064cf20` | **`hkVehicleFramework_preUpdate`** |
| `+0x18` | `0x0064bc70` | **`hkVehicleFramework_postTickApplyForces`** |

Secondary table at `this+0x1f0` → `0x009e4a38` (first entry `0x0064cce0`) is a Havok MI helper surface; not needed for the tick math path.

---

## 8. Compact framework layout map (port checklist)

```
+0x000  vtable (0x9e4a40)
+0x004  u16 size = 0x360
+0x006  u16 refcount
+0x008  f32  time accumulator (+= dt each tick)
+0x00c  ptr  wheels
+0x010  ptr  vehicleData
+0x014  ptr  tick component 0
+0x018  ptr  tick component 1
+0x01c  ptr  tick component 2
+0x020  ptr  tick component 3
+0x024  ptr  tick component 4
+0x028  ptr  suspension (tick 5)
+0x02c  ptr  tick component 6
+0x030  ptr  chassis shell
+0x034  ptr  (set by build AFTER ctor — world/context)
+0x038  zeroed by ctor
...
+0x1f0  secondary vtable (0x9e4a38)
+0x1f4  zeroed
+0x1f8  *(wheels+8) / desc[1]+8
+0x1fc  friction setup block (initFromDescriptor / FUN_006c4150)
...
+0x2cc  friction output block (solver write target)
...
+0x304  maxSuspLen / normal-clip threshold
+0x310..+0x31c  real inv-inertia (4 × f32; +0x31c = 0)
+0x320..+0x32c  solver inv-inertia (4 × f32)
+0x330  action list data*
+0x334  action list count
+0x338  action list capacity
+0x33c..+0x344  spin-torque factors (3 × f32)
+0x348  extraTorqueFactor
+0x34c  companion scalar
+0x350..+0x358  unit inertia raw (3 × f32)
+0x35c  frictionEqualizer
+0x360  END
```

---

## 9. Port-facing notes (size/layout only)

1. **Allocate `0x360` bytes** per vehicle framework instance (or C# class with explicit layout totaling that logical footprint — fields need not be binary-identical padding if all consumed offsets exist).
2. **Wire components before** inertia/geometry init; `initFromDescriptor` reads `fw+0x0c` (wheels), `fw+0x28` (suspension), `fw+0x30` (chassis RB).
3. **Dual inertia blocks** (`+0x310` vs `+0x320`) are both required for retail-like friction authority; do not substitute one for the other.
4. **Action list** at `+0x330` must include AVD (and any other desc-listed force objects); empty list ⇒ no AVD spin damping from that path.
5. **Friction buffers** at `+0x1fc` / `+0x2cc` are in-object (not separate heap for the axle rows used by the stock 2-axle path); zero them at construct.
6. **Refcount / size halfwords** at `+0x04/+0x06` are Havok allocator conventions; a managed port can omit them if ownership is GC/scope-based — keep them only if sharing the same free path as the client.
7. Downstream verified drivers: `fn_0064bc70_postTick.md`, `fn_0064dae0_aero.md`, `fn_0064f840_steering.md`, Phase-0 `0.3`/`0.4`/`0.8`.

## Confidence

| Claim | Level |
|-------|-------|
| Object size `0x360` | **High** (allocator `+4` write + last field `+0x35c`) |
| Component slot map `+0x0c..+0x30` | **High** (wireComponents decompile) |
| Tick order / vtbl `+0x14`/`+0x18` | **High** (tickSubsystems + vtable read) |
| Action list `+0x330` copy from desc`+0x50` | **High** (ctor body) |
| Scalar map `+0x304`/`+0x310..+0x35c` | **High** (initFromDescriptor + Phase-0 name map) |
| Exact identity of every ticked slot `+0x14..+0x24` class | **Medium** (build order known; desc index → slot swap on susp/index7 needs component-pointer dump if naming each class) |
| Full zero-coverage of `+0x2cc` region | **Medium** (selective stores; port should zero the full OUT region) |
