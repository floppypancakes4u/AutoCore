# Verified: `Vehicle_BuildTransmissionDescriptor` @ `0x5fc840`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fc840` – `0x005fcaf1` |
| Symbol | `Vehicle_BuildTransmissionDescriptor` |
| Convention | MSVC stack args (Ghidra: `param_1` entity, `param_2` unused in body, `param_3` out descriptor) |
| Sole caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (`CALL` @ `0x5fd5bf`) |
| Downstream | `hkDefaultTransmission_ctor` @ `0x64f610` (size `0x60`, vtable `0x9e4dac`) via desc-copy `FUN_0064f100` @ `0x64f100` |
| Verified | Ghidra `decompile_function` (re-gate) + `read_memory` + loader string xrefs |

---

## Tools used (this verification)

1. **`decompile_function`** `0x5fc840` program=`autoassault.exe`
2. **`decompile_function`** `0x64f610` (`hkDefaultTransmission_ctor`), `0x64f100` (desc→component), `0x64f750` (desc zero-init), `0x4f5560` (wheel count)
3. **`decompile_function`** `0x5fd390` (call site + construction order)
4. **`read_memory`** `0x00a0f2a0` length=4 (`g_flOne`)
5. **`read_memory`** `0x9e4dac` / `0x9e4e50` (Havok reflection member names + offsets)
6. **`search_strings`** / **`get_xrefs_to`** / **`get_assembly_context`** on `VehicleDb_LoadCloneBase` column strings (DB → EBP store offsets)

Did **not** use `disassemble_bytes` as primary analysis. Emulation skipped (pointer-heavy VehSpec walks + dynamic arrays).

---

## Role

Fill the stack **`hkDefaultTransmission` descriptor** from `VehicleSpecific` (clone template + `0x3c`) plus per-instance entity multipliers and wheel counts. Covers:

1. **Scalars** — downshift/upshift RPM, primary ratio, reverse ratio, clutch delay
2. **Gears** — `numGears` + `gearsRatio[i]` array
3. **Torque split** — per-wheel `wheelsTorqueRatio[i]` from front/rear axle share ÷ axle wheel count
4. **Sanity check** — warn if Σ torque ratios > `1.0`

Runtime gear-shift / RPM stepping (if any residual Havok child still runs) is **not** this function. AA drive torque is custom (`VehicleAction_calcWheelTorque` / `torqueCurve2D`); transmission setup still matters for component state, HUD/RPM analogues, and the framework tail top-speed precompute at `vehicle+0x110`.

---

## Signature / args

```
void Vehicle_BuildTransmissionDescriptor(
    entity*  param_1,   // vehicle / entity instance
    undef    param_2,   // unused by this body (caller still passes context)
    desc*    param_3    // out: transmission descriptor (pre-zeroed by FUN_0064f750)
)
```

Caller sequence inside `0x5fd390`:

```
FUN_0064f750(auStack_148);                         // zero-init desc arrays/scalars
Vehicle_BuildTransmissionDescriptor(entity, ctx, auStack_148);
alloc 0x60 → hkDefaultTransmission_ctor(auStack_150); // copies desc via FUN_0064f100
```

---

## VehSpec / helper access chains

### Standard VehicleSpecific (most fields)

```
cloneSlot = *(int*)(param_1 + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec   = *(int*)( *(int*)(param_1 + slotOff + 0xac) + 0x3c )
```

Same convention as other framework builders (`setup-field-mapping.md`).

### Front-axle count (`+0x4cc`) only

```
related   = *(int*)(param_1 + 600)          // decimal 600 = 0x258 = entity[0x96]
cloneSlot = *(int*)(related + 4)
slotOff   = *(int*)(cloneSlot + 4)
VehSpec2  = *(int*)( *(int*)(related + slotOff + 0xac) + 0x3c )
frontN    = (int)*(char*)(VehSpec2 + 0x4cc)
```

Same shape as the steering builder; base object is `entity+0x258` rather than `entity`. Port should use logical **axle-0 wheel count** (`VehicleSpecific+0x4cc`).

### Wheel count — `FUN_004f5560` @ `0x4f5560` (`__fastcall`, `this`=entity)

```
return *(byte*)( *(int*)(entity + 600) + 0xb0 );   // total wheel count
```

Used for torque-ratio array capacity, stored count, rear-axle size, and the sum check.

---

## Descriptor layout (`param_3`)

Havok reflection member names (`0x9e4e50`…`0x9e4ebc`) match these **descriptor** offsets (type `0xb` = float unless noted):

| Off | Type | Written | Reflection / meaning |
|---|---|---|---|
| `+0x00` | `byte` | yes | wheel count (copy of `FUN_004f5560`) — not a named float member |
| `+0x04` | `float32` | yes | **`downshiftRPM`** |
| `+0x08` | `float32` | yes | **`upshiftRPM`** |
| `+0x0c` | `float32` | yes | **`primaryTransmissionRatio`** |
| `+0x10` | `float32` | yes | **`clutchDelayTime`** |
| `+0x14` | `float32` | yes | **`reverseGearRatio`** |
| `+0x18` | `ptr` | grow via `FUN_005b3300` | **`gearsRatio[]`** base |
| `+0x1c` | `int32` | yes | **numGears** (`tinNumberOfGears`) |
| `+0x20` | `uint32` | capacity | high bit `0x80000000` = unowned/sentinel; usable = `val & 0x7fffffff` |
| `+0x24` | `ptr` | grow via `FUN_005b3300` | **`wheelsTorqueRatio[]`** base |
| `+0x28` | `int32` | yes | wheel count |
| `+0x2c` | `uint32` | capacity | same sentinel convention as `+0x20` |

### Array grow rule (gears element size **4**, torque element size **4**)

```
// gears
nG = (uint)(byte)VehSpec[0x699]
cap = *(uint*)(desc + 0x20) & 0x7fffffff
if (cap < nG):
    newCap = cap * 2
    if ((int)newCap <= (int)nG): newCap = nG
    FUN_005b3300(desc + 0x18, newCap, 4)
*(uint*)(desc + 0x1c) = nG

// wheels torque
nW = (int)(char)FUN_004f5560()
cap = *(uint*)(desc + 0x2c) & 0x7fffffff
if (cap < nW):
    newCap = cap * 2
    if (newCap <= nW): newCap = nW
    FUN_005b3300(desc + 0x24, newCap, 4)
*(int*)(desc + 0x28) = nW
*desc = (byte)FUN_004f5560()
```

Zero-init (`FUN_0064f750`) sets both capacities to `0x80000000` and both counts/ptrs to 0.

---

## Exact algorithm (from decompile)

```
VehSpec = VehicleSpecific_from(entity)          // standard chain

// --- grow + store gear count / wheel count (see above) ---

// RPM thresholds: int16 DB values cast to float, then × entity mult
downshiftRPM = (float)(int)*(int16*)(VehSpec + 0x69c) * *(float*)(entity + 0x1fc)
upshiftRPM   = (float)(int)*(int16*)(VehSpec + 0x69e) * *(float*)(entity + 0x1fc)
*(float*)(desc + 4) = downshiftRPM
*(float*)(desc + 8) = upshiftRPM

// verbatim float copies (no entity mult)
*(float*)(desc + 0x14) = *(float*)(VehSpec + 0x6cc)   // reverseGearRatio
*(float*)(desc + 0x0c) = *(float*)(VehSpec + 0x6c4)   // primaryTransmissionRatio
*(float*)(desc + 0x10) = *(float*)(VehSpec + 0x6c8)   // clutchDelayTime

// --- gear ratios ---
nG = (byte)VehSpec[0x699]
for (i = 0; i < nG; i++):
    gearsRatio[i] = *(float*)(VehSpec + 0x6d0 + i*4)
    // note: decompile reloads VehSpec each iteration; same pointer in practice

// --- per-wheel torque split ---
frontN = (int)*(char*)(VehSpec_via_entity_plus_600 + 0x4cc)
i = 0
if (frontN > 0):
    invFront = g_flOne / (float)frontN          // 1.0 / frontN
    do:
        wheelsTorqueRatio[i] = *(float*)(VehSpec + 0x5e8) * invFront
        i++
    while (i < frontN)

// rear axle: remaining wheels [frontN .. nW)
nW = FUN_004f5560()
while (i < nW):
    rearN = (int)FUN_004f5560() - frontN
    wheelsTorqueRatio[i] = *(float*)(VehSpec + 0x5ec) / (float)rearN
    i++
    // FUN_004f5560 re-read each iteration (stable)

// --- sum check ---
sum = 0
for (i = 0; i < nW; i++):
    sum += wheelsTorqueRatio[i]
if (g_flOne < sum):   // strict greater than 1.0
    FUN_007a4480(0,
        "Vehicle %d has incorrect wheel torque values: %0.2f",
        vehicleId_from_clone_slot_plus_0x34,
        (double)sum)
```

### Torque-split formulas (verified)

Let `F = VehSpec+0x5e8` (front axle share), `R = VehSpec+0x5ec` (rear axle share),
`nF = axle0Count`, `nR = nWheels − nF`.

```
for i in [0, nF):     wheelsTorqueRatio[i] = F / nF     // coded as F * (1/nF)
for i in [nF, nW):    wheelsTorqueRatio[i] = R / nR
```

- Front uses multiply by reciprocal; rear uses direct divide — same real value.
- If `nF == 0`, the front loop is skipped; all wheels take the rear formula with `nR = nW`.
- If `nF == nW`, rear loop is empty (all wheels share `F/nF`).
- Expected well-formed content: `F + R == 1` (or ≤ 1). Sum of **per-wheel** ratios equals `F+R` when both axles are non-empty.

### Critical details

1. **Down/upshift RPM** are **signed int16** promoted to float, then scaled by **`entity+0x1fc`** (gear/wheel-dim runtime mult from prefixes — same register as wheel-radius scaling in other builders).
2. **Primary / clutch / reverse** are **unscaled** float copies.
3. **Gear ratio array** starts at `VehSpec+0x6d0`, stride 4, length `tinNumberOfGears` (`+0x699` byte). Indexed as `gearsRatio[0..nG-1]` (forward gears; reverse is the separate scalar).
4. **Axle split is index-based**: wheels `[0 .. frontN)` front, `[frontN .. nW)` rear — identical convention to steering/brake builders.
5. **Torque-ratio check** logs only; it does **not** normalize or clamp the array.
6. Descriptor write order for the three middle floats is `+0x14`, then `+0x0c`, then `+0x10` (no semantic effect).

---

## DB column → VehSpec offset (loader cross-check)

`VehicleDb_LoadCloneBase` (`0x7efb40`) stores SQL columns into a row base `EBP`. Those stores sit **`0x4c0` below** the `VehSpec` offsets used by this builder (`VehSpec` ≈ row + `0x4c0`, matching the Phase-0 “vehicle-specific block starts at +0x4c0” map).

Pattern: after each parse, the **next** column-name `PUSH` is immediately followed by the store of the **previous** parse result.

| DB column | String VA | Row (`EBP`) store | **VehSpec off** | Used by builder as |
|---|---|---|---|---|
| `tinNumberOfGears` | `0x00a92d28` | `+0x1d9` (byte) | **`+0x699`** | numGears / loop bound |
| `sinDownshiftRPM` | `0x00a92cec` | `+0x1dc` (i16) | **`+0x69c`** | downshiftRPM (× `entity+0x1fc`) |
| `sinUpshiftRPM` | `0x00a92cd0` | `+0x1de` (i16) | **`+0x69e`** | upshiftRPM (× `entity+0x1fc`) |
| `rlTransmissionRatio` | `0x00a92b5c` | `+0x204` | **`+0x6c4`** | primaryTransmissionRatio |
| `rlClutchDelayTime` | `0x00a92b38` | `+0x208` | **`+0x6c8`** | clutchDelayTime |
| `rlReverseGearRatio` | `0x00a92b10` | `+0x20c` | **`+0x6cc`** | reverseGearRatio |
| `rlGearRatios0` | `0x00a92af4` | `+0x210` | **`+0x6d0`** | gearsRatio[0] (then +4…) |
| `rlWheelTorqueRatiosFront` | `0x00a92f74` | `+0x128` | **`+0x5e8`** | front axle torque share |
| `rlWheelTorqueRatiosRear` | `0x00a92f44` | `+0x12c` | **`+0x5ec`** | rear axle torque share |

---

## Constant (`read_memory`)

| Symbol / address | LE bytes | float32 | Role |
|---|---|---|---|
| `g_flOne` @ `0x00a0f2a0` | `00 00 80 3f` | **`1.0`** | `1/frontN` reciprocal numerator; sum-check threshold |

No other `DAT_*` floats in this function. Logging goes through `FUN_007a4480` with format string `"Vehicle %d has incorrect wheel torque values: %0.2f"`.

---

## Handoff into `hkDefaultTransmission` (`FUN_0064f100` @ `0x64f100`)

Called from `hkDefaultTransmission_ctor` with the filled descriptor:

| Desc off | Component off | Field |
|---|---|---|
| `+0x04` | `+0x2c` | downshiftRPM |
| `+0x08` | `+0x30` | upshiftRPM |
| `+0x0c` | `+0x34` | primaryTransmissionRatio |
| `+0x10` | `+0x38` | clutchDelayTime |
| `+0x14` | `+0x3c` | reverseGearRatio |
| `+0x18` → copy | `+0x40` | gearsRatio[] (count at component `+0x44`) |
| `+0x1c` | `+0x44` | numGears |
| `+0x24` → copy | `+0x4c` | wheelsTorqueRatio[] (count at component `+0x50`) |
| `+0x28` | `+0x50` | wheel count |

Ctor size **`0x60`**. Desc byte at `+0x00` is **not** copied by `FUN_0064f100` (count lives at component `+0x50`).

---

## Conflicts vs Phase-0 evidence

| Item | `setup-field-mapping.md` / `0.7-transmission.md` | This re-verify | Verdict |
|---|---|---|---|
| `+0x699` → numGears | yes | yes (byte + loader `tinNumberOfGears`) | **match** |
| `+0x69c/+0x69e` i16 × `entity+0x1fc` → down/up RPM | yes | yes | **match** |
| `+0x6c4` → primaryTransmissionRatio | yes | yes (loader `rlTransmissionRatio`) | **match** |
| `+0x6c8` → reverseGearRatio | **setup says reverse** | **loader + reflection: clutchDelayTime** | **CONFLICT — binary wins** |
| `+0x6cc` → clutchDelayTime | **setup says clutch** | **loader + reflection: reverseGearRatio** | **CONFLICT — binary wins** |
| `+0x6d0[i]` → gearsRatio[i] | yes | yes | **match** |
| `+0x5e8/+0x5ec` ÷ axle counts → wheelsTorqueRatio | yes (“÷axle count”) | yes (`F/nF`, `R/nR`) | **match** |
| Split at `+0x4cc` | yes | yes (via `entity+600`) | **match** (chain nuance noted) |
| Sum > 1.0 warning | not in Phase-0 map | present | **additive** |

### Correction for `setup-field-mapping.md` (do not invent; binary + loader)

| VehSpec off | DB field (authoritative) | Havok desc field |
|---|---|---|
| `+0x6c8` | **`rlClutchDelayTime`** | **`clutchDelayTime`** (`desc+0x10`) |
| `+0x6cc` | **`rlReverseGearRatio`** | **`reverseGearRatio`** (`desc+0x14`) |

Phase-0 had these two labels **swapped**. Numbers at the offsets are still what the builder reads; only the **names** were inverted in the map.

### Distinction vs wheels builder (`FUN_005fcce0`)

`setup-field-mapping` also lists `VehSpec+0x600[i]` / `+0x740` under **Wheels** as “per-wheel torque-ratio”. That is a **different** path (wheel component / top-speed precompute in the `0x5fd390` tail). **This** transmission builder does **not** read `+0x600` or `+0x740`; it only fans `+0x5e8/+0x5ec` across axles.

---

## Port checklist (descriptor → `HkVehicleData` / transmission component)

```
numGears                  = (byte)  VehSpec[+0x699]
downshiftRPM              = (float)(int16)VehSpec[+0x69c] * gearDimMult   // entity+0x1fc
upshiftRPM                = (float)(int16)VehSpec[+0x69e] * gearDimMult
primaryTransmissionRatio  = (float) VehSpec[+0x6c4]                       // rlTransmissionRatio
clutchDelayTime           = (float) VehSpec[+0x6c8]                       // rlClutchDelayTime
reverseGearRatio          = (float) VehSpec[+0x6cc]                       // rlReverseGearRatio
for g in 0..numGears-1:
    gearsRatio[g]         = (float) VehSpec[+0x6d0 + 4*g]                 // rlGearRatios*

nF = axle0Count            // VehSpec[+0x4cc]
nW = wheelCount
nR = nW - nF
for i in 0..nW-1:
    if i < nF:  wheelsTorqueRatio[i] = frontShare / nF    // VehSpec[+0x5e8]
    else:       wheelsTorqueRatio[i] = rearShare  / nR    // VehSpec[+0x5ec]
// optional parity: if sum(wheelsTorqueRatio) > 1.0 → log warning (no clamp)
```

Constant: `g_flOne = 1.0` @ `0x00a0f2a0`. Emulation skipped (pointer-heavy). Goldens for this builder: synthetic `VehicleSpecific` + mults + axle counts → expected RPM floats, gear array, and per-wheel torque ratios.
)
