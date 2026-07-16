# Verified: entity drive axes `+0x614` / `+0x618` / `+0x61c`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Object | `CVOGVehicle` entity (`this` of MoveToTarget / PushDrive / setDrivingInputs) |
| Offsets | `+0x614` (f32 thr), `+0x618` (f32 steer), `+0x61c` (u8 sharp/handbrake) |
| Primary writer | `CVOGVehicle::MoveToTarget3DPoint` @ `0x004fc650` |
| Bridge | `VehicleEntity_PushDriveAxesToController` @ `0x004fbc10` |
| Primary physics consumer | `VehicleAction::applyAction` @ `0x00598650` (+ callees) |
| RE tools | decompile of `0x4fc650`, `0x4fbc10`; cross-check `0.8-struct-offsets`, `steering-spec`, `fn_00598040_calcWheelTorque`, `drive-controller-spec` |
| Status | **Verified** (writers + consumer map) |
| Scope | RE evidence only — **no C#** |

---

## 0. One-line map

| Entity off | Type | Meaning | Written by MoveToTarget? | How applyAction family uses it |
|-----------:|------|---------|:------------------------:|--------------------------------|
| `+0x614` | f32 | longitudinal / throttle (`Accel = −1`, `Reverse = +1`) | **yes** (always on drive path) | **Indirect:** PushDrive → input-ctrl `+0x20` (throttle). applyAction uses VA throttle; does **not** re-read entity `+0x614` for steer. |
| `+0x618` | f32 | raw steer axis `[-1,+1]` | **yes** (gated; deadband uses SetSteerInput) | **Direct:** applyAction stage-1 ramp target: `delta = ent[0x618] − VA[0x24]` |
| `+0x61c` | u8 | sharp / handbrake-assist (0/1) | **yes** (drive path + arrival path) | **Indirect in applyAction body;** direct in **`calcWheelTorque`** (called from applyAction): rear drive torque ×0.5. Also PushDrive → ctrl `+0x24`. |

**Critical correction (do not reintroduce):** `VehicleAction+0x24` is **steer ramp stage 1**, **not** a brake float. Handbrake lives on the **input controller** byte at `[entity+0x1a0]+8 + 0x24` and on **entity `+0x61c`**. See `brake-spec.md` / `0.8-struct-offsets.md`.

---

## 1. Pipeline (AI local drive)

```
aim point at entity+0x190..+0x198
        │
        ▼
CVOGVehicle::MoveToTarget3DPoint   0x004fc650
  writes  entity+0x614  (throttle f32)
  writes  entity+0x618  (steer f32, gated)
  writes  entity+0x61c  (sharp u8)
  clears  entity+0x101, +0x109  (drive path)
        │
        ▼
VehicleEntity_PushDriveAxesToController   0x004fbc10
  requires entity+0x101==0 && entity+0x1a0!=0
  ctrl = *(entity+0x1a0)+8
  ctrl+0x20 ← entity+0x614     (throttle; optional clamp 0.9)
  ctrl+0x24 ← entity+0x61c     (handbrake byte)
  // entity+0x618 is NOT copied here
        │
        ▼  (each Havok sim step)
VehicleAction::applyAction   0x00598650
  entity = *(VA+0x44)
  stage-1: ramp VA+0x24 toward entity+0x618  →  wheelsDesc+0x1c
  mode 0x02: ramp VA+0x28 toward speed-scaled target → setSteeringAngle
  VehicleAction_calcWheelTorque  0x00598040
      reads entity+0x61c → rear torque × 0.5
  VehicleAction_airStabilization  0x00598320
      may SetDriveAxes(0) → clear 0x614/618/61c + push
```

---

## 2. Writer: `MoveToTarget3DPoint` @ `0x004fc650`

**Signature (thiscall):**

```
undefined4 MoveToTarget3DPoint(
    entity this,
    float  acceptDist,     // param_2 — planar arrival radius
    float  cruiseScale,    // param_3
    void*  aim_UNUSED,     // param_4 — aim is pre-written at this+0x190
    char   allowReverse)   // param_5
```

**Gates to enter drive body:** `*(this+8) != 0` and `*(u8*)(this+0x101) == 0`.

### 2.1 Arrival / brake path (writes `+0x61c`, clears thr, **does not touch `+0x618`**)

Condition (decompile inverted gate):

```
// drive when: acceptDist < |distXZ|  OR  entity+0x103 != 0
// else arrival:
entity+0x61c = 1
VehicleEntity_SetLongitudinalInput(0)   // FUN_004f5650 — throttle axis → 0
PushDriveAxesToController()
return 0                                // +0x618 left unchanged
```

### 2.2 Drive path — three axes

Authoritative reconstruction (constants re-verified in `drive-controller-spec.md`):

```
// basis: right R (FUN_004e8ad0), forward F (FUN_004e8a40)
// dir = normalize(aim - pos); lateral = R·dir; fAlign = F·dir
// speed = |linVel|; fwdSpeed = V·F
// base = (allowReverse && fAlign < -0.4) ? +1.0 : -1.0
//   → forward cruise produces NEGATIVE throttle at +0x614

// --- +0x618 steer ---
if |lateral| >= 0.01:                          // DAT_00a0f718
    steer = clamp(base * lateral * 2.0, -1, +1)  // gain DAT_00a10e74
    // write ONLY if steer-lock object null or (obj+0xb4 & 0xC7)==0
    entity+0x618 = steer
else:                                          // deadband
    if fAlign >= 0:  SetSteerInput(0)          // 0x4f5620 → writes +0x618
    else:            SetSteerInput(sign from lateral)  // hard ±1 spin

// --- +0x614 throttle ---
thr = base
if speed > 5.0:                                // DAT_00aaa688
    if 0 < distXZ < 30: thr *= distXZ * (1/30) // near-target ease
    if cruiseScale > 0.1:
        thr *= (fwdSpeed < 0 ? -cruiseScale : cruiseScale)
entity+0x101 = 0
entity+0x109 = 0
entity+0x614 = thr

// --- +0x61c sharp ---
entity+0x61c = (speed > 15.0 && |lateral| > 0.70) ? 1 : 0
// DAT_00aaa7a4 / DAT_00a0f710

PushDriveAxesToController()
return 1
```

Decompile sites (tmp `decompile_4fc650.txt`):

| Write | Decompile line (approx) |
|-------|-------------------------|
| `*(float*)(param_1 + 0x618) = fVar7` | proportional steer (gated) |
| `*(float*)(param_1 + 0x614) = fVar8` | throttle after ease/cruise |
| `*(u8*)(param_1 + 0x61c) = 0/1` | sharp test vs speed/lateral |
| `*(u8*)(param_1 + 0x61c) = 1` | arrival path |

### 2.3 Sign convention (verified)

- Normal forward: **`base = −1`** → cruise throttle at `+0x614` is **negative**.
- Reverse (allow + aim behind): **`base = +1`** → positive throttle.
- Ports that “normalize” forward to +1 will invert retail physics response.

---

## 3. Bridge: `PushDriveAxesToController` @ `0x004fbc10`

Does **not** invent axes; copies entity → input controller.

| Condition | Effect |
|-----------|--------|
| `entity+0x101 != 0` or `entity+0x1a0 == 0` | **no-op** (null VehicleAction class of `nullWheels`) |
| `entity+0x109 != 0` (hard stop) | `ctrl+0x20 = 0`, `ctrl+0x24 = 1`, return |
| else | `ctrl+0x20 = entity+0x614` (if `ctrl+0x19`: clamp ≤ `DAT_00a0f734` = 0.9) |
| | speed-cap gate may zero `ctrl+0x20` when over max and throttle opposes forward |
| | `ctrl+0x24 = entity+0x61c` |
| | `ctrl+0x25 = 0` |

**`entity+0x618` is never written here** — steer stays on the entity for applyAction.

Input controller is **`[entity+0x1a0]+8`**, a struct that shares throttle layout with VehicleAction but is treated as **distinct** for `+0x24` (byte handbrake vs float steer ramp on the Havok action). See `0.8-struct-offsets.md` §2.

---

## 4. Consumers in / under `applyAction` @ `0x00598650`

`entity = *(VehicleAction + 0x44)`.

### 4.1 `entity+0x618` — **direct consumer (steer ramp)**

Mode-independent stage 1 (from `steering-spec.md`, reconciled with decompile plate):

```
delta = entity[+0x618] - VA[+0x24]
step  = VA[+0x20] * dt * sign_toward_target   // rate uses VA throttle slot * dt
// (also documented with DAT_00a10e74 = 2.0 as ramp-rate family — do not conflate with steer gain 2.0 at MoveToTarget)
step  = min(|delta|, |step|)
VA[+0x24] += ±step
VA[+0x24] = clamp(VA[+0x24], -1, +1)
wheelsDesc[+0x1c] = VA[+0x24]                 // *( *(VA+0x40)+0x14 ) + 0x1c
```

Then **only if** movement-mode byte `entity…+0x4ce == 0x02` **and** `entity+0x102 == 0`:

```
speedFactor = min(|chassisLinVel| / 20.0, 1.0)   // DAT_00af3388 = 20.0  (NOT 0.6 neighbor)
targetSteer = wheelsDesc[+0x1c] * speedFactor
// VA+0x28 ramps toward targetSteer by ±0.05 per tick (DAT_00a10e78)
// clamp [-1,+1]; hkpVehicleSteering_setSteeringAngle(steeringObj, VA+0x28)
```

Else (mode ≠ `0x02`): different velocity-coupled angular-impulse law — still does **not** re-read `+0x614/+0x61c` for that block.

Downstream physical angles: `hkDefaultSteering_update` @ `0x64f840` (quadratic inverse-speed falloff) — consumes the **normalized command**, not entity axes.

### 4.2 `entity+0x614` — **not read as entity field inside applyAction**

- Throttle reaches the controller via **PushDrive** (`ctrl+0x20`).
- applyAction’s throttle-related state is **`VA+0x20`** (and `param_2` input block for dt/throttle family).
- Service deceleration is **not** a brake torque from `+0x614`; zero/reverse throttle → friction solver longitudinal term (see `brake-spec.md`).

### 4.3 `entity+0x61c` — **consumer is `calcWheelTorque` (called from applyAction)**

Call order (tail of applyAction):

```
… steer ramp / mode branch …
VehicleAction_calcWheelTorque();     // 0x598040
VehicleAction_airStabilization();    // 0x598320
```

In `calcWheelTorque` (verified `fn_00598040_calcWheelTorque.md`):

```
// per wheel i in contact, after μ · upright · curve:
if (entity[+0x61c] != 0) && isRear(i):     // isRear: i > *(u8*)(vehData+0x4cc)
    torque *= 0.5                          // DAT_00a0f298
// clamp [0, 1000] → wheelsDesc+0x28[i]
```

This is a **rear drive-torque cut** (handbrake/burnout assist), **not** service-brake torque and **not** `VehicleAction+0x24`.

### 4.4 applyAction-adjacent writer that **clears** all three

`VehicleAction_airStabilization` recovery path calls:

```
VehicleEntity_SetDriveAxes(0)   // 0x4fbec0
// clears entity+0x614 / +0x618 / +0x61c then PushDriveAxesToController
```

Evidence: `fn_00598320_airStab.md` §3.2 step 5.

---

## 5. Other writers (not MoveToTarget, same slots)

| Function | Addr | What it writes |
|----------|------|----------------|
| `Vehicle_setDrivingInputs` | `0x00504c70` | Network ghost entry: same three axes from wire, then PushDrive + pose apply `FUN_0053eec0` |
| `VehicleEntity_SetDriveAxes` | `0x004fbec0` | Clear/set all three then push (air-stab recovery; general axis set) |
| `VehicleEntity_SetSteerInput` | `0x004f5620` | Steer only (`+0x618`) — deadband path inside MoveToTarget |
| `VehicleEntity_SetLongitudinalInput` | `0x004f5650` | Throttle only (`+0x614`) — arrival path |
| Local player DriveControlTick / PollBoundActions | (callers of PushDrive) | Same entity slots from player input |

All of these feed the **same** applyAction / calcWheelTorque consumers.

---

## 6. Struct reference (entity + action + controller)

### Entity `CVOGVehicle`

| Off | Type | Role |
|----:|------|------|
| `+0x101` | u8 | disabled / no-input (blocks MoveToTarget + PushDrive) |
| `+0x102` | u8 | mode-0x02: suppress `setSteeringAngle` when set |
| `+0x103` | u8 | forced-drive override (skip arrival stop) |
| `+0x109` | u8 | hard-stop → PushDrive thr=0, handbrake=1 |
| `+0x190..+0x198` | f32×3 | AI aim point (MoveToTarget input) |
| `+0x1a0` | ptr | input-controller holder; ctrl = `*+8` |
| `+0x614` | f32 | throttle |
| `+0x618` | f32 | steer |
| `+0x61c` | u8 | sharp / handbrake |

### Havok `VehicleAction` (`this` of applyAction)

| Off | Type | Role |
|----:|------|------|
| `+0x20` | f32 | throttle (current / ramp-rate partner) |
| `+0x24` | f32 | **steer stage-1** (ramps toward `entity+0x618`) — **NOT brake** |
| `+0x28` | f32 | steer final (mode 0x02) → setSteeringAngle |
| `+0x40` | ptr | wheel container → wheelsDesc |
| `+0x44` | ptr | entity back-ref |

### Input controller `[entity+0x1a0]+8`

| Off | Type | Role |
|----:|------|------|
| `+0x19` | u8 | if set, clamp thr ≤ 0.9 |
| `+0x20` | f32 | copy of `entity+0x614` |
| `+0x24` | u8 | copy of `entity+0x61c` |
| `+0x25` | u8 | cleared each push |

---

## 7. Constants used by writers / consumers

| Address | Value | Used by |
|---------|------:|---------|
| `0x00a0f718` | 0.01 | MoveToTarget steer deadband |
| `0x00a10e74` | 2.0 | MoveToTarget steer gain; also rear driver-mod ×2 / thr ramp family |
| `0x00aaa668` | −1.0 | clamp min / forward base |
| `0x00a0f2a0` | 1.0 | clamp max / reverse base |
| `0x009cd238` | −0.4 (double) | reverse gate on fAlign |
| `0x00aaa688` | 5.0 | thr scale only above this speed |
| `0x00a0f694` | 30.0 | near-target thr ease distance |
| `0x00aaab14` | ~0.0333 | `1/30` near-target ease |
| `0x00aaa7a4` | 15.0 | sharp speed gate (+ low-speed μ boost elsewhere) |
| `0x00a0f710` | 0.70 | sharp \|lateral\| threshold |
| `0x00a0f734` | 0.9 | PushDrive thr clamp |
| `0x00a10e78` | 0.05 | applyAction mode-0x02 `VA+0x28` ramp step |
| `0x00af3388` | **20.0** | mode-0x02 speedFactor divisor (`min(speed/20,1)`) |
| `0x00af3384` | 0.6 | **neighbor only** — not the speedFactor DAT |
| `0x00a0f298` | 0.5 | calcWheelTorque rear cut when `+0x61c≠0` |

---

## 8. Confirmed / not confirmed

### Confirmed

- [x] MoveToTarget **writes** `+0x614`, `+0x618` (gated), `+0x61c` then always PushDrive on both drive and arrival paths.
- [x] Arrival path sets `+0x61c=1`, zeroes longitudinal input, **leaves `+0x618` alone**.
- [x] PushDrive copies **`+0x614` → ctrl+0x20** and **`+0x61c` → ctrl+0x24**; **does not copy `+0x618`**.
- [x] applyAction **reads `entity+0x618`** as stage-1 steer target into `VA+0x24` / wheelsDesc `+0x1c`.
- [x] applyAction mode `0x02` further ramps `VA+0x28` with speedFactor **`speed/20`**.
- [x] `entity+0x61c` is applied in **`calcWheelTorque`** (applyAction callee) as rear ×0.5.
- [x] `VehicleAction+0x24` is **steer**, not handbrake.

### Not in this file / open

- Full bit-exact decompile of entire `applyAction` body (only steer-axis consumer + call order needed here).
- Whether input-controller object at `[+0x1a0]+8` is ever the same allocation as the Havok VehicleAction instance (layout of `+0x24` argues **no** for type safety).
- Live debugger confirmation of ghost pack → `setDrivingInputs` float packing for sharp nibble (wire format is separate from entity layout).

---

## 9. Related evidence

| Doc | Relevance |
|-----|-----------|
| [`../drive-controller-spec.md`](../drive-controller-spec.md) | MoveToTarget formulas + goldens |
| [`../steering-spec.md`](../steering-spec.md) | applyAction ramp + hkDefaultSteering |
| [`../brake-spec.md`](../brake-spec.md) | `+0x61c` vs false VA+0x24 brake myth |
| [`../0.8-struct-offsets.md`](../0.8-struct-offsets.md) | full entity/VA/ctrl layout |
| [`fn_00598040_calcWheelTorque.md`](fn_00598040_calcWheelTorque.md) | rear cut consumer |
| [`fn_00598320_airStab.md`](fn_00598320_airStab.md) | SetDriveAxes clear on recovery |
| [`fn_0064f840_steering.md`](fn_0064f840_steering.md) | physical wheel angle after ramp |
| tmp `decompile_4fc650.txt`, `decompile_4fbc10.json` | raw decompile dumps |

---

## 10. RE checklist

| Step | Result |
|------|--------|
| Decompile `0x4fc650` MoveToTarget | Writes `+0x614/+0x618/+0x61c` confirmed |
| Decompile `0x4fbc10` PushDrive | `+0x614→ctrl+0x20`, `+0x61c→ctrl+0x24`; no `+0x618` |
| Cross-read applyAction steer path | `entity+0x618` → `VA+0x24` → mode-0x02 `VA+0x28` |
| Cross-read calcWheelTorque | `entity+0x61c` rear ×0.5 |
| Cross-read airStab recovery | SetDriveAxes clears all three |
| `DAT_00af3388` | 20.0 (not 0.6) |
| No C# in this file | satisfied |
