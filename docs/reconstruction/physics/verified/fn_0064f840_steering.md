# Verified: `hkDefaultSteering_update` @ `0x64f840`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkDefaultSteering_update` |
| Address | `0x0064f840` |
| Role | Normalized steer input → per-wheel physical wheel angle (Havok default steering) |
| RE tools | `decompile_function` @ `0x64f840`; `read_memory` on DAT constants below |
| Status | **Verified** (re-read) |

Related (not this function): steer **input ramp** lives in `VehicleAction::applyAction` (`0x598650`, mode `0x02`). Constants in §2 feed that ramp / clamp path and are re-verified here because ports often conflate them with the angle math.

---

## 1. Decompile (authoritative)

```c
void __fastcall hkDefaultSteering_update(int param_1)
{
  int iVar1;
  float fVar2;
  float fVar3;
  undefined4 local_20;
  undefined4 local_1c;
  undefined4 local_18;

  iVar1 = *(int *)(param_1 + 8);
  // angle = SteeringMaxAngle * steerInput
  fVar2 = *(float *)(*(int *)(iVar1 + 0x14) + 0x14) * *(float *)(param_1 + 0x24);

  // load chassis forward axis into local_20/1c/18 (via helper)
  FUN_005d6ae0(*(int *)(*(int *)(iVar1 + 0x30) + 0x3c) + 0x80,
               *(int *)(iVar1 + 0x10) + 0x10);

  // forwardSpeed = dot(chassisLinearVel, forwardAxis)
  iVar1 = *(int *)(*(int *)(*(int *)(param_1 + 8) + 0x30) + 0x3c);
  fVar3 = *(float *)(iVar1 + 0x48) * local_18
        + *(float *)(iVar1 + 0x44) * local_1c
        + *(float *)(iVar1 + 0x40) * local_20;

  // QUADRATIC inverse-speed falloff when speed >= FullSpeedLimit
  if (*(float *)(param_1 + 0x28) <= fVar3) {
    fVar3 = *(float *)(param_1 + 0x28) / fVar3;   // r = fullSpeedLimit / forwardSpeed
    fVar2 = fVar3 * fVar3 * fVar2;               // angle *= r²
  }

  *(float *)(param_1 + 0x10) = fVar2;            // computedAngle

  // per-wheel: doesWheelSteer[i] ? computedAngle : 0
  iVar1 = 0;
  if (0 < *(int *)(param_1 + 0x30)) {
    do {
      if (*(char *)(iVar1 + *(int *)(param_1 + 0x2c)) == '\0') {
        *(undefined4 *)(*(int *)(param_1 + 0x14) + iVar1 * 4) = 0;
      }
      else {
        *(undefined4 *)(*(int *)(param_1 + 0x14) + iVar1 * 4) =
            *(undefined4 *)(param_1 + 0x10);
      }
      iVar1 = iVar1 + 1;
    } while (iVar1 < *(int *)(param_1 + 0x30));
  }
  return;
}
```

---

## 2. Object layout (from decompile)

| Offset | Type | Meaning |
|---|---|---|
| `this+0x08` | ptr | Parent vehicle framework |
| `this+0x10` | f32 | `computedAngle` (post-falloff) |
| `this+0x14` | ptr | `outAngle[]` (f32 per wheel) |
| `this+0x24` | f32 | Normalized steer input `[-1, +1]` |
| `this+0x28` | f32 | `fullSpeedLimit` (SteeringFullSpeedLimit) |
| `this+0x2c` | ptr | `doesWheelSteer[]` (bool/char per wheel) |
| `this+0x30` | i32 | Wheel count |

| Indirection | Meaning |
|---|---|
| `*( *(parent+0x14) + 0x14 )` | `SteeringMaxAngle` (radians) |
| Chassis rigid body via `*( *(parent+0x30) + 0x3c )` | Linear vel at `+0x40..+0x48`; transform helper uses `+0x80` / framework `+0x10+0x10` for forward axis |

---

## 3. Authoritative formula

```
angle = SteeringMaxAngle * steer

forwardSpeed = dot(chassisLinearVel, chassisForwardAxis)

if fullSpeedLimit <= forwardSpeed:
    r     = fullSpeedLimit / forwardSpeed          // 0 < r ≤ 1
    angle = r * r * angle                          // QUADRATIC, not linear

computedAngle = angle
for w in 0 .. wheelCount-1:
    outAngle[w] = doesWheelSteer[w] ? computedAngle : 0.0
```

Closed form:

```
wheelAngle =
    SteeringMaxAngle * steer
    * ( forwardSpeed <= FullSpeedLimit
          ? 1.0
          : (FullSpeedLimit / forwardSpeed)² )
```

### Confirmation: quadratic falloff

- Gate: `if (fullSpeedLimit <= forwardSpeed)` — falloff **only above** limit (inclusive on equality → `r=1`, identity).
- Scale: `fVar3 = full / speed` then `fVar2 = fVar3 * fVar3 * fVar2`.
- **Not** linear `angle *= full/speed`.
- **Not** a clamp-only form without squaring.

---

## 4. DAT constants (raw `read_memory`, LE float32)

| Address | Hex (4 B) | Float | Role |
|---|---|---|---|
| `0x00a10e78` | `cd cc 4c 3d` | **0.05** | Steer-command ramp step per tick (`VA+0x28` toward `targetSteer` in applyAction mode `0x02`) |
| `0x00af3388` | `00 00 a0 41` | **20.0** | Mode-`0x02` **speed-factor divisor**: `speedFactor = min(speed / 20.0, 1.0)` |
| `0x00af3384` | `9a 99 19 3f` | **0.6** | **Neighbor** float only — **not** the speed-factor divisor |
| `0x00aaa668` | `00 00 80 bf` | **-1.0** | Steer clamp min |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Steer clamp max (`g_flOne`) |

### Speed-factor divisor: 20.0 vs neighbor 0.6

- Memory at **`0xaf3388` is unambiguously 20.0f** (`00 00 a0 41`).
- Immediately preceding dword **`0xaf3384` is 0.6f** (`9a 99 19 3f`).
- Older notes / symbol confusion that listed the speed-factor DAT as **0.6** were reading the **wrong neighbor**. Port **must** use `speed / 20.0`, not `speed / 0.6` (which would saturate steering by ~0.6 m/s).
- These ramp/clamp DATs are **not** loaded inside `0x64f840`; they belong to the applyAction ramp that produces `this+0x24`. Documented here so the pair of stages stay consistent.

---

## 5. Port notes / non-goals

- This function does **not** ramp input; it only applies max-angle scale + quadratic speed falloff + per-wheel flags.
- Emulation skipped: pointer-heavy chassis / framework graph; goldens can be hand-derived from the closed form above.
- No C# in this file (RE evidence only).

---

## 6. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x64f840` | OK — quadratic `r*r` confirmed |
| `read_memory` `0xa10e78` | 0.05 |
| `read_memory` `0xaf3388` | **20.0** (speed divisor) |
| `read_memory` `0xaf3384` | **0.6** (neighbor only) |
| `read_memory` `0xaaa668` | -1.0 |
| `read_memory` `0xa0f2a0` | 1.0 |
| Conflict with prior docs | Prior “0.6 speed factor” is **wrong**; binary wins → **20.0** |
