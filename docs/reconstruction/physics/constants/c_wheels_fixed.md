# Physics constants — wheels fixed plate (`FUN_005fcce0`)

| Field | Value |
|---|---|
| Program | `autoassault.exe` |
| Tool | Ghidra MCP `read_memory` + `decompile_function` |
| Endian | little-endian |
| Scope | Fixed (non-DB) scalars written by the wheels descriptor builder |
| Builder | `FUN_005fcce0` @ `0x005fcce0` → `hkDefaultWheels` desc |
| Verified | 2026-07-15 |

No C# in this file (RE evidence only).

---

## Summary table

| Address | Symbol | Length | Raw LE hex | float32 | Havok / runtime role |
|---|---|---|---:|---|---|---|
| `0x00aaa7a4` | `DAT_00aaa7a4` | 4 | `00 00 70 41` | **15.0** | Written to `desc+0x58[i]`; later `wheel[i]+0x84`. Also low-speed / sharp speed gate elsewhere |
| `0x00aaa68c` | `DAT_00aaa68c` | 4 | `00 00 c0 3f` | **1.5** | `wheelsMaxFriction[i] = wheelsFriction[i] × 1.5` |
| `0x00a0f718` | `DAT_00a0f718` | 4 | `0a d7 23 3c` | **0.01** | `wheelsForceFeedbackMultiplier` (`desc+0x4c[i]`) |
| `0x00a0f72c` | `g_flMsToSeconds_Inferred` | 4 | `6f 12 83 3a` | **0.001** | `wheelsViscosityFriction` (`desc+0x34[i]`); shared ms→s scale |

All four are **constants for every wheel** (no VehSpec / DB fan-out), except μmax which is **derived** from per-wheel μ0 after the rear friction scalar.

---

## Per-address dumps

### `0x00aaa7a4` — 15.0 (`DAT_00aaa7a4`)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 70 41` |
| Bit pattern (BE dword) | `0x41700000` |
| float32 | **15.0** |
| Wheels builder use | `desc+0x58[i] = DAT_00aaa7a4` for each wheel. `FUN_005fa9b0` copies this array into **`wheel[i]+0x84`** (stride `0xC0`). Reflection table does **not** label this desc slot as `wheelsMass` (see verified builder note). |
| Other call sites | Low-speed traction boost cutoff / sharp speed gate (`calcWheelTorque`, drive controller). Same plate value, multi-use. |

### `0x00aaa68c` — 1.5 (`DAT_00aaa68c`)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 c0 3f` |
| Bit pattern | `0x3FC00000` |
| float32 | **1.5** |
| Wheels builder use | After base μ is written (and rear wheels scaled by `VehSpec+0x740`): |
| | `desc+0x40[i] = desc+0x28[i] * DAT_00aaa68c` |
| | → Havok **`wheelsMaxFriction`** = μ0 × **1.5** |
| Notes | Multiplier only; not a standalone μ table entry. |

### `0x00a0f718` — 0.01 (`DAT_00a0f718`)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `0a d7 23 3c` |
| Bit pattern | `0x3C23D70A` |
| float32 | **0.01** |
| Wheels builder use | `desc+0x4c[i] = DAT_00a0f718` → **`wheelsForceFeedbackMultiplier`** |
| Other call sites | Steer deadband on `\|lateral\|`; near-zero velocity gate (`DAT_00a0f718` is multi-use). |

### `0x00a0f72c` — 0.001 (`g_flMsToSeconds_Inferred`)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `6f 12 83 3a` |
| Bit pattern | `0x3A83126F` |
| float32 | **0.001** exactly (IEEE32 for 1/1000) |
| Wheels builder use | `desc+0x34[i] = g_flMsToSeconds_Inferred` → **`wheelsViscosityFriction`** |
| Symbol | Ghidra label `g_flMsToSeconds_Inferred` @ `0x00a0f72c` (plate: ms→seconds for UI / timers; ~212 xrefs) |
| Other call sites | Client integrate dt: `ghostObj+0xBC * 0.001` (network pose soft path) |

---

## Builder algorithm (fixed fields only)

Source: `decompile_function` `0x5fcce0` program=`autoassault.exe`.

```
for each wheel i in 0 .. wheelCount-1:
    desc+0x58[i] = 15.0     // DAT_00aaa7a4  → later wheel+0x84
    // … radius / width / μ0 (DB + wheelset; not fixed) …
    if rear:  μ0 *= fRearWheelFrictionScalar   // VehSpec+0x740
    desc+0x40[i] = μ0 * 1.5 // DAT_00aaa68c  → wheelsMaxFriction
    desc+0x4c[i] = 0.01     // DAT_00a0f718  → wheelsForceFeedbackMultiplier
    desc+0x34[i] = 0.001    // g_flMsToSeconds @ 0xa0f72c → wheelsViscosityFriction
```

### Descriptor array ptr offsets (growable arrays on `param_3`)

| Desc off | Filled with | Source |
|---|---|---|
| `+0x34` | viscosity friction | **fixed** `0.001` |
| `+0x40` | max friction | **derived** μ0 × `1.5` |
| `+0x4c` | force-feedback mult | **fixed** `0.01` |
| `+0x58` | per-wheel payload → `wheel+0x84` | **fixed** `15.0` |

DB-driven arrays (`+0x10` radius, `+0x1c` width, `+0x28` friction, `+0x04` axle flag) are **not** plate constants — see `verified/fn_005fcce0_wheelsBuilder.md`.

---

## Cross-checks / gotchas

1. **`g_flMsToSeconds` address is `0x00a0f72c`**, not a free-floating symbol without a VA. Prior phase-0 text had it “inferred”; this re-read pins the plate.
2. **`DAT_00aaa7a4 = 15.0` is multi-use** — wheel-setup payload **and** speed gates (traction boost / sharp turn). Do not invent a separate 15.0 constant for each site.
3. **`DAT_00a0f718 = 0.01` is multi-use** — force-feedback mult in wheels desc **and** steer / velocity deadbands.
4. **μmax uses `DAT_00aaa68c` only as a scale**; rear wheels already have μ0 scaled by `fRearWheelFrictionScalar` before the ×1.5.
5. **Do not confuse** `0xa0f72c` (0.001) with neighbor `0xa0f730` (0.1) or `0xa0f718` (0.01).

---

## RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x5fcce0` | Writes `DAT_00aaa7a4`, `DAT_00aaa68c`, `DAT_00a0f718`, `g_flMsToSeconds_Inferred` |
| `read_memory` `0xaaa7a4` len=4 | `00 00 70 41` → **15.0** |
| `read_memory` `0xaaa68c` len=4 | `00 00 c0 3f` → **1.5** |
| `read_memory` `0xa0f718` len=4 | `0a d7 23 3c` → **0.01** |
| `read_memory` `0xa0f72c` len=4 | `6f 12 83 3a` → **0.001** |
| `list_globals` name=`g_flMsToSeconds` | Symbol @ `00a0f72c` |

---

## Related docs

| Doc | Relation |
|---|---|
| [`verified/fn_005fcce0_wheelsBuilder.md`](../verified/fn_005fcce0_wheelsBuilder.md) | Full builder algorithm + VehSpec map |
| [`0.5-wheel-collide.md`](../0.5-wheel-collide.md) | Friction-table setup context (pre-pin of 0.001) |
| [`setup-field-mapping.md`](../setup-field-mapping.md) | Framework build order (Wheels first) |
| [`batch_A.md`](batch_A.md) / [`batch_B.md`](batch_B.md) | Neighbor plate constants (`0xa0f7xx`, `0xaaa7a4` elsewhere) |
