# Engine Torque — Bit-Exact Port Spec

Reverse-engineered from `autoassault.exe` (Ghidra project `AA-decode`, image base `0x400000`),
read-only. Covers:

- `VehicleEngine::torqueCurve2D` @ `0x4a9750` (the 2D byte-indexed torque LUT) — **fully specified, bit-exact**.
- `VehicleAction::calcWheelTorque` @ `0x598040` (per-wheel drive-torque assembly) — combination formula, one gap noted.

Source: `docs/NPCDriving.md`. This document verifies and corrects that summary.

---

## 1. Verified constants (read from memory, little-endian float32)

| Symbol | Address | Bytes | Value | Role |
|---|---|---|---|---|
| `DAT_00a0f298` | `0x00a0f298` | `00 00 00 3f` | **0.5** | RPM-binning base factor; also rear handbrake torque ×0.5 |
| `DAT_00a0f2a0` / `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | engine-disabled return; neutral driver-mod |
| `DAT_00a0f698` | `0x00a0f698` | `cd cc 4c 3f` | **0.8** | upright dot threshold |
| `DAT_00aaa7a4` | `0x00aaa7a4` | `00 00 70 41` | **15.0** | low-speed traction-boost cutoff (units/s) |
| `DAT_00a0f70c` | `0x00a0f70c` | `cd cc 4c 3e` | **0.2** | low-speed boost slope |
| `DAT_00a0f520` | `0x00a0f520` | `00 00 7a 44` | **1000.0** | torque clamp max |
| `DAT_00a10e74` | `0x00a10e74` | `00 00 00 40` | **2.0** | rear-wheel driver-mod doubling (mislabeled `g_flLevelUpUiBase_Inferred` in decomp) |

All match `docs/NPCDriving.md`. The one correction: the `0x00a10e74` "level-up UI base" rename is a
Ghidra misnomer — it is the **rear-wheel ×2.0** factor in `calcWheelTorque`.

---

## 2. `VehicleEngine::torqueCurve2D` @ `0x4a9750`  (BIT-EXACT)

Signature (MSVC `__thiscall`): `float torqueCurve2D(Engine* this /*ECX*/, float rpm, float throttle)`.
Return is an x87 `float10` (long double) carrying a float32 value.

### Engine struct fields used

| Offset | Type | Meaning |
|---|---|---|
| `+0x0c` | `int8` | **enabled** flag |
| `+0x10` | `int32` | **rows** = number of RPM (X) bins |
| `+0x14` | `int32` | **cols** = number of throttle (Y) bins; also the row stride |
| `+0x18` | `float32` | **RPM range scale** (bin width) |
| `+0x344` | `float32[8]` | **factor table** — the 8 discrete torque-factor levels |
| `+0x3dc` | `int32` | **pointer** to the byte LUT (`rows*cols` bytes) |

### Exact algorithm (reproduce verbatim in C#)

```csharp
// all math in float32; casts truncate TOWARD ZERO (C (int) semantics), not floor.
if (engine.enabled == 0)                 // +0x0c
    return 1.0f;                         // DAT_00a0f2a0

float scale = engine.rangeScale;         // +0x18
float baseVal = scale * 0.5f;            // DAT_00a0f298
float inv     = 1.0f / scale;

int xbin = (int)((rpm      - baseVal) * inv);   // truncate toward zero
int ybin = (int)((throttle - baseVal) * inv);

if (xbin >= 0 && xbin < engine.rows &&          // +0x10
    ybin >= 0 && ybin < engine.cols)            // +0x14
{
    byte b = (byte)(lut[engine.cols * xbin + ybin] & 7);   // row-major, row=xbin
    // NOTE: original has an unreachable assert `if (b > 7) DEBUG_STOP; return 1.0`
    //       — b is already &7 so it can never fire. Do not port it.
    return engine.factors[b];             // +0x344 + b*4
}
return engine.factors[0];                 // out-of-range default = factors[0]
```

### Critical, easy-to-miss details

1. **8-level quantization**: the raw LUT byte is masked `& 7`, so only the low 3 bits select
   one of `factors[0..7]`. The upper 5 bits of each table byte are ignored.
2. **Truncation toward zero, NOT floor.** Because `(int)` truncates toward zero, an RPM slightly
   *below* `baseVal` still yields `xbin = 0` (in range), not a negative index. The low-side
   out-of-range branch only triggers when `(value - baseVal)*inv <= -1.0`, i.e.
   `value <= baseVal - scale = -scale*0.5`. In practice the low guard almost never fires;
   the **high** guard (`bin >= rows/cols`) is the meaningful one.
3. **Row stride is `cols` (`+0x14`), addressing is row-major `[xbin][ybin]`.** `rows` (`+0x10`) is
   used only for the X range check.
4. **Two different fallbacks:** engine *disabled* → **1.0**; index *out-of-range* → **`factors[0]`**.
   These are not the same value.
5. `baseVal` and `inv` both derive from the *same* `+0x18` scale, so a bin is exactly `scale` wide
   and the grid origin sits at `scale*0.5`.

### Golden vectors (hand-derived; emulation impractical — see §4)

Config used for all rows below:
`enabled=1, rows=4, cols=4, scale=100.0` → `baseVal=50.0, inv=0.01`.
`factors = [0.10, 0.20, 0.40, 0.60, 1.00, 1.10, 1.20, 1.60]`.
LUT (row-major, values are the stored bytes = effective index since all <8):
```
        y0 y1 y2 y3
row x0:  0  1  2  3
row x1:  4  5  6  7
row x2:  0  1  2  3
row x3:  4  5  6  7
```

| # | rpm | throttle | xbin | ybin | linIdx=4*x+y | byte&7 | **return** | notes |
|---|----|----|----|----|----|----|----|----|
| 1 | 240 | 350 | 1 | 3 | 7 | 7 | **1.60** | mid-high |
| 2 | 50 | 50 | 0 | 0 | 0 | 0 | **0.10** | grid origin |
| 3 | 250 | 250 | 2 | 2 | 10 | 2 | **0.40** | |
| 4 | 30 | 350 | 0 | 3 | 3 | 3 | **0.60** | rpm<base still xbin=0 (trunc-to-zero) |
| 5 | 600 | 350 | 5 | 3 | — | — | **0.10** | xbin≥rows → out-of-range → factors[0] |
| 6 | any | any | — | — | — | — | **1.00** | engine.enabled==0 → disabled return |

Worked check for #1: `xbin=(int)((240-50)*0.01)=(int)1.9=1`; `ybin=(int)((350-50)*0.01)=(int)3.0=3`;
`lut[4*1+3]=lut[7]=7`; `7&7=7`; `factors[7]=1.60`. ✓

---

## 3. `VehicleAction::calcWheelTorque` @ `0x598040`  (per-wheel drive torque)

Loops wheels `i = 0 .. wheelCount-1` (count from `FUN_004f5560`; wheel stride `0xc0`). Per wheel,
gated by entity anim/power flag `+0xe4f8 != 0`:

```
if (!wheel.inContact /*+0x80*/) { wheelEngineTorque[i] = 0; continue; }

t = torqueCurve2D(engine, wheel.rpm /*+0x20*/, wheel.throttle /*+0x28*/);

// ---- driver modifier m (entity..vtbl+0x214 -> +0x118 float, default 0) ----
if (m > 0)        t = 1 - (1 - m) * (1 - t);          // blend toward 1
else if (m < 0) {
    float mm = m;
    if (isRear(i)) mm = m * 2.0;                       // DAT_00a10e74; rear doubles the (negative) mod
    t = (mm + 1.0) * t;                                // scale down
}
// m == 0: t unchanged

// ---- upright falloff ----
float upright = 1.0;
if (abs(dot(bodyUp, worldUp)) < 0.8 /*DAT_00a0f698*/)  // worldUp = DAT_00af3390/94/98
    upright = pow(...);                                // see GAP below

// ---- friction & low-speed traction boost ----
float mu = FUN_004f5550(i);                            // per-wheel friction (same table as wheels desc)
float v  = length(chassisVel);                         // sqrt of vec3 at (bodyTM+0x3c)+0x40/44/48
if (v < 15.0 /*DAT_00aaa7a4*/)
    mu *= (15.0 - v) * 0.2 /*DAT_00a0f70c*/ + 1.0;

// ---- combine ----
float torque = mu * upright * t;

// ---- handbrake rear traction cut ----
if (entity.flag_0x61c != 0 && isRear(i))
    torque *= 0.5;                                     // DAT_00a0f298

torque = clamp(torque, 0.0, 1000.0);                  // DAT_00a0f520
wheelEngineTorque[i] = torque;                        // (chassis+0x40..+0xc)+0x28 [i]
```

`this+0x2c` = "all wheels airborne" flag: set to 1 only if no wheel was in contact this tick.

`isRear(i)`: `i > *(byte*)(axleDesc + 0x4cc)` — a per-vehicle rear-axle boundary index. The **same**
test drives both the `m<0` rear ×2.0 and the handbrake ×0.5.

### Corrections vs prior notes
- The `×0.5` under `+0x61c` is a **rear traction cut under a state flag** (handbrake/burnout suspect),
  **not** `RearWheelFrictionScalar` (which lives at `vehicleData+0x740` and scales the wheels-descriptor
  friction table at setup — a different code path).
- Output feeds `hkDefaultWheels+0x28[i]`, aggregated per axle by
  `hkVehicleFramework_postTickApplyForces` (`0x64bc70`) as the drive impulse into the friction solver
  (`0x6c4450`). There is **no** `hkDefaultEngine` — this is AA's engine replacement.

### GAP (not in these two functions)
- **`upright = pow(...)`**: the `_CIpow` base and exponent arrive via the x87 stack and are not visible
  in this decompilation. Trace the instructions immediately preceding `_CIpow` @ `0x598040` to recover
  them before claiming bit-exactness of the upright falloff. Structure is known (only active when
  `|dot| < 0.8`); the exact curve is not.

---

## 4. Emulation feasibility (Task 2)

Attempted `emulate_function` @ `0x4a9750` with `ECX`=engine struct, floats on the stack, and an
indirect LUT region. Result: the function **executed** and returned via x87
`ST0 = 0x3fff8000000000000000 = 1.0`, i.e. the **engine-disabled** path — the `+0x0c` enabled byte
read back as 0 because the multi-region struct + indirect-table memory image did not populate
reliably, and the `float10` return rides the x87 stack rather than a GPR.

**Conclusion: emulation is impractical for golden table-lookup vectors** (pointer-heavy `__thiscall`
struct + indirect LUT pointer + x87 `float10` return). It *did* positively confirm the disabled path
returns exactly `1.0`. The §2 golden vectors are therefore hand-derived from the verified assembly and
are authoritative.

---

## 5. Mapping to `VehicleSpecific` engine fields (port guidance)

The §2 algorithm is bit-exact for the **runtime** lookup. The **population** of `factors[8]` (`+0x344`),
the byte LUT (`+0x3dc`), `rows/cols`, and `rangeScale` (`+0x18`) happens in a separate engine-setup
function (not `0x4a9750`/`0x598040`); locating that writer is required for bit-exact *table values*.
Based on the LUT shape and Havok `hkVehicleDefaultEngine` lineage, the intended mapping is:

| Struct field | VehicleSpecific source (inferred) | Notes |
|---|---|---|
| `+0x18` rangeScale (bin width) | derived from RPM breakpoints `MinimumRPM`, `OptimumRPMMin/Max`, `MaximumRPMMax` | grid spans the RPM band; `baseVal = scale*0.5` |
| `+0x10`/`+0x14` rows/cols | table dimensions from the tuning asset | X = RPM bins, Y = throttle bins |
| `factors[0..7]` `+0x344` | interpolants between `MinTorqueFactor` and `MaxTorqueFactor` | 8 discrete levels; index 0 = out-of-range default |
| byte LUT `+0x3dc` | authored 2D curve (per-cell factor level, `&7`) | selects which of the 8 factors per (rpm,throttle) cell |

**Recommended C# port:** implement §2 verbatim (guarantees identical behavior once the table is
loaded), and load `factors[8]` + the byte LUT + `rows`/`cols`/`rangeScale` directly from the engine
tuning data. A smooth piecewise-linear LERP across the 4 RPM breakpoints × 2 torque factors
approximates the curve *shape* but is **not** bit-exact against the 8-level quantized LUT — use the
LUT if bit-exactness matters.

### Follow-up to close bit-exactness
1. Trace `_CIpow` operands in `calcWheelTorque` for the upright falloff (§3 GAP).
2. Find the engine-setup writer to `+0x344`/`+0x3dc`/`+0x18` (xrefs to those offsets / to the engine
   ctor) to pin the exact `factors[8]` and LUT bytes from `VehicleSpecific`.
