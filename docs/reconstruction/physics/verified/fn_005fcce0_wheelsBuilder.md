# Verified: wheels descriptor builder `FUN_005fcce0` @ `0x5fcce0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x005fcce0` |
| Symbol | `FUN_005fcce0` (wheels desc builder) |
| Caller | `Vehicle_buildHavokVehicleFramework` @ `0x005fd390` |
| Downstream | `hkDefaultWheels_ctor` @ `0x0064fee0` → `FUN_005fbbb0` → `FUN_005fa9b0` (desc → object) |
| Object size | `0x390` (allocation before ctor) |
| Verified | Ghidra `decompile_function` + `read_memory` (re-gate) + Havok reflection member table |

---

## Role

Builds the **hkDefaultWheels descriptor** (`param_3`) that is immediately fed to
`hkDefaultWheels_ctor`. Per wheel it:

1. Grows eight growable float/uint arrays on the descriptor (via `FUN_005b3300`).
2. Fills each wheel slot from **VehicleSpecific**, a **wheelset friction table**, and fixed constants.
3. Applies **`fRearWheelFrictionScalar` (`VehSpec+0x740`)** to **rear** base μ only.

---

## Constants (`read_memory`, length 4)

| Symbol | Address | Raw LE bytes | float32 | Role in this fn |
|---|---|---|---|---|
| `DAT_00aaa7a4` | `0x00aaa7a4` | `00 00 70 41` | **15.0** | Written to `desc+0x58[i]` (later copied to each wheel struct `+0x84`) |
| `DAT_00aaa68c` | `0x00aaa68c` | `00 00 c0 3f` | **1.5** | `wheelsMaxFriction[i] = wheelsFriction[i] * 1.5` |
| `DAT_00a0f718` | `0x00a0f718` | `0a d7 23 3c` | **0.01** | Written to `desc+0x4c[i]` (`wheelsForceFeedbackMultiplier`) |
| `g_flMsToSeconds_Inferred` | `0x00a0f72c` | `6f 12 83 3a` | **0.001** | Written to `desc+0x34[i]` (`wheelsViscosityFriction`) |

(Not used as a scalar here, but related sentinel for `FUN_005a6f20` OOR: `DAT_00aaa668` = `-1.0`.)

---

## Access paths

### VehicleSpecific (`VehSpec`)

Standard clone-template chain used for most field reads:

```
VehSpec = *(*( *(param_1+4) + 4 ) + 0xac + param_1) + 0x3c
```

`firstRear` / axle-0 count is loaded once via the **entity+0x258** path (same structural type):

```
wsOrClone = *(param_1 + 0x258)           // 600 decimal
VehSpec'  = *(*( *(wsOrClone+4) + 4 ) + 0xac + wsOrClone) + 0x3c
firstRear = *(char*)(VehSpec' + 0x4cc)   // tinNumWheelsAxle0
```

### Wheel count + base μ (wheelset object, **not** VehSpec)

| Helper | Asm / decompile | Meaning |
|---|---|---|
| `FUN_004f5560` | `AL = *(*(ECX+0x258) + 0xb0)` | **wheelCount** (byte) |
| `FUN_004f5550(i)` | `ECX = *(ECX+0x258); JMP FUN_005a6f20` | **μ_base[i]** |
| `FUN_005a6f20(this,i)` | switch `i` 0..5 → `*(float*)(this + 0xb4 + i*4)` | per-wheel friction table; default `DAT_00aaa668` (−1.0) |

So μ_base lives on the object at `entity+0x258` offsets `+0xb4..+0xc8` (up to 6 wheels). This builder does **not** read a VehSpec friction array for μ0.

---

## Descriptor layout (`param_3`) — fill sources

Each growable array is `{ ptr @ off, count @ off+4, capacity @ off+8 }` (capacity high bit used as flag; grow path masks with `0x7fffffff`).

| Desc off (array ptr) | Havok reflection name (member table @ `0x9e47e8+`) | Source | Notes |
|---|---|---|---|
| `+0x04` | `wheelsAxle` (object lands at `+0x58` after `FUN_005fa9b0`) | `1` if `i < firstRear`, else `0` | **uint**, not float. Rear → axle **0**, front → axle **1** |
| `+0x10` | `wheelsRadius` | `*(float*)(VehSpec + 0x600 + i*4)` | per-wheel; **no** `+0x740` scale |
| `+0x1c` | `wheelsWidth` | `*(float*)(VehSpec + 0x618 + i*4)` | per-wheel; **no** `+0x740` scale |
| `+0x28` | `wheelsFriction` (μ0) | `FUN_004f5550(i)`; if rear `*= *(VehSpec+0x740)` | **only** rear-scalar site |
| `+0x34` | `wheelsViscosityFriction` | `g_flMsToSeconds_Inferred` (**0.001**) | constant all wheels |
| `+0x40` | `wheelsMaxFriction` (μmax) | `wheelsFriction[i] * DAT_00aaa68c` (**×1.5**) | after rear scale |
| `+0x4c` | `wheelsForceFeedbackMultiplier` | `DAT_00a0f718` (**0.01**) | constant all wheels |
| `+0x58` | (not the reflection `wheelsMass` slot) | `DAT_00aaa7a4` (**15.0**) | `FUN_005fa9b0` copies this array into **`wheel[i]+0x84`** (stride `0xC0`), not object `+0x58` |

`FUN_005fa9b0` copies `desc+0x10/1c/28/34/40/4c` arrays to the **same offsets** on the wheels object; remaps `desc+0x04` → object `+0x58` (axle indices) and `desc+0x58` → each wheel’s `+0x84`.

Caller also stamps `local_88[0] = 8` immediately after this builder (before ctor) — header field, not filled here.

---

## VehicleSpecific → wheels field map (this function only)

| VehSpec off | DB / known name | Dest | Transform |
|---|---|---|---|
| `+0x4cc` (byte) | `tinNumWheelsAxle0` | rear/front split | `isFront = (i < firstRear)`; rear when `i >= firstRear` |
| `+0x600[i]` (f32) | per-wheel radius table (feeds `wheelsRadius`) | `desc+0x10[i]` | copy |
| `+0x618[i]` (f32) | per-wheel width table (feeds `wheelsWidth`) | `desc+0x1c[i]` | copy |
| `+0x740` (f32) | **`fRearWheelFrictionScalar`** (SQL column @ string `0x00a93d40`) | scales **rear** `wheelsFriction` only | `μ0 *= scalar` when `i >= firstRear` |

**Not consumed by this builder:** gear ratios (`+0x6d0`), collide radius/width (`+0x6a8/+0x6ac`), torque-split (`+0x5e8/+0x5ec`), hardpoints, suspension, brakes, etc.

---

## Exact per-wheel algorithm (from decompile)

```
// After resizing all 8 arrays to wheelCount = FUN_004f5560():

firstRear = (char)*(VehSpec_via_ent+0x258 + 0x4cc)   // axle-0 wheel count

for (i = 0; i < wheelCount; i++) {
    desc+0x58[i] = 15.0;                               // DAT_00aaa7a4 → later wheel+0x84
    desc+0x10[i] = VehSpec[+0x600 + i*4];              // wheelsRadius
    desc+0x1c[i] = VehSpec[+0x618 + i*4];              // wheelsWidth

    isFront = (i < firstRear);                         // evaluated before μ fetch
    mu0 = FUN_004f5550(i);                             // wheelset +0xb4[i]
    desc+0x28[i] = mu0;                                // wheelsFriction

    if (!isFront) {                                    // i >= firstRear
        desc+0x28[i] *= VehSpec[+0x740];               // fRearWheelFrictionScalar
    }

    desc+0x40[i] = desc+0x28[i] * 1.5;                 // wheelsMaxFriction
    desc+0x4c[i] = 0.01;                               // wheelsForceFeedbackMultiplier
    desc+0x34[i] = 0.001;                              // wheelsViscosityFriction
    desc+0x04[i] = (uint)isFront;                      // wheelsAxle: front=1, rear=0
}
```

### Critical details

1. **`+0x740` multiplies μ0 / μmax only** — never `+0x600` (radius) or `+0x618` (width).
2. **Rear test is `i >= firstRear`** (`!(i < firstRear)`). Wheel index equal to `+0x4cc` is rear.
3. **μmax is derived after the rear scale**, so rear μmax also carries `× fRearWheelFrictionScalar`.
4. **Axle encoding is inverted vs “front=0” intuition:** rear → `0`, front → `1` (still yields two axles; max index + 1 = 2).
5. **Base μ is wheelset state** (`entity+0x258 + 0xb4[i]`), not a VehSpec float. Port must supply that table (or the object that `FUN_004f5550` reads) separately from `VehicleSpecific`.
6. **Constants are fixed** for viscosity (0.001), force-feedback mult (0.01), and the 15.0 per-wheel payload — not DB fields.

---

## Conflicts vs prior evidence

| Item | Prior note | This re-verify | Verdict |
|---|---|---|---|
| `+0x740` target | `0.5-wheel-collide.md`: scales solver rear μ0 | same | **match** |
| `+0x740` target | `setup-field-mapping.md`: multiplies `wheelsTorqueRatio` / `+0x600` | **false** — multiplies `wheelsFriction` only; `+0x600` is radius | **binary wins** |
| `+0x600` identity | setup map: “torque-ratio / gear-ratio reuse” | Havok reflection @ desc `+0x10` = **`wheelsRadius`** | **binary / reflection wins** |
| `+0x618` identity | setup map: “friction/second value” | reflection = **`wheelsWidth`** | **binary / reflection wins** |
| Rear boundary | some torque docs: `i > +0x4cc` | setup uses **`i >= +0x4cc`** | use `>=` for this builder |
| DB name for `+0x740` | `RearWheelFrictionScalar` | SQL string **`fRearWheelFrictionScalar`** | **match** (prefix `f`) |
| Constants 1.5 / 0.01 / 15.0 / 0.001 | `0.5-wheel-collide.md` (0.001 was “inferred”) | LE re-read; `g_flMsToSeconds` @ `0xa0f72c` = **0.001** | **match + pinned** |

**No C# change in this gate** — doc-only. Existing C# that multiplies rear **torque ratio** by `RearWheelFrictionScalar` is **not** what `FUN_005fcce0` does (friction table only).

---

## Related addresses (not re-verified here)

| Addr | Symbol / role |
|---|---|
| `0x005fd390` | `Vehicle_buildHavokVehicleFramework` — sole caller; allocates `0x390`, calls this then `hkDefaultWheels_ctor` |
| `0x0064fee0` | `hkDefaultWheels_ctor` |
| `0x005fa9b0` | desc → wheels object array copy / axle histogram |
| `0x004f5550` / `0x004f5560` / `0x005a6f20` | μ_base + wheelCount accessors |
| `0x009e48xx` | Havok reflection names for wheels* members |
| `0x00a93d40` | SQL selecting `fRearWheelFrictionScalar` from `tVehicle` |

---

## Emulation

Not practical: pointer-heavy growable arrays, entity/clone indirection, and wheelset `this` for `FUN_004f5550`. Goldens for a port should hand-derive from the algorithm above with known `wheelCount`, `firstRear`, `μ_base[]`, `radius[]`, `width[]`, and `fRearWheelFrictionScalar`.
