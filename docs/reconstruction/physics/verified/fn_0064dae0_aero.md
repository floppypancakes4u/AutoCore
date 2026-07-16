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

- `body+0x2c` as inverse mass is consistent with force units (drag/lift are forces; extraG·mass matches). Not independently cross-checked against a full Havok RB type dump in this pass.
- front/up axis labels are usage-inferred (speed along +0x10 → front; lift along +0x20 → up); consistent with Havok vehicle defaults and the map doc.
- Output w-lane is zero for drag/lift and only non-zero if `extraGravity.w ≠ 0` (descriptor builder typically leaves w uninit/pad).
- Emulation not practical without a full fake vehicle/body graph; goldens for TDD should be hand-derived from the formulas above with known ρ, A, Cd, Cl, v, axes, and mass.

## Related addresses (not re-decompiled this pass)

| Addr | Name | Role |
|------|------|------|
| `0x0064da90` | `hkDefaultAerodynamics_ctor` | Copies descriptor → +0x30..+0x4c |
| `0x005fc4f0` | `Vehicle_BuildAerodynamicsDescriptor` | VehicleSpecific → descriptor words |
| `0x00636a60` | `VehicleAction_tickSubsystems` | Dispatches vtbl+0x14 on framework children |
|
