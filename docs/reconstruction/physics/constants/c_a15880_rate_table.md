# Constant pool `0x00a15880` — period-like floats vs sim dt

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Tool | Ghidra MCP `read_memory` |
| Address | `0x00a15880` |
| Length | **32** bytes (8 × float32) |
| Section | `.rdata` |
| Endian | little-endian |

No C# in this file (RE evidence only).

---

## 1. Raw dump

```
read_memory address=0xa15880 length=32 program=autoassault.exe
hex: 89 88 88 3c  89 88 08 3d  89 88 88 3d  cd cc cc 3d
     da 0f 49 40  c3 f5 1c c1  00 00 c8 42  00 00 80 40
```

| Offset | VA | LE bytes | u32 | float32 | Common name |
|---:|---|---|---:|---:|---|
| +0x00 | `0x00a15880` | `89 88 88 3c` | `0x3C888889` | **0.0166666675** | ≈ **1/60** (60 Hz period) |
| +0x04 | `0x00a15884` | `89 88 08 3d` | `0x3D088889` | **0.0333333351** | ≈ **1/30** (30 Hz period) |
| +0x08 | `0x00a15888` | `89 88 88 3d` | `0x3D888889` | **0.0666666701** | ≈ **1/15** (15 Hz period) |
| +0x0C | `0x00a1588C` | `cd cc cc 3d` | `0x3DCCCCCD` | **0.1000000015** | **0.1** (10 Hz period / 10 fps) |
| +0x10 | `0x00a15890` | `da 0f 49 40` | `0x40490FDA` | **3.14159250** | **π** (float32) |
| +0x14 | `0x00a15894` | `c3 f5 1c c1` | `0xC11CF5C3` | **−9.81000042** | **−9.81** gravity |
| +0x18 | `0x00a15898` | `00 00 c8 42` | `0x42C80000` | **100.0** | general scalar |
| +0x1C | `0x00a1589C` | `00 00 80 40` | `0x40800000` | **4.0** | general scalar |

Neighbor context (not part of the 32-byte request, but frames the pool):

- `0x00a15870` — `g_abTfidInvalid_A15870` (16 bytes, skill/target-id invalid mask; **has** xrefs).
- After `0x00a158a0` — another `ff…` / zero block, then unrelated float litter (π copies, epsilons, etc.).

---

## 2. What this pool is (layout interpretation)

The first **four** dwords form a descending period ladder in seconds:

| Index | Period (s) | Implied rate |
|---:|---:|---|
| 0 | 1/60 | 60 Hz |
| 1 | 1/30 | 30 Hz |
| 2 | 1/15 | 15 Hz |
| 3 | 0.1 | 10 Hz |

That layout is why earlier phase notes call `0xa15880` an **LOD / update-rate** float pool.

The next four dwords are **shared math/physics scalars** co-located in `.rdata` (π, −9.81, 100, 4) — **not** additional rate tiers.

**Important xref finding:** Ghidra `get_xrefs_to` / PE absolute-address search found **no static code or data pointers** to `0xa15880`…`0xa1589c`. So this specific cluster is either:

1. Orphaned MSVC constant-pool residue (same bit patterns live elsewhere with real readers), or
2. Only ever reached via a base+index that analysis has not recovered.

Do **not** treat this VA as a live, indexed LOD API without a consumer.

### Same bit patterns elsewhere (with real readers)

| Value | Live VA (examples) | Role when referenced |
|---|---|---|
| 1/60 | `0x00aaa9ec` | Used as a float constant in several systems (e.g. setup path `FUN_004b4eb0` loads it into a local) |
| 1/30 | `0x00aaab14` | Drive / NPC near-target ease scale (`drive-controller-spec.md`) — **not** sim dt |
| 1/15 | `0x00aaac18` | Vector-component lookup (`FUN_0051f230` cases 4–9); **not** a rate table |
| 0.1 | `0x00a0f730` (`g_flMultiKillCountBlend`) | **Max frameDt clamp** for sub-stepping (see below) |
| −9.81 | `0x009cb240` | Gravity constant loaded by physics setup (`FUN_004b4eb0`) |

---

## 3. vs vehicle sim dt (authoritative)

**This pool is NOT the Havok vehicle sub-step dt.**

Sim dt is computed each frame in `CVOGSectorMap::StepTo` @ **`0x004d6c80`** (`decompile_function`):

```
// param_2 = frame delta (seconds)
if (g_flMultiKillCountBlend < param_2)   // DAT_00a0f730 = 0.1
    param_2 = g_flMultiKillCountBlend;

N = floor(param_2 * _DAT_009cc798) + 1;  // DAT_009cc798 = 29.999998 ≈ 30
// loop N times; each island step uses:
substep_related_accum += param_2 / N;
FUN_00561910();   // island sim → applyAction with that sub-step
```

Verified companion constants (`read_memory` length 4):

| VA | LE hex | float32 | Role in sim dt |
|---|---|---:|---|
| `0x009cc798` | `ff ff ef 41` | **29.999998** | sub-step frequency cap (≈30 Hz) |
| `0x00a0f730` | `cd cc cc 3d` | **0.1** | max frameDt (10 fps floor) |
| `0x00a0f2a0` | `00 00 80 3f` | **1.0** | `g_flOne` used as `+ 1` in `N` |

Behaviour summary (from `0.1-step-rate.md`):

| Client frame rate | frameDt | N | substep_dt |
|---|---:|---:|---:|
| 60 fps | ≈0.01667 | 1 | ≈1/60 |
| 30 fps | ≈0.03333 | 1 | ≈1/30 |
| <30 fps | … | ≥2 | frameDt/N ≤ 1/30 |

So **numeric coincidence** only: at 60 fps the live sub-step happens to equal the float stored at `0xa15880` (1/60). That does **not** mean StepTo indexes this table.

Call chain for real dt delivery:

```
CVOGSectorMap::StepTo        0x004d6c80
  └ FUN_00561910             island step ("LtSimulate"); dt stored island+0x150
      └ … → VehicleAction::applyAction  0x00598650   param_2[0] = substep_dt
```

---

## 4. Porting rules

| Do | Don't |
|---|---|
| Implement sim dt via `N = floor(frameDt·30)+1`, `substep_dt = frameDt/N`, `frameDt ≤ 0.1` | Index `0xa15880` as if it were the vehicle integrator rate table |
| Use **−9.81** from a named gravity constant (e.g. live site `0x9cb240` or verified pool slot `0xa15894`) when a formula needs gravity | Assume every dword in this pool is an LOD tier |
| Treat 1/60, 1/30, 1/15, 0.1 as **possible** distant-update periods only if a consumer is later proven | Confuse `0xaaab14` (1/30 ease scale) or `0xa0f730` (frameDt clamp) with “sim fixed dt” |

Server Phase 5 notes already call full-rate NPC vehicle sim the MVP and leave distance LOD as optional — consistent with **not** wiring this pool into the integrator.

---

## 5. Cross-references

| Doc | Relation |
|---|---|
| `docs/reconstruction/physics/0.1-step-rate.md` | Authoritative sim dt / sub-step formula |
| `docs/reconstruction/physics/constants/batch_B.md` | `0xa15894` = −9.81 note inside this pool |
| `docs/reconstruction/physics/PORTING_RULES.md` | Always re-`read_memory` before porting |

---

## 6. Verification log

| Step | Result |
|---|---|
| `read_memory` `0xa15880` len=32 | hex above; 8 float32 decoded |
| `read_memory` `0x9cc798` len=4 | `ff ff ef 41` → 29.999998 |
| `read_memory` `0xa0f730` len=4 | `cd cc cc 3d` → 0.1 |
| `decompile_function` `0x4d6c80` | StepTo uses `0xa0f730` + `0x9cc798`, not `0xa15880` |
| `get_xrefs_to` / PE search for VAs `0xa15880`…`0xa1589c` | **0** static absolute references |
| PE section map | file off `0x614a80` → `.rdata` VA `0xa15880` |
