# Verified: `VehicleAction::applyAction` offsets — `0x00598650`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK + AA custom layer.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Reconciled against `0.8-struct-offsets.md`, `steering-spec.md`, `brake-spec.md`, `docs/NPCDriving.md` §6.1.

| Item | Value |
|------|--------|
| Address | `0x00598650` |
| Ghidra name | `VehicleAction_applyAction` |
| Body | `00598650` – `005994c4` |
| Calling convention | `__thiscall` (`this` = VehicleAction in ECX; `param_2` = `{dt, …}`) |
| Role | Per-substep vehicle driver: steer ramp → wheel torque → air-stab → boost tail |
| Assert strings | `"VehicleAction::havok code"` `@ 0x9d5534`; `"VehicleAction::applyAction"` `@ 0x9d5550` (xref `@ 0x598773`) |
| Status | **Verified** (re-decompile + raw `read_memory`) |

Related (decompiled this pass):

| Address | Name | Role |
|--------:|------|------|
| `0x004fb660` | `Vehicle_createVehicleAction` | Builds entity `+0x1a0` handle + VA + framework + driverInput |
| `0x00597f90` | `VehicleAction_ctor` | Inits VA `+0x20..+0x44` |
| `0x004fbc10` | `VehicleEntity_PushDriveAxesToController` | Entity axes → **driverInput** (not VA floats) |
| `0x00598040` | `VehicleAction_calcWheelTorque` | Per-wheel drive torque; handbrake rear cut |
| `0x00636410` | `hkpVehicleSteering_setSteeringAngle` | Trivial float store (called with stage-2 steer) |
| `0x0064f840` | `hkDefaultSteering_update` | Normalized steer → wheel angle (separate stage) |

---

## Tools used (this verification)

1. **`decompile_function`** `0x598650` (`VehicleAction_applyAction`)
2. **`decompile_function`** `0x4fbc10`, `0x598040`, `0x4fb660`, `0x597f90` (ctor entry), `0x636410`
3. **`read_memory`** on all DAT floats listed in §3
4. **`get_xrefs_to`** `0x9d54e0` (sole reader: `VehicleAction_ctor`)

Did **not** use `disassemble_bytes`. Emulation skipped (pointer-heavy entity/framework graph).

---

## 1. Three distinct “drive” objects (do not conflate)

`Vehicle_createVehicleAction` allocates a **12-byte handle** at `entity+0x1a0`:

```
entity+0x1a0 → { +0x00 VehicleAction*, +0x04 hkVehicleFramework*, +0x08 driverInput* }
```

| Object | How reached | `+0x20` | `+0x24` |
|--------|-------------|--------|--------|
| **driverInput** (`hkDefaultAnalogDriverInput`) | `*(entity+0x1a0)+8` | **throttle float** (`entity+0x614`) | **handbrake byte** (`entity+0x61c`) |
| **VehicleAction** (this of applyAction) | `**(entity+0x1a0)` | **steer stage-1 ramp rate** (ctor constant) | **steer stage-1 command float** |
| **Entity** (`CVOGVehicle`) | VA`+0x44` | n/a | n/a — axes at `+0x614/618/61c` |

`PushDriveAxesToController` writes **only** the driverInput object. It does **not** write VehicleAction `+0x20/+0x24`.

---

## 2. VehicleAction layout (`this` of applyAction) — `+0x20..+0x34`

| Off | Type | Meaning (binary) | Evidence |
|----:|------|------------------|----------|
| `+0x1c` | u8 | in-collision flag (airStabilization) | other fn; ctor zeros nearby |
| **`+0x20`** | **f32** | **Steer stage-1 ramp rate** (not live throttle) | ctor: `this[8] = DAT_009d54e0`; applyAction: `step = *(this+0x20) * dt * rateFactor` |
| **`+0x24`** | **f32** | **Steer stage-1 command** — ramps toward `entity+0x618` | applyAction; mirrored to `wheelsDesc+0x1c` |
| **`+0x28`** | **f32** | **Steer stage-2 command** — speed-scaled + ±0.05 tick ramp → `setSteeringAngle` | applyAction mode `0x02` |
| **`+0x2c`** | **u8** | **All-wheels-airborne** (`1` if no wheel in contact) | written by `calcWheelTorque`; read by applyAction boost/ground paths |
| **`+0x30`** | **f32** | **Boost timer** | applyAction tail |
| **`+0x34`** | **f32** | **Boost cooldown timer** | applyAction tail |
| `+0x3c` | ptr | Steering / mode-0x02 helper (`+0x5c` = steer-enable byte) | applyAction |
| `+0x40` | ptr | Wheel container / framework side (`+0xc` = wheels desc) | applyAction, calcWheelTorque |
| `+0x44` | ptr | **Entity back-ref** (`CVOGVehicle`) | every access |

### ctor init (`VehicleAction_ctor` @ `0x00597f90`)

```
*(u8*)(this + 0x1c) = 0;          // param_1[7] as byte
*(u8*)(this + 0x2c) = 0;          // param_1[0xb] as byte
*(f32*)(this + 0x20) = DAT_009d54e0;  // ≈ 2.857143  (only xref to this DAT)
*(f32*)(this + 0x24) = 0;
*(f32*)(this + 0x28) = 0;
*(f32*)(this + 0x30) = DAT_009c7bc0;  // large sentinel (~1e7)
*(f32*)(this + 0x34) = 0;
// +0x40 = framework, +0x44 = entity
```

`get_xrefs_to(0x9d54e0)` → **only** `VehicleAction_ctor` READ. applyAction only **reads** `+0x20`; no other writer found on create/PushDrive paths. Treat `+0x20` as a **fixed ramp-rate field**, not a copy of `entity+0x614`.

---

## 3. Entity drive axes (`entity = *(VA+0x44)`)

| Off | Type | Meaning | Producer | Consumers |
|----:|------|---------|----------|-----------|
| **`+0x614`** | f32 | Longitudinal / throttle axis (AI: forward ≈ **−1**, reverse ≈ **+1**) | `MoveToTarget3DPoint` `0x4fc650`; net `Vehicle_setDrivingInputs` `0x504c70` | `PushDrive` → `driverInput+0x20` |
| **`+0x618`** | f32 | Steer axis target `[-1,+1]` | same AI / net | **applyAction** stage-1 ramp target |
| **`+0x61c`** | u8 | Sharp / handbrake assist (0/1) | AI: `speed>15 && \|lateral\|>0.70` | `PushDrive` → `driverInput+0x24` (byte); **`calcWheelTorque`** rear drive ×0.5 |

Related entity gates used by applyAction / PushDrive:

| Off | Meaning |
|----:|---------|
| `+0x101` | disabled — PushDrive no-op if set |
| `+0x102` | mode-0x02: skip steer set, clear steer-enable |
| `+0x103` | forced-stop / dead — applies boost / skip paths |
| `+0x109` | hard-stop request → PushDrive zeros throttle + handbrake=1 |
| `+0x14` | last-collision tick (idle watchdog vs `0x77a1`) |
| `+0x1a0` | action handle (null → no PushDrive / no VA) |

Movement-mode byte (mode `0x02` = analog steer ramp path):

```
entity+0x258 → … → vehicleData+0x4ce == 0x02
```

(exact chain in decompile: `*(…(entity+600)…+0x3c)+0x4ce`).

---

## 4. Constants (`read_memory`, length 4, LE float32)

| Address | Hex | float32 | Role |
|---------|-----|--------:|------|
| `0x00a10e78` | `cd cc 4c 3d` | **0.05** | Stage-2 steer ramp step per substep (`±` toward target) |
| `0x00af3388` | `00 00 a0 41` | **20.0** | Mode-0x02 **speed-factor divisor**: `min(\|v\|/20, 1)` |
| `0x00af3384` | `9a 99 19 3f` | **0.6** | **Neighbor only** — **not** used by this formula |
| `0x00aaa668` | `00 00 80 bf` | **−1.0** | Steer clamp min |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** | `g_flOne` — clamp max / identity |
| `0x009d54e0` | `6e db 36 40` | **≈2.857143** (`20/7`) | VA `+0x20` ctor ramp rate |
| `0x009c7bc0` | `7f 96 18 4b` | **≈1e7** | VA `+0x30` ctor init |
| `0x00a0f298` | `00 00 00 3f` | **0.5** | Handbrake rear traction cut in `calcWheelTorque` |
| `0x00af3380` | `33 33 33 3f` | **0.7** | Non-0x02 upright gate (quat branch) |
| `0x00a0f734` | `66 66 66 3f` | **0.9** | PushDrive optional throttle clamp |
| `0x00aaa7a4` | `00 00 70 41` | **15.0** | AI sharp speed gate / calcWheelTorque low-speed μ |
| `0x00a0f710` | `33 33 33 3f` | **0.70** | AI sharp `\|lateral\|` threshold |

Decompile also uses `g_flLevelUpUiBase_Inferred` as a **2.0-class** rate factor in the stage-1 branch (same magnitude as `DAT_00a10e74` used for rear ×2.0 in `calcWheelTorque`). Treat stage-1 `rateFactor ∈ {1.0, 2.0}` per the reconstructed condition below.

---

## 5. Steer pipeline inside applyAction (authoritative)

### 5.1 Stage 1 — `entity+0x618` → `VA+0x24` → `wheelsDesc+0x1c`

```
delta = entity[+0x618] - VA[+0x24]
if delta != 0:
    // rateFactor: 2.0 when the OR-condition in decompile holds, else 1.0
    //   (VA+0x24 < 0 && target > -1) || (VA+0x24 > 0 && target < +1)
    //   when VA+0x24 == 0 → rateFactor = 1.0
    step = VA[+0x20] * dt * rateFactor      // VA+0x20 ≈ 2.857143 from ctor
    step = min(|delta|, step)               // decompile: if |delta| < step → step = |delta|
    if delta > 0:  VA[+0x24] = min(VA[+0x24] + step, +1.0)
    if delta < 0:  VA[+0x24] = max(VA[+0x24] - step, -1.0)
    wheelsDesc[+0x1c] = VA[+0x24]           // *( *(VA+0x40)+0x14 ) + 0x1c
```

**This is a steering ramp, not throttle and not brake.**

### 5.2 Stage 2 — mode `0x02` only: speed scale + ±0.05 tick ramp → steering object

Requires `vehicleData+0x4ce == 0x02` and `entity+0x102 == 0` (else clears `*(VA+0x3c)+0x5c = 0` and skips):

```
speed       = |chassisLinearVel|            // body+0x40..+0x48
speedFactor = min(speed / DAT_00af3388, 1)  // DAT_00af3388 = 20.0  (**not** 0.6)
targetSteer = wheelsDesc[+0x1c] * speedFactor

if targetSteer != VA[+0x28]:
    VA[+0x28] += (targetSteer > VA[+0x28]) ? +0.05 : -0.05   // DAT_00a10e78
    if |targetSteer - VA[+0x28]| < 0.05:
        VA[+0x28] = targetSteer
VA[+0x28] = clamp(VA[+0x28], -1.0, +1.0)
hkpVehicleSteering_setSteeringAngle(…, VA[+0x28])
*(VA+0x3c)+0x5c = 1   // steer enable
```

### 5.3 Mode ≠ `0x02`

Large quaternion / basis block builds a corrective angular impulse (upright / velocity-coupled). **Does not** use the `+0x24/+0x28` wheel-angle ramp. Upright gate uses `DAT_00af3380 = 0.7`.

### 5.4 After steer

```
VehicleAction_calcWheelTorque()
VehicleAction_airStabilization()
// ground helpers when not all-airborne; boost impulse tail when entity+0x103 / disabled flags
```

---

## 6. Handbrake / brake — where they actually live

| Claim | Verdict |
|-------|---------|
| `VA+0x24` is brake / handbrake | **FALSE** — float steer stage-1 |
| `VA+0x28` is brake | **FALSE** — float final steer |
| Service brake torque in applyAction / calcWheelTorque | **None** — drive torque clamp floor is **0** |
| Handbrake effect | `entity+0x61c != 0` → rear wheels only: `torque *= 0.5` (`DAT_00a0f298`) in **`calcWheelTorque`** |
| PushDrive handbrake byte | `driverInput+0x24 = entity+0x61c` (distinct object from VA) |

Deceleration without handbrake is **throttle release / reverse** + friction solver (see `brake-spec.md`), not a VA float.

---

## 7. Myths vs binary (corrections)

| Myth (old plate / `NPCDriving.md` §6.1) | Binary |
|-----------------------------------------|--------|
| `VA+0x24` = “current brake” | **`VA+0x24` = steer stage-1** (ramps `entity+0x618`) |
| `VA+0x20` = live throttle from PushDrive | **`VA+0x20` = ctor rate ≈2.857**; live throttle is **`driverInput+0x20`** ← `entity+0x614` |
| Mode-0x02 `speedFactor = min(\|v\|/0.6, 1)` with `DAT_00af3388=0.6` | **`DAT_00af3388 = 20.0`**; neighbor `0xaf3384 = 0.6` is unused here |
| “Throttle ramp toward entity+0x618” | **Steer** ramps toward `entity+0x618`; throttle is a separate axis |
| Handbrake on VA floats | Handbrake is **`entity+0x61c`** (u8) + rear torque cut |

---

## 8. Per-tick order (applyAction body, condensed)

1. Idle / disabled / body-type early outs (incl. no-drive watchdog `> 0x77a1` ms).
2. Profiler enter; **`VehicleAction_tickSubsystems`** on framework (`VA+0x40` call site) — Havok component updates.
3. Suspension anti-sink: scan wheels stride `0xc0`, field `+0xb0`; if min compression `< 0`, raise chassis Y by `−min`.
4. **Steer stage-1** (§5.1).
5. **Steer stage-2** if mode `0x02` (§5.2), else quat impulse branch (§5.3).
6. **`calcWheelTorque`** (uses `entity+0x61c` for rear cut; sets `VA+0x2c` airborne).
7. **`airStabilization`**.
8. Ground / anim hooks when not airborne.
9. Boost timer / impulse tail when stop/disabled flags set (`VA+0x30/+0x34`).

---

## 9. Port notes / non-goals

- Ports that stream retail-shaped axes must keep **entity `+0x614/618/61c`** semantics; the client’s applyAction **only reads `+0x618`** for steering among those three.
- Do **not** map a brake pedal onto `VA+0x24` or `VA+0x28`.
- Speed-factor divisor is **20.0**, not 0.6 (0.6 would saturate steering by ~0.6 m/s).
- Stage-2 `±0.05` is **per applyAction invocation** (substep), not per wall-clock second.
- Downstream wheel angle uses **`hkDefaultSteering_update`** quadratic falloff (`fn_0064f840_steering.md`) on the normalized command — separate from this ramp.
- No C# in this file (RE evidence only).

---

## 10. Cross-refs

- Struct map: `docs/reconstruction/physics/0.8-struct-offsets.md`
- Steering stages: `docs/reconstruction/physics/steering-spec.md`, `verified/fn_0064f840_steering.md`
- Brake myth: `docs/reconstruction/physics/brake-spec.md`
- AI axis production: `docs/reconstruction/physics/drive-controller-spec.md`
- Overview (partially outdated on 0.6 / VA+0x24): `docs/NPCDriving.md` §6.1
