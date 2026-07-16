# Verified: `VehicleAction_tickSubsystems` @ `0x636a60`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x00636a60` |
| Symbol | `VehicleAction_tickSubsystems` (Ghidra name; **`this` is `hkVehicleFramework*`**, not `VehicleAction`) |
| Convention | MSVC `__thiscall` — `this` in `ECX`, `param_2` = `float*` substep context (`param_2[0]` = `dt`) |
| Caller | `VehicleAction_applyAction` @ `0x00598650` (call site `0x005987a2`: `ECX = [VehicleAction+0x40]`) |
| Related | `hkVehicleFramework_wireComponents` @ `0x00636940`; `hkVehicleFramework_preUpdate` @ `0x0064cf20`; `hkVehicleFramework_postTickApplyForces` @ `0x0064bc70` |
| Verified | Ghidra `decompile_function` @ `0x636a60`, `0x636940`, component `update` slots; reflection member names @ `0x9e5918..0x9e5988`; framework vtable @ `0x9e4a40` (`read_memory`) |

---

## 1. Role

Per-substep **framework component dispatcher**. Accumulates `dt` on the framework, runs
**preUpdate**, then **`vtbl+0x14` (`update`)** on exactly **seven** child components in a fixed
slot order, then **postTick** (force apply + friction solve).

This is the Havok 2.x vehicle tick spine. AA-layer torque (`calcWheelTorque`) and air-stab run
**after** this returns, inside `applyAction`.

---

## 2. Decompile (authoritative)

```c
// this = hkVehicleFramework*  (NOT VehicleAction)
// param_2[0] = substep dt
void __thiscall VehicleAction_tickSubsystems(int *param_1, float *param_2)
{
  // optional profiler bookends around "TtVehicle" (rdtsc markers) — omit in port

  param_1[2] = (int)((float)param_1[2] + *param_2);   // fw+0x08 += dt

  (**(code **)(*param_1 + 0x14))(param_2);            // self preUpdate

  (**(code **)(*(int *)param_1[5]  + 0x14))(param_2); // fw+0x14
  (**(code **)(*(int *)param_1[6]  + 0x14))(param_2); // fw+0x18
  (**(code **)(*(int *)param_1[7]  + 0x14))(param_2); // fw+0x1c
  (**(code **)(*(int *)param_1[8]  + 0x14))(param_2); // fw+0x20
  (**(code **)(*(int *)param_1[9]  + 0x14))(param_2); // fw+0x24
  (**(code **)(*(int *)param_1[10] + 0x14))(param_2); // fw+0x28
  (**(code **)(*(int *)param_1[0xb]+ 0x14))(param_2); // fw+0x2c

  (**(code **)(*param_1 + 0x18))(param_2);            // self postTick
}
```

Notes:

- No null checks — all seven child pointers are assumed wired.
- No branching on component type — pure fixed order.
- `param_1[i]` is a dword index: byte offset = `i * 4`.

---

## 3. Component update order (the answer)

### 3a. Full pipeline inside `tickSubsystems`

| Step | `fw` off | `param_1[]` | Source (`wireComponents`) | Component | `update` (`vtbl+0x14`) |
|-----:|---------:|------------:|---------------------------|-----------|------------------------|
| 0 | self | — | framework vtable `0x9e4a40` | **`hkVehicleFramework_preUpdate`** | **`0x0064cf20`** |
| 1 | `+0x14` | `[5]` | `desc[2]` | **`hkDefaultAnalogDriverInput`** (driver status) | **`0x005fe520`** `calcStatus` |
| 2 | `+0x18` | `[6]` | `desc[3]` | **`hkDefaultSteering`** / **`TankSteering`** | **`0x0064f840`** / **`0x0064fb60`** |
| 3 | `+0x1c` | `[7]` | `desc[4]` | **Engine slot** — `FUN_005d6640` (size `0x3c`, vtable `0x9dad44`; AA stand-in, holds wheel-collide params) | **`0x005d66a0`** |
| 4 | `+0x20` | `[8]` | `desc[5]` | **`hkDefaultTransmission`** | **`0x0064f510`** |
| 5 | `+0x24` | `[9]` | `desc[6]` | **`hkDefaultBrake`** | **`0x0064e6f0`** |
| 6 | `+0x28` | `[10]` | `desc[8]` ⚠ | **`hkDefaultSuspension`** | **`0x0064de50`** |
| 7 | `+0x2c` | `[11]` | `desc[7]` ⚠ | **`hkDefaultAerodynamics`** | **`0x0064dae0`** |
| 8 | self | — | framework vtable | **`hkVehicleFramework_postTickApplyForces`** | **`0x0064bc70`** (`vtbl+0x18`) |

⚠ **desc index swap:** `wireComponents` maps **`desc[8] → fw+0x28` (suspension)** and
**`desc[7] → fw+0x2c` (aero)**. Tick order is therefore **suspension then aero**, not
aero-then-suspension.

### 3b. Compact order (port checklist)

```
fw+0x08 += dt
preUpdate          (0x64cf20)     // raycast / wheel geometry / spin integrate
driverInput        (0x5fe520)     // pedal → accel/brake/steer/handbrake/reverse status
steering           (0x64f840)     // status steer → per-wheel angles (quadratic speed falloff)
engine-slot        (0x5d66a0)     // AA component in Havok engine slot (scale @ +0xc for xmit)
transmission       (0x64f510)     // gear/RPM (outputs largely unused by AA drive path)
brake              (0x64e6f0)     // per-wheel brake torque from status brake/handbrake
suspension         (0x64de50)     // spring+damper → susp+0x34[i]
aerodynamics       (0x64dae0)     // drag + lift + extraGravity·mass
postTick           (0x64bc70)     // apply forces, friction solve, wheel writeback
```

### 3c. Not ticked by this function

| `fw` off | Component | Why |
|---------:|-----------|-----|
| `+0x0c` | `hkDefaultWheels` | Data container (arrays); no per-tick `update` here |
| `+0x10` | vehicle data / axes | Read-only sim params for children |
| `+0x30` | `hkDefaultChassis` | Rigid-body wrapper; forces applied in postTick |
| action list `+0x330` (`count@+0x334`) | **`hkAngularVelocityDamper`** | Built into **desc action list**, not a `+0x14..+0x2c` child. Invoked from **postTick** force-list path, not the 7-child loop |

**No `hkDefaultEngine`.** Drive torque is AA-layer `VehicleAction_calcWheelTorque` (`0x598040`)
**after** `tickSubsystems` returns.

---

## 4. Slot wiring evidence (`hkVehicleFramework_wireComponents` @ `0x636940`)

```c
void __thiscall hkVehicleFramework_wireComponents(int fw, undefined4 *desc)
{
  *(fw + 0x10) = desc[0];   // data
  *(fw + 0x0c) = desc[1];   // wheels
  *(fw + 0x14) = desc[2];   // driverInput   ← tick 1
  *(fw + 0x18) = desc[3];   // steering      ← tick 2
  *(fw + 0x1c) = desc[4];   // engine slot   ← tick 3
  *(fw + 0x20) = desc[5];   // transmission  ← tick 4
  *(fw + 0x24) = desc[6];   // brake         ← tick 5
  *(fw + 0x28) = desc[8];   // suspension    ← tick 6  (desc index 8)
  *(fw + 0x2c) = desc[7];   // aerodynamics  ← tick 7  (desc index 7)
  *(fw + 0x30) = desc[9];   // chassis

  // each of the 7 ticked comps:
  *(comp + 8) = fw;         // back-pointer used by all child updates
}
```

### Reflection name table (descriptor slots, `.rdata` near `0x9e5868`)

| `desc[i]` | Byte off in desc | Name string |
|----------:|-----------------:|-------------|
| 0 | `+0x00` | `chassis` *(runtime usage = vehicle data / axes @ `fw+0x10`)* |
| 1 | `+0x04` | `wheels` |
| 2 | `+0x08` | `driverInput` |
| 3 | `+0x0c` | `steering` |
| 4 | `+0x10` | `engine` |
| 5 | `+0x14` | `transmission` |
| 6 | `+0x18` | `brake` |
| 7 | `+0x1c` | `aerodynamics` |
| 8 | `+0x20` | `suspension` |
| 9 | `+0x24` | `chassisMotionState` *(runtime = `hkDefaultChassis` @ `fw+0x30`)* |

Name `chassis` / `chassisMotionState` are reflection labels; **runtime consumers** treat
`fw+0x10` as data axes and `fw+0x30` as the chassis shell with `RB @ +0x3c` (see preUpdate /
postTick / aero).

### Cross-checks that pin identities

| Slot | Evidence |
|------|----------|
| `fw+0x14` = driverInput | `calcStatus` plate + brake/transmission read status `+0x10/+0x18/+0x19`; createAction stores same object at `entity+0x1a0+8` |
| `fw+0x18` = steering | `hkDefaultSteering_update` uses `this+0x24` input / `this+0x28` fullSpeedLimit; reads status steer via `*(fw+0x14)+0x14` |
| `fw+0x1c` = engine slot | transmission multiplies by `*(fw+0x1c)+0xc`; `FUN_005d66a0` writes `this+0xc/+0x10` and reads xmit RPM @ `*(fw+0x20)+0x18` |
| `fw+0x20` = transmission | RPM at `+0x18` (see above); update `0x64f510` |
| `fw+0x24` = brake | `hkDefaultBrake_update` reads status from `fw+0x14` |
| `fw+0x28` = suspension | postTick / susp update; `initFromDescriptor` hardpoint loop |
| `fw+0x2c` = aero | `hkDefaultAerodynamics_update` `0x64dae0` |
| `fw+0x0c` = wheels | ubiquitous `wheels+0x80` stride `0xC0` |
| `fw+0x30` = chassis | `chassis+0x3c` = rigid body |

---

## 5. Framework vtable (`PTR_FUN_009e4a40`, `read_memory`)

| Slot | Address | Role in this tick |
|-----:|---------|-------------------|
| `+0x14` | `0x0064cf20` | **preUpdate** (step 0) |
| `+0x18` | `0x0064bc70` | **postTickApplyForces** (step 8) |
| `+0x20` | `0x0064bbd0` | wheel raycast — called **from** preUpdate, not from the 7-child loop |
| `+0x24` | `0x0051e900` | collide post-helper — also from preUpdate |

---

## 6. Call-site context (`applyAction` @ `0x598650`)

```
VehicleAction_applyAction (this = VehicleAction)
  ...
  VehicleAction_tickSubsystems( framework = this+0x40, &dt )   // THIS FUNCTION
  ... steer ramp / setSteeringAngle / mode branches ...
  VehicleAction_calcWheelTorque()     // AA engine replacement — AFTER subsystems
  VehicleAction_airStabilization()    // AA collision/airborne assist — AFTER
  ...
```

Port order implication: **wheel contact / susp / aero / friction solve happen before** AA drive
torque is written for the **next** postTick. Current-tick drive torque was written on the previous
`applyAction` (or is zero on the first tick).

---

## 7. `fw+0x08` accumulator

```
fw+0x08 += dt   // float; starts 0 in base ctor FUN_00636b30
```

Consumed by transmission clutch-delay timing (`hkDefaultTransmission_update` compares against
component clutch fields). Not a free-running wall clock — it is **integrated sim time** on the
framework.

---

## 8. Porting notes (no C#)

1. Reproduce **exactly** this order: preUpdate → driverInput → steering → engine-slot →
   transmission → brake → **suspension → aero** → postTick.
2. Do **not** insert AVD into the 7-child list; it belongs on the postTick action list.
3. Do **not** invent `hkDefaultEngine`; keep torque in the AA layer after this dispatcher.
4. Tank vehicles only swap the steering **vtable** (`0x64fb60` vs `0x64f840`); slot index is
   unchanged.
5. `this` for the port of this function is the **framework / vehicle sim object**, reached from
   `VehicleAction+0x40` (client) / the server equivalent of that handle.

---

## 9. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x636a60` | OK — fixed 7-child dispatch + pre/post |
| `decompile_function` `0x636940` (`wireComponents`) | OK — slot map + desc[7]/[8] swap |
| Framework vtable `read_memory` `0x9e4a40` | preUpdate `0x64cf20`, postTick `0x64bc70` |
| Child `update` addresses from component vtables | driver `0x5fe520`, steer `0x64f840`, engine-slot `0x5d66a0`, xmit `0x64f510`, brake `0x64e6f0`, susp `0x64de50`, aero `0x64dae0` |
| Reflection names `driverInput`…`suspension` | Match desc indices used by `wireComponents` |
| Conflict vs Phase-0 maps | Prior notes said “7 children” without full names; this file is authoritative for **order**. AVD is **not** one of the 7. |

---

## 10. Related evidence

| File | Relation |
|------|----------|
| [`fn_0064cd30_frameworkCtor.md`](fn_0064cd30_frameworkCtor.md) | Ctor + partial slot table (this file completes identities) |
| [`fn_005fd390_buildFramework.md`](fn_005fd390_buildFramework.md) | Construction order of components handed to the framework |
| [`fn_004fb660_createVehicleAction.md`](fn_004fb660_createVehicleAction.md) | driverInput created + passed into build |
| [`fn_0064bc70_postTick.md`](fn_0064bc70_postTick.md) | postTick body (step 8) |
| [`fn_0064f840_steering.md`](fn_0064f840_steering.md) | steering update (step 2) |
| [`fn_0064dae0_aero.md`](fn_0064dae0_aero.md) | aero update (step 7) |
| [`../0.4-suspension.md`](../0.4-suspension.md) | Phase-0 susp pipeline (matches steps 0 / 6 / 8) |
| [`../PORTING_RULES.md`](../PORTING_RULES.md) | Binary wins on conflict |
