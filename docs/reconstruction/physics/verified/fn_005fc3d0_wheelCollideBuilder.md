# Verified: `FUN_005fc3d0` @ `0x5fc3d0` (engine desc builder — historical name “wheel-collide”)

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fc3d0` |
| Body | `0x005fc3d0` – `0x005fc4ee` (64 insns, **single basic block**, no callees) |
| Ghidra name | `FUN_005fc3d0` |
| Historical label | “wheel-collide descriptor builder” in `setup-field-mapping.md` / `fn_005fd390_buildFramework.md` |
| **Binary identity** | **`hkDefaultEngine` descriptor fill** (reflection names @ `0x9dad60` → strings at `0x9dae18..0x9daec4`) |
| Caller | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` (call site `0x005fd55d`) |
| Downstream | heap `0x3c` → `FUN_005d6640` (vtable `PTR_FUN_009dad44`) → post `FUN_0064f750` |
| Verified | Ghidra `decompile_function` (`0x5fc3d0`, `0x5d6640`, `0x5d66a0`, `0x5d6460`) + `read_memory` on reflection / `DAT_*` |

RE gate per `docs/reconstruction/physics/PORTING_RULES.md`. **No C# in this file.**

---

## 1. Naming correction (critical)

Existing setup notes place this builder in the **Steering → Transmission** construction slot and call it **wheel-collide**. That slot is where **Havok’s engine component** lives in the classic vehicle component order.

Evidence this is **engine**, not wheel-collide:

| Evidence | Detail |
|---|---|
| Havok reflection member table @ `0x9dad60` | 10 floats named `minRPM`, `optRPM`, `maxRPM`, `torque`, `torqueFactorAtMinRPM`, `torqueFactorAtMaxRPM`, `resistanceFactorAtMinRPM`, `resistanceFactorAtOptRPM`, `resistanceFactorAtMaxRPM`, `clutchSlipRPM` |
| Class string near table | `hkDefaultEngine` / `EhkDefaultEngine` @ `0x9daed3` |
| Desc sources | VehSpec engine/RPM/torque/resistance fields (`+0x69a`…`+0x6c0`), **not** `rlWheelRadius*` / `rlWheelWidth*` |
| Component methods | vtable slot uses `FUN_005d66a0` / `FUN_005d6460` — engine RPM→torque/resistance curves |
| True wheel cast | framework method `0x64bbd0` (`TtPhantom::castRay` path; see `0.5-wheel-collide.md`) — **no link to this builder** |
| True radius/width | `FUN_005fcce0` @ `0x5fcce0` → `VehSpec+0x600[i]` / `+0x618[i]` (Havok `wheelsRadius` / `wheelsWidth`; see `fn_005fcce0_wheelsBuilder.md`) |

**Filename keeps the historical “wheelCollideBuilder” token for discoverability; treat the symbol as the engine-descriptor builder.**

---

## 2. Decompile (authoritative)

```c
// FUN_005fc3d0 @ 0x005fc3d0
// param_1 = entity / vehicle instance (float* in caller; used as int here)
// param_2 = unused in body (present in signature from caller)
// param_3 = float[10] engine descriptor (stack block; fed to FUN_005d6640)

void FUN_005fc3d0(int param_1, undefined4 param_2, float *param_3)
{
  // VehSpec = *(*( *(param_1+4) + 4 ) + 0xac + param_1) + 0x3c
  // mult    = *(float*)(param_1 + 0x1fc)   // gear/wheel-dim / RPM prefix mult

  *param_3 =
      *(float*)(VehSpec + 0x6a8) * *(float*)(param_1 + 0x1fc);   // minRPM

  param_3[1] =
      *(float*)(VehSpec + 0x6ac) * *(float*)(param_1 + 0x1fc);   // optRPM

  param_3[2] =
      *(float*)(VehSpec + 0x6b4) * *(float*)(param_1 + 0x1fc);   // maxRPM

  param_3[9] = *param_3;                                         // clutchSlipRPM := minRPM

  param_3[3] = (float)(
      (int)*(short*)(VehSpec + 0x69a)                            // sinTorqueMax (i16)
      + *(int*)(param_1 + 0x218));                               // + entity filter/offset add

  param_3[4] = *(float*)(VehSpec + 0x6a0);                       // torqueFactorAtMinRPM
  param_3[5] = *(float*)(VehSpec + 0x6a4);                       // torqueFactorAtMaxRPM
  param_3[6] = *(float*)(VehSpec + 0x6b8);                       // resistanceFactorAtMinRPM
  param_3[7] = *(float*)(VehSpec + 0x6bc);                       // resistanceFactorAtOptRPM
  param_3[8] = *(float*)(VehSpec + 0x6c0);                       // resistanceFactorAtMaxRPM
}
```

`VehSpec` expansion (same clone-template chain as every other Build* helper):

```
VehSpec = *(*( *(param_1 + 4) + 4 ) + 0xac + param_1) + 0x3c
```

---

## 3. Descriptor layout (`param_3` / `float[10]`)

Aligned with Havok reflection table @ `0x9dad60` (member type `0x0b` = float, offsets `0..0x24`):

| `param_3[i]` | Refl. offset | Havok name | VehSpec source | Transform |
|---:|---:|---|---|---|
| **0** | `+0x00` | **`minRPM`** | `+0x6a8` `rlMinimumRPM` | × `entity+0x1fc` |
| **1** | `+0x04` | **`optRPM`** | `+0x6ac` `rlOptimumRPMMin` | × `entity+0x1fc` |
| **2** | `+0x08` | **`maxRPM`** | `+0x6b4` `rlMaximumRPMMax` | × `entity+0x1fc` |
| **3** | `+0x0c` | **`torque`** | `+0x69a` `sinTorqueMax` (**i16**) | `(float)((int)i16 + *(int*)(entity+0x218))` |
| **4** | `+0x10` | **`torqueFactorAtMinRPM`** | `+0x6a0` `rlMinimumTorqueFactor` | copy |
| **5** | `+0x14` | **`torqueFactorAtMaxRPM`** | `+0x6a4` `rlMaximumTorqueFactor` | copy |
| **6** | `+0x18` | **`resistanceFactorAtMinRPM`** | `+0x6b8` `rlMinimumResistance` | copy |
| **7** | `+0x1c` | **`resistanceFactorAtOptRPM`** | `+0x6bc` `rlOptimumResistance` | copy |
| **8** | `+0x20` | **`resistanceFactorAtMaxRPM`** | `+0x6c0` `rlMaximumResistance` | copy |
| **9** | `+0x24` | **`clutchSlipRPM`** | — | **`= param_3[0]`** (post-scale minRPM) |

### Write order (decompile order)

```
[0] minRPM → [1] optRPM → [2] maxRPM → [9] clutchSlipRPM
→ [3] torque → [4..8] factors/resistances
```

`param_3[9]` is assigned **after** `[0]` is scaled, so clutch slip tracks the **multiplied** minRPM.

---

## 4. Component object after `FUN_005d6640` (size `0x3c`)

```c
// FUN_005d6640 @ 0x005d6640  (thiscall)
// copies desc float[10] → object +0x14 .. +0x38
void FUN_005d6640(undefined4 *this, undefined4 *desc /* float[10] */)
{
  *(uint16*)((int)this + 6) = 1;
  this[3] = 0;
  this[4] = 0;
  *this = &PTR_FUN_009dad44;
  this[5]  = desc[0];   // +0x14 minRPM
  this[6]  = desc[1];   // +0x18 optRPM
  this[7]  = desc[2];   // +0x1c maxRPM
  this[8]  = desc[3];   // +0x20 torque
  this[9]  = desc[4];   // +0x24 torqueFactorAtMinRPM
  this[10] = desc[5];   // +0x28 torqueFactorAtMaxRPM
  this[11] = desc[6];   // +0x2c resistanceFactorAtMinRPM
  this[12] = desc[7];   // +0x30 resistanceFactorAtOptRPM
  this[13] = desc[8];   // +0x34 resistanceFactorAtMaxRPM
  this[14] = desc[9];   // +0x38 clutchSlipRPM
}
```

| Object off | Desc index | Field |
|---:|---:|---|
| `+0x00` | — | vtable `0x9dad44` |
| `+0x04` | — | size stamp `0x3c` (written by alloc site) |
| `+0x06` | — | `uint16 = 1` |
| `+0x0c` / `+0x10` | — | zeroed |
| `+0x14` … `+0x38` | `0` … `9` | engine floats (table above) |

Runtime consumers on this object (not re-gated in full here): `FUN_005d66a0` / `FUN_005d6460` read `+0x14..+0x38` and parent framework pointers. AA’s primary drive torque path remains `VehicleAction_calcWheelTorque` + `VehicleEngine_torqueCurve2D` (no reliance on a full Havok engine tick for NPC drive math).

---

## 5. Radius / width / final-drive — where they **actually** live

`setup-field-mapping.md` labeled this builder’s first three floats as “wheel radius / width / final-drive”. **That labeling is wrong.** Binary assignment:

### 5.1 Wheel radius & width — **not** this function

| Role | VehSpec | DB | Builder | Havok dest |
|---|---|---|---|---|
| **Per-wheel radius** | `+0x600 + i*4` | `rlWheelRadius0..5` | **`FUN_005fcce0`** | desc `+0x10[i]` → `wheelsRadius` |
| **Per-wheel width** | `+0x618 + i*4` | `rlWheelWidth0..5` | **`FUN_005fcce0`** | desc `+0x1c[i]` → `wheelsWidth` |

- No `entity+0x1fc` scale on radius/width in `FUN_005fcce0`.
- Runtime cast/compression/spin use `wheels+0x10` radius array (see `0.5-wheel-collide.md`, `fn_offsets_wheel.md`).

### 5.2 “Final drive” / transmission ratio — **not** this function’s `param[2]`

| Role | VehSpec | DB | Builder | Notes |
|---|---|---|---|---|
| **Primary transmission ratio (final drive)** | `+0x6c4` | `rlTransmissionRatio` | `Vehicle_BuildTransmissionDescriptor` `0x5fc840` | → `primaryTransmissionRatio` |
| Gear ratios | `+0x6d0[i]` | `rlGearRatios*` | same | array |
| Reverse | `+0x6c8` | `rlReverseGearRatio` | same | |
| Clutch delay | `+0x6cc` | `rlClutchDelayTime` | same | |

### 5.3 What `FUN_005fc3d0`’s three scaled floats really are

| Prior (incorrect) label | Actual VehSpec | Actual field |
|---|---|---|
| “param[0] wheel radius” | `+0x6a8` | **`minRPM`** (`rlMinimumRPM`) |
| “param[1] wheel width” | `+0x6ac` | **`optRPM`** (`rlOptimumRPMMin`) |
| “param[2] final-drive / rolling radius” | `+0x6b4` | **`maxRPM`** (`rlMaximumRPMMax`) |

### 5.4 Speed-governor tail uses **maxRPM × radius**, not “final-drive as radius”

From `Vehicle_buildHavokVehicleFramework` tail (re-verified in `fn_005fd390_buildFramework.md`):

```
fVar8 = (VehSpec+0x6b4) / ( (VehSpec+0x6c4) * VehSpec+0x6d0[lastGear] ) * DAT_009dd348
//      maxRPM          / ( primaryTransRatio * topGearRatio )         * (2π/60)

entity[0x44] += torqueShare[i] * (VehSpec+0x600)[i] * fVar8
//                             * wheelsRadius[i]
```

| Symbol | Address | LE | float32 | Meaning |
|---|---|---|---|---|
| `DAT_009dd348` | `0x009dd348` | `50 77 d6 3d` | **≈ 0.10471976** | **`2π/60`** (RPM → rad/s) |

So linear top-speed scale ≈  
`radius[i] · (maxRPM / (primaryRatio · topGear)) · 2π/60`  
weighted by axle torque share. **`+0x6b4` is max engine RPM; rolling radius is `+0x600[i]`.**

---

## 6. VehSpec engine block layout (context)

Offsets relative to `VehSpec` (same base as setup map). Engine block used here sits after shock/visual fields:

| Off | Type | Field (C# / DB) | Used by `FUN_005fc3d0`? |
|---|---|---|---|
| `+0x698` | u8 | `EngineType` | no |
| `+0x699` | u8 | `NumberOfGears` | no (transmission / governor) |
| `+0x69a` | i16 | **`TorqueMax` / `sinTorqueMax`** | **yes → `torque`** |
| `+0x69c` | i16 | `DownshiftRPM` | no (transmission) |
| `+0x69e` | i16 | `UpshiftRPM` | no (transmission) |
| `+0x6a0` | f32 | `MinTorqueFactor` | **yes** |
| `+0x6a4` | f32 | `MaxTorqueFactor` | **yes** |
| `+0x6a8` | f32 | `MinimumRPM` | **yes → minRPM** |
| `+0x6ac` | f32 | `OptimumRPMMin` | **yes → optRPM** |
| `+0x6b0` | f32 | **`OptimumRPMMax`** | **no — skipped** |
| `+0x6b4` | f32 | `MaximumRPMMax` | **yes → maxRPM** |
| `+0x6b8` | f32 | `MinimumResistance` | **yes** |
| `+0x6bc` | f32 | `OptimumResistance` | **yes** |
| `+0x6c0` | f32 | `MaximumResistance` | **yes** |
| `+0x6c4`… | — | transmission block | transmission builder |

**Gap:** `rlOptimumRPMMax` at `+0x6b0` is **not** written into the Havok engine desc (Havok only exposes a single `optRPM`; AA maps `OptimumRPMMin` → that slot).

---

## 7. Runtime multipliers

| Entity off | Type | Applied to |
|---|---|---|
| `+0x1fc` | f32 | **`minRPM`, `optRPM`, `maxRPM` only** (desc indices 0–2). Same register setup-field-mapping calls “gear/wheel-dim mult”. |
| `+0x218` | i32 | **Added** to `sinTorqueMax` before float cast → `torque` |

Torque factors and resistance factors are **unscaled** copies.

---

## 8. Conflicts vs prior evidence

| Item | Prior | This re-verify | Verdict |
|---|---|---|---|
| Function role | “wheel-collide desc” | **`hkDefaultEngine` desc** | **binary / reflection wins** |
| `+0x6a8` | wheel radius | **`minRPM`** | **correct prior label was wrong** |
| `+0x6ac` | wheel width | **`optRPM`** (`OptimumRPMMin`) | **wrong prior** |
| `+0x6b4` | final-drive / rolling radius | **`maxRPM`** | **wrong prior**; final drive is `+0x6c4`; radius is `+0x600[i]` |
| `+0x69a` | “collision filter info” | **`sinTorqueMax` (i16)** + `entity+0x218` | **wrong prior** |
| `+0x6a0/+0x6a4` | unnamed collide params | torque factors | **named** |
| `+0x6b8/+0x6bc/+0x6c0` | unnamed collide params | resistance factors | **named** |
| `param[9]` | (implied spare) | **`clutchSlipRPM = minRPM`** | **new** |
| Engine fields “not in setup” (`setup-field-mapping.md` § flagged) | torque/RPM only on AA torqueCurve path | **Also filled into this component** | **update needed** |
| Radius/width source | this builder | **`FUN_005fcce0` @ `+0x600/+0x618`** | matches `fn_005fcce0_wheelsBuilder.md` |
| Wheel cast | this component | **`0x64bbd0` + phantom cast** | matches `0.5-wheel-collide.md` |

---

## 9. Call-site wiring (framework)

From `Vehicle_buildHavokVehicleFramework` (construction order phase 4):

```
FUN_005fc3d0(entity, param_3, afStack_110)   // fill float[10] desc
alloc 0x3c
FUN_005d6640(component, afStack_110)         // copy 10 dwords → +0x14
FUN_0064f750(...)                            // post-helper (stack desc teardown / transfer)
```

Placed **after** steering, **before** transmission — standard Havok engine slot.

---

## 10. Porting notes (doc only)

1. **Do not** map `FUN_005fc3d0` outputs to wheel radius/width or raycast collide geometry.
2. Port radius/width from **`FUN_005fcce0`** / `VehSpec+0x600/+0x618`.
3. Port final drive from **`+0x6c4`** via the transmission builder.
4. If an engine desc is needed for fidelity: implement the 10-float table + `entity+0x1fc` / `+0x218` transforms exactly; leave `OptimumRPMMax` out of the Havok desc.
5. NPC drive torque still primarily follows **`calcWheelTorque` + `torqueCurve2D`**; do not assume this component alone produces drive force without a verified runtime consumer on the framework tick path.

---

## 11. Related addresses

| Addr | Role |
|---|---|
| `0x005fd390` | `Vehicle_buildHavokVehicleFramework` — sole caller |
| `0x005d6640` | engine component ctor (size `0x3c`, vtable `0x9dad44`) |
| `0x005d66a0` / `0x005d6460` | engine update / torque-resistance helpers |
| `0x005fcce0` | **true** wheels radius/width builder |
| `0x005fc840` | transmission (primary ratio / gears) |
| `0x0064bbd0` | **true** per-wheel collide cast entry |
| `0x009dad60` | Havok engine reflection member table |
| `0x009dae18`…`0x9daec4` | `clutchSlipRPM` … `minRPM` strings |
| `0x009dd348` | `2π/60` constant for speed governor |

---

## 12. Emulation

Not practical: pure field copy with entity/VehSpec pointer chains; no closed-form pure math beyond multiplies/add. Goldens = hand vectors from known `VehSpec` RPM/torque/resistance values and `entity+0x1fc` / `+0x218`.
