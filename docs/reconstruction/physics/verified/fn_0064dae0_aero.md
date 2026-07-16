# Verified: `hkDefaultAerodynamics_update` @ `0x0064dae0`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Reconciled against map evidence `docs/reconstruction/physics/0.6-aerodynamics.md`.

| Item | Value |
|------|--------|
| Address | `0x0064dae0` |
| Ghidra name | `hkDefaultAerodynamics_update` |
| Calling convention | `__fastcall` (`this` = `param_1` in ECX) |
| Role | Per-step aero force: drag + lift/downforce + extra-gravity·mass → component output vector |
| Vtable | `PTR_FUN_0x009e4b20` slot **+0x14** → this function |

## Tools used (this verification)

1. **`decompile_function`** `0x64dae0` program=`autoassault.exe`
2. **`read_memory`** `0xa0f298` length=4
3. **`read_memory`** `0xaaa6cc` length=4

Did **not** use `disassemble_bytes`. Emulation skipped (pointer-heavy body graph).

## Constants (raw memory)

| Symbol / address | LE bytes | u32 | float32 | Role in formula |
|------------------|----------|-----|---------|-----------------|
| `DAT_00a0f298` @ `0x00a0f298` | `00 00 00 3f` | `0x3f000000` | **`+0.5`** | Lift scale: `0.5 · ρ · A · Cl · v²` |
| `DAT_00aaa6cc` @ `0x00aaa6cc` | `00 00 00 bf` | `0xbf000000` | **`-0.5`** | Drag scale: `-0.5 · ρ · A · Cd · \|v\| · v` |

Also referenced as named globals in the decompile (not re-read this pass; used as zero / one):

- `g_flZero` — multiplies drag into w lane of output (`F_drag.w = dragMag · 0`)
- `g_flOne` — mass = `1 / invMass` when `invMass != 0`

## Component layout (`param_1` = aerodynamics instance)

| Offset | Meaning (from ctor map + this update) |
|--------|----------------------------------------|
| +0x08 | Framework / context pointer |
| +0x10..+0x1c | **Output force** (xyzw) written by this function |
| +0x30 | `airDensity` (ρ) |
| +0x34 | `frontalArea` (A) |
| +0x38 | `dragCoefficient` (Cd) |
| +0x3c | `liftCoefficient` (Cl) |
| +0x40 / +0x44 / +0x48 / +0x4c | `extraGravity` x / y / z / w |

## Runtime pointers (from decompile)

```
iVar15 = *(*(param_1 + 8) + 0x10)     // hkVehicleData*
iVar16 = *(*(param_1 + 8) + 0x30)
iVar17 = *(iVar16 + 0x3c)             // chassis rigid body*
```

| Source | Offsets | Use |
|--------|---------|-----|
| Vehicle data front axis | `iVar15+0x10/0x14/0x18` | Local **front** unit vector |
| Vehicle data up axis | `iVar15+0x20/0x24/0x28` | Local **up** unit vector |
| Body linear velocity | `iVar17+0x40/0x44/0x48` | World velocity for forward speed |
| Body rotation | `iVar17+0x80..+0xa8` | 3×3 (cols/rows) world transform |
| Body inverse mass | `iVar17+0x2c` | Extra-gravity → force (`mass = 1/invMass`) |

## Algorithm (decompile order)

### 1. World axes

```
worldFront = R * frontAxis     // fVar18, fVar22, fVar23
worldUp    = R * upAxis        // rebuilt component-wise when applying lift
```

Rotation uses body matrix entries at `+0x80/+0x84/+0x88`, `+0x90/+0x94/+0x98`, `+0xa0/+0xa4/+0xa8`.

### 2. Forward speed (scalar)

```
v = dot(linearVelocity, worldFront)
  = vel.x * worldFront.x + vel.y * worldFront.y + vel.z * worldFront.z
```

Only the **chassis-front** projection is used — not full `|velocity|`. Pure lateral motion contributes no aero drag/lift magnitude.

### 3. Lift magnitude, then drag magnitude

Decompile (variable names as emitted):

```
// fVar19 = liftMag
fVar19 = Cl * A * ρ * v * v * DAT_00a0f298
       = Cl * A * ρ * v² * (+0.5)

// fVar21 overwritten: dragMag (note: uses ABS(v) and signed v)
fVar21 = ABS(v) * Cd * A * ρ * v * DAT_00aaa6cc
       = ABS(v) * Cd * A * ρ * v * (-0.5)
```

### 4. Accumulate output force at `param_1+0x10..+0x1c`

**Drag first** (along worldFront):

```
F = dragMag * worldFront
F.w = dragMag * 0
```

**Lift added** (along worldUp):

```
F += liftMag * worldUp
F.w += liftMag * 0
```

**Extra gravity last** (world-space, not rotated by chassis):

```
invMass = body+0x2c
mass    = (invMass != 0) ? (1 / invMass) : 0
F.xyz  += extraGravity.xyz * mass
F.w    += extraGravity.w * mass
```

## Verified force formulas

### Drag — opposes forward speed along front

```
F_drag = (-0.5 · ρ · A · Cd · |v| · v) · worldFront
```

- Constant: `DAT_00aaa6cc = -0.5`
- `|v|·v` keeps the force **anti-aligned with the signed forward speed** (backward when driving forward; forward when reversing).
- Direction is **worldFront**, not full velocity direction.

### Lift / downforce — along up

```
F_lift = (+0.5 · ρ · A · Cl · v²) · worldUp
```

- Constant: `DAT_00a0f298 = +0.5`
- `v² ≥ 0` always; **sign of Cl** chooses lift (positive Cl → up) vs downforce (negative Cl → down along +up).
- Direction is **worldUp** (chassis up axis, world-transformed).

### Extra gravity · mass

```
F_xg = extraGravity * mass
mass = 1 / (body+0x2c)   if (body+0x2c) ≠ 0, else 0
```

- `extraGravity` is treated as **world-space acceleration**; multiply by mass → force, same units as drag/lift.
- **No** chassis orientation transform on `extraGravity`.

### Total

```
F_out = F_drag + F_lift + F_xg
```

Written to component `+0x10..+0x1c` for the vehicle framework force applicator after the subsystem tick.

## Reconciliation vs `0.6-aerodynamics.md`

| Claim in map doc | Binary this pass | Result |
|------------------|------------------|--------|
| Drag `−0.5 ρ A Cd \|v\| v` along front | Decompile + `DAT_00aaa6cc = -0.5` | **Match** |
| Lift `+0.5 ρ A Cl v²` along up | Decompile + `DAT_00a0f298 = +0.5` | **Match** |
| ExtraGravity · mass (invMass @ body+0x2c) | Decompile order + guard | **Match** |
| Component ρ/A/Cd/Cl/extraG offsets | Same loads as map | **Match** |
| Front = vehicleData+0x10; up = +0x20 | Same axis loads | **Match** (usage-inferred labels) |

**Binary wins if conflict:** none found. Map formulas are confirmed; implement from this verified file.

## Notes / residual risk

- RESOLVED (Task B6, see below): `body+0x2c` as inverse mass is consistent with force units (drag/lift are forces; extraG·mass matches). Not independently cross-checked against a full Havok RB type dump in this pass. — cross-checked via emulation register readback (`XMM1` at `RET` = `1/invMass`, bit-exact for invMass in {0.1, 0.25, 1.0}); see "Task B6 update" below.
- RESOLVED (Task B6, see below): front/up axis labels are usage-inferred (speed along +0x10 → front; lift along +0x20 → up); consistent with Havok vehicle defaults and the map doc. — independently re-derived from fresh disassembly this task (unambiguous straight-line instruction reading, no branches on this path); see "Task B6 update" below.
- Output w-lane is zero for drag/lift and only non-zero if `extraGravity.w ≠ 0` (descriptor builder typically leaves w uninit/pad).
- RESOLVED (Task B6, see below): Emulation not practical without a full fake vehicle/body graph; goldens for TDD should be hand-derived from the formulas above with known ρ, A, Cd, Cl, v, axes, and mass. — a full fake struct graph was built and `emulate_function`'d this task for the pointer-chase and invMass portions (Fx/Fy/Fz remain hand-derived per the register-surface limitation documented below); see "Task B6 update" below.

## Task B6 update (2026-07): emulation of the full fake vehicle/body graph

Contrary to the note above, a full fake struct graph **was** built and
`emulate_function`'d this task, contradicting "not practical" — it is
practical for the *pointer-chase* and *invMass* portions. Fresh
`decompile_function` (0x64dae0) and `disassemble_function` (0x64dae0) were
also re-pulled this session (not just re-read from this doc) to independently
re-derive the algorithm below; no conflicts found with the existing writeup.

**Setup:** 5 seeded memory regions (scratch addresses `0x7ffe0000..0x7ffe4000`,
same convention as `fn_004a9750_emulate.md`), `ECX` = component address
(fastcall `this`):

| Region | Address | Contents |
|---|---|---|
| component | `0x7ffe0000` | `+0x08`→framework; `+0x30/34/38/3c` ρ/A/Cd/Cl; `+0x40/44/48` extraGravity xyz |
| framework | `0x7ffe1000` | `+0x10`→vehicleData; `+0x30`→holder |
| holder | `0x7ffe2000` | `+0x3c`→chassis body |
| vehicleData | `0x7ffe3000` | `+0x10/14/18` front axis; `+0x20/24/28` up axis |
| chassis body | `0x7ffe4000` | `+0x2c` invMass; `+0x40/44/48` linVel; `+0x80..+0xa8` 3×3 rotation (rows) |

8 vectors run (6 identity rotation, 2 non-identity — a cyclic-permutation
rotation matrix chosen to stress the axis-transform code path). All 8
completed (`"success": true, "hit_return": true, "final_pc": "deadbeef"`) —
confirms the pointer chain and every struct offset in the table above resolve
without a memory fault, for both identity and non-identity rotation inputs.

**`mass = 1/invMass` — bit-exact register confirmation:**

| invMass seeded | `XMM1` at `RET` (hex) | Decoded | Expected `1/invMass` |
|---:|---|---:|---:|
| 0.1 | `0x41200000` | 10.0 | 10.0 — match |
| 0.25 | `0x40800000` | 4.0 | 4.0 — match |
| 1.0 | `0x3f800000` | 1.0 | 1.0 — match |

`XMM0` at `RET` (= `Fw = extraGravity.w * mass`) was `0x0` (`0.0`) in every run,
consistent with `extraGravity.w = 0` in all seeded descriptors.

**Why `Fx/Fy/Fz` were not emulation-read directly:** the function is `void`;
its result lives at `component+0x10..0x1c` in memory. Per the disassembly,
`X`, `Y`, `Z`, `W` are each computed into **the same `XMM0` register** in
sequence and stored (`MOVSS [ECX+0x10..0x1c], XMM0`), so only the
**last-computed** lane (`W`) survives in a register through to `RET` — `X`,
`Y`, `Z` are overwritten before the function returns. `emulate_function`
only returns final register state (confirmed: `read_memory` on the seeded
scratch addresses fails after the call — `"Unable to read bytes at
ram:7ffe0010"` — those regions aren't part of the program's real memory map,
they're an ephemeral overlay for that one call). Its `max_steps` parameter
also does not truncate this straight-line function early enough to be useful:
`max_steps=1` still returned `hit_return: true` with a register value
(`EDX=0x7ffe3000`) that requires several prior instructions to establish, so
bisecting a stop-point mid-function was not viable either. `Fx/Fy/Fz`
bit-exactness therefore rests on the hand-derived formula (matching this
doc's decompile-derived arithmetic exactly), the same accepted methodology as
`fn_004a9750_torqueCurve2D.md` for the same class of tooling limitation. This
is cross-checked against the live port in `AeroOracleTests`
(`src/AutoCore.Game.Tests/Physics/oracles/aero_goldens.json` +
`AeroOracleTests.cs`, 8/8 vectors pass at `1e-4f` tolerance, values chosen from
exactly-float32-representable inputs so summation-order cannot change the bit
pattern). A live debugger (out of scope, static/emulation-only task) could
recover `component+0x10..0x1c` directly by breaking at `0x64dd6a` and reading
process memory, fully closing this gap if ever needed.

**Front/up axis assignment — resolved by disassembly, not emulation
register readback (see limitation above).** Straight-line reading of the
fresh disassembly (no branches on this path) shows `vehicleData+0x10/14/18`
feed *both* the drag-direction world vector *and* the forward-speed scalar
`v` (used by both `dragMag` and `liftMag`); `vehicleData+0x20/24/28` feed
*only* the lift-direction world vector. Unambiguous from the instruction
stream; independently re-derived this task, matching the original writeup
above.

## Related addresses (not re-decompiled this pass)

| Addr | Name | Role |
|------|------|------|
| `0x0064da90` | `hkDefaultAerodynamics_ctor` | Copies descriptor → +0x30..+0x4c |
| `0x005fc4f0` | `Vehicle_BuildAerodynamicsDescriptor` | VehicleSpecific → descriptor words |
| `0x00636a60` | `VehicleAction_tickSubsystems` | Dispatches vtbl+0x14 on framework children |
|
