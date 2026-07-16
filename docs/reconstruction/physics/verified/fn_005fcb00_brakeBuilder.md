# Verified: `FUN_005fcb00` — brake descriptor builder @ `0x5fcb00`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `FUN_005fcb00` (unnamed; brake desc builder) |
| Address | `0x005fcb00` |
| Role | Build stack `hkDefaultBrake` **descriptor** from `VehicleSpecific` + entity runtime brake mults |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (call site ~`0x5fd630`) |
| Downstream | `hkDefaultBrake_ctor` @ `0x64ed40` (heap size **`0x54`**, vtable `PTR_FUN_009e4cb8`) |
| Desc init | `FUN_0064ef20` @ `0x64ef20` (zeros hkArray headers + `minTimeToBlock`) before this builder |
| Desc → component copy | `FUN_0064e840` (called from ctor; copies three per-wheel arrays + scalar min-time) |
| RE tools | `decompile_function` @ `0x5fcb00`, `0x64ed40`, `0x64ef20`, `0x64e840`, `0x64e6f0`, `0x4f5560`; `read_memory` Havok member-name strings @ `0x9e4d28+` |
| Status | **Verified** (setup builder fields) |

Cross-refs: `setup-field-mapping.md` (Brake table), `brake-spec.md` (runtime custom-path behavior), `fn_005fd390_buildFramework.md` (construction order).

---

## 1. Signature & call pattern

```c
// thiscall/stdcall as decompiled — three args from 0x5fd390:
//   param_1 = entity / vehicle instance
//   param_2 = unused by this function body (framework arg passthrough)
//   param_3 = out BrakeDescriptor* (stack local ~0x2C bytes)
void FUN_005fcb00(int param_1, undefined4 param_2, undefined1 *param_3);
```

Caller sequence inside `Vehicle_buildHavokVehicleFramework`:

```
FUN_0064ef20(brakeDesc);                 // zero-init arrays + minTime = 0
FUN_005fcb00(entity, arg, brakeDesc);    // THIS — fill per-wheel arrays
heap = alloc(0x54);
hkDefaultBrake_ctor(heap, brakeDesc);    // 0x64ed40 → FUN_0064e840 copies arrays in
```

---

## 2. Decompile (authoritative, annotated)

```c
void FUN_005fcb00(int entity, undefined4 unused, BrakeDesc *desc)
{
  // --- grow/fill three hkArray headers to wheelCount ---
  int nWheels = (int)(char)FUN_004f5560(entity);   // entity[+600]+0xb0 (byte)

  // desc+0x04.. : hkArray<float>  wheelsMaxBrakingTorque   (elem size 4)
  ensure_capacity(&desc->maxTorqueArr, nWheels, 4);
  desc->maxTorqueCount = nWheels;

  // desc+0x10.. : hkArray<float>  wheelsMinPedalInputToBlock (elem size 4)
  ensure_capacity(&desc->minPedalArr, nWheels, 4);
  desc->minPedalCount = nWheels;

  // desc+0x20.. : hkArray<byte>   wheelsIsConnectedToHandbrake (elem size 1)
  ensure_capacity(&desc->handbrakeArr, nWheels, 1);
  desc->handbrakeCount = nWheels;

  *desc = (byte)nWheels;                 // desc+0x00 = numWheels
  desc->minTimeToBlock = 0;              // desc+0x1c = **const 0** (DB MinBlockTime ignored)

  VehSpec *vs = ResolveVehSpec(entity);  // *(*(entity + *(entity[1]+4) + 0xac) + 0x3c)
  int frontCount = (byte)vs[+0x4cc];     // axle-0 wheel count
  int i = 0;

  // ---- front axle: i ∈ [0, frontCount) ----
  for (; i < frontCount; ++i) {
    maxTorque[i]  = vs[+0x57c] * entity[+0x200];          // f32 × front brake mult
    handbrake[i]  = vs[+0x5f0] & 1;                       // bit0 → byte 0/1
    minPedal[i]   = vs[+0x58c];                           // f32, no scale
  }

  // ---- rear axle: i ∈ [frontCount, nWheels) ----
  for (; i < nWheels; ++i) {
    maxTorque[i]  = vs[+0x580] * entity[+0x204];          // f32 × rear brake mult
    handbrake[i]  = (vs[+0x5f0] >> 1) & 1;                // bit1 → byte 0/1
    minPedal[i]   = vs[+0x590];                           // f32, no scale
  }
}
```

`FUN_004f5560` (wheel count helper):

```c
// __fastcall: returns byte at *(entity + 600) + 0xb0
byte FUN_004f5560(int entity) {
  return *(byte *)(*(int *)(entity + 600) + 0xb0);
}
```

`ensure_capacity` is the shared `FUN_005b3300` grow path: if `(capacity & 0x7fffffff) < need`, grow to `max(capacity*2, need)`.

---

## 3. Brake descriptor layout (`param_3`, stack)

Initialized by `FUN_0064ef20`, filled by this function, consumed by `hkDefaultBrake_ctor` / `FUN_0064e840`.

| Desc off | Type | Havok name (reflection strings @ `0x9e4d28+`) | Source in builder |
|---|---|---|---|
| `+0x00` | `u8` | wheel count (passed as `*param_2` into ctor) | `FUN_004f5560` → entity wheel-count byte |
| `+0x04` | `float*` | `wheelsMaxBrakingTorque[]` data | per-wheel filled |
| `+0x08` | `i32` | array size | `= nWheels` |
| `+0x0c` | `u32` | array capacity (`0x80000000` = empty) | grown by `FUN_005b3300` |
| `+0x10` | `float*` | `wheelsMinPedalInputToBlock[]` data | per-wheel filled |
| `+0x14` | `i32` | array size | `= nWheels` |
| `+0x18` | `u32` | array capacity | grown |
| `+0x1c` | `f32` | `wheelsMinTimeToBlock` (**scalar**) | **hardcoded `0`** |
| `+0x20` | `byte*` | `wheelsIsConnectedToHandbrake[]` data | per-wheel filled |
| `+0x24` | `i32` | array size | `= nWheels` |
| `+0x28` | `u32` | array capacity | grown |

Havok reflection name strings (confirmed raw memory):

| Address | C-string |
|---|---|
| `0x009e4d28` | `wheelsIsConnectedToHandbrake` |
| `0x009e4d48` | `wheelsMinTimeToBlock` |
| `0x009e4d60` | `wheelsMinPedalInputToBlock` |
| `0x009e4d7c` | `wheelsMaxBrakingTorque` |
| `0x009e4d94` | `hkDefaultBrake` |

---

## 4. VehicleSpecific / entity field map

### 4a. Inputs (read)

| Source | Off | DB / meaning | Type | Transform |
|---|---|---|---|---|
| `VehSpec` | `+0x4cc` | front-axle wheel count | `u8` | split index for front/rear fan-out |
| `VehSpec` | `+0x57c` | `rlBrakesMaxTorqueFront` | `f32` | × `entity+0x200` → all front wheels |
| `VehSpec` | `+0x580` | `rlBrakesMaxTorqueRear` | `f32` | × `entity+0x204` → all rear wheels |
| `VehSpec` | `+0x58c` | `rlBrakesPedalInputFront` (`m_minPedalInputToBlock`) | `f32` | verbatim → front wheels |
| `VehSpec` | `+0x590` | `rlBrakesPedalInputRear` | `f32` | verbatim → rear wheels |
| `VehSpec` | `+0x5f0` bit0 | front handbrake connect flag | bit | → `handbrake[front]=1` if set |
| `VehSpec` | `+0x5f0` bit1 | rear handbrake connect flag | bit | → `handbrake[rear]=1` if set |
| entity | `+0x200` | front brake max-torque **runtime mult** (prefix/upgrade) | `f32` | multiplies front MaxTorque |
| entity | `+0x204` | rear brake max-torque **runtime mult** | `f32` | multiplies rear MaxTorque |
| entity graph | `+600 → +0xb0` | total wheel count | `u8` | via `FUN_004f5560` |

`VehSpec` resolution (same pattern as other builders):

```
VehSpec = *(*(entity + *( *(entity+4) + 4 ) + 0xac) + 0x3c)
```

(Front-count read also walks a parallel path via `entity+600`’s nested clone pointer; same `+0x3c` / `+0x4cc` fields.)

### 4b. Outputs (written into desc arrays)

| Desc array | Front `i < frontCount` | Rear `i ≥ frontCount` |
|---|---|---|
| `wheelsMaxBrakingTorque[i]` | `VehSpec+0x57c * entity+0x200` | `VehSpec+0x580 * entity+0x204` |
| `wheelsMinPedalInputToBlock[i]` | `VehSpec+0x58c` | `VehSpec+0x590` |
| `wheelsIsConnectedToHandbrake[i]` | `VehSpec+0x5f0 & 1` | `(VehSpec+0x5f0 >> 1) & 1` |
| `wheelsMinTimeToBlock` (scalar) | **`0` always** | **`0` always** |

### 4c. Fields **not** consumed by this builder

| DB field | Notes |
|---|---|
| `rlBrakesMinBlockTimeFront` / `Rear` | Present in clonebase bind block (`brake-spec.md`); **never read here**. Scalar `desc+0x1c` is forced to `0`. Instant wheel-lock eligibility once pedal ≥ minPedal (timer starts at 0 remaining). |
| Service-brake **pedal input** | Not a setup field — runtime only (status/input `+0x10` in `hkDefaultBrake_update`). |

---

## 5. Component layout after ctor (`hkDefaultBrake`, size `0x54`)

From `hkDefaultBrake_ctor` (`0x64ed40`) + copy helper `FUN_0064e840` + consumer `hkDefaultBrake_update` (`0x64e6f0`, plate `WI-MOV-005`):

| Comp off | Type | Meaning |
|---|---|---|
| `+0x00` | vtable* | `PTR_FUN_009e4cb8` |
| `+0x08` | ptr | parent `hkVehicleFramework*` |
| `+0x0c` | i32 | wheel count (loop bound in update) |
| `+0x10` | float* | **output** per-wheel brake torque (written by update) |
| `+0x1c` | byte* | per-wheel **isBlocked / handbrake-lock** flags (written by update) |
| `+0x28` | float* | `wheelsMaxBrakingTorque[]` (from desc) |
| `+0x2c` / `+0x30` | size / cap | max-torque array |
| `+0x34` | float* | `wheelsMinPedalInputToBlock[]` (from desc) |
| `+0x38` / `+0x3c` | size / cap | min-pedal array |
| `+0x40` | byte* | `wheelsIsConnectedToHandbrake[]` (from desc) |
| `+0x44` / `+0x48` | size / cap | handbrake-connect array |
| `+0x4c` | f32 | `wheelsMinTimeToBlock` (from desc `+0x1c`, always **0** after this builder) |
| `+0x50` | f32 | remaining block-timer (reset to `+0x4c` when pedal below min on all wheels) |

`FUN_0064e840` copies `desc+0x1c` into **both** `comp+0x4c` and `comp+0x50` at construct time.

### Runtime semantics (`hkDefaultBrake_update` @ `0x64e6f0`) — context only

Not part of the builder, but defines what the built fields *mean*:

```
status     = framework[+0x14]
brakePedal = status[+0x10]          // f32 service brake [0..1]
handbrake  = status[+0x18]          // byte/bool

for each wheel i:
  isBlocked[i] = (handbrakeConnected[i] && handbrake)
  peak         = brakePedal * maxBrakingTorque[i]
  // clamp opposing spin torque into ±peak  →  outBrakeTorque[i]
  if minPedal[i] <= brakePedal: arm block timer path
// when all-armed and timer expired (minTime==0 → immediate): force isBlocked[i]=1
```

---

## 6. Critical note — custom path may not apply service brake

> **Task B8 update (RESOLVED):** the runtime ambiguity flagged at the end of this section (whether
> `hkDefaultBrake_update` is ever vtable-dispatched) is now closed — it **is** ticked every
> substep. See `brake-spec.md` §5 item 1 for the full call chain (`tickSubsystems` → `fw+0x24` →
> `hkDefaultBrake_update` `0x64e6f0`, pedal from the throttle axis's reverse component, output
> consumed by `postTickApplyForces`/`preUpdate`). Points 2–5 below (the DB→Havok field mapping)
> remain accurate; only the "does the runtime ever call it" question changes.

**This builder only configures `hkDefaultBrake` at vehicle setup.** It does **not** apply braking forces.

Retail AA custom drive path:

| Stage | Address | Service brake? |
|---|---|---|
| `PushDriveAxesToController` | `0x4fbc10` | Copies entity `+0x614` throttle + `+0x61c` **handbrake** only — **no service-brake pedal** on the input controller (`0.8-struct-offsets.md`) |
| `VehicleAction::applyAction` | `0x598650` | Ramps throttle / steer / boost; **no brake-torque term** |
| `VehicleAction::calcWheelTorque` | `0x598040` | Drive torque clamped to **`[0, 1000]`** — never negative; rear handbrake only applies **×0.5 traction cut** (`DAT_00a0f298`) |
| Friction solver | `0x6c4450` | Coast / reverse deceleration via longitudinal slip impulses when `drivePack≈0` or reverse |

Implications for the port:

1. **Populated MaxTorque / MinPedal / handbrake-connect arrays are real setup data** (this function proves the DB → Havok mapping).
2. **If the runtime never feeds `status+0x10` (brake pedal)**, `hkDefaultBrake_update` emits **zero service-brake torque** even with a fully built component — peak = `0 * maxTorque`.
3. Observed retail NPC/player deceleration is explained by **friction-solver coast + reverse throttle**, not by this descriptor’s MaxTorque, unless a separate path writes the pedal / invokes the brake component.
4. Handbrake **connect flags** from `VehSpec+0x5f0` matter only if the Havok brake update runs with handbrake asserted; the AA custom path’s primary handbrake effect is the **rear drive-torque cut**, not necessarily `hkDefaultBrake` lock torque.
5. **`wheelsMinTimeToBlock` is always 0** after this builder — DB MinBlockTime Front/Rear are dead for this setup path.

See `brake-spec.md` §1 and §5 for the longer open-item discussion (whether the component is vtable-dispatched every tick).

---

## 7. Port recipe (setup only — no C#)

When constructing a brake config from vehicle definition:

```
nWheels    = wheelCount
frontCount = VehSpec.axle0Count          // +0x4cc
for i in 0 .. nWheels-1:
  if i < frontCount:
    maxTorque[i] = VehSpec.maxTorqueFront * entity.brakeMultFront   // +0x57c * +0x200
    minPedal[i]  = VehSpec.minPedalFront                            // +0x58c
    handbrake[i] = (VehSpec.flags5f0 & 1) != 0
  else:
    maxTorque[i] = VehSpec.maxTorqueRear  * entity.brakeMultRear    // +0x580 * +0x204
    minPedal[i]  = VehSpec.minPedalRear                             // +0x590
    handbrake[i] = (VehSpec.flags5f0 & 2) != 0
minTimeToBlock = 0.0   // always
```

Do **not** invent a service-brake application from MaxTorque alone in the custom driver path; match retail: zero-floor drive torque + solver coast, unless implementing a separate pedal-fed `hkDefaultBrake_update` equivalent.

---

## 8. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x5fcb00` | OK — front/rear fan-out, three arrays, minTime=0 |
| `decompile_function` `0x64ef20` | OK — desc zero-init layout matches |
| `decompile_function` `0x64ed40` / `0x64e840` | OK — desc arrays → component `+0x28/+0x34/+0x40`, scalar → `+0x4c/+0x50` |
| `decompile_function` `0x64e6f0` | OK — runtime meaning of built fields (pedal × maxTorque) |
| `decompile_function` `0x4f5560` | OK — wheel count = `*(entity+600)+0xb0` |
| `read_memory` `0x9e4d28+` | OK — Havok member / class name strings |
| Caller | sole: `Vehicle_buildHavokVehicleFramework` |
| Conflict vs prior | Relative DB EBP offsets in older `brake-spec.md` bind notes (`+0xbc…`) are **not** final VehSpec offs; **binary builder uses `+0x57c…+0x590` / `+0x5f0`** (`setup-field-mapping.md` matches) |
| Emulation | Skipped — pointer-heavy entity/VehSpec graph; goldens = table above |

---

## 9. Confidence

| Claim | Confidence |
|---|---|
| Desc field → Havok name mapping | **High** (reflection strings + copy helper) |
| VehSpec offsets / front-rear fan-out | **High** (direct decompile) |
| Runtime mults `entity+0x200/+0x204` | **High** |
| `minTimeToBlock` forced 0 | **High** |
| Custom path does not apply service brake via this data alone | **High** for applyAction/calcWheelTorque; **Medium** whether framework still vcalls `hkDefaultBrake_update` with a non-zero pedal from some other writer |
