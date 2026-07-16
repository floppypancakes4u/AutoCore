# Physics constants — batch B

| Field | Value |
|---|---|
| Program | `autoassault.exe` |
| Tool | Ghidra MCP `read_memory` |
| Endian | little-endian |
| Scope | raw plate constants used by vehicle sim / drive controller |

No C# in this file (RE evidence only).

---

## Summary table

| Address | Length | Raw LE hex | Interpreted | Likely role |
|---|---:|---|---|---|
| `0x009cc798` | 4 | `ff ff ef 41` | **29.999998** f32 (`0x41EFFFFF`) | sub-step frequency cap ≈30 Hz |
| `0x00a0d2f4` | 4 | `00 00 00 34` | **1.1920929e-7** f32 (`2^-23`) | `1/x` denominator epsilon |
| `0x009cd238` | 8 | `9a 99 99 99 99 99 d9 bf` | **-0.4** f64 | reverse gate on forward-alignment |
| `0x00af3390` | 12 | `00 00 00 00` / `00 00 80 3f` / `00 00 00 00` | **(0, 1, 0)** f32×3 | world-up vector (Y-up) |
| `0x00aaab14` | 4 | `89 88 08 3d` | **0.033333335** f32 (`≈1/30`) | near-target ease scale |
| `0x009d54a8` | 4 | `00 00 00 34` | **1.1920929e-7** f32 (`2^-23`) | airStab “is moving” speed epsilon |
| `0x00a0f2a0` | 4 | `00 00 80 3f` | **1.0** f32 | `g_flOne` (clamp max / unit scale) |
| `0x00aaa7a4` | 4 | `00 00 70 41` | **15.0** f32 | low-speed / sharp-speed gate |
| `0x00a15894` | 4 | `c3 f5 1c c1` | **-9.81** f32 | gravity (LOD/update pool; not sim dt) |
| `0x00af4618` | 4 | `00 00 20 41` | **10.0** f32 | default ang-vel / spin damping scale |
| `0x00af4614` | 4 | `cd cc 4c 3e` | **0.2** f32 | low-speed grip multiplier slope |

---

## Per-address dumps

### `0x009cc798` — sub-step frequency cap (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `ff ff ef 41` |
| Bit pattern (BE dword) | `0x41EFFFFF` |
| float32 | **29.999998092651367** |
| Notes | Used as `N = floor(frameDt * this) + 1` in StepTo sub-stepping. Practical port: treat as **30**. See `0.1-step-rate.md`. |

### `0x00a0d2f4` — friction / impulse denominator epsilon (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 00 34` |
| Bit pattern | `0x34000000` |
| float32 | **1.1920928955078125e-7** (`2^-23`) |
| Notes | Guard in forms like `1.0 / (x + eps)`. Same bit pattern as `0x9d54a8`. Do not confuse with loop counters that decompile as denormal floats. See `0.3-friction-solver.md`. |

### `0x009cd238` — reverse gate (float64 / double)

| Field | Value |
|---|---|
| `read_memory` length | 8 |
| Raw bytes (LE) | `9a 99 99 99 99 99 d9 bf` |
| float64 | **-0.4** exactly (IEEE binary64 for −2/5) |
| Notes | Drive-controller reverse gate on forward-alignment. Symbol `_DAT_009cd238`. See `drive-controller-spec.md`. |

### `0x00af3390` — world up (3 × float32 = 12 bytes)

| Field | Value |
|---|---|
| `read_memory` length | 12 |
| Bytes `+0x0` | `00 00 00 00` → **0.0** (X) |
| Bytes `+0x4` | `00 00 80 3f` → **1.0** (Y) |
| Bytes `+0x8` | `00 00 00 00` → **0.0** (Z) |
| Vector | **`(0, 1, 0)`** |
| Notes | World-up is **Y-up** in this plate. Used for upright / body-up dots (e.g. air-stab, chassis orientation). Neighbor words may hold related thresholds; this triple is the pure up axis. |

### `0x00aaab14` — near-target ease scale (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `89 88 08 3d` |
| Bit pattern | `0x3D088889` |
| float32 | **0.03333333507180214** (`≈ 1/30`) |
| Notes | `_DAT_00aaab14`. Tight-corner / near-target throttle ease scale in NPC / drive controller paths. See `drive-controller-spec.md`, `NPCDriving.md`. |

### `0x009d54a8` — airStab moving epsilon (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 00 34` |
| Bit pattern | `0x34000000` |
| float32 | **1.1920928955078125e-7** (`2^-23`) |
| Notes | `DAT_009d54a8`. “Is moving” speed epsilon for air-stab. Same raw value as friction eps at `0xa0d2f4` but a distinct plate symbol/site. See `avd-airstab-spec.md`. |

### `0x00a0f2a0` — one (`g_flOne`, float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 80 3f` |
| Bit pattern | `0x3F800000` |
| float32 | **1.0** |
| Notes | Shared unit constant: steer clamp max, torque-curve scale, many `1.0` numerators. See steering / torque-curve verified notes. |

### `0x00aaa7a4` — 15.0 speed gate (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 70 41` |
| Bit pattern | `0x41700000` |
| float32 | **15.0** |
| Notes | Low-speed traction-boost threshold / sharp speed gate (wheel collide + drive controller). See `0.5-wheel-collide.md`, `drive-controller-spec.md`. |

### `0x00a15894` — gravity (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `c3 f5 1c c1` |
| Bit pattern | `0xC11CF5C3` |
| float32 | **-9.8100004196167** (IEEE32 for −9.81) |
| Notes | Lives in the `0xa15880` LOD/update-rate float pool (`{1/60, 1/30, 1/15, 0.1, pi, −9.81, …}`). Confirmed **gravity magnitude −9.81**; **not** the vehicle sub-step dt. See `0.1-step-rate.md`. |

### `0x00af4618` — default 10.0 (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `00 00 20 41` |
| Bit pattern | `0x41200000` |
| float32 | **10.0** |
| Notes | Default mass/inertia descriptor scale (`desc+0x3c`); related to ang-vel / spin-damping defaults (`DAT_00a110d8 = 10.0` is a sibling default). See `0.2-mass-inertia.md`. |

### `0x00af4614` — 0.2 (float32)

| Field | Value |
|---|---|
| `read_memory` length | 4 |
| Raw bytes (LE) | `cd cc 4c 3e` |
| Bit pattern | `0x3E4CCCCD` |
| float32 | **0.20000000298023224** |
| Notes | Immediate neighbor of `0xaf4618` (dword below). Descriptor default / low-speed grip slope (`μ ×= (15−|v|)×0.2 + 1` style uses). See `0.2-mass-inertia.md`. |

---

## Cross-checks / gotchas

1. **`0x9cc798` vs “30”** — stored value is `0x41EFFFFF` ≈ 29.999998, not exact 30.0 (`0x41F00000`). Floor math still yields the intended 30 Hz cap.
2. **`0xa0d2f4` vs `0x9d54a8`** — identical raw eps (`2^-23`); different call sites (friction denom vs airStab speed).
3. **`0x9cd238` is double** — 8-byte read required; do not reinterpret as float32.
4. **World up is Y-up `(0,1,0)`** at `0xaf3390` — not Z-up.
5. **`0xa15894` gravity** is in a shared float table; do not treat the whole pool as sim dt.
6. **`0xaf4614` / `0xaf4618`** are adjacent dwords (`…14` = 0.2, `…18` = 10.0).

---

## RE checklist

| Step | Result |
|---|---|
| `read_memory` `0x9cc798` len=4 | `ff ff ef 41` → 29.999998 |
| `read_memory` `0xa0d2f4` len=4 | `00 00 00 34` → 1.192e-7 |
| `read_memory` `0x9cd238` len=8 | `9a…d9 bf` → −0.4 double |
| `read_memory` `0xaf3390` len=12 | `(0, 1, 0)` world up |
| `read_memory` `0xaaab14` len=4 | `89 88 08 3d` → ≈1/30 |
| `read_memory` `0x9d54a8` len=4 | `00 00 00 34` → 1.192e-7 |
| `read_memory` `0xa0f2a0` len=4 | `00 00 80 3f` → 1.0 |
| `read_memory` `0xaaa7a4` len=4 | `00 00 70 41` → 15.0 |
| `read_memory` `0xa15894` len=4 | `c3 f5 1c c1` → −9.81 |
| `read_memory` `0xaf4618` len=4 | `00 00 20 41` → 10.0 |
| `read_memory` `0xaf4614` len=4 | `cd cc 4c 3e` → 0.2 |
