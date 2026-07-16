# Verified: `VehicleEntity_PushDriveAxesToController` @ `0x4fbc10`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `VehicleEntity_PushDriveAxesToController` |
| Address | `0x004fbc10` |
| Body | `004fbc10` – `004fbeb2` |
| Convention | MSVC `__fastcall` / `__thiscall` — entity `this` in `ECX` (`param_1`) |
| Role | Copy entity drive axes into the **input controller** (`ctrl = *(entity+0x1a0)+8`); apply hard-stop, optional thr clamp, speed-cap throttle zeroing |
| RE tools | `decompile_function` @ `0x4fbc10`; `read_memory` on DAT constants below; callees `FUN_00404a20`, `FUN_004c4e20`, `FUN_004e8a40` |
| Status | **Verified** (re-decompiled) |
| Scope | RE evidence only — **no C#** |

Plate comment (Ghidra): *WI-MOV-002: Push entity drive axes into VehicleAction controller.*  
Naming note: the write target is the **input controller** at `[entity+0x1a0]+8`, which is **layout-distinct** from the Havok `VehicleAction` instance used by `applyAction` (see §4).

---

## 1. How thr / steer / sharp reach the controller

Entity axes are produced earlier (e.g. `MoveToTarget3DPoint` `0x4fc650`, `Vehicle_setDrivingInputs` `0x504c70`, local `DriveControlTick`). This function is the **bridge** for two of three axes:

| Axis | Entity | This function | Downstream consumer |
|------|--------|---------------|---------------------|
| **Throttle** | `+0x614` f32 (`Accel=−1`, `Reverse=+1`) | **Copied** → `ctrl+0x20` (may clamp / zero) | Input controller thr; Havok side uses `VehicleAction+0x20` / applyAction family |
| **Steer** | `+0x618` f32 | **Not written here** | **`VehicleAction::applyAction`** reads `entity+0x618` directly as stage-1 ramp target into `VA+0x24` → wheelsDesc `+0x1c` → mode-`0x02` `VA+0x28` → `setSteeringAngle` |
| **Sharp / handbrake** | `+0x61c` u8 | **Copied** → `ctrl+0x24` | Controller handbrake byte; **also** read as entity field in `calcWheelTorque` (rear drive torque ×0.5) |

```
entity+0x614 ──PushDrive──► ctrl+0x20   (throttle float)
entity+0x61c ──PushDrive──► ctrl+0x24   (handbrake byte)
entity+0x618 ──(skip)─────► applyAction ramps VA+0x24 toward entity+0x618
```

**Critical failure class (`nullWheels`):** if `entity+0x1a0 == 0` **or** `entity+0x101 != 0`, this function is a **complete no-op** — throttle/handbrake never reach the controller; steer still only matters if applyAction is running with a live action/entity.

---

## 2. Decompile (authoritative)

```c
/* WI-MOV-002: Push entity drive axes into VehicleAction controller.
   Requires entity+0x101==0 and entity+0x1a0!=0.
   ctrl = *(entity+0x1a0)+8
   Writes:
     ctrl+0x20 = entity+0x614 (longitudinal/throttle)
     if ctrl+0x19: clamp throttle to DAT_00a0f734 (0.9)
     if entity+0x109: force ctrl+0x20=0 and ctrl+0x24=1 (stop)
     ctrl+0x24 = entity+0x61c (handbrake byte)
     ctrl+0x25 = 0
   Also speed-cap gate: may zero ctrl+0x20 when max < entity+0x10c and thr
   would accelerate along travel direction.
   entity+0x618 is NOT written here.
*/

void __fastcall VehicleEntity_PushDriveAxesToController(int param_1)  // entity
{
  float fVar1;
  int iVar2;          // ctrl
  int *piVar3;
  uint uVar4;
  float *pfVar5;
  int iVar6;
  bool bVar7;
  float10 fVar8;
  float fVar9;
  float fStack_40;    // speed-bonus accumulator (see §5)
  float local_3c;     // derived max speed
  float local_34;     // |chassis lin vel| (computed; see note)
  float fStack_30, fStack_2c, fStack_28;
  float fStack_24, fStack_20, fStack_1c;  // forward basis after FUN_004e8a40

  if ((*(char *)(param_1 + 0x101) == '\0') && (*(int *)(param_1 + 0x1a0) != 0)) {
    iVar2 = *(int *)(*(int *)(param_1 + 0x1a0) + 8);   // ctrl
    *(undefined1 *)(iVar2 + 0x25) = 0;

    // ---- HARD STOP ----
    if (*(char *)(param_1 + 0x109) != '\0') {
      *(undefined4 *)(iVar2 + 0x20) = 0;               // thr = 0
      *(undefined1 *)(iVar2 + 0x24) = 1;               // handbrake on
      return;                                          // does NOT copy +0x61c
    }

    // ---- THROTTLE COPY ----
    fVar9 = *(float *)(param_1 + 0x614);
    *(float *)(iVar2 + 0x20) = fVar9;

    // optional soft thr ceiling when controller flag +0x19 set
    if (*(char *)(*(int *)(*(int *)(param_1 + 0x1a0) + 8) + 0x19) != '\0') {
      if (DAT_00a0f734 <= fVar9) {
        fVar9 = DAT_00a0f734;                          // 0.9
      }
      *(float *)(iVar2 + 0x20) = fVar9;
    }

    // ---- SPEED / CAP PREP ----
    if (*(int *)(param_1 + 8) == 0) {
      pfVar5 = (float *)&DAT_00b041b0;                 // fallback lin vel
    }
    else {
      pfVar5 = (float *)(*(int *)(*(int *)(param_1 + 8) + 0x3c) + 0x40);
    }
    fVar9 = *(float *)(param_1 + 0x10c);               // requested/target speed field
    piVar3 = *(int **)(*(int *)(*(int *)(param_1 + 4) + 4) + 0xb0 + param_1);

    local_34 = SQRT(pfVar5[2]*pfVar5[2] + pfVar5[1]*pfVar5[1] + *pfVar5 * *pfVar5);

    // base max from driver/vehicle object (+0xb0 chain) via vfunc 0x1d8 + FUN_004c4e20
    if ((piVar3 == (int *)0x0) || (iVar6 = (**(code **)(*piVar3 + 0x1d8))(), iVar6 == 0)) {
      local_3c = 0.0;
    }
    else {
      (**(code **)(**(int **)(*(int *)(*(int *)(param_1 + 4) + 4) + 0xb0 + param_1) + 0x1d8))();
      fVar8 = (float10)FUN_004c4e20();
      local_3c = (float)fVar8;
    }

    // driver speed bonus (+0xd48) under feature flag DAT_00af1854
    iVar6 = (**(code **)(*(int *)(*(int *)(*(int *)(param_1 + 4) + 4) + 4 + param_1) + 0x210))(0);
    if ((iVar6 != 0) && (DAT_00af1854 != '\0')) {
      iVar6 = (**(code **)(*(int *)(*(int *)(*(int *)(param_1 + 4) + 4) + 4 + param_1) + 0x210))(0);
      fStack_40 = *(float *)(iVar6 + 0xd48) + fStack_40;
    }

    iVar6 = *(int *)(*(int *)(param_1 + 4) + 4);
    uVar4 = *(uint *)(iVar6 + 0xb8 + param_1);
    iVar6 = iVar6 + param_1;

    // overheat-style penalty on bonus
    if (((uVar4 & 0x1000) != 0) ||
        ((*(int *)(iVar6 + 0xb0) != 0 && ((*(byte *)(*(int *)(iVar6 + 0xb0) + 0xb5) & 0x10) != 0))))
    {
      fStack_40 = fStack_40 - g_flOverheatCoolFrac;    // 0.3
    }
    // boost-style add
    if (((uVar4 & 0x4000) != 0) ||
        ((*(int *)(iVar6 + 0xb0) != 0 && ((*(byte *)(*(int *)(iVar6 + 0xb0) + 0xb5) & 0x40) != 0))))
    {
      fStack_40 = fStack_40 + DAT_009cd0d8;            // 0.5
    }

    // vehicleData AbsoluteTopSpeed at +0x634; −1 means "no cap"
    fVar1 = *(float *)(*(int *)(*(int *)(iVar6 + 0xac) + 0x3c) + 0x634);
    local_3c = (fStack_40 + g_flOne) * local_3c;       // (bonus+1) * baseMax
    if ((fVar1 != DAT_00aaa668) && (fVar1 < local_3c)) {
      local_3c = fVar1;                                // clamp to vehicle top speed
    }

    // ---- SPEED-CAP THROTTLE GATE ----
    // when derived max < entity+0x10c, zero thr if it would accelerate along travel dir
    if (local_3c < fVar9) {
      pfVar5 = (float *)FUN_00404a20();                // chassis basis (or fallback)
      local_34 = *pfVar5;
      fStack_30 = pfVar5[1];
      fStack_2c = pfVar5[2];
      fStack_28 = pfVar5[3];
      FUN_004e8a40(&local_34, &fStack_24);             // forward → fStack_24/20/1c

      if (*(int *)(param_1 + 8) == 0) {
        pfVar5 = (float *)&DAT_00b041b0;
      }
      else {
        pfVar5 = (float *)(*(int *)(*(int *)(param_1 + 8) + 0x3c) + 0x40);
      }

      // forwardSpeed = dot(linVel, forward)
      if (pfVar5[2] * fStack_1c + pfVar5[1] * fStack_20 + *pfVar5 * fStack_24 <= 0.0) {
        fVar9 = *(float *)(iVar2 + 0x20);
        bVar7 = fVar9 < 0.0;                           // thr negative (Accel)?
      }
      else {
        fVar9 = *(float *)(iVar2 + 0x20);
        bVar7 = 0.0 < *(float *)(iVar2 + 0x20);        // thr positive (Reverse)?
      }
      // zero thr when it is non-zero AND not the "toward travel" polarity
      // (see §5.3 for sign-convention interpretation)
      if (!bVar7 && fVar9 != 0.0) {
        *(undefined4 *)(iVar2 + 0x20) = 0;
      }
    }

    // ---- HANDBRAKE COPY (always on normal path) ----
    *(undefined1 *)(iVar2 + 0x24) = *(undefined1 *)(param_1 + 0x61c);
  }
  return;
}
```

---

## 3. Object layout

### 3.1 Entity (`param_1` = `CVOGVehicle`)

| Offset | Type | Role in this function |
|-------:|------|------------------------|
| `+0x04` | ptr | component base for `+4+4` / `+0xb0` / `+0xb8` / `+0xac` chains |
| `+0x08` | ptr | physics wrapper; lin vel at `*[+0x08]+0x3c + 0x40` |
| `+0x101` | u8 | disabled — must be `0` or entire function no-ops |
| `+0x109` | u8 | hard-stop — thr 0, handbrake 1, early return |
| `+0x10c` | f32 | requested / target speed compared to derived max (`local_3c`) |
| `+0x1a0` | ptr | input-controller holder; must be non-null |
| `+0x614` | f32 | throttle source |
| `+0x618` | f32 | steer — **unread** |
| `+0x61c` | u8 | sharp/handbrake source |

Vehicle top-speed field (not on entity root):

```
vehicleData = *(*( (*(*(entity+4)+4) + entity) + 0xac ) + 0x3c)
vehicleData+0x634 = AbsoluteTopSpeed  (f32; −1.0 = uncapped)
```

### 3.2 Input controller — `ctrl = *(entity+0x1a0)+8`

| Offset | Type | Written? | Meaning |
|-------:|------|:--------:|---------|
| `+0x19` | u8 | read | if set, clamp thr magnitude path to ≤ 0.9 |
| `+0x20` | f32 | **yes** | throttle copy / post-gate thr |
| `+0x24` | u8 | **yes** | handbrake (`entity+0x61c`, or `1` on hard-stop) |
| `+0x25` | u8 | **yes** | cleared to `0` every successful entry |

This is **not** the Havok `VehicleAction` object:

| Slot | Input controller (`[ent+0x1a0]+8`) | Havok `VehicleAction` |
|------|------------------------------------|------------------------|
| `+0x20` | thr float (PushDrive write) | thr / ramp-rate partner (applyAction) |
| `+0x24` | **u8 handbrake** | **f32 steer stage-1** |
| `+0x28` | — | f32 steer final (mode 0x02) |

---

## 4. Control flow (summary)

```
entry
  │
  ├─ entity+0x101 != 0  OR  entity+0x1a0 == 0  ──► return (no-op)
  │
  ctrl = *(entity+0x1a0)+8
  ctrl+0x25 = 0
  │
  ├─ entity+0x109 != 0  ──► ctrl+0x20=0; ctrl+0x24=1; return
  │
  ctrl+0x20 = entity+0x614
  ├─ ctrl+0x19  ──► ctrl+0x20 = min(thr, 0.9)   // only upper clamp vs DAT_00a0f734
  │
  derive local_3c (max speed) from driver/vehicle + modifiers + top-speed clamp
  │
  ├─ local_3c < entity+0x10c
  │     └─ if thr would accelerate along travel dir polarity fails test ──► ctrl+0x20 = 0
  │
  ctrl+0x24 = entity+0x61c
  return
```

---

## 5. Authoritative behavior notes

### 5.1 Throttle path

1. Always assign `ctrl+0x20 ← entity+0x614` first (after hard-stop gate).
2. If `ctrl+0x19 != 0` and `thr >= DAT_00a0f734 (0.9)`, store `0.9`.  
   Decompile only compares `DAT_00a0f734 <= thr` (positive ceiling). With retail sign convention (forward thr often **negative**), this mainly affects **reverse / positive** thr ≥ 0.9; it is **not** a symmetric `±0.9` clamp.
3. Speed-cap gate may later force `ctrl+0x20 = 0`.
4. Sign convention is preserved: no negate, no abs — raw entity thr.

### 5.2 Sharp / handbrake path

- Normal path: last write `ctrl+0x24 = entity+0x61c` (0 or 1 typical).
- Hard-stop path: `ctrl+0x24 = 1` without reading `+0x61c`.
- **Does not** write Havok `VehicleAction+0x24` (that is steer ramp).
- Physics effect of the **entity** byte also occurs in `VehicleAction_calcWheelTorque` @ `0x598040`: rear wheels × `0.5` when `entity+0x61c != 0` (`DAT_00a0f298`). That read is independent of this controller copy.

### 5.3 Steer path (why it does not go through this function)

Steer never touches `[entity+0x1a0]+8` in this body. Pipeline:

```
entity+0x618
    → VehicleAction::applyAction (0x598650)
         stage-1: ramp VA+0x24 toward entity+0x618  (rate uses VA+0x20 * dt)
                  wheelsDesc+0x1c = VA+0x24
         mode 0x02: VA+0x28 ← ramp(wheelsDesc+0x1c * min(|v|/20, 1))
                    hkpVehicleSteering_setSteeringAngle(VA+0x28)
    → hkDefaultSteering_update (0x64f840): maxAngle * input, quadratic speed falloff
```

Evidence: decompile of `0x4fbc10` has **zero** loads/stores of `param_1+0x618`. Cross-check `steering-spec.md`, `fn_entity_driveAxes_offsets.md`, `0.8-struct-offsets.md`.

### 5.4 Speed-cap gate (polarity)

Given `forwardSpeed = dot(linVel, chassisForward)`:

| Travel | `bVar7` true when | Zero thr when |
|--------|-------------------|---------------|
| `forwardSpeed > 0` | `thr > 0` (reverse input) | thr ≠ 0 and thr ≤ 0 → **zeros Accel (negative thr)** |
| `forwardSpeed ≤ 0` | `thr < 0` (accel input) | thr ≠ 0 and thr ≥ 0 → **zeros Reverse (positive thr)** |

Interpretation under `Accel=−1` / `Reverse=+1`: when over the derived limit relative to `entity+0x10c`, throttle that would **push further in the direction of travel** is cleared; opposing thr is left alone.

Gate condition is **`local_3c < entity+0x10c`** (derived max vs entity field), **not** a direct `\|v\| > max` compare in the decompile.  
`\|v\|` is computed into `local_34` before the max derivation, then that slot is **reused** as basis storage inside the gate — so the early `SQRT` has **no later use** in the decompiled SSA (possible dead compute / analysis artifact). Ports should implement the **comparison operands as decompiled** (`local_3c` vs `+0x10c`).

### 5.5 Max-speed derivation (port caution)

```
baseMax = 0  if driverObj null / vfunc 0x1d8 fails
        else FUN_004c4e20(...) related float   // driver/vehicle max family

bonus  ≈ 0
       + driver+0xd48 when DAT_00af1854 and driver resolve via vfunc 0x210
       − 0.3 when flag 0x1000 or (obj+0xb5 & 0x10)
       + 0.5 when flag 0x4000 or (obj+0xb5 & 0x40)

local_3c = (bonus + 1.0) * baseMax
if vehicleData+0x634 != −1 and vehicleData+0x634 < local_3c:
    local_3c = vehicleData+0x634
```

`fStack_40` appears **without an explicit zeroing store** in the decompile before the `+0xd48` add — treat initial bonus as **0** for porting unless live traces show otherwise (typical MSVC stack reuse). Full bit-exact `FUN_004c4e20` / vfunc `0x1d8` wiring is out of scope of the thr/steer/sharp bridge; those only feed this gate.

---

## 6. Constants (`read_memory`, length 4)

| Symbol | Address | LE bytes | float32 / value | Role |
|--------|---------|----------|----------------:|------|
| `DAT_00a0f734` | `0x00a0f734` | `66 66 66 3f` | **0.9** | thr ceiling when `ctrl+0x19` |
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **−1.0** | “no AbsoluteTopSpeed” sentinel |
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | `(bonus+1)*baseMax` |
| `g_flOverheatCoolFrac` | `0x00a0f714` | `9a 99 99 3e` | **0.3** | bonus penalty |
| `DAT_009cd0d8` | `0x009cd0d8` | `00 00 00 3f` | **0.5** | bonus add |
| `DAT_00af1854` | `0x00af1854` | `01` (u8) | **1** | enables driver `+0xd48` bonus path |
| `DAT_00b041b0` | `0x00b041b0` | (vec3) | fallback lin vel when `entity+0x08==0` | |

---

## 7. Callees

| Addr | Name | Role |
|------|------|------|
| `0x004c4e20` | `FUN_004c4e20` | Base max-speed float (driver field `+500` / `+0x1f4` family + optional `+0xd48`) |
| `0x00404a20` | `FUN_00404a20` | Returns chassis basis ptr (`phys+0x3c+0x30`) or fallback |
| `0x004e8a40` | `FUN_004e8a40` | Extract **forward** unit vector from basis |

No call into `applyAction`, steering, or torque here — pure input staging.

---

## 8. Callers (xrefs)

| Caller | Addr | Context |
|--------|------|---------|
| `CVOGVehicle::MoveToTarget3DPoint` | `0x4fc650` | AI drive/arrival — after writing `+0x614/+0x618/+0x61c` |
| `Vehicle_setDrivingInputs` | `0x504c70` | Network ghost axes |
| `VehicleEntity_SetDriveAxes` | `0x4fbec0` | Set/clear all three then push |
| `Client_Input_DriveControlTick` | `0x923676` area | Local player |
| `Client_Input_PollBoundActions` | `0x9260da` area | Local player binds |
| `Vehicle_TryActivatePhysics` | `0x5014d8` | Activation path |
| others | `0x4fbef0`, `0x5057c0`, `0x5d73a0`, `0x636ba0`, `0x915670`, `0x93a5c0` | auxiliary |

---

## 9. Related docs

| Doc | Relevance |
|-----|-----------|
| [`fn_entity_driveAxes_offsets.md`](fn_entity_driveAxes_offsets.md) | Full writer → bridge → consumer map for `+0x614/618/61c` |
| [`../drive-controller-spec.md`](../drive-controller-spec.md) | MoveToTarget axis generation (writes entity before this push) |
| [`../steering-spec.md`](../steering-spec.md) | How `entity+0x618` reaches wheels (not this fn) |
| [`../brake-spec.md`](../brake-spec.md) | `+0x61c` handbrake semantics; VA+0x24 is **not** brake |
| [`../0.8-struct-offsets.md`](../0.8-struct-offsets.md) | Entity / VA / ctrl layouts |
| [`fn_00598040_calcWheelTorque.md`](fn_00598040_calcWheelTorque.md) | Entity `+0x61c` rear ×0.5 |
| [`fn_0064f840_steering.md`](fn_0064f840_steering.md) | Physical steer after applyAction |

---

## 10. RE checklist

| Step | Result |
|------|--------|
| `decompile_function` `0x4fbc10` | OK — full body; thr→`ctrl+0x20`, sharp→`ctrl+0x24`, no `+0x618` |
| `get_function_by_address` | `VehicleEntity_PushDriveAxesToController` `004fbc10`–`004fbeb2` |
| `read_memory` `0xa0f734` | **0.9** thr ceiling |
| `read_memory` `0xaaa668` | **−1.0** top-speed sentinel |
| `read_memory` `0xa0f714` | **0.3** overheat cool frac |
| `read_memory` `0x9cd0d8` | **0.5** bonus add |
| `read_memory` `0xaf1854` | **1** (feature flag byte) |
| Callee decompiles | `FUN_00404a20` basis; `FUN_004c4e20` max-speed helper |
| Steer path | Confirmed **out of band** via applyAction (cross-doc) |
| Emulation | Skipped — pointer graph (`+0x1a0`, driver vfuncs, chassis) |
| No C# | satisfied |
