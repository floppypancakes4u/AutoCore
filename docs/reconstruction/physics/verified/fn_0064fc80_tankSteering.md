# Verified: `TankSteering_ctor` @ `0x64fc80`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x0064fc80` – `0x0064fc98` |
| Symbol | `TankSteering_ctor` |
| RTTI | `.?AVTankSteering@@` @ `0x00af5118` |
| Convention | `__thiscall` (`this` = heap object, stack desc) |
| Heap size | **`0x38`** (same as `hkDefaultSteering`) |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (call site `0x5fd512`) |
| Predicate | **iff** `*(char*)(VehSpec + 0x4c0) == 4` |
| Verified | Ghidra `decompile_function` + `read_memory` (vtables + DAT scales) + parent ctor / update peeks |

---

## Purpose

AA’s tracked / tank steering **subclass** of Havok `hkDefaultSteering`. Construction is a thin
wrapper: run the full default steering ctor (descriptor fill included), then **swap the vtable**
to `PTR_FUN_009e4f1c` so the runtime `update` slot uses tank-specific per-wheel angle writeback.

Descriptor build is **shared** — `Vehicle_BuildSteeringDescriptor` (`0x5fc710`) runs once
before the branch; both Tank and Default consume the same stack blob.

---

## 1. Decompile (authoritative)

### `TankSteering_ctor` @ `0x64fc80`

```c
undefined4 * __thiscall TankSteering_ctor(undefined4 *param_1, undefined4 param_2)
{
  hkDefaultSteering_ctor(param_2);   // parent: base + desc fill
  *param_1 = &PTR_FUN_009e4f1c;      // TankSteering vtable
  return param_1;
}
```

Body is **24 bytes** (`0x64fc80`–`0x64fc98`). No extra field writes. Object layout after return is
identical to `hkDefaultSteering` except `*(this+0)` (vtable).

### Parent `hkDefaultSteering_ctor` @ `0x64fac0` (always runs first)

```c
undefined4 * __thiscall hkDefaultSteering_ctor(undefined4 *param_1, undefined4 param_2)
{
  FUN_0065e5f0(param_2);              // hkSteeringComponent base
  *param_1 = &PTR_FUN_009e4ee4;       // hkDefaultSteering vtable (overwritten by Tank)
  param_1[0xb] = 0;                  // doesWheelSteer array base = 0
  param_1[0xc] = 0;                  // count / related
  param_1[0xd] = 0x80000000;         // capacity sentinel (unowned)
  FUN_0064f920(param_2);             // copy descriptor → object
  return param_1;
}
```

`FUN_0065e5f0` (`0x65e5f0`) sets base component vtable `PTR_FUN_009e7238`, short flag at `+6`,
and another growable array triple at `param_1[5..7]`, then `FUN_0065e530(param_2)`.

### Descriptor handoff `FUN_0064f920` @ `0x64f920` (via parent)

| Desc off | Object off | Field |
|---|---|---|
| `+0x04` | `this+0x24` | maxSteeringAngle (product already includes entity mult) |
| `+0x08` | `this+0x28` | maxSpeedFullSteeringAngle / fullSpeedLimit |
| `+0x0c` → byte copy | `this+0x2c` | `doesWheelSteer[]` |
| `+0x10` | `this+0x30` **and** `this+0x20` | wheel count |

Same mapping documented in `fn_005fc710_steeringBuilder.md`. Tank does **not** re-copy or alter
descriptor fields.

---

## 2. Build-framework path when `VehSpec+0x4c0 == 4`

Source: `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (re-decompiled). Steering sits after
chassis, before wheel-collide — component #3 in the construction order.

### Exact branch (decompiler)

```c
Vehicle_BuildSteeringDescriptor();          // fills shared steering desc (stack)

// VehSpec = *(*(entity + *(entity[1]+4) + 0xac) + 0x3c)
if (*(char *)(VehSpec + 0x4c0) == '\x04') {
    heap = heap_alloc(size = 0x38);         // DAT_00b05060+0x10
    *(uint16 *)(heap + 4) = 0x38;           // object size stamp
    uStack_1ec = TankSteering_ctor(desc);   // call site ~0x5fd512
}
else {
    heap = heap_alloc(size = 0x38);
    *(uint16 *)(heap + 4) = 0x38;
    uStack_1ec = hkDefaultSteering_ctor(desc);
}

FUN_005d6720();                             // shared post-helper (both paths)
// … wheel-collide / transmission / … continue unchanged
```

### Side-by-side: Tank path vs Default path

| Step | `+0x4c0 == 4` (Tank) | else (Default) |
|---|---|---|
| Descriptor builder | `Vehicle_BuildSteeringDescriptor` `0x5fc710` | **same** |
| Heap size | `0x38` | **same** |
| Size stamp `object+4` | `0x38` | **same** |
| Ctor | `TankSteering_ctor` `0x64fc80` | `hkDefaultSteering_ctor` `0x64fac0` |
| Ctor body | parent ctor + vtable `0x9e4f1c` | base + vtable `0x9e4ee4` + array init + `FUN_0064f920` |
| Post-helper | `FUN_005d6720` | **same** |
| Rest of framework | unchanged (collide → xmit → brake → susp → aero → AVD → framework) | **same** |

**No other framework branch** keys off `+0x4c0`. Only the steering ctor selection changes.

### Predicate access chain (matches other builders)

```
cloneSlot = *(int*)(entity + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec   = *(int*)( *(int*)(entity + slotOff + 0xac) + 0x3c )
typeByte  = *(char*)(VehSpec + 0x4c0)
if typeByte == 4 → Tank
```

`VehSpec+0x4c0` is the vehicle-type / chassis-class byte used elsewhere as the tracked-vehicle
discriminator. Value **`4`** → `TankSteering`; any other value → `hkDefaultSteering`.

---

## 3. Vtable difference (why the subclass exists)

Raw `read_memory` (LE function pointers), first six slots:

| Slot | Off | `hkDefaultSteering` `PTR_FUN_009e4ee4` | `TankSteering` `PTR_FUN_009e4f1c` |
|---:|---:|---|---|
| 0 | `+0x00` | `0x0064fb00` (default dtor wrapper) | `0x0064fca0` (tank dtor wrapper) |
| 1 | `+0x04` | `0x005ffd80` | **same** |
| 2 | `+0x08` | `0x005ffdb0` | **same** |
| 3 | `+0x0c` | `0x0064fcf0` | **same** |
| 4 | `+0x10` | `0x005ffc80` | **same** |
| 5 | `+0x14` | **`0x0064f840`** `hkDefaultSteering_update` | **`0x0064fb60`** tank update |

**Behavioral fork = vtable slot `+0x14` (update).** Construction stores the same max-angle /
full-speed-limit / `doesWheelSteer[]` / counts; only the per-tick write of `outAngle[]` differs.

Class strings near reflection blocks: `"hkDefaultSteering"` @ `0x9e4fbc`,
`".?AVTankSteering@@"` @ `0xaf5118`, `".?AVhkDefaultSteering@@"` @ `0xaf50f8`.

---

## 4. Runtime update: tank vs default

### Shared front-end (both updates)

```
angle = SteeringMaxAngle * steerInput          // *(parent+0x14)+0x14  ×  this+0x24
forwardSpeed = dot(chassisLinVel, forwardAxis) // via FUN_005d6ae0 + body+0x40..0x48
if fullSpeedLimit <= forwardSpeed:             // this+0x28
    r = fullSpeedLimit / forwardSpeed
    angle *= r * r                             // quadratic falloff
computedAngle = angle                          // this+0x10
```

### Per-wheel writeback

**Default** (`hkDefaultSteering_update` `0x64f840`) — loop bound `this+0x30`:

```
for i in 0 .. wheelCount-1:
    outAngle[i] = doesWheelSteer[i] ? computedAngle : 0.0
```

**Tank** (`FUN_0064fb60` `0x64fb60`–`0x64fc68`) — loop bound `this+0x20` (also set to wheel
count by `FUN_0064f920`):

```
// DAT_00af50c8 = 0.6f, DAT_00af50c4 = 1.0f  (read_memory)
for i in 0 .. count-1:
    if doesWheelSteer[i]:
        outAngle[i] = computedAngle
    else if i < 4:
        outAngle[i] = -computedAngle * 0.6     // DAT_00af50c8
    else:
        outAngle[i] = -computedAngle * 1.0     // DAT_00af50c4
```

### Tank-only constants (`read_memory`)

| Symbol | Address | LE bytes | float32 | Role |
|---|---|---|---:|---|
| `DAT_00af50c4` | `0x00af50c4` | `00 00 80 3f` | **1.0** | Non-steer wheel scale when `i >= 4` |
| `DAT_00af50c8` | `0x00af50c8` | `9a 99 19 3f` | **0.6** | Non-steer wheel scale when `i < 4` |

Non-steering tank wheels get a **negated, scaled** copy of `computedAngle` (skid / counter-angle
style), not zero. Index split is hard-coded at **`i < 4`**, independent of `VehSpec+0x4cc`
axle-0 count used when building `doesWheelSteer[]`.

---

## 5. Object layout (shared with default after ctor)

| Offset | Type | Meaning |
|---|---|---|
| `+0x00` | ptr | Vtable (`0x9e4f1c` tank / `0x9e4ee4` default) |
| `+0x04` | u16 | Heap size stamp (`0x38`) |
| `+0x08` | ptr | Parent vehicle framework |
| `+0x10` | f32 | `computedAngle` (written each update) |
| `+0x14` | ptr | `outAngle[]` (f32 per wheel) |
| `+0x20` | i32 | Wheel count (tank update loop) |
| `+0x24` | f32 | Normalized steer input `[-1,+1]` |
| `+0x28` | f32 | `fullSpeedLimit` |
| `+0x2c` | ptr | `doesWheelSteer[]` (char per wheel) |
| `+0x30` | i32 | Wheel count (default update loop) |
| `+0x34` | u32 | Array capacity (`& 0x7fffffff`; high bit unowned) |

---

## 6. Conflicts vs prior evidence

| Item | Prior docs | This re-verify | Verdict |
|---|---|---|---|
| Branch `VehSpec+0x4c0 == 4` → Tank ctor | `fn_005fd390_buildFramework.md`, `setup-field-mapping.md` | yes | **match** |
| Both paths size `0x38`, shared desc + `FUN_005d6720` | buildFramework verified | yes | **match** |
| Tank ctor = parent + vtable only | not previously detailed | decompile | **new detail** |
| Runtime tank update ≠ zero non-steer angles | not in Phase-0 steering-spec | `0x64fb60` + DAT 0.6/1.0 | **new detail** |
| Quadratic falloff shared with default | `fn_0064f840_steering.md` | same math in tank update | **match** |

**No construction-order conflict.** Port implication: selecting tank is not a different
descriptor; it is a **different update law** on the same fields.

---

## 7. Port notes (no C# in this file)

1. When `vehicleType == 4`, construct a steering component whose **update** implements the tank
   writeback (`-angle·0.6` for non-steer `i<4`, `-angle` for non-steer `i≥4`); else default
   zero non-steer.
2. Keep **one** descriptor path (`rlSteeringMaxAngle` / `FullSpeedLimit` / axle doesSteer bits).
3. Do not invent extra tank ctor fields — binary only rebinds the vtable.
4. Emulation skipped (pointer-heavy framework graph). Goldens: synthetic steer input + speed +
   flag patterns → expected `outAngle[]` from the closed forms above.
5. Full default update evidence: `fn_0064f840_steering.md`. Descriptor: `fn_005fc710_steeringBuilder.md`.
   Framework order: `fn_005fd390_buildFramework.md`.

---

## 8. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x64fc80` | OK — parent + vtable only |
| `decompile_function` `0x64fac0` / `0x64f920` / `0x65e5f0` | OK — parent fill chain |
| `decompile_function` `0x5fd390` steering branch | OK — `+0x4c0 == 4` → Tank |
| `decompile_function` `0x64fb60` (tank update) | OK — negate × 0.6 / 1.0 |
| `read_memory` `0x9e4ee4` / `0x9e4f1c` | vtable slots; update at `+0x14` |
| `read_memory` `0xaf50c4` / `0xaf50c8` | **1.0** / **0.6** |
| `get_xrefs_to` `0x64fc80` | sole call from buildFramework |
| Conflict with prior docs | none on construction; runtime tank law is additive |
