# Verified: `Vehicle_BuildAerodynamicsDescriptor` @ `0x5fc4f0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fc4f0` |
| Body | `0x005fc4f0` – `0x005fc5ac` |
| Symbol | `Vehicle_BuildAerodynamicsDescriptor` |
| Convention | MSVC `__thiscall` / stack args (Ghidra: `param_1` vehicle/entity, `param_2` unused in body, `param_3` out descriptor) |
| Callees | none (leaf; pure loads/stores) |
| Caller | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` → feeds `hkDefaultAerodynamics_ctor` @ `0x0064da90` |
| Verified | Ghidra `decompile_function` (re-gate) — **no `DAT_*` constants** in this function |

---

## Role

Build the 8-word (`0x20` bytes) **`hkDefaultAerodynamics` descriptor** by reading float fields from the
vehicle’s **VehicleSpecific / chassisData** blob. Values are copied **verbatim** (no scale, clamp,
or entity-prefix multiply — unlike steering/brake/AVD builders).

Downstream:

1. This builder fills `param_3[0..7]`.
2. `hkDefaultAerodynamics_ctor` (`0x64da90`) copies those 8 dwords into the component at
   `this+0x30..+0x4c` (`param_1[0xc..0x13]`).
3. Runtime force math lives in `hkDefaultAerodynamics_update` (`0x64dae0`) — see
   `0.6-aerodynamics.md` (not re-verified here).

---

## VehSpec pointer chain (from decompile)

Every field load uses the same base:

```
VehSpec = *(*( *( *(param_1 + 4) + 4 ) + 0xac + param_1 ) + 0x3c)
```

Same convention as other framework builders (`setup-field-mapping.md`): clone/entity walk +
`+0x3c` → VehicleSpecific (chassis data). Offsets below are **relative to `VehSpec`**.

---

## Density / area / Cd / Cl / extraG mapping

### Descriptor slot → VehSpec offset → identity

| desc index | byte off in desc | VehSpec offset | Semantic | DB / reflection string | String VA |
|---:|---:|---:|---|---|---|
| **0** | `+0x00` | **`+0x5a8`** | **air density (ρ)** | `rlAerodynamicsAirDensity` | `0x00a93244` |
| **1** | `+0x04` | **`+0x59c`** | **frontal area (A)** | `rlAerodynamicsFrontalArea` | `0x00a932c8` |
| **2** | `+0x08` | **`+0x5a0`** | **drag coeff (Cd)** | `rlAerodynamicsDrag` | `0x00a932a0` |
| **3** | `+0x0c` | **`+0x5a4`** | **lift coeff (Cl)** | `rlAerodynamicsLift` | `0x00a93278` |
| **4** | `+0x10` | **`+0x5ac`** | **extraGravity.X** | `rlAerodynamicsExtraGravityX` | `0x00a9320c` |
| **5** | `+0x14` | **`+0x5b0`** | **extraGravity.Y** | `rlAerodynamicsExtraGravityY` | `0x00a931d4` |
| **6** | `+0x18` | **`+0x5b4`** | **extraGravity.Z** | `rlAerodynamicsExtraGravityZ` | `0x00a9319c` |
| **7** | `+0x1c` | *(none)* | pad / **uninitialized** | — | — |

### Struct layout (sequential on VehSpec; **not** descriptor order)

```
VehSpec+0x59c  float  FrontalArea     ──► desc[1]
VehSpec+0x5a0  float  Drag (Cd)       ──► desc[2]
VehSpec+0x5a4  float  Lift (Cl)       ──► desc[3]
VehSpec+0x5a8  float  AirDensity (ρ)  ──► desc[0]   ← written first in code, but offset is after Cl
VehSpec+0x5ac  float  ExtraGravity.X  ──► desc[4]
VehSpec+0x5b0  float  ExtraGravity.Y  ──► desc[5]
VehSpec+0x5b4  float  ExtraGravity.Z  ──► desc[6]
```

**Important:** descriptor word order is **Havok’s** (ρ, A, Cd, Cl, extraG…), **not** the sequential
struct order (A, Cd, Cl, ρ, extraG…). Ports that bulk-copy 7 floats from `+0x59c` without
reordering will put **density and frontal area in the wrong slots**.

### Component layout after `hkDefaultAerodynamics_ctor` (cross-check)

| Component offset | dword index | Value |
|---|---|---|
| `+0x30` | `this[0xc]` | airDensity (ρ) ← desc[0] |
| `+0x34` | `this[0xd]` | frontalArea (A) ← desc[1] |
| `+0x38` | `this[0xe]` | dragCoefficient (Cd) ← desc[2] |
| `+0x3c` | `this[0xf]` | liftCoefficient (Cl) ← desc[3] |
| `+0x40` | `this[0x10]` | extraGravity.x ← desc[4] |
| `+0x44` | `this[0x11]` | extraGravity.y ← desc[5] |
| `+0x48` | `this[0x12]` | extraGravity.z ← desc[6] |
| `+0x4c` | `this[0x13]` | desc[7] (garbage/pad) |

Ctor decompile (verbatim copy of 8 dwords from descriptor `param_2` → `param_1[0xc..0x13]`):
confirmed at `0x64da90`.

---

## Exact algorithm (from decompile pseudocode)

```
// param_1 = vehicle / framework-owner entity
// param_3 = float out[8]  (descriptor)

VehSpec = *(*( *( *(param_1 + 4) + 4 ) + 0xac + param_1 ) + 0x3c)

param_3[0] = *(float*)(VehSpec + 0x5a8)   // ρ  airDensity
param_3[1] = *(float*)(VehSpec + 0x59c)   // A  frontalArea
param_3[2] = *(float*)(VehSpec + 0x5a0)   // Cd drag
param_3[3] = *(float*)(VehSpec + 0x5a4)   // Cl lift

// ExtraGravity vec3 bulk-read style (loads Y/Z then stores X,Y,Z)
param_3[4] = *(float*)(VehSpec + 0x5ac)   // extraG.x
param_3[5] = *(float*)(VehSpec + 0x5b0)   // extraG.y
param_3[6] = *(float*)(VehSpec + 0x5b4)   // extraG.z

param_3[7] = local_14                     // UNINITIALIZED stack float — pad

return VehSpec + 0x5ac                    // pointer to extraGravity.x (caller typically ignores)
```

### Critical details

1. **No transforms.** No `* entity[…]` multipliers, no `×0.5`, no clamps. Setup is a pure field map.
2. **desc[7] is uninitialized** (`local_14` never written). Harmless for update: drag/lift use only
   slots 0–3; extraGravity force uses xyz (slots 4–6). Do **not** invent a zero-fill unless matching
   retail stack garbage is irrelevant for tests — for C# ports, **zero pad** is the safe equivalent.
3. **Return value** is `VehSpec + 0x5ac` (address of extraGravity.x). Framework caller uses the
   out-buffer `param_3`, not this return, for the ctor.
4. **Identity of ρ vs A:** code order writes density first (`+0x5a8` → desc[0]) and area second
   (`+0x59c` → desc[1]). Reflection names confirm: density/area/drag/lift/extraG strings at
   `0x00a93244` … `0x00a9319c`. Matches `setup-field-mapping.md` and `0.6-aerodynamics.md`.

---

## Constants

**None.** This function has no `DAT_*` float/int immediates beyond address arithmetic offsets
(`0x5a8`, `0x59c`, `0x5a0`, `0x5a4`, `0x5ac`, `0x5b0`, `0x5b4`, pointer chain `+4` / `+0xac` / `+0x3c`).
`read_memory` for formula constants is N/A; offsets are the evidence.

---

## Conflicts vs Phase 0 evidence

| Item | `0.6-aerodynamics.md` / `setup-field-mapping.md` | This re-verify | Verdict |
|---|---|---|---|
| ρ = VehSpec+0x5a8 → desc[0] | yes | yes | **match** |
| A = +0x59c → desc[1] | yes | yes | **match** |
| Cd = +0x5a0 → desc[2] | yes | yes | **match** |
| Cl = +0x5a4 → desc[3] | yes | yes | **match** |
| extraG xyz = +0x5ac/0x5b0/0x5b4 → desc[4/5/6] | yes | yes | **match** |
| desc[7] uninitialized | noted | confirmed (`local_14`) | **match** |
| Verbatim copy (no scale) | yes | yes | **match** |
| ctor maps desc → component +0x30..+0x4c | yes | re-decompiled `0x64da90` | **match** |
| Reflection names `rlAerodynamics*` | yes | re-`search_strings` | **match** |

**No algorithm conflict.** Phase 0 aero builder map is bit-exact for this function.

---

## Port notes (builder only)

When building `HkDefaultAerodynamics` / aero params from server `VehicleSpecific`:

```
airDensity     = VehSpec.AerodynamicsAirDensity      // +0x5a8
frontalArea    = VehSpec.AerodynamicsFrontalArea     // +0x59c
dragCoeff      = VehSpec.AerodynamicsDrag            // +0x5a0
liftCoeff      = VehSpec.AerodynamicsLift            // +0x5a4
extraGravity   = (X,Y,Z) from +0x5ac..+0x5b4
// pad w = 0
```

Do **not** reorder fields as struct sequential memory if the C# type packs A,Cd,Cl,ρ sequentially —
map by **semantic name / documented offset**, not by “copy seven floats starting at +0x59c into
desc[0..]”.

Force formulas (drag/lift/extraG application) are **out of scope** for this note; see
`hkDefaultAerodynamics_update` @ `0x64dae0` / `0.6-aerodynamics.md`.

---

## Emulation

Not useful here: pure pointer-chased loads from live entity layout. Mapping is fully determined by
the decompile + string anchors. No golden float vectors for the builder itself (identity transform).
