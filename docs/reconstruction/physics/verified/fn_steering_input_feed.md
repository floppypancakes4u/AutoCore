# Verified: Steer-input feed into `hkDefaultSteering_update` @ `0x64f840`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Consumer | `hkDefaultSteering_update` @ `0x0064f840` (also `TankSteering` update @ `0x0064fb60` — same input load) |
| RE tools | `decompile_function` / `disassemble_function` on the chain below; `get_function_xrefs` / vtable DATA xrefs; `read_memory` on DAT constants; reflection strings @ `0x9e4f8c` / `0x9e4fa8` |
| Status | **Verified** (2026-07-15) |
| Related | `fn_0064f840_steering.md`, `fn_0064fac0_steeringCtor.md`, `fn_00598650_steerRamp.md`, `fn_00636a60_tickSubsystems.md`, `fn_004f5620_setSteerInput.md`, `fn_004fb660_createVehicleAction.md` |

**Scope:** how the **runtime steer command** that multiplies into wheel angle is produced **before** `0x64f840` runs.  
**Not this file:** quadratic speed falloff / per-wheel out angles (see `fn_0064f840_steering.md`); mode-`0x02` upright / impulse branch.

**No C#.**

---

## 0. Critical correction (read first)

`0x64f840` does **not** read a “normalized steer input” from `hkDefaultSteering+0x24`.

Assembly at entry:

```
ESI = this                          ; hkDefaultSteering*
EAX = [ESI+0x08]                    ; framework*
ECX = [EAX+0x14]                    ; framework+0x14 = hkDefaultAnalogDriverInput*
XMM0 = [ECX+0x14]                   ; driverInput+0x14  ← RUNTIME STEER COMMAND
MULSS XMM0, [ESI+0x24]              ; * this+0x24       ← maxSteeringAngle (radians)
```

| Operand | Location | Identity | When written |
|---|---|---|---|
| Runtime steer command | `*( *(fw+0x14) + 0x14 )` | `hkDefaultAnalogDriverInput+0x14` | Every substep, **first** child update (`calcStatus` @ `0x5fe520`) |
| Max angle scale | `hkDefaultSteering+0x24` | Havok `maxSteeringAngle` | **Init only** via `FUN_0064f920` from steering descriptor (`desc+0x04`) |

Evidence that `this+0x24` is max angle (not input):

* Reflection member name `maxSteeringAngle` @ `0x9e4fa8`, offset `+0x04` inside the class body that starts at object `+0x20` → absolute `+0x24`.
* `FUN_0064f920` (called from `hkDefaultSteering_ctor` @ `0x64fac0`): `*(this+0x24) = *(desc+0x04)`.
* Object size is **`0x38`** — `hkpVehicleSteering_setSteeringAngle` (`0x636410`) stores to **`+0x50`**, so it **cannot** target this object.

Older notes / `fn_0064f840_steering.md` §2 that label `this+0x24` as “normalized steer input” and `*(fw+0x14)+0x14` as max angle have the **two multiply operands swapped**. Binary + reflection win.

---

## 1. End-to-end feed chain

```
entity+0x618          raw steer axis  [-1,+1]
        │  writers: SetSteerInput 0x4f5620, MoveToTarget 0x4fc650, DriveControlTick, …
        ▼
VehicleAction+0x24    stage-1 ramp (dt-scaled)          applyAction AFTER tickSubsystems
        │  always mirrors ↓
        ▼
driverInput+0x1c      raw command for analog curve      same store as "stage-1 mirror"
        │  (value used NEXT substep)
        ▼
FUN_005fdf20          deadzone + piecewise linear curve
        │  called from calcStatus
        ▼
driverInput+0x14      filtered steer status  [-1,+1]    written in tick slot 1
        │
        ▼
hkDefaultSteering_update 0x64f840
        angle0 = (driverInput+0x14) * (steering+0x24 /*maxAngle*/)
        then quadratic FullSpeedLimit falloff; per-wheel outAngle[]
```

### Tick ordering (same `applyAction` call)

`VehicleAction_applyAction` @ `0x598650`:

| Order | Site | What happens for steer |
|------:|---|---|
| 1 | `0x5987a2` `tickSubsystems` (`ECX = VA+0x40` = framework) | **Uses previous frame’s `DI+0x1c`** |
| 1a | child `fw+0x14` `vtbl+0x14` = `0x5fe520` | `DI+0x14 = FUN_005fdf20(DI)` |
| 1b | child `fw+0x18` `vtbl+0x14` = `0x64f840` | multiplies `DI+0x14 * maxAngle` |
| 2 | `0x5988a9`… stage-1 ramp | `entity+0x618 → VA+0x24 → DI+0x1c` for **next** substep |
| 3 | mode `0x02` only | `VA+0x28` ±0.05, then `setSteeringAngle(VA+0x3c, VA+0x28*0.6)` — **not** an input to `0x64f840` |

So `0x64f840` always sees steer status computed from the **previous** stage-1 sample (1-substep lag).

---

## 2. Object identity: `framework+0x14` = analog driver input

### Wiring

`Vehicle_createVehicleAction` @ `0x4fb660`:

```
handle = entity+0x1a0          // 0xC block
handle[0] = VehicleAction*
handle[4] = hkVehicleFramework*
handle[8] = hkDefaultAnalogDriverInput*   // ctor 0x5fe020, heap size 0x40
```

`Vehicle_buildHavokVehicleFramework` @ `0x5fd390` receives that driver-input pointer as **arg** and packs it into the setup descriptor; `hkVehicleFramework_wireComponents` @ `0x636940`:

```
fw+0x14 = desc[2] = driverInput     // ticked 1st
fw+0x18 = desc[3] = steering        // ticked 2nd  (update 0x64f840)
```

Same pointer is also what `PushDriveAxesToController` (`0x4fbc10`) treats as `ctrl = handle[8]` for throttle/handbrake (`+0x20` / `+0x24`).

### Stage-1 mirror target (corrected name)

Asm in applyAction (stage-1 end):

```
MOV  ECX, [ESI+0x40]      ; framework
MOV  EAX, [ECX+0x14]      ; driverInput
MOV  [EAX+0x1c], EDX      ; DI+0x1c = VA+0x24
```

`fn_00598650_steerRamp.md` calling this `wheelsDesc+0x1c` is a **name error** — the pointer is `framework+0x14` = **driver input**, not wheels. Offset `+0x1c` is correct.

---

## 3. Producers of each stage

### 3.1 Raw axis — `entity+0x618`

| Writer | Addr | Notes |
|---|---|---|
| `VehicleEntity_SetSteerInput` | `0x4f5620` | Gated store (`wobj+0xb4 & 0xC7`); see `fn_004f5620_setSteerInput.md` |
| `CVOGVehicle::MoveToTarget3DPoint` | `0x4fc650` | AI proportional steer (may write `+0x618` inline with same gate) |
| `Client_Input_DriveControlTick` | (via setter) | Held L/R → ±1 / soft ±0.5 |

### 3.2 Stage-1 — `VA+0x24` → `DI+0x1c`

Full bit-exact ramp: **`fn_00598650_steerRamp.md` §3**.

Summary:

```
delta  = entity[0x618] - VA[0x24]
factor = 2.0 if "open band" else 1.0     // DAT_00a10e74 = 2.0
step   = min(|delta|, VA[0x20] * dt * factor)
VA[0x24] += sign(delta)*step ; clamp [-1,+1]
DI[0x1c] = VA[0x24]
```

* Always runs (not gated on movement mode `0x02`).
* If `VA+0x20 == 0`, stage-1 does not move (step 0).

### 3.3 Status filter — `DI+0x14` via `FUN_005fdf20` @ `0x5fdf20`

Sole caller: `hkDefaultAnalogDriverInput_calcStatus` @ `0x5fe520` (`CALL` @ `0x5fe58d`, `ECX = this`).

```c
// this = hkDefaultAnalogDriverInput*
// __fastcall FUN_005fdf20(this)
float10 FUN_005fdf20(int this)
{
  float x = fabsf(*(float*)(this + 0x1c));   // stage-1 command
  if (x < *(float*)(this + 0x38))            // deadZone
    return 0;

  float sign = (*(float*)(this + 0x1c) <= 0.f) ? -1.f : +1.f;  // DAT_00aaa668 / g_flOne

  if (x < *(float*)(this + 0x28)) {          // below slopeChangePoint
    return (x - *(float*)(this + 0x38)) * *(float*)(this + 0x2c) * sign;
  }
  return ( (x - *(float*)(this + 0x28)) * *(float*)(this + 0x30)
           + *(float*)(this + 0x34) ) * sign;
}
```

`calcStatus` then:

```
*(float*)(this + 0x14) = (float)FUN_005fdf20(this);
```

(Also fills accel `+0x0c`, brake `+0x10`, handbrake `+0x18`, reverse `+0x19` from pedal/handbrake fields — not used by `0x64f840`.)

### 3.4 Curve constants (from `hkDefaultAnalogDriverInput_ctor` @ `0x5fe020`)

Create path (`0x4fb660`) seeds the ctor arg block:

| Arg | Source | `read_memory` | → field |
|---|---|---:|---|
| `[0]` slopeChangePoint | `DAT_00a0f710` | **0.7** (`33 33 33 3f`) | `DI+0x28` |
| `[1]` lowRangeGain | `DAT_009cd0d8` | **0.5** (`00 00 00 3f`) | `DI+0x2c` |
| `[2]` deadZone | `0` (immediate) | **0.0** | `DI+0x38` |
| `[3]` autoReverse byte | `1` | — | `DI+0x3c` |

Ctor-derived high-range coeffs:

```
lowSpan   = (slopeChange - deadZone) * lowRangeGain     // 0.7 * 0.5 = 0.35 → DI+0x34
highGain  = (1 - lowSpan) / ((1 - deadZone) - (slopeChange - deadZone))
          // 0.65 / 0.3 ≈ 2.1666667 → DI+0x30
```

With retail seeds (deadZone 0, slope 0.7, gain 0.5):

| `|raw|` | filtered `|out|` |
|--------:|----------:|
| 0 | 0 |
| 0.35 | 0.175 |
| 0.7 | 0.35 |
| 1.0 | 1.0 |

Sign is preserved from `DI+0x1c`.

---

## 4. What does **not** feed `0x64f840`

| Path | Where | Why not |
|---|---|---|
| Stage-2 `VA+0x28` ±0.05 | applyAction mode `0x02` | Never read by steering update |
| `× 0.6` (`DAT_00af3384`) | immediately before `0x636410` | Scales value into **`VA+0x3c+0x50`** only |
| `hkpVehicleSteering_setSteeringAngle` @ `0x636410` | `*(obj+0x50) = arg` | Target is the **`0x60`-byte** action object at `VA+0x3c` (created by `FUN_00636490`), not `hkDefaultSteering` (`0x38` bytes). Consumer of `+0x50` is `FUN_00636520` (orientation assist when `+0x5c != 0`), not `0x64f840` |
| `PushDriveAxes` | writes `DI+0x20` throttle / `DI+0x24` handbrake | No write to `DI+0x1c` / `+0x14` for steer |

Ports that feed `VA+0x28 * 0.6` (or raw `VA+0x28`) into wheel-angle math as “the steer input for `0x64f840`” are **wrong**. The angle math multiplies **filtered `DI+0x14`**, which tracks **stage-1** (`VA+0x24` / `DI+0x1c`), **not** stage-2.

---

## 5. `hkDefaultSteering+0x24` (max angle) — set once

| Step | Addr | Action |
|---|---|---|
| Build desc | `Vehicle_BuildSteeringDescriptor` `0x5fc710` | `desc+0x04 = VehSpec+0x594 * entity+0x208` |
| Ctor | `hkDefaultSteering_ctor` `0x64fac0` | calls `FUN_0064f920` |
| Copy | `FUN_0064f920` | `this+0x24 ← desc+0x04`; `this+0x28 ← desc+0x08` (FullSpeedLimit) |
| Re-init helper | `FUN_0064fa50` | base reinit + `FUN_0064f920` again |

No per-tick writer to `steering+0x24` was found (only ctor / re-init xrefs of `0x64f920`).

---

## 6. Xrefs summary

| Symbol | Addr | Xrefs / call sites |
|---|---|---|
| `hkDefaultSteering_update` | `0x64f840` | **DATA only** @ `0x9e4ef8` = vtable slot `+0x14` of `PTR_FUN_009e4ee4`. Invoked only via `tickSubsystems` child dispatch. |
| `hkDefaultAnalogDriverInput_calcStatus` | `0x5fe520` | **DATA only** @ `0x9dd37c` = driver-input vtable `+0x14`. |
| `FUN_005fdf20` (steer curve) | `0x5fdf20` | Sole code xref: `0x5fe58d` inside `calcStatus`. |
| Stage-1 mirror store | `0x598966` / `0x598991` | Inside `applyAction` (two paths after ramp). |
| `setSteeringAngle` | `0x636410` | `applyAction` `0x598ad2`; init `FUN_00597ec0` `0x597f42` — **orthogonal** to `0x64f840`. |

---

## 7. Compact port recipe (behavior)

1. Keep **stage-1** float (`VA+0x24` semantics): ramp raw axis with `rateBase * dt * {1|2}`, clamp ±1.
2. Publish stage-1 into the **driver-input raw steer slot** (`DI+0x1c`).
3. On the Havok vehicle tick, **before** steering:
   - Run the piecewise curve (`FUN_005fdf20`) → status steer (`DI+0x14`).
4. Steering update:
   - `mainAngle = statusSteer * maxSteeringAngle`
   - apply quadratic FullSpeedLimit falloff; write per-wheel angles.
5. Do **not** wire stage-2 / `*0.6` / `+0x50` into step 4 unless porting the separate `VA+0x3c` orientation action.

---

## 8. Constants — `read_memory` this pass

| Address | LE bytes | Float | Role in **this** feed |
|---|---|---:|---|
| `0x00a0f710` | `33 33 33 3f` | **0.7** | Analog DI slopeChangePoint (`DI+0x28`) |
| `0x009cd0d8` | `00 00 00 3f` | **0.5** | Analog DI low-range gain (`DI+0x2c`) |
| `0x00a10e74` | `00 00 00 40` | **2.0** | Stage-1 open-band rate factor |
| `0x00aaa668` | `00 00 80 bf` | **−1.0** | Sign / clamp min |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Clamp max / sign + |

Stage-2 / ×0.6 constants (`0xa10e78=0.05`, `0xaf3388=20`, `0xaf3384=0.6`) are **real** but belong to the **non-feeding** path; listed in `fn_00598650_steerRamp.md`.

---

## 9. Misread registry

| Claim | Verdict |
|---|---|
| `hkDefaultSteering+0x24` is runtime normalized steer input | **FALSE** — `maxSteeringAngle` (init) |
| `*(fw+0x14)+0x14` is max angle | **FALSE** — it is **filtered steer status** |
| `setSteeringAngle` / `VA+0x28*0.6` feeds `0x64f840` | **FALSE** — different object (`VA+0x3c+0x50`) |
| Stage-1 mirror goes to “wheelsDesc” | **FALSE name** — target is **driverInput** at `fw+0x14` |
| Same-frame stage-1 is visible to `0x64f840` | **FALSE** — stage-1 runs **after** tick; 1-substep lag |

---

## 10. RE checklist

| Step | Result |
|---|---|
| Disasm `0x64f840` load of `[fw+0x14]+0x14` × `[this+0x24]` | OK |
| Wire map `fw+0x14` = driver input (`0x636940` + createVehicleAction) | OK |
| Decompile `0x5fe520` → store `+0x14` from `0x5fdf20` | OK |
| Decompile `0x5fdf20` reads `+0x1c`, deadzone/slope math | OK |
| Asm applyAction stage-1 → `[fw+0x14]+0x1c` | OK |
| `tickSubsystems` order: driverInput then steering | OK (`fn_00636a60`) |
| Ctor/reflection: `this+0x24` = `maxSteeringAngle` | OK |
| `0x636410` xrefs only applyAction + init; writes `+0x50` | OK |
| `read_memory` 0.7 / 0.5 / 2.0 / ±1 | OK |
| No C# | OK |
