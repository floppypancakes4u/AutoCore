# Verified: `hkAngularVelocityDamper_ctor` @ `0x64d900`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `hkAngularVelocityDamper_ctor` |
| Address | `0x0064d900` |
| Size of instance | **`0x14`** (20 bytes) — set at alloc site as `word [obj+0x4] = 0x14` |
| Convention | MSVC `__thiscall` — `this` in `ECX`, one stack arg (desc*), `RET 4` |
| Role | Copy three floats from a stack descriptor into the Havok AVD action object |
| Vtable | `0x009e4a68` (`PTR_FUN_009e4a68`) |
| Primary call site | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` (call @ `0x005fd7bd`) |
| Secondary helper | Factory-style wrapper @ `0x0064d990` (alloc `0x14` + ctor; desc from caller) |
| RE tools | `decompile_function` @ `0x64d900`, `0x5fd390`, `0x64d810`; `disassemble_function` @ ctor + setup AVD block; `read_memory` reflection strings |
| Status | **Verified** (re-read) |

Related (not this function): per-step application is `hkAngularVelocityDamper_update` @ `0x64d810` — see `docs/reconstruction/physics/avd-airstab-spec.md`.

---

## 1. Decompile (authoritative)

```c
void __thiscall hkAngularVelocityDamper_ctor(undefined4 *param_1, undefined4 *param_2)
{
  *(undefined2 *)((int)param_1 + 6) = 1;
  *param_1 = &PTR_FUN_009e4a68;
  param_1[2] = *param_2;       // +0x08 ← desc[0]
  param_1[3] = param_2[1];     // +0x0c ← desc[1]
  param_1[4] = param_2[2];     // +0x10 ← desc[2]
  return;
}
```

### Assembly (11 instructions — no DAT constants)

```
0064d900  MOV  EAX, ECX                 ; this
0064d902  MOV  ECX, [ESP+0x4]           ; desc*
0064d906  MOV  word ptr [EAX+0x6], 1
0064d90c  MOV  dword ptr [EAX], 0x9e4a68
0064d912  MOV  EDX, [ECX]
0064d914  MOV  [EAX+0x8], EDX           ; normalSpinDamping
0064d917  MOV  EDX, [ECX+0x4]
0064d91a  MOV  [EAX+0xc], EDX           ; collisionSpinDamping
0064d91d  MOV  ECX, [ECX+0x8]
0064d920  MOV  [EAX+0x10], ECX          ; collisionThreshold
0064d923  RET  4
```

**Ctor does no math.** All scaling happens at the sole vehicle-setup call site.

---

## 2. Instance layout (post-ctor)

| Offset | Size | Field | Source at vehicle setup |
|-------:|-----:|-------|-------------------------|
| `+0x00` | 4 | vtable | `0x009e4a68` |
| `+0x04` | 2 | alloc size stamp | `0x0014` (written by allocator site, not ctor) |
| `+0x06` | 2 | flag | **`1`** (hardcoded in ctor) |
| `+0x08` | 4 | `normalSpinDamping` | desc[0] |
| `+0x0c` | 4 | `collisionSpinDamping` | desc[1] |
| `+0x10` | 4 | `collisionThreshold` | desc[2] |

Havok reflection member names (`.rdata` strings near `0x9e4ad0`):

| String | Address | Maps to damper off |
|--------|---------|-------------------:|
| `normalSpinDamping` | `0x009e4af4` | `+0x08` |
| `collisionSpinDamping` | `0x009e4adc` | `+0x0c` |
| `nThreshold` / threshold tail | `0x009e4ad0` | `+0x10` |
| class `hkAngularVelocityDamper` | `0x009e4b08` | — |

---

## 3. Call site — `Vehicle_buildHavokVehicleFramework` @ `0x5fd390`

AVD is the **last component before** `hkVehicleFramework_ctor`. No separate `Build*Descriptor` helper: three floats are built **inline**, then ctor is called.

### 3.1 VehicleSpecific access chain

`ESI` = vehicle (`param_1`). Clonebase / VehicleSpecific:

```
EAX = [ESI + 0x4]
EDX = [EAX + 0x4]                    ; RTTI / base adjust
ECX = [ESI + EDX + 0xac]
VehSpec = [ECX + 0x3c]               ; vehicleData / clonebase struct
```

Same convention as the rest of the setup path (`setup-field-mapping.md`).

### 3.2 Scale factor

```
scale = *(float*)(vehicle + 0x210)
```

In the decompile this appears as `param_1[0x84]` because `param_1` is typed `float*`:

```
float-index 0x84  →  byte offset 0x84 * 4 = 0x210
```

**Do not confuse** with a struct field at byte `+0x84`. Runtime AVD spin multiplier lives at **`vehicle+0x210`**.

### 3.3 Assembly (AVD block — authoritative scales)

```
; after FUN_0064d930 (aero desc cleanup)
005fd739  MOVSS  XMM1, [VehSpec + 0x5b8]     ; rlAVDNormalSpinDamping
005fd741  MOVSS  XMM0, [ESI + 0x210]         ; scale
005fd749  MULSS  XMM0, XMM1
005fd74d  MOVSS  [ESP+0x2c], XMM0            ; desc[0]

005fd760  MOVSS  XMM1, [VehSpec + 0x5bc]     ; rlAVDCollisionSpinDamping
005fd768  MOVSS  XMM0, [ESI + 0x210]         ; scale
005fd776  MULSS  XMM0, XMM1
005fd77a  MOVSS  [ESP+0x30], XMM0            ; desc[1]

005fd78d  MOVSS  XMM0, [VehSpec + 0x5c0]     ; rlAVDCollisionThreshold
005fd795  PUSH   0xa                         ; alloc arg (tag)
005fd797  MOVSS  [ESP+0x38], XMM0            ; → desc[2] @ pre-push ESP+0x34 (unscaled!)
005fd79f  PUSH   0x14                        ; size
005fd7a1  CALL   [heap+0x10]                 ; allocate 0x14
005fd7a4  MOV    word ptr [EAX+0x4], 0x14
005fd7ae  LEA    ECX, [ESP+0x2c]             ; &desc
005fd7b2  PUSH   ECX
005fd7b3  MOV    ECX, EAX                     ; this = damper
005fd7bd  CALL   0x0064d900                  ; hkAngularVelocityDamper_ctor
```

Stack ESP accounting: after `PUSH 0xa`, threshold write `[ESP+0x38]` lands at **pre-push `ESP+0x34`**, i.e. contiguous `desc[2]` after `desc[0]/[1]` at `+0x2c/+0x30`.

### 3.4 Field sources + scales (summary)

| Damper field | Damper off | VehSpec off | DB / clonebase column | Scale |
|---|---:|---:|---|---|
| `normalSpinDamping` | `+0x08` | `+0x5b8` | `rlAVDNormalSpinDamping` | **`× (vehicle+0x210)`** |
| `collisionSpinDamping` | `+0x0c` | `+0x5bc` | `rlAVDCollisionSpinDamping` | **`× (vehicle+0x210)`** |
| `collisionThreshold` | `+0x10` | `+0x5c0` | `rlAVDCollisionThreshold` | **none** (verbatim copy) |

```
normalSpinDamping    = *(float*)(vehicle + 0x210) * *(float*)(VehSpec + 0x5b8)
collisionSpinDamping = *(float*)(vehicle + 0x210) * *(float*)(VehSpec + 0x5bc)
collisionThreshold   = *(float*)(VehSpec + 0x5c0)
```

### 3.5 Wire into framework

After ctor returns (`EDI` = damper*):

1. Grow action-list buffer if full (`FUN_005b3370`).
2. `actionList[count++] = damper`.
3. Allocate / construct `hkVehicleFramework` (`0x360` bytes, ctor `0x64cd30`) with the full component descriptor (includes this action).

Construction order in `0x5fd390`:  
**Wheels → Chassis → Steering → WheelCollide → Transmission → Brake → Suspension → Aerodynamics → AngularVelocityDamper → Framework**.

---

## 4. Consumer — `hkAngularVelocityDamper_update` @ `0x64d810` (how fields are used)

Not part of the ctor, but documents the **units / semantics** of the three stored floats:

```
w2 = ωx² + ωy² + ωz²          // chassis rb angVel at +0x50/+0x54/+0x58
if w2 <= collisionThreshold²:
    d = normalSpinDamping * dt
else:
    d = collisionSpinDamping * dt
f = max(0, 1 − d)
ω *= f                         // all three axes (+ w-lane), then setAngularVelocity
```

| Field | Runtime role | Units (effective) |
|---|---|---|
| `normalSpinDamping` | Rate when `‖ω‖ ≤ threshold` | 1/s  (`rate · dt` dimensionless) |
| `collisionSpinDamping` | Rate when `‖ω‖ > threshold` | 1/s |
| `collisionThreshold` | Angular-speed gate on `‖ω‖` | rad/s (compared via squares) |

Collision branch is **speed-triggered only** — not gated by the 6400 ms airStabilization collision window.

---

## 5. Corrections vs earlier map notes

| Prior claim | Verified truth |
|---|---|
| Plate on `0x5fd390`: all three AVD floats `× param_1[0x84]` | Only **damping rates** scaled; **threshold unscaled** |
| `setup-field-mapping.md`: scale as `entity[0x84]` | Scale is **`vehicle+0x210`** (= float index `[0x84]` on a `float*` vehicle) |
| Ctor owns scaling | Ctor is a pure copy; scaling is **inline in `0x5fd390`** |

---

## 6. Confidence

| Item | Confidence |
|---|---|
| Ctor layout / vtable / three dword copy | **High** (disasm + decompile) |
| VehSpec `+0x5b8/+0x5bc/+0x5c0` → three fields | **High** (setup asm) |
| Damping × `vehicle+0x210`; threshold unscaled | **High** (setup asm) |
| DB names `rlAVD*` for those offsets | **High** (clonebase load strings + prior map) |
| Origin of `vehicle+0x210` value (prefix adjust product) | **Medium** — not re-traced this pass; treated as “runtime AVD spin mult” |

---

## 7. Port recipe (no code — data path only)

At vehicle framework build:

1. Read clonebase `rlAVDNormalSpinDamping`, `rlAVDCollisionSpinDamping`, `rlAVDCollisionThreshold`.
2. Multiply the two damping rates by the per-vehicle AVD spin multiplier (`vehicle+0x210` on client).
3. Leave threshold as the raw clonebase value.
4. Store into a 20-byte action object at `+0x08/+0x0c/+0x10` and register it as a per-step Havok vehicle action (update formula in §4).

Emulation: skipped — ctor is pointer/copy only; scaling is pure float multiply at setup (no DAT constants).
