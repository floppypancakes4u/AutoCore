# `0x00a15894` — world gravity Y (`−9.81`)

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x00a15894` (`DAT_00a15894`) |
| Tool | Ghidra MCP `read_memory` length **4** |
| Endian | little-endian |
| Role | Plate **float32 gravity** (Y-down magnitude in Y-up world) |
| Related | Pool base `0x00a15880`; live twin `DAT_009cb240` @ `0x009cb240` |

No C# in this file (RE evidence only).

---

## Raw dump

| Field | Value |
|---|---|
| `read_memory` address | `0xa15894` |
| `read_memory` length | **4** |
| Raw bytes (LE) | `c3 f5 1c c1` |
| Hex (packed) | `c3f51cc1` |
| Bit pattern (BE dword) | `0xC11CF5C3` |
| float32 | **−9.8100004196167** (IEEE binary32 encoding of **−9.81**) |

```
read_memory(0xa15894, length=4)
→ address=00a15894  data=[195, 245, 28, 193]  hex=c3f51cc1
```

---

## Neighborhood — `0xa15880` float pool

`0xa15894` is **slot +0x14** of a shared plate float table starting at `0xa15880`
(near the `VOG_DEBUG_STOP` string @ `0xa15844`).

| Address | Offset | Raw LE | float32 | Label |
|---|---:|---|---:|---|
| `0xa15880` | +0x00 | `89 88 88 3c` | **≈1/60** (0.01666667) | dt-ish / LOD period |
| `0xa15884` | +0x04 | `89 88 08 3d` | **≈1/30** (0.03333334) | dt-ish / LOD period |
| `0xa15888` | +0x08 | `89 88 88 3d` | **≈1/15** (0.06666667) | dt-ish / LOD period |
| `0xa1588c` | +0x0c | `cd cc cc 3d` | **0.1** | 10 Hz / clamp sibling |
| `0xa15890` | +0x10 | `da 0f 49 40` | **π ≈ 3.141593** | math constant |
| **`0xa15894`** | **+0x14** | **`c3 f5 1c c1`** | **−9.81** | **gravity** |
| `0xa15898` | +0x18 | `00 00 c8 42` | **100.0** | misc scale |
| `0xa1589c` | +0x1c | `00 00 80 40` | **4.0** | misc scale |

`read_memory(0xa15880, length=32)` →  
`8988883c8988083d8988883dcdcccc3dda0f4940c3f51cc10000c84200008040`

**Gotcha:** this pool is **not** the vehicle sub-step `dt` table. Sub-stepping uses
`0x009cc798` (≈30 Hz) and `0x00a0f730` (frame clamp 0.1). See `0.1-step-rate.md`.

`analyze_data_region(0xa15880)`: classification `PRIMITIVE`, **xref_count = 0** for the whole
scanned span — Ghidra has **no** code/data references to any slot of this pool, including
`0xa15894`.

---

## Cross-references to `0xa15894`

| Query | Result |
|---|---|
| `get_xrefs_to(0xa15894)` | **No references found** |
| `get_bulk_xrefs` on pool slots `0xa15880`…`0xa1589c` | all empty |
| `analyze_data_region` xref map | empty |

**Interpretation:** the plate at `0xa15894` holds the **canonical −9.81 bit pattern** in a shared
constant pool, but the vehicle/world init paths that actually **load** gravity do **not**
reference this address. They use a **twin** data symbol or an **immediate** with the same IEEE bits.

---

## Identical IEEE bits elsewhere (vehicle gravity path)

`search_byte_patterns("c3 f5 1c c1")` hits **three** sites:

| Address | Kind | Vehicle-related role |
|---|---|---|
| **`0x00a15894`** | **.rdata float** (this constant) | Pool gravity slot; **no** Ghidra xrefs |
| **`0x009cb240`** | **.rdata float** `DAT_009cb240` | **Live** world-gravity Y loaded by setup |
| **`0x009320b4`** | **code stream** (not data) | Immediate `push 0xC11CF5C3` inside `InitPhysics` |

### 1. Live twin — `DAT_009cb240` @ `0x009cb240`

| Field | Value |
|---|---|
| `read_memory` | same `c3 f5 1c c1` → **−9.81** |
| `get_xrefs_to` | **From `004b4ee6` in `FUN_004b4eb0` [READ]** |

`FUN_004b4eb0` (body `0x4b4eb0`–`0x4b50e6`) builds a gravity **vec3** on the stack and hands it to
a physics factory vtable:

```
local_74 = 0;                 // g.x
local_70 = DAT_009cb240;      // g.y = −9.81
local_6c = 0;                 // g.z
// …
piVar1 = (**(*param_1 + 0x10))(&local_74);   // create / configure with &g
```

Caller: **`FUN_004d4890`** → `FUN_004b4eb0()` during sector/world physics bring-up.

Neighbor double at `0x009cb238` (`_DAT_009cb238`) is also read in the same function (`floor` →
timestep-related setup with imm `0x3d088889` ≈ 1/30); gravity Y sits immediately after it.

### 2. InitPhysics immediate — `FUN_00932060` @ `0x00932060`

Log strings: `"@@inside InitPhysics"`, `"start initPhysics"`.

Decompile (args to physics init vcall):

```
// push order reconstructs gravity = (0, −9.81, 0)
uStack_27c = 0;            // x
uStack_278 = 0xc11cf5c3;   // y  ← same bits as 0xa15894, as IMMEDIATE
uStack_274 = 0;            // z
(**(code **)**(unaff_ESI + 0xe04))();
```

Bytes at `0x9320b3`: `68 c3 f5 1c c1` = `push 0xC11CF5C3` (pattern hit at `0x9320b4` is the
imm32 payload, not a standalone float object).

Same function also reads **`DAT_00a15868`** (pool neighborhood: float **450** used thrice for a
material/terrain-related triple) — same `0xa158xx` plate region as gravity, different slot.

### 3. Console / docs — magnitude confirmation

| String addr | Text |
|---|---|
| `0x00a2a0d0` | `setgravity` |
| `0x00a2a0e0` | `Sets the strength of gravity, in -(m/s^2), where 9.81 is default` |

Registered in command table `FUN_00959230`. Help text locks the **default magnitude** to **9.81**
(sign convention: strength as **−(m/s²)** ⇒ acceleration Y = **−9.81** in Y-up).

---

## Vehicle sim relationship

| Mechanism | Constant / source | Notes |
|---|---|---|
| **World gravity** (all dynamic bodies, chassis included) | `−9.81` on **Y** → vector **`(0, −9.81, 0)`** | Set at physics world init (`InitPhysics` / `FUN_004b4eb0`); not re-read from `0xa15894` each step |
| **World up** | `(0, 1, 0)` @ `0x00af3390` | Y-up; gravity is **negative Y** |
| **Aerodynamics ExtraGravity** | VehSpec `+0x5ac/+0x5b0/+0x5b4` → aero component `+0x40/+0x44/+0x48` | **Additional** world-space acceleration · mass in `hkDefaultAerodynamics::update` @ `0x64dae0` — **not** this plate |
| **Vehicle sub-step dt** | `0x9cc798` / frame clamp | **Unrelated** to `0xa15894` despite shared pool with 1/60, 1/30, … |

Chassis rigid bodies therefore fall under the **world** gravity vector. Vehicle framework forces
(aero drag/lift, ExtraGravity, suspension, friction) are applied **on top** of that world gravity
inside the Havok island step / `postTickApplyForces` chain.

---

## Porting notes (evidence only)

1. Default world gravity for the server vehicle stack: **`g = (0, −9.81, 0)`** (Y-up).
2. Prefer the exact IEEE32 plate: **`0xC11CF5C3`** (C# `−9.81f` matches this bit pattern).
3. Do **not** treat `0xa15880` pool entries as sim `dt` solely because 1/60 and 1/30 appear next to gravity.
4. `0xa15894` itself is a **dead / unreferenced plate** under Ghidra xrefs; authoritative **runtime** loads are `DAT_009cb240` and the `InitPhysics` immediate — same value.
5. Vehicle **ExtraGravity** (aero) is orthogonal; leave it at descriptor zeros unless clonebase supplies non-zero XYZ.

---

## RE checklist

| Step | Result |
|---|---|
| `read_memory` `0xa15894` len=4 | `c3 f5 1c c1` → **−9.81** f32 |
| `read_memory` `0xa15880` len=32 | pool `{1/60, 1/30, 1/15, 0.1, π, −9.81, 100, 4}` |
| `get_xrefs_to` `0xa15894` | **none** |
| `search_byte_patterns` `c3 f5 1c c1` | `0x9320b4` (imm), `0x9cb240` (data), `0xa15894` (data) |
| `get_xrefs_to` `0x9cb240` | `FUN_004b4eb0` @ `0x4b4ee6` → world gravity Y |
| Decompile `FUN_00932060` | `InitPhysics` pushes `(0, 0xC11CF5C3, 0)` |
| Axis | Y-up; gravity **−Y** |

## See also

- `batch_B.md` — summary row for this address among batch-B plates  
- `0.1-step-rate.md` — clarifies pool is **not** sim dt  
- `0.6-aerodynamics.md` / `verified/fn_0064dae0_aero.md` — ExtraGravity (separate)  
- `verified/fn_0064bc70_postTick.md` — aero/extra-gravity force application order  
- World-up plate `0x00af3390` in `batch_B.md`
