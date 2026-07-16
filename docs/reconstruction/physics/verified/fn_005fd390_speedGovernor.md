# Verified: top-speed precompute → `vehicle+0x110` (tail of `0x5fd390`)

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Host function | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` – `0x005fda48` |
| Focus | **tail only** — after `hkVehicleFramework_ctor`, before descriptor teardown |
| Write site | `param_1[0x44] = fVar10` → **byte offset `vehicle+0x110`** |
| `param_1` | vehicle entity (`float*`; index ×4 = byte off) |
| Verified | Ghidra `decompile_function` @ `0x5fd390`; `read_memory` @ `0x9dd348`; `get_xrefs_to` @ `0x9dd348` |
| Scope | formula + constant + field map. **No C#.** |

Related (full framework construction, not this note):
[`fn_005fd390_buildFramework.md`](fn_005fd390_buildFramework.md).

---

## Placement in setup

Runs **once** at vehicle-physics build time, after all Havok components are constructed and
`hkVehicleFramework_ctor` (`0x64cd30`) returns. It does **not** run per tick.

Order in the host function:

```
… build components …
hkVehicleFramework_ctor(...)
// ← SPEED-GOVERNOR PRECOMPUTE (this note)
entity[+0x110] = fVar10
FUN_005fde60 / FUN_005fdb80 / FUN_005fdaf0 / …  // descriptor teardown
return framework*
```

---

## Decompiled tail (authoritative)

VehSpec access (same clonebase chain used throughout setup):

```
VehSpec = *(*(entity + *(entity[+4]+4) + 0xac) + 0x3c)
```

Core scalar (shared by every wheel contribution):

```c
// iVar2 = VehSpec*
fVar8 =
  (*(float*)(VehSpec + 0x6b4)
   / ( (*(float*)(VehSpec + 0x6c4)
        * *(float*)(VehSpec + 0x6d0 + ((char)(*(char*)(VehSpec + 0x699) - 1)) * 4) )
     )
  ) * _DAT_009dd348;
```

Per-wheel accumulation:

```c
fVar10 = 0.0f;
axle0  = *(byte*)(VehSpec_via_entity[+0x258]_chain + 0x4cc);  // front-axle wheel count
// decompiler packs axle0 in the high byte of iVar9; recovered as (iVar9 >> 24)
nWheels = FUN_004f5560();   // wheel count (entity path)

for (i = 0; i < nWheels; ++i) {
    if (i < axle0)
        split = *(float*)(VehSpec + 0x5e8);          // front torque split (total front share)
        nAxle  = axle0;
    else
        split = *(float*)(VehSpec + 0x5ec);          // rear torque split (total rear share)
        nAxle  = nWheels - axle0;

    share  = split / (float)nAxle;                   // per-wheel share on that axle
    radius = *(float*)(VehSpec + 0x600 + i*4);       // per-wheel radius
    fVar10 += share * radius * fVar8;
}

*(float*)(entity + 0x110) = fVar10;   // param_1[0x44]
```

Exact decompiler write: `param_1[0x44] = fVar10` with `param_1` typed `float*` →
byte offset `0x44 * 4 = 0x110`.

---

## Formula (compact)

Let:

| Symbol | VehSpec / source | Identity |
|---|---|---|
| `N` | `+0x699` (byte) | `tinNumberOfGears` / number of forward gears |
| `G_top` | `+0x6d0[(N-1)]` | top (highest) forward **gear ratio** |
| `P` | `+0x6c4` | **primary transmission ratio** (`rlTransmissionRatio`) |
| `RPM_max` | `+0x6b4` | **maximum engine RPM** (`rlMaximumRPMMax`) — layout-adjacent to transmission block |
| `k` | `DAT_009dd348` | **π/30** ≈ `0.104719758` (RPM → rad/s) |
| `T_f`, `T_r` | `+0x5e8`, `+0x5ec` | front / rear **wheel torque-split totals** |
| `n_f` | `+0x4cc` (byte) | axle-0 (front) wheel count |
| `n` | `FUN_004f5560()` | total wheel count |
| `R_i` | `+0x600[i]` | **wheel radius** `i` (confirmed `wheelsRadius` in wheels builder) |

Then:

```
ω_wheel_top = (RPM_max / (P * G_top)) * k
            = RPM_max * (π/30) / (P * G_top)

entity[+0x110] = Σ_i  share_i * R_i * ω_wheel_top

where share_i =
    T_f / n_f          if i < n_f
    T_r / (n - n_f)    otherwise
```

### Idealized closed form

If torque splits are normalized (`T_f + T_r = 1`, as `Vehicle_BuildTransmissionDescriptor`
warns when the fanned per-wheel shares sum above 1) and all radii equal `R`:

```
entity[+0x110] ≈ R * RPM_max * (π/30) / (P * G_top)
```

i.e. classical **linear top speed from max engine RPM in top gear**:

```
v_top = ω_engine_max / (primary * gear_top) * wheel_radius
      with ω_engine_max [rad/s] = RPM_max * π/30
```

With unequal radii, the result is the **torque-share-weighted** mean radius times that wheel ω.

---

## `DAT_009dd348` (constant)

| Item | Value |
|---|---|
| Address | `0x009dd348` |
| Xrefs | **sole** read: `0x005fd89d` in `Vehicle_buildHavokVehicleFramework` |
| `read_memory` LE bytes | `50 77 d6 3d` |
| IEEE-754 f32 | **`0.104719758`** (`u32 0x3DD67750`) |
| Identity | **`π/30`** = **`2π/60`** (RPM → rad/s) |
| `|value − π/30|` | ≈ `2.9e-9` (float rounding) |

Not degrees→radians (`π/180 ≈ 0.01745`). Not `1/30` or `1/9.55` as a free parameter — those are
the same conversion written differently (`1/(60/(2π))`).

**Role in formula:** convert `RPM_max` into engine angular rate before dividing by the total
gear reduction (`P * G_top`), yielding top-gear **wheel** angular rate (rad/s).

---

## Field map (tail operands only)

| Off | Type | Role in precompute | Cross-check |
|---|---|---|---|
| `+0x699` | `u8` | `N` = gear count; index `N-1` selects top gear | transmission builder `0x5fc840` → `numGears` |
| `+0x6d0[i]` | `f32[]` | gear ratios; **only last gear** used here | transmission builder → `gearsRatio[i]` |
| `+0x6c4` | `f32` | primary transmission ratio `P` | transmission builder → `primaryTransmissionRatio` |
| `+0x6b4` | `f32` | numerator `RPM_max` | sequential after engine RPM block; **not** re-read as radius here |
| `+0x5e8` / `+0x5ec` | `f32` | front / rear torque-split totals | same split fan-out as transmission `wheelsTorqueRatio` |
| `+0x4cc` | `u8` | front axle wheel count | steering/transmission/wheels builders |
| `+0x600[i]` | `f32[]` | wheel radius `R_i` | `fn_005fcce0_wheelsBuilder.md`: Havok `wheelsRadius` |
| `FUN_004f5560` | — | total wheel count loop bound | entity wheel count helper |

### Explicit non-inputs

This precompute **does not read**:

- `VehSpec+0x630` `rlSpeedLimiter`
- `VehSpec+0x634` `rlAbsoluteTopSpeed` (entity-data speed-cap field used elsewhere)
- reverse gear (`+0x6c8`)
- clutch delay / upshift / downshift RPMs

So **`vehicle+0x110` is a kinematic gearing estimate**, not a copy of the DB absolute-top-speed
column. Docs that equate this write with “SpeedLimiter/AbsoluteTopSpeed baked in” overstate —
those columns remain separate investigation targets for **consumers** of `+0x110` / `+0x634`.

---

## Destination

| Location | How expressed | Meaning |
|---|---|---|
| Entity byte `+0x110` | `param_1[0x44]` (`float*`) | Stored top-speed / speed-governor constant |
| Adjacent | entity `+0x10c` = requested/target speed (other paths) | Different field; not written here |

Runtime **readers** of `entity+0x110` are out of scope for this gate (setup write only).
Treat as the setup-side constant those readers will use.

---

## Worked structure (4 wheels, 2 front)

```
axle0 = 2, n = 4
fVar8 = RPM_max / (P * GearRatios[N-1]) * (π/30)

entity[+0x110] =
    (T_f/2)*R0*fVar8 + (T_f/2)*R1*fVar8
  + (T_r/2)*R2*fVar8 + (T_r/2)*R3*fVar8
```

If `T_f=T_r=0.5` and `R_i=R`: result = `R * fVar8`.

---

## Conflicts / corrections vs older notes

| Claim | Source | This re-gate | Verdict |
|---|---|---|---|
| Write to `entity+0x110` / `param_1[0x44]` | `buildFramework`, setup map | yes | **match** |
| `fVar8 = (+0x6b4) / ((+0x6c4)*(+0x6d0[last])) * DAT_009dd348` | `buildFramework` | exact decompile | **match** |
| Loop uses `+0x600[i]` × front/rear split | `buildFramework` | yes; `+0x600` = **radius** | **match ops**; clarify identity |
| `+0x600` = gear ratios | older mass-inertia / setup blurbs | **false** — gears are `+0x6d0`; `+0x600` = radius | **binary + wheels builder win** |
| `DAT_009dd348 ≈ 0.1047` | `0.2-mass-inertia.md` | `0.104719758` = **π/30** | **match + exact ID** |
| Precompute **is** AbsoluteTopSpeed | README / setup “investigation anchor” wording | **does not load** `+0x630/+0x634` | **not a direct bake** |

---

## Porting notes (no C#)

1. Compute once when the vehicle instance / framework is built, not every sim tick.
2. Use **top forward gear only** (`NumberOfGears - 1`); ignore reverse.
3. Multiply by **π/30**, not a guessed “0.1” scale.
4. Fan torque split exactly like transmission setup: total front/rear ÷ axle wheel counts.
5. Weight by **per-wheel radius** (`WheelRadius[i]` / VehSpec `+0x600[i]`).
6. Store the scalar on the entity-equivalent of **`+0x110`**.
7. Keep `SpeedLimiter` / `AbsoluteTopSpeed` as separate fields until their read sites are gated;
   do not replace this formula with those DB columns.

---

## Evidence checklist

- [x] Decompile of `0x5fd390` tail (post-`hkVehicleFramework_ctor`)
- [x] `read_memory` of `DAT_009dd348` → LE `50 77 d6 3d` → f32 `0.104719758`
- [x] Sole xref of `0x9dd348` is the speed precompute mul at `0x5fd89d`
- [x] Cross-check `+0x6c4/+0x6d0/+0x699/+0x5e8/+0x5ec` via transmission builder `0x5fc840`
- [x] Cross-check `+0x600` = radius via wheels builder verified note
- [ ] Runtime consumer(s) of `entity+0x110` (follow-up)
- [ ] Whether `+0x634` AbsoluteTopSpeed clamps or replaces this value at drive time (follow-up)

---

## Related

- [`fn_005fd390_buildFramework.md`](fn_005fd390_buildFramework.md) — full setup order
- [`fn_005fcce0_wheelsBuilder.md`](fn_005fcce0_wheelsBuilder.md) — `+0x600` = `wheelsRadius`
- [`../setup-field-mapping.md`](../setup-field-mapping.md) — VehSpec → component map (speed section is summary only)
- [`../0.7-transmission.md`](../0.7-transmission.md) — gear / primary ratio DB names
- [`../0.8-struct-offsets.md`](../0.8-struct-offsets.md) — entity `+0x10c` target speed; data `+0x634` AbsoluteTopSpeed
