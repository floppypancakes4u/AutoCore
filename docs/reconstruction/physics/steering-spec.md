# Vehicle Steering Subsystem — Bit-Exact Port Spec

Source: `autoassault.exe`, project **AA-decode**, image base `0x400000`. Read-only RE.

The steering pipeline has three stages:
1. **Input ramp** — `VehicleAction::applyAction` (0x598650) ramps the raw steer axis into a
   normalized command and stores it via `hkpVehicleSteering_setSteeringAngle`.
2. **Storage** — `hkpVehicleSteering_setSteeringAngle` (0x636410) is a trivial setter.
3. **Angle math** — `hkDefaultSteering_update` (0x64f840) converts the normalized command into
   a per-wheel physical wheel angle, applying the inverse-speed falloff.

The descriptor that seeds the steering object is built by
`Vehicle_BuildSteeringDescriptor` (0x5fc710).

---

## Function Addresses

| Function | Addr | Role |
|---|---|---|
| `VehicleAction::applyAction` | `0x598650` | Per-tick driver; input ramp (mode 0x02) |
| `hkpVehicleSteering_setSteeringAngle` | `0x636410` | `*(this+0x50) = angle` — trivial setter |
| `hkDefaultSteering_update` | `0x64f840` | Normalized steer -> per-wheel wheel angle |
| `hkDefaultSteering_ctor` | `0x64fac0` | Constructor |
| `Vehicle_BuildSteeringDescriptor` | `0x5fc710` | Fills SteeringMaxAngle / FullSpeedLimit / per-wheel steer flags |
| `TankSteering_ctor` | `0x64fc80` | Alternate (tank) steering variant |

---

## Stage 1 — Input ramp (applyAction, mode 0x02)

`param_1` = VehicleAction (this); `param_2[0]` = dt; `param_2[1]` = throttle input.
`entity = *(this+0x44)`; `entity+0x618` = raw steer axis; movement-mode byte = `entity..+0x4ce`.

Steer command ramp toward the wheel-desired value, into `VA+0x24`
(mirrored to `[*(VA+0x40)+0x14]+0x1c` = wheelsDesc desired steer):

```
delta = entity[0x618] - VA[0x24]
step  = VA[0x20] * dt * sign_factor         // sign_factor = +1 / -1 depending on cross-zero
step  = min(|delta|, step)
VA[0x24] += ±step   ; clamp to [-1, +1]      // +1 = g_flOne, -1 = DAT_00aaa668
wheelsDesc[0x1c] = VA[0x24]
```

Then, **only when `entity..+0x4ce == 0x02`** and `entity+0x102 == 0` (else the branch clears
`*(VA+0x3c)+0x5c = 0` and skips):

```
speed       = |chassisLinearVel| = sqrt(vx²+vy²+vz²)   // vel @ (*(*(VA+0x44)+8)+0x3c)+0x40..0x48
speedFactor = min(speed / DAT_00af3388, 1.0)           // DAT_00af3388 = 20.0  (see note)
targetSteer = wheelsDesc[0x1c] * speedFactor           // desired * speedFactor
```

`VA+0x28` (the value ultimately pushed to the steering object) ramps toward `targetSteer`:

```
if targetSteer != VA[0x28]:
    VA[0x28] += (targetSteer > VA[0x28]) ? +DAT_00a10e78 : -DAT_00a10e78   // ±0.05 per tick
    if |targetSteer - VA[0x28]| < DAT_00a10e78: VA[0x28] = targetSteer      // snap
VA[0x28] = clamp(VA[0x28], -1.0, +1.0)
hkpVehicleSteering_setSteeringAngle(steeringObj, VA[0x28])   // stores into steeringObj+0x50
```

The **else** branch (mode != 0x02) does NOT use this ramp; it computes a velocity-coupled
desired angular velocity from the chassis orientation matrix and applies an impulse (the large
quaternion/`acos` block) — a different control law, not wheel-angle steering.

### NOTE on `DAT_00af3388`
The prior KNOWN doc listed this as **0.6**. Raw memory read at `0x00af3388` is
`00 00 A0 41` = **20.0f**. The mode-0x02 divisor is `_DAT_00af3388` (leading-underscore
overlapping symbol at the same address); its 4-byte float value is **20.0**. Port MUST use
`speedFactor = min(speed / 20.0, 1.0)`. (0.6 would saturate steering at ~0.6 m/s, which is
inconsistent with the read value — treat 20.0 as authoritative unless a live debugger check
proves otherwise.)

---

## Stage 3 — Physical wheel angle (hkDefaultSteering_update, 0x64f840)

`param_1` = hkDefaultSteering; `param_1+8` = parent framework.
`steer = param_1[0x24]` (normalized [-1,1], the value fed from applyAction),
`fullSpeedLimit = param_1[0x28]`, `doesWheelSteer[] = param_1[0x2c]`,
`wheelCount = param_1[0x30]`, `outAngle[] = param_1[0x14]`, `computedAngle = param_1[0x10]`.
`maxAngle = *(*(parent+0x14)+0x14)` (SteeringMaxAngle, radians).

```
angle = maxAngle * steer
forwardSpeed = dot(chassisLinearVel_local, chassisForwardAxis)   // rows @ chassisTM+0x40..0x48
if fullSpeedLimit <= forwardSpeed:
    r     = fullSpeedLimit / forwardSpeed        // 0 < r < 1
    angle = r * r * angle                        // QUADRATIC falloff, not linear
computedAngle = angle
for w in 0..wheelCount:
    outAngle[w] = doesWheelSteer[w] ? computedAngle : 0.0
```

**Steering-angle formula (authoritative):**

```
wheelAngle = SteeringMaxAngle * steer * ( speed <= FullSpeedLimit ? 1
                                                                   : (FullSpeedLimit/speed)² )
```

This is Havok's `hkVehicleDefaultSteering`: the inverse-speed reduction is **squared**
(`(fullSpeedLimit/speed)²`), and is applied only above `FullSpeedLimit`. The task's guessed
`clamp(fullSpeedLimit/max(speed,fullSpeedLimit))` is correct in shape but is the SQUARE of that
ratio.

---

## Which wheels steer (Vehicle_BuildSteeringDescriptor, 0x5fc710)

The descriptor (`param_3`) is populated as:

| Descriptor field | Value | Meaning |
|---|---|---|
| `param_3+0x04` | `VehicleSpecific[0x594] * entity[0x208]` | **SteeringMaxAngle** (base × multiplier) |
| `param_3+0x08` | `VehicleSpecific[0x598] * entity[0x20c]` | **SteeringFullSpeedLimit** (base × multiplier) |
| `param_3+0x0c` | wheel steer-flag array (bool per wheel) | per-wheel doesSteer |
| `param_3+0x10` | wheel count | |

Per-wheel steer flag is set per **axle**, keyed off `VehicleSpecific+0x4cc` (= count of front
wheels / steer split index) and the flag byte `VehicleSpecific+0x5f0`:

```
for each wheel index i:
    if i < VehicleSpecific[0x4cc]:                 // FRONT axle
        doesSteer[i] = (VehicleSpecific[0x5f0] >> 2) & 1     // bit 2 = front steers
    else:                                          // REAR axle
        doesSteer[i] = (VehicleSpecific[0x5f0] >> 3) & 1     // bit 3 = rear steers
```

So AA supports front-steer, rear-steer, and four-wheel-steer via bits 2/3 of the vehicle
flags byte at `VehicleSpecific+0x5f0`. Standard cars: front only (bit2 set, bit3 clear).

---

## Field mapping summary

| Concept | Source (game struct) | Runtime steering-object offset |
|---|---|---|
| SteeringMaxAngle (rad) | `VehicleSpecific+0x594 × entity+0x208` | via `*(parent+0x14)+0x14` |
| SteeringFullSpeedLimit | `VehicleSpecific+0x598 × entity+0x20c` | `hkDefaultSteering+0x28` |
| Normalized steer input | `VA+0x28` (post-ramp) → setter → obj+0x50 → `hkDefaultSteering+0x24` | |
| Per-wheel steer flags | `VehicleSpecific+0x5f0` bits 2 (front) / 3 (rear), split at `+0x4cc` | `hkDefaultSteering+0x2c[]` |
| Raw steer axis (input) | `entity+0x618` | |

---

## DAT constants

| Symbol | Addr | Value | Use |
|---|---|---|---|
| `DAT_00a10e78` | `0x00a10e78` | **0.05** (`cd cc 4c 3d`) | steer ramp step per tick (`VA+0x28`) |
| `_DAT_00af3388` | `0x00af3388` | **20.0** (`00 00 a0 41`) | mode-0x02 speed-normalization divisor (speedFactor). KNOWN doc said 0.6 — CORRECTED to 20.0 |
| `DAT_00a10e74` | `0x00a10e74` | 2.0 | throttle ramp rate (not steering) |
| `DAT_00a0f2a0` / `g_flOne` | — | 1.0 | steer clamp max |
| `DAT_00aaa668` | `0x00aaa668` | -1.0 | steer clamp min |

Falloff is **quadratic** above `FullSpeedLimit`; below it, full authority.

---

## Task B7 update (2026-07): emulation goldens + a found port deviation

`hkDefaultSteering_update` (0x64f840) was fully re-verified by emulation this
task (not just decompile reading) — see `verified/fn_0064f840_steering.md`
§"Task B7 update" for the full setup and register-readback table. Summary:

- 8 golden vectors are **bit-exact register readbacks** (`XMM0` at `RET`) of
  the real formula, covering both sides of the `FullSpeedLimit` boundary, the
  boundary itself (inclusive `<=`, `r=1` identity), one ULP past the boundary,
  zero steer, negative steer, and negative (reversing) forward speed. All 8
  match the current C# port (`HkVehicleSteering.ComputeWheelAngles`)
  bit-exact — see `src/AutoCore.Game.Tests/Physics/oracles/steering_goldens.json`
  + `SteeringOracleTests.cs`.
- A 9th, degenerate vector (`FullSpeedLimit = 0`, `forwardSpeed = 0`) found a
  **real deviation**: the retail binary's gate is inclusive (`0.0 <= 0.0` is
  true) so it takes the falloff branch and computes `0.0/0.0 = NaN`
  (`0x7fc00000`, confirmed by emulation register readback). The C# port has
  an added `forwardSpeed > 0f` guard (not in the retail binary) that skips
  the branch for this input and returns a non-NaN identity value instead.
  Not fixed here (out of scope for this task) — documented and covered by an
  `[Ignore("unblocked by C-phase steering fix")]` test so it doesn't silently
  regress or silently pass.
