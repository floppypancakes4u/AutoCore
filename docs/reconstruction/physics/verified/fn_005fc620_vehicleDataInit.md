# Verified: `FUN_005fc620` — definition → `hkVehicleData` descriptor init @ `0x5fc620`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fc620` |
| Body | `0x005fc620` – `0x005fc70a` |
| Symbol | `FUN_005fc620` (unnamed in binary; setup overlay for vehicle-data desc) |
| Convention | MSVC stack args (Ghidra: `param_1` entity, `param_2` unused in body, `param_3` out descriptor) |
| Callees | **none** (leaf; pure pointer-chased loads/stores) |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` (xref `0x005fd3d5`) |
| Verified | Ghidra `decompile_function` + `read_memory` on `DAT_00af4614` / `DAT_00af4618` / `DAT_00a110d8` + `get_function_callers` / `get_function_by_address` (re-gate) |

---

## Role

After `FUN_00650020` default-fills the framework / `hkVehicleData` descriptor (`+0x28..+0x58`), this function
**overlays** the simulation scalars from the entity’s **VehicleSpecific** blob.

It is **not** a component builder: no heap alloc, no ctor. Caller sequence in `0x5fd390`:

```
FUN_00650020()      // defaults (unit inertia = 1.0, +0x3c = 10.0, …)
FUN_005fc620()      // THIS — VehSpec overlay
FUN_0064ff90()      // desc container prep → FUN_00649e10
… component builders …
hkVehicleFramework_ctor / initFromDescriptor  // consumes desc+0x28..+0x4c
```

Downstream consumer of these slots: `initFromDescriptor` @ `0x64b2b0` (see
`fn_0064cd30_frameworkCtor.md` §5) copies them into framework fields used by the friction solver /
preUpdate path.

---

## VehSpec pointer chain (from decompile)

Every field load uses the same base:

```
VehSpec = *(*( *( *(param_1 + 4) + 4 ) + 0xac + param_1 ) + 0x3c)
```

Same convention as other framework builders (`setup-field-mapping.md`): clone/entity walk +
`+0x3c` → VehicleSpecific (chassis data). Offsets below are **relative to `VehSpec`**.

---

## Descriptor map (authoritative)

### Full store list (code order)

| desc byte | Source | Value / identity | DB / reflection |
|---:|---|---|---|
| **`+0x28`** | `VehSpec+0x5c4` | **RVFrictionEqualizer** | `rlRVFrictionEqualizer` @ `0x00a930dc` |
| **`+0x2c`** | `VehSpec+0x5c8` | **RVSpinTorqueRoll** | `rlRVSpinTorqueRoll` @ `0x00a930b4` |
| **`+0x30`** | `VehSpec+0x5cc` | **RVSpinTorquePitch** | `rlRVSpinTorquePitch` @ `0x00a9308c` |
| **`+0x34`** | `VehSpec+0x5d0` | **RVSpinTorqueYaw** | `rlRVSpinTorqueYaw` @ `0x00a93068` |
| **`+0x38`** | `VehSpec+0x5d8` | **RVExtraTorqueFactor** | `rlRVExtraTorqueFactor` @ `0x00a9300c` |
| **`+0x3c`** | **`DAT_00af4618`** | **10.0** (const) — `maxVelocityForPositionalFriction` slot | Havok string `0x009e5190` |
| **`+0x40`** | **`DAT_00af4614`** | **0.2** (const) — see §Constants / framework note | Havok `frictionEqualizer` string `0x009e5200` |
| **`+0x44`** | `VehSpec+0x5e4` | **`chassisUnitInertiaYaw`** = `RVInertiaYaw` | `rlRVInertiaYaw` @ `0x00a92fa8` |
| **`+0x48`** | `VehSpec+0x5dc` | **`chassisUnitInertiaRoll`** = `RVInertiaRoll` | `rlRVInertiaRoll` @ `0x00a92fec` |
| **`+0x4c`** | `VehSpec+0x5e0` | **`chassisUnitInertiaPitch`** = `RVInertiaPitch` | `rlRVInertiaPitch` @ `0x00a92fc8` |

VehSpec field order matches `VehicleSpecific.ReadNew` (server binary blob) immediately after AVD:

```
+0x5c0  AVDCollisionThreshold     (NOT read here; AVD path in 0x5fd390)
+0x5c4  RVFrictionEqualizer
+0x5c8  RVSpinTorqueRoll
+0x5cc  RVSpinTorquePitch
+0x5d0  RVSpinTorqueYaw
+0x5d4  RVExtraAngularImpulse     ← SKIPPED by this function
+0x5d8  RVExtraTorqueFactor
+0x5dc  RVInertiaRoll
+0x5e0  RVInertiaPitch
+0x5e4  RVInertiaYaw
```

### Skipped definition field

**`VehSpec+0x5d4` (`RVExtraAngularImpulse` / `rlRVExtraAngularImpulse` @ `0x00a93038`)** is
**not** written into the descriptor by `FUN_005fc620`. Gap between `+0x5d0` and `+0x5d8` is intentional
in this function (may be consumed elsewhere; not part of this overlay).

---

## Inertia slots — yaw / roll / pitch (primary re-verify target)

### Havok descriptor order = **Yaw, Roll, Pitch**

```
desc+0x44  chassisUnitInertiaYaw    ← VehSpec+0x5e4  (rlRVInertiaYaw)
desc+0x48  chassisUnitInertiaRoll   ← VehSpec+0x5dc  (rlRVInertiaRoll)
desc+0x4c  chassisUnitInertiaPitch  ← VehSpec+0x5e0  (rlRVInertiaPitch)
```

Havok reflection names confirm slot order (strings `0x009e5164` / `0x009e514c` / `0x009e5134`):
`chassisUnitInertiaYaw`, `chassisUnitInertiaRoll`, `chassisUnitInertiaPitch`.

### VehSpec / DB sequential order = **Roll, Pitch, Yaw**

```
VehSpec+0x5dc  RVInertiaRoll
VehSpec+0x5e0  RVInertiaPitch
VehSpec+0x5e4  RVInertiaYaw
```

### Reorder table (do not bulk-copy)

| Semantic | VehSpec | desc | Havok name |
|---|---:|---:|---|
| **Yaw** unit inertia | **`+0x5e4`** | **`+0x44`** | `m_chassisUnitInertiaYaw` |
| **Roll** unit inertia | **`+0x5dc`** | **`+0x48`** | `m_chassisUnitInertiaRoll` |
| **Pitch** unit inertia | **`+0x5e0`** | **`+0x4c`** | `m_chassisUnitInertiaPitch` |

**Critical:** a port that copies three floats starting at `VehSpec+0x5dc` into `desc+0x44..` as
`[Roll, Pitch, Yaw]` will place **Roll into the Yaw slot**. Map by **name / documented offset**, not
by sequential struct order.

### Unit-mass semantics

These three values are **per-unit-mass** chassis unit inertia (Havok raycast vehicle). With chassis
mass `m` (from the rigid-body asset, not this function):

```
I_yaw   = m * chassisUnitInertiaYaw    = m * RVInertiaYaw
I_roll  = m * chassisUnitInertiaRoll   = m * RVInertiaRoll
I_pitch = m * chassisUnitInertiaPitch  = m * RVInertiaPitch
```

`FUN_00650020` defaults all three to **1.0** before this overlay. This function replaces them with
DB values **verbatim** (no scale, clamp, or entity-prefix multiply).

Framework `initFromDescriptor` stores raw unit inertias at `fw+0x350 / +0x354 / +0x358` from
`desc+0x44 / +0x48 / +0x4c` (axis order as written: yaw→roll→pitch in those three slots).

---

## Exact algorithm (from decompile pseudocode)

```
// param_1 = vehicle / framework-owner entity
// param_2 = unused in body
// param_3 = hkVehicleData / framework descriptor (out)

VehSpec = *(*( *( *(param_1 + 4) + 4 ) + 0xac + param_1 ) + 0x3c)

*(param_3 + 0x28) = *(VehSpec + 0x5c4)   // RVFrictionEqualizer
*(param_3 + 0x2c) = *(VehSpec + 0x5c8)   // RVSpinTorqueRoll
*(param_3 + 0x30) = *(VehSpec + 0x5cc)   // RVSpinTorquePitch
*(param_3 + 0x34) = *(VehSpec + 0x5d0)   // RVSpinTorqueYaw
*(param_3 + 0x38) = *(VehSpec + 0x5d8)   // RVExtraTorqueFactor  (skip +0x5d4)

*(param_3 + 0x3c) = DAT_00af4618         // 10.0f
*(param_3 + 0x40) = DAT_00af4614         // 0.2f

*(param_3 + 0x44) = *(VehSpec + 0x5e4)   // unitInertiaYaw
*(param_3 + 0x48) = *(VehSpec + 0x5dc)   // unitInertiaRoll
*(param_3 + 0x4c) = *(VehSpec + 0x5e0)   // unitInertiaPitch

return;
```

Write order for the constants matches the decompiler’s temp shuffle:

1. load `DAT_00af4618` → store `desc+0x3c`
2. load `VehSpec+0x5d8` → store `desc+0x38`
3. load `DAT_00af4614` → store `desc+0x40`
4. then the three inertia stores

### Critical details

1. **No transforms.** No `* entity[…]` multipliers, no clamps, no default fallback if DB is 0.
2. **Two constants override defaults every time** — even if `FUN_00650020` already set `+0x3c = 10.0`,
   this function re-stores the same magnitude from a **different** symbol (`DAT_00af4618` vs
   `DAT_00a110d8`). `+0x40` is **always** forced to **0.2**, independent of any VehSpec field.
3. **DB `rlRVFrictionEqualizer` → `desc+0x28`**, not `desc+0x40`. The Havok reflection name
   `frictionEqualizer` sits at the `+0x40` class-member slot, which AA hardcodes. Framework
   `initFromDescriptor` maps `desc+0x28` → `fw+0x35c` (equalizer used by sim) and `desc+0x40` →
   `fw+0x304` (0.2 threshold / clip scalar). Ports must keep both slots distinct.
4. **Spin-torque triplet** (`+0x2c/+0x30/+0x34`) is Roll/Pitch/Yaw in VehSpec order (sequential) and
   lands in consecutive desc slots — **not** reordered like inertia.
5. **Extra torque** is `VehSpec+0x5d8` → `desc+0x38` (Havok `extraTorqueFactor` family).

---

## Constants (`read_memory`)

| Symbol | VA | LE bytes | float32 |
|---|---|---|---:|
| `DAT_00af4614` | `0x00af4614` | `CD CC 4C 3E` | **0.2** |
| `DAT_00af4618` | `0x00af4618` | `00 00 20 41` | **10.0** |
| `DAT_00a110d8` | `0x00a110d8` | `00 00 20 41` | **10.0** (defaults only; used by `FUN_00650020` for `desc+0x3c`) |

Xrefs: `DAT_00af4614` / `DAT_00af4618` are read from this function body only (among vehicle setup).
`DAT_00a110d8` is a global 10.0 pool (many xrefs including `FUN_00650020` @ `0x650056`).

### Defaults before overlay (`FUN_00650020` @ `0x650020`, decompile cross-check)

| desc | Default after `FUN_00650020` | After `FUN_005fc620` |
|---:|---|---|
| `+0x28..+0x30` | `0` | VehSpec `+0x5c4..+0x5cc` |
| `+0x34` | `1.0` (`g_flOne`) | VehSpec `+0x5d0` |
| `+0x38` | `0` | VehSpec `+0x5d8` |
| `+0x3c` | `10.0` (`DAT_00a110d8`) | `10.0` (`DAT_00af4618`) |
| `+0x40` | `g_flMultiKillCountBlend` (symbol name unrelated; then overwritten) | **`0.2`** |
| `+0x44..+0x4c` | `1.0` each | VehSpec yaw/roll/pitch unit inertia |
| `+0x50..+0x54` | `0` | **untouched** by this fn |
| `+0x58` | `0x80000000` | **untouched** by this fn |

---

## Conflicts vs Phase 0 evidence

| Item | Prior evidence | This re-verify | Verdict |
|---|---|---|---|
| Inertia: `desc+0x44 ← +0x5e4` (Yaw) | `0.2-mass-inertia.md` | same | **match** |
| Inertia: `desc+0x48 ← +0x5dc` (Roll) | same | same | **match** |
| Inertia: `desc+0x4c ← +0x5e0` (Pitch) | same | same | **match** |
| `desc+0x3c = 10.0`, `+0x40 = 0.2` | same (`DAT_00af4618/14`) | `read_memory` confirmed | **match** |
| `desc+0x28..+0x38` from `+0x5c4/5c8/5cc/5d0/5d8` | same offsets | + identities from `VehicleSpecific.ReadNew` / DB strings | **match** (names refined) |
| “`FUN_00650020` default `+0x40 = 0.0`” | `0.2-mass-inertia.md` | decompile: `g_flMultiKillCountBlend`, then forced to 0.2 here | **binary wins** — pre-overlay default is not necessarily 0; final value is always 0.2 |
| “`rlRVFrictionEqualizer` → `hkVehicleData.frictionEqualizer`” | TL;DR in `0.2-mass-inertia.md` | DB field → **`desc+0x28`**; **`desc+0x40` is const 0.2** | **nuance** — equalizer data is used, but not the Havok member at `+0x40` |
| “RVInertia not read in any framework builder” | `setup-field-mapping.md` §Fields NOT consumed | **This function reads them** | **binary wins** — outdated map entry |
| Verbatim copy (no scale) | implied | confirmed leaf loads | **match** |

**No inertia-slot conflict** with `0.2-mass-inertia.md`. Binary confirms yaw/roll/pitch remapping.

---

## Port notes (this function only)

When filling server `HkVehicleData` / framework desc from `VehicleSpecific`:

```
desc.FrictionEqualizerSlot   = vs.RVFrictionEqualizer   // desc+0x28 → fw equalizer path
desc.SpinTorqueRoll          = vs.RVSpinTorqueRoll      // +0x2c
desc.SpinTorquePitch         = vs.RVSpinTorquePitch     // +0x30
desc.SpinTorqueYaw           = vs.RVSpinTorqueYaw       // +0x34
desc.ExtraTorqueFactor       = vs.RVExtraTorqueFactor   // +0x38
desc.MaxVelPosFriction       = 10.0f                    // const; not from DB
desc.FrictionEqualizerConst  = 0.2f                     // const at +0x40 / fw threshold
desc.UnitInertiaYaw          = vs.RVInertiaYaw          // +0x44 ← NOT sequential first
desc.UnitInertiaRoll         = vs.RVInertiaRoll         // +0x48
desc.UnitInertiaPitch        = vs.RVInertiaPitch        // +0x4c
// do NOT write RVExtraAngularImpulse into this desc block from this function
```

For unit-mass server sims (`mass = 1`): diagonal inertia components equal `RVInertia*` directly.

Do **not** invent a map that sets Havok `+0x40` from `RVFrictionEqualizer` — retail hardcodes 0.2.

---

## Emulation

Not useful: pure pointer-chased loads from live entity layout. Mapping is fully determined by the
decompile + `read_memory` constants + VehSpec/DB name anchors. No golden float vectors for the
builder itself (identity transform + two fixed floats).
