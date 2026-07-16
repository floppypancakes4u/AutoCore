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
- Emulation skipped: pointer-heavy chassis / framework graph; goldens can be hand-derived from the closed form above. — **SUPERSEDED (Task B7, see below):** emulation of the full struct graph turned out to be practical, and every golden vector is bit-exact register-readback, not hand-derived.
- C# port: `AutoCore.Game.Physics.Vehicle.HkVehicleSteering.ComputeWheelAngles` (see Task B7 update for a documented deviation).

## Task B7 update (2026-07): full-graph emulation, register readback, and a found deviation

Contrary to the note above, emulation of the full pointer-chase graph **is**
practical for this function, using the same "force a short-circuit branch,
then read the still-live register" technique as `fn_004a9750_emulate.md` and
the B6 aero update, combined with a new trick specific to this function: this
function is `void` and stores its result to `component+0x10` in memory, but
by seeding **`wheelCount = 0`** (`param_1+0x30`), the per-wheel loop's `JLE`
is taken immediately after `MOVSS [ESI+0x10],XMM0` and before anything else
touches `XMM0` — confirmed by fresh `disassemble_function` of `0x64f840`
(the `POP ESI; MOV ESP,EBP; POP EBP; RET 0x4` epilogue at `0x64f911` does not
reference `XMM0`). So **`XMM0` at `hit_return` is `computedAngle`, bit-exact**,
for every vector — no hand-derivation fallback was needed for the core
formula in this task (unlike aero's `Fx/Fy/Fz`).

**Callee resolved:** `FUN_005d6ae0` (`0x5d6ae0`) is a plain local-axis → world-axis
rotation helper: `__thiscall FUN_005d6ae0(float* out, float* rotationMatrix, float* localAxis)`,
computing `*out = R * localAxis` where `R` is a 3×4 column-major matrix (columns
at `+0x00/+0x10/+0x20`, stride 16 bytes — same convention as the aero chassis
rotation at `body+0x80..+0xa8`). Confirmed via fresh `decompile_function` +
`disassemble_function` of `0x5d6ae0` this task.

**Setup:** seeded scratch graph (fastcall `ECX` = component address `0x7ffe0000`):

| Region | Address | Contents |
|---|---|---|
| component | `0x7ffe0000` | `+0x08`→framework; `+0x24` steerInput; `+0x28` fullSpeedLimit; `+0x30` wheelCount=**0** |
| framework | `0x7ffe1000` | `+0x10`→axisHolder; `+0x14`→maxAngleHolder; `+0x30`→holder |
| maxAngleHolder | `0x7ffe1100` | `+0x14` maxAngle (0.6 constant across all vectors) |
| axisHolder | `0x7ffe1200` | `+0x10..+0x18` local steer axis = `(1,0,0)` |
| holder | `0x7ffe1300` | `+0x3c`→chassis body |
| chassis body | `0x7ffe1400` | `+0x40/44/48` linVel xyz; `+0x80..+0xa8` 3×3 rotation (identity) |

With an identity rotation and local axis `(1,0,0)`, `worldFront = (1,0,0)`, so
`forwardSpeed = dot(linVel, worldFront) = linVel.x` exactly — lets every
vector's `forwardSpeed` be seeded directly as `linVel.x`, same trick as the B6
aero setup.

**8 core vectors run**, all `"success": true, "hit_return": true, "final_pc": "deadbeef"`,
`XMM0` read back bit-exact and cross-checked against independent `numpy.float32`
hand arithmetic (all matched exactly except the degenerate case below):

| # | name | steer | maxAngle | FSL | fwdSpeed | `XMM0` @ RET | decoded |
|---|---|---:|---:|---:|---:|---|---:|
| 1 | below-fsl-identity | 0.5 | 0.6 | 15.0 | 10.0 | `0x3e99999a` | 0.3 |
| 2 | above-fsl-quadratic-falloff | 0.8 | 0.6 | 15.0 | 30.0 | `0x3df5c290` | 0.12 |
| 3 | exact-boundary (speed==FSL) | 0.7 | 0.6 | 15.0 | 15.0 | `0x3ed70a3e` | 0.42 |
| 4 | zero-steer-above-fsl | 0.0 | 0.6 | 15.0 | 30.0 | `0x0` | 0.0 |
| 5 | negative-steer-falloff-sign | -0.8 | 0.6 | 15.0 | 30.0 | `0xbdf5c290` | -0.12 |
| 6 | negative-forward-speed (reversing) | 0.5 | 0.6 | 15.0 | -10.0 | `0x3e99999a` | 0.3 |
| 7 | extreme-high-speed-tiny-ratio | 1.0 | 0.6 | 15.0 | 1000.0 | `0x390d8eca` | 0.000135 |
| 8 | just-past-boundary (FSL+1ulp) | 0.7 | 0.6 | 15.0 | 15.000000954 | `0x3ed70a3c` | 0.41999996 |

Vector 3 vs 8 are exactly one ULP apart (`0x3ed70a3e` vs `0x3ed70a3c`),
confirming the `<=` boundary is inclusive (r=1 identity at equality) and the
transition is continuous just past it.

### Found deviation: degenerate `fullSpeedLimit == 0 && forwardSpeed == 0`

A 9th vector (`steer=0.5, maxAngle=0.6, fullSpeedLimit=0.0, forwardSpeed=0.0`)
was run to probe the `0/0` edge implied by the branch gate. Retail binary
`XMM0` at `RET` = **`0x7fc00000`** (a quiet NaN) — the branch `fullSpeedLimit
<= forwardSpeed` (`0.0 <= 0.0`) **is taken** (inclusive), and `r = fsl/fwd =
0.0/0.0` is a hardware-default QNaN, propagating through `r*r*angle`.

Note: an independent Python/NumPy computation of the same `0.0/0.0` produced
a *different* QNaN payload (`0xffc00000`, sign bit set) — NaN payload bits
for `0/0` are implementation-defined and NumPy's software/library division
does not necessarily match x86 hardware `DIVSS`. The **emulator's value
(`0x7fc00000`) is authoritative** here since it runs the actual x86 P-code
semantics, not NumPy's.

The current C# port, `HkVehicleSteering.ComputeWheelAngles`, has an
intentional extra guard not present in the retail binary:

```csharp
if (fullSpeedLimit <= forwardSpeed && forwardSpeed > 0f)  // "> 0f" is NOT in the retail binary
```

For `forwardSpeed = 0f`, `forwardSpeed > 0f` is `false`, so the port **skips**
the branch and returns the plain identity value `maxAngle*steerInput = 0.3`
instead of retail's `NaN`. **This is a genuine, confirmed-by-emulation
deviation** for this degenerate input (an SteeringFullSpeedLimit of exactly
`0.0` is not expected to occur with real vehicle data, but the divergence is
real and worth tracking). Per task scope, **not fixed here** — see
`SteeringOracleTests.KnownDeviation_ZeroFullSpeedLimitZeroForwardSpeed_PortDiffersFromRetail`
(marked `[Ignore("unblocked by C-phase steering fix")]`) and
`steering_goldens.json`'s `knownDeviation` object.

### Goldens

`src/AutoCore.Game.Tests/Physics/oracles/steering_goldens.json` +
`SteeringOracleTests.cs` — 8/8 core vectors pass bit-exact
(`BitConverter.SingleToInt32Bits` equality, no tolerance) against
`HkVehicleSteering.ComputeWheelAngles`; the 9th (deviation) vector is
present in the fixture and covered by an `[Ignore]`d test that documents,
rather than hides, the mismatch.

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
| `decompile_function` + `disassemble_function` `0x64f840` (Task B7 re-pull) | OK — matches §1 exactly; `XMM0`/`MOVSS [ESI+0x10]` epilogue path confirmed for `wheelCount=0` trick |
| `decompile_function` + `disassemble_function` `0x5d6ae0` (callee, Task B7) | OK — plain `R*localAxis` rotation helper, column-major stride-16 matrix |
| `emulate_function` `0x64f840` × 9 (Task B7) | All `success:true, hit_return:true, final_pc:deadbeef`; 8 core vectors bit-exact via `XMM0` register readback, 1 degenerate vector surfaced a real port deviation (see Task B7 update above) |
