# VERIFIED — “Throttle ramp” / `DAT_00a10e74=2.0` / `entity+0x614` in `applyAction` @ `0x598650`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x598650`, `0x4fbc10` (`PushDriveAxesToController`),  
`0x597f90` (`VehicleAction_ctor`); `read_memory` on DAT list below;  
`get_xrefs_to` @ `0x9d54e0`; instruction walk of stage-1 region `0x5988a3`–`0x598999`  
(from `disassemble_function` — **not** `disassemble_bytes`).

**Scope of this file:** the path that older notes call the **“throttle ramp (rate 2.0/s)”**,  
and how it relates to **`entity+0x614`**.  
**Not this file:** full two-stage steer pipeline detail → `fn_00598650_steerRamp.md`;  
applyAction orchestration → `fn_00598650_applyAction.md`; entity axis writers →  
`fn_entity_driveAxes_offsets.md` / `fn_004fbc10_pushDriveAxes.md`.

---

## 0. One-line verdict

| Claim | Verdict |
|-------|---------|
| `applyAction` ramps **`entity+0x614` (throttle)** with rate **2.0/s** | **FALSE** |
| Constant **`DAT_00a10e74 = 2.0`** is used in this fn | **TRUE** — as stage-1 **open-band rate factor** only |
| Target of that ramp is **`entity+0x618` (steer)** → **`VA+0x24`** | **TRUE** |
| Rate base is **`VA+0x20`**, ctor-seeded **≈ 2.857** (`DAT_009d54e0`) | **TRUE** — **not** live thr |
| Live throttle **`entity+0x614`** is consumed by **`PushDrive`** → **`driverInput+0x20`** | **TRUE** — **zero** loads of `+0x614` inside `applyAction` |

**There is no throttle-axis ramp inside `VehicleAction_applyAction`.**  
What was labeled “throttle ramp” is the **steer stage-1 ramp**, whose step multiplies a **fixed rate field** by **`dt`** and by **`{1.0 | 2.0}`**.

---

## 1. Three objects (must not share offsets)

`Vehicle_createVehicleAction` (`0x4fb660`) builds handle `entity+0x1a0`:

```
entity+0x1a0 → { +0x00 VehicleAction*, +0x04 framework*, +0x08 driverInput* }
```

| Object | Access | `+0x20` | `+0x24` |
|--------|--------|--------|--------|
| **Entity** | `*(VA+0x44)` | n/a | n/a — axes at **`+0x614` thr / `+0x618` steer / `+0x61c` sharp** |
| **driverInput** | `*(entity+0x1a0)+8` | **live throttle** ← `entity+0x614` | **handbrake byte** ← `entity+0x61c` |
| **VehicleAction** | `this` of `applyAction` | **stage-1 ramp rate** (ctor constant) | **stage-1 steer command** (ramps to `+0x618`) |

`PushDriveAxesToController` writes **only** driverInput. It never writes `VehicleAction+0x20/+0x24`.

---

## 2. `entity+0x614` path (actual throttle — no ramp here)

### 2.1 Inside `applyAction` (`0x598650`)

- Full-function disassembly / decompile: **0** references to offset **`0x614`**.
- Longitudinal command is **not** ramped, clamped, or mirrored in this function.

### 2.2 Bridge — `VehicleEntity_PushDriveAxesToController` @ `0x4fbc10`

```
if entity+0x101 != 0 OR entity+0x1a0 == 0: return

ctrl = *(entity+0x1a0) + 8

if entity+0x109 != 0:
    ctrl+0x20 = 0
    ctrl+0x24 = 1          // handbrake
    return

ctrl+0x20 = entity+0x614   // raw thr; sign preserved (AI forward often −1)
if ctrl+0x19 != 0 and thr >= DAT_00a0f734 (0.9):
    ctrl+0x20 = 0.9        // upper-only ceiling (not ±0.9)

// optional speed-cap gate may later force ctrl+0x20 = 0
ctrl+0x24 = entity+0x61c   // handbrake byte last
```

| Address | Bytes (LE) | float | Role |
|---------|------------|------:|------|
| `0x00a0f734` | `66 66 66 3f` | **0.9** | optional thr ceiling on driverInput |

### 2.3 Downstream of thr (outside this “ramp”)

| Stage | Where | Role |
|-------|-------|------|
| Entity axis | `entity+0x614` | AI / net / player writers |
| Controller | `driverInput+0x20` | PushDrive copy / gates |
| Drive force | `calcWheelTorque` @ `0x598040` | per-wheel torque (not a thr ramp) |
| Friction | framework `postTick` / solver | impulse from drive pack |

Handbrake cut is **`entity+0x61c`** in `calcWheelTorque` (rear ×0.5), not thr ramp.

---

## 3. The mislabeled “throttle ramp” — stage-1 **steer** + **2.0** factor

Asm region: **`0x5988a3` – `0x598999`** (after anti-sink, before mode-`0x02` stage-2).

### 3.1 Key loads (instruction walk)

| Addr | Instruction | Meaning |
|------|-------------|---------|
| `0x5988a9` | `MOVSS XMM0, [ECX+0x618]` | **entity+0x618** steer target (`ECX = entity`) |
| `0x5988b1` | `SUBSS XMM0, [ESI+0x24]` | `delta = target − VA+0x24` |
| `0x5988b6` | `MOVSS XMM7, [0x00aaa668]` | **−1.0** clamp |
| `0x5988be` | `MOVSS XMM5, [0x00a10e74]` | **2.0** open-band factor |
| `0x5988c6` | `MOVSS XMM2, [0x00a0f2a0]` | **1.0** clamp / default factor |
| `0x598921` | `MOVSS XMM0, [ESI+0x20]` | **VA+0x20** rate base |
| `0x59892a` | `MULSS XMM0, XMM3` | × **dt** (`*param_2`) |
| `0x59892e` | `MULSS XMM0, XMM1` | × **factor** (1.0 or 2.0) |
| `0x59894a` / `0x598975` | `SUBSS` / `ADDSS` on `[ESI+0x24]` | apply step by sign of delta |
| `0x598963` / `0x59898e` | store `VA+0x24` → `*( *(VA+0x40)+0x14 )+0x1c` | wheelsDesc desired steer |

Decompiler symbol for the 2.0 load: `g_flLevelUpUiBase_Inferred` (shared float; **misnamed**).  
Raw site: **`DAT_00a10e74`**.

### 3.2 Authoritative formula

```
// ESI = VehicleAction  this
// entity = *(this+0x44)
// param_2[0] = substep dt

delta = entity[+0x618] - this[+0x24]
if delta == 0:
    // no stage-1 write this tick
else:
    // open-band factor (asm default XMM1 = 1.0; set XMM1 = 2.0 when OR holds)
    factor = 1.0                                 // g_flOne @ 0xa0f2a0
    if (this[+0x24] < 0.0 && entity[+0x618] > -1.0) ||
       (this[+0x24] > 0.0 && entity[+0x618] < +1.0):
        factor = 2.0                             // DAT_00a10e74 @ 0xa10e74

    step = this[+0x20] * dt * factor
    if |delta| < step:
        step = |delta|

    if delta < 0:
        this[+0x24] -= step
        if this[+0x24] < -1.0: this[+0x24] = -1.0
    elif delta > 0:
        this[+0x24] += step
        if this[+0x24] > +1.0: this[+0x24] = +1.0

    wheelsDesc = *( *(this+0x40) + 0x14 )
    wheelsDesc[+0x1c] = this[+0x24]
```

**Always runs** (not gated on movement mode `0x02`). Mode `0x02` only adds stage-2  
(`VA+0x28` ±0.05 + `setSteeringAngle`) — see `fn_00598650_steerRamp.md`.

### 3.3 Open-band factor (what “2.0” means)

| Condition on current `VA+0x24` and target `entity+0x618` | `factor` |
|----------------------------------------------------------|--------:|
| `VA+0x24 == 0` (or neither open-band arm true) | **1.0** |
| Current **strictly negative** and target **above −1** | **2.0** |
| Current **strictly positive** and target **below +1** | **2.0** |

Interpretation: while the command is **off zero** and the target is still **inside** the open  
interval of the clamp rails, the ramp steps **twice as fast**. At the rails / zero, factor is 1.0.

### 3.4 Effective rate (why “2.0/s” is wrong)

| Quantity | Value | Source |
|----------|------:|--------|
| `VA+0x20` | **≈ 2.857143** (`20/7`) | ctor only — `DAT_009d54e0` |
| `factor` | **1.0 or 2.0** | `g_flOne` / `DAT_00a10e74` |
| step units | **command units per second × dt** | `step = rate × dt × factor` |

```
max |d(VA+0x24)/dt|  ≈  2.857 × factor
                     ≈  2.857 /s   (factor 1)
                     ≈  5.714 /s   (factor 2)
```

So:

- **Not** “rate = 2.0/s” alone.
- **Not** “rate = `entity+0x614` × 2.0”.
- Full open-band slew is **`VA+0x20 × 2.0`** ≈ **5.71 units/s** toward `entity+0x618`.

### 3.5 `VA+0x20` is fixed after ctor

`VehicleAction_ctor` @ `0x597f90` (decompile uses int-index on `undefined4*`; `param_1[8]` = byte **`+0x20`**):

```
param_1[8] = DAT_009d54e0;   // +0x20  ≈ 2.857143
param_1[9] = 0;              // +0x24  steer stage-1
param_1[10] = 0;             // +0x28  steer stage-2
```

| Check | Result |
|-------|--------|
| `get_xrefs_to(0x9d54e0)` | **sole** code xref: **`VehicleAction_ctor` READ** |
| `applyAction` | only **reads** `this+0x20` (asm `0x598921`) |
| PushDrive / createVehicleAction | write **driverInput**, not this field |

Treat `VA+0x20` as a **constant ramp-rate** for the life of the action object.

---

## 4. Constants (`read_memory`)

| Address | Bytes (LE) | float32 | Role in this note |
|---------|------------|--------:|-------------------|
| `0x00a10e74` | `00 00 00 40` | **2.0** | Stage-1 **open-band factor** (and shared elsewhere e.g. rear mod ×2 in torque) |
| `0x009d54e0` | `6e db 36 40` | **≈2.857143** | ctor seed for **`VA+0x20`** rate base |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** | default factor / clamp max (`g_flOne`) |
| `0x00aaa668` | `00 00 80 bf` | **−1.0** | clamp min for `VA+0x24` |
| `0x00a0f734` | `66 66 66 3f` | **0.9** | PushDrive thr ceiling (driverInput path only) |

---

## 5. Pipeline diagram

```
                    entity+0x614  (throttle axis)
                           │
                           │  PushDrive 0x4fbc10
                           ▼
                    driverInput+0x20     ──►  (Havok / drive path; NOT applyAction stage-1)
                           │
                           ✕  does NOT write VA+0x20
                           ✕  does NOT enter stage-1 ramp

                    entity+0x618  (steer axis)
                           │
                           │  applyAction stage-1  @ 0x5988a3
                           ▼
              VA+0x24  ← ramp( target= +0x618,
                               rate = VA+0x20 * dt * {1|2} )
                           │
                           ▼
                    wheelsDesc+0x1c
                           │
              mode 0x02: stage-2 VA+0x28 ±0.05 → setSteeringAngle  (see steerRamp note)

    VA+0x20  ── seeded once ──  DAT_009d54e0 ≈ 2.857
    factor 2 ── DAT_00a10e74 ── only when open-band OR true
```

---

## 6. Misread registry (this topic)

| Source | Claim | Verdict |
|--------|-------|---------|
| Ghidra plate on `0x598650` | “Throttle ramps toward target at rate DAT_00a10e74 (=2.0/sec)” | **FALSE** — target is **steer** `+0x618`; rate is **`VA+0x20 × factor`** |
| Plate / old layout | `this+0x24 = current brake` | **FALSE** — stage-1 **steer** float |
| Plate / old layout | `this+0x20 = current throttle` (live) | **FALSE for thr** — fixed **rate** ≈2.857 |
| `docs/NPCDriving.md` §6.1 | “VA+0x24 ramps toward entity+0x618 at rate entity+0x20 × dt × sign (rate base 2.0/s)” | **Partially wrong** — target yes; **rate base is VA+0x20 not entity thr**; 2.0 is **factor**, not sole rate |
| `0.7-transmission.md` / brake-spec tables | “2.0 = throttle ramp rate” | **Incomplete / misnamed** — stage-1 **steer** open-band factor; also reused for rear driver-mod ×2 in torque |
| “entity+0x614 path uses DAT_00a10e74” | thr ramp by 2.0 | **FALSE** — `+0x614` path is PushDrive; **2.0** is on **`+0x618` stage-1** |

Aligned verified notes: `fn_00598650_steerRamp.md`, `fn_offsets_vehicleAction.md`,  
`fn_004fbc10_pushDriveAxes.md`, `fn_00598650_applyAction.md` §5 / corrections table.

---

## 7. Port implications (behavior only — no C#)

1. **Do not** implement a Havok-side ramp of the longitudinal axis from `entity+0x614` inside the  
   applyAction equivalent.
2. **Do** copy thr through a PushDrive-like bridge onto the **driver input** object (with hard-stop /  
   0.9 / speed-cap gates as needed).
3. **Do** implement stage-1 as:  
   `steerCmd ← approach(steerCmd, entity.steerAxis, rate=R0*dt*factor)` with  
   `R0 ≈ 2.857`, `factor ∈ {1,2}` per §3.3, clamp ±1, publish to wheels desired-steer.
4. Keep **thr** and **steer** on separate entity slots (`+0x614` / `+0x618`); do not feed thr into the  
   stage-1 target.

---

## 8. Verification checklist

- [x] Decompile `0x598650` — stage-1 uses **`entity+0x618`**, factor **2.0**, rate **`VA+0x20 * dt`**
- [x] Asm walk `0x5988a9` (`+0x618`), `0x5988be` (`a10e74`), `0x598921` (`VA+0x20`)
- [x] Full-fn scan: **no** `0x614` in applyAction
- [x] Decompile `0x4fbc10` — `entity+0x614` → `driverInput+0x20` only
- [x] Decompile `0x597f90` — `VA+0x20 = DAT_009d54e0`
- [x] `read_memory` `0xa10e74` → **2.0**
- [x] `read_memory` `0x9d54e0` → **≈2.857143**
- [x] `read_memory` `0xa0f2a0` / `0xaaa668` → **±1.0**
- [x] `get_xrefs_to(0x9d54e0)` → ctor only
- [x] No C# in this pass
