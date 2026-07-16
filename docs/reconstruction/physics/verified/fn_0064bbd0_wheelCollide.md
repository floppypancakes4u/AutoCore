# Verified: `hkVehicleWheelCollide::collide` @ `0x64bbd0` (+ cast path `TtPhantom::castRay` @ `0x580ed0`)

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Primary | `0x0064bbd0` — framework vtbl `+0x20` wheel-collide entry |
| Cast | `0x00580ed0` — `TtPhantom::castRay` (profiler string `@ 0x9d4574`) |
| Caller | `hkVehicleFramework_preUpdate` @ `0x64cf20` (builds ray, reads result, writes compression) |
| Convention | MSVC `__thiscall` — framework / phantom in `ECX` |
| RE tools | Ghidra `batch_decompile` on `0x64bbd0`, `0x580ed0`, `0x64cf20`, `0x5d6ae0`; `read_memory` on string + constants |
| Status | **Verified** (re-read) |

Related phase map: `docs/reconstruction/physics/0.5-wheel-collide.md`.

---

## 1. Call chain

```
hkVehicleFramework_preUpdate (0x64cf20)
  │  per wheel i:
  │  1. build world ray on wheel slot (hardpoint → hardpoint + suspDir·(radius+restLen))
  │  2. seed result: fraction = 1.0, hitBody = 0
  │  3. framework->vtbl[+0x20](i, &result)     ──►  0x64bbd0  wheelCollide
  │        packs ray + phantom, calls cast
  │                                                    ──►  0x580ed0  TtPhantom::castRay
  │                                                          broadphase overlap list → each shape castRay
  │  4. if hit: compression = (radius+restLen)·hitFraction − radius
  │     if miss: compression = restLen, inContact = 0
```

**Not** `CVOGMap::CastTerrainHeight` (`0x4cfe60`). Wheel contact is a **Havok phantom broadphase raycast against overlapping rigid bodies** (movable included), not a terrain-height-only cast.

---

## 2. Decompile — `FUN_0064bbd0` (`0x64bbd0`)

```c
// this = hkVehicleFramework*
// param_2 = wheelIndex
// param_3 = hk contact / raycast result out (see §5)
void __thiscall FUN_0064bbd0(int param_1, int param_2, undefined4 param_3)
{
  undefined4 *puVar1;
  undefined4 local_40;  // ray start xyz + pad  (4 f32)
  undefined4 local_3c;
  undefined4 local_38;
  undefined4 local_34;
  undefined4 local_30;  // ray end   xyz + pad  (4 f32)
  undefined4 local_2c;
  undefined4 local_28;
  undefined4 local_24;
  undefined1 local_20;  // flag = 1
  undefined4 local_1c;  // phantom pointer (also loaded into ECX as this for castRay)

  // wheel base = *( *(framework+0xc) + 0x80 ) + wheelIndex * 0xC0
  puVar1 = (undefined4 *)(param_2 * 0xc0 + *(int *)(*(int *)(param_1 + 0xc) + 0x80));

  // copy 8 floats: wheel+0x00..0x1c  →  local ray start/end
  local_40 = *puVar1;
  local_3c = puVar1[1];
  local_38 = puVar1[2];
  local_34 = puVar1[3];
  local_30 = puVar1[4];
  local_2c = puVar1[5];
  local_28 = puVar1[6];
  local_24 = puVar1[7];

  local_1c = *(undefined4 *)(param_1 + 0x1f8);  // TtPhantom*
  local_20 = 1;

  // thiscall: ECX = phantom (from fw+0x1f8); args = (&rayPacket, result)
  FUN_00580ed0(&local_40, param_3);
  return;
}
```

### What this function does / does not do

| Does | Does not |
|---|---|
| Index the wheel slot (`stride 0xC0`, base `*(fw+0xc)+0x80`) | Build the hardpoint / ray end (that is preUpdate) |
| Copy stored world ray (8 floats) | Write compression / in-contact / normal |
| Attach vehicle **TtPhantom** (`fw+0x1f8`) and call cast | Terrain height query |

Compression and contact flags are written by the **caller** (`0x64cf20`) after the cast returns.

---

## 3. Decompile — `TtPhantom::castRay` (`0x580ed0`)

Profiler name confirmed: ASCII `"TtPhantom::castRay\0"` at `0x9d4574` (`read_memory`).

```c
// this / param_1 = TtPhantom*
// param_2       = ray packet (float*): start[0..3], end[4..7], flag@byte(+0x20), …
// param_3       = result struct (see §5)
void __thiscall FUN_00580ed0(int param_1, float *param_2, int param_3)
{
  // optional rdtsc profiler bookends using string "TtPhantom::castRay"

  // Optional filter object from phantom internals when packet flag != 0
  // (param_2[8] as char); details not needed for compression math.

  int count = *(int *)(param_1 + 0x84);           // overlap count
  int **list = *(int ***)(param_1 + 0x80);        // collidable* array

  if (count - 1 >= 0) {
    do {
      int *collidable = *list;
      int *shape = (int *)*collidable;            // collidable[0] → shape
      if (shape != 0) {
        int bodyXform = collidable[2];            // motion / body transform owner

        // World → local: p' = R * (p − T)
        // T at bodyXform+0x50..0x58; R rows at +0x20/+0x30/+0x40 (cols +0/+4/+8)
        float sx = param_2[0] - *(float *)(bodyXform + 0x50);
        float sy = param_2[1] - *(float *)(bodyXform + 0x54);
        float sz = param_2[2] - *(float *)(bodyXform + 0x58);
        // local_start = R * (start − T)   → stack local_40/3c/38

        float ex = param_2[4] - *(float *)(bodyXform + 0x50);
        float ey = param_2[5] - *(float *)(bodyXform + 0x54);
        float ez = param_2[6] - *(float *)(bodyXform + 0x58);
        // local_end   = R * (end − T)     → stack local_30/2c/28

        // shape->vtbl[+0x20] = hkShape::castRay (local ray + result)
        (**(code **)(*shape + 0x20))(/* local input */, /* local ray */, param_3);

        // On hit flag: record which collidable was hit
        if (/* hit */) {
          *(int *)(param_3 + 0x20) = (int)collidable;
        }
      }
      list++;
      count--;
    } while (count != 0);
  }

  // If any hit: transform hit fields (normal) back to world via body transform
  if (*(int *)(param_3 + 0x20) != 0) {
    // FUN_005d6ae0( body_transform_at_collidable[2]+0x20 , result )
    FUN_005d6ae0(*(int *)(*(int *)(param_3 + 0x20) + 8) + 0x20, param_3);
  }
  return;
}
```

### Cast semantics — Havok broadphase (all overlapping bodies)

| Fact | Evidence |
|---|---|
| Cast target set | `phantom+0x80` = collidable pointer array, `phantom+0x84` = count |
| Scope | **Every** collidable currently in the phantom’s broadphase **overlap list** — not a terrain-only channel |
| Per body | World ray → local frame → `hkShape::castRay` (vtbl `+0x20`) |
| Hit record | `result+0x20` = hit collidable pointer |
| Movables | Hit body is a real Havok collidable; preUpdate later tests `body+0x18 == 1` for movable / mass path |
| Post-hit | `FUN_005d6ae0` rotates hit result (normal) with the body’s transform |

Retail = **full Havok world geometry that overlaps the vehicle phantom**. Server v1 may substitute terrain heightfield via `IVehicleCollisionQuery`, but must **not** hard-code “terrain only” into the compression math (see `PORTING_RULES.md` § Collision geometry).

---

## 4. Ray construction (caller `0x64cf20`, before vtbl+0x20)

Per wheel, preUpdate first writes the world ray onto the wheel slot, then calls collide:

```
// radius[i]  = (*(wheels + 0x10))[i]
// restLen[i] = (*(suspensionDesc + 0x28))[i]   // via fw wheels / component arrays
// hardpoint  already at wheel+0x00 (world), suspDir at wheel+0x50 (puVar6[0x14..0x16])

float rayLen = radius + restLen;
wheel.end = wheel.hardpoint;                    // copy start → end slots (+0x10)
wheel.end += suspDir * rayLen;                  // end = hardpoint + dir*(radius+restLen)
```

`0x64bbd0` then memcpy’s `wheel+0x00..0x1c` (start xyzw + end xyzw) into the cast packet.

| Wheel offset | Content at cast time |
|---|---|
| `+0x00..0x0c` | Ray **start** (hardpoint, world) |
| `+0x10..0x1c` | Ray **end** (hardpoint + suspDir·(radius+restLen)) |
| stride | `0xC0`; base `*( *(fw+0xc) + 0x80 )` |

---

## 5. Result struct (as read by preUpdate)

preUpdate seeds and passes `&fStack_a8`:

```
fStack_94 = g_flOne;   // 1.0  → default hit fraction (miss)
iStack_88 = 0;         // hit collidable
framework->vtbl[+0x20](wheelIndex, &fStack_a8);
```

| Result off | Field | Seed | Writer |
|---|---|---|---|
| `+0x00..0x0c` | contact **normal** (world after `FUN_005d6ae0`) | — | shape cast + world xform |
| `+0x14` | hit **fraction** along ray | `1.0` (`g_flOne` @ `0xa0f2a0`) | shape castRay |
| `+0x20` | hit **collidable** pointer | `0` | `TtPhantom::castRay` on hit |

Miss ⇔ `result+0x20 == 0` (preUpdate branch `if (iStack_88 == 0)`).

---

## 6. Compression formula (caller `0x64cf20`, authoritative)

### Hit (`result+0x20 != 0`)

```
radius  = (*(wheels + 0x10))[i]
restLen = (*(suspLengthArray + 0x28 path))[i]    // per-wheel suspension rest/max length
frac    = result[+0x14]                           // hit fraction ∈ (0,1]

// decompile:
//   fVar10 = (radius + restLen) * frac;
//   wheel[+0xb0] = fVar10 - radius;
compression = (radius + restLen) * hitFraction - radius;
```

Also on hit:

| Wheel off | Write |
|---|---|
| `+0x30..0x3c` (`pfVar7[0xc..0xf]`) | contact normal |
| `+0x80` (byte, `pfVar7+0x20`) | `1` (in-contact) |
| `+0xa4` (`pfVar7[0x29]`) | hit body / related pointer path |
| `+0xb0` (`pfVar7[0x2c]`) | **compression** (formula above) |

Contact point along the same ray:

```
travel = (radius + restLen) * hitFraction
contactPt = hardpoint + suspDir * travel
```

### Miss (`result+0x20 == 0`)

```
wheel[+0x80] = 0                 // in-contact clear
wheel[+0xb0] = restLen           // fully extended
// +0xac path uses 1.0 normalization; rate term +0xb4 cleared
```

### Interpretation

| `hitFraction` | `compression = (r+L)·f − r` |
|---|---|
| `0` | `−r` (contact at hardpoint; extreme) |
| `r/(r+L)` | `0` (wheel just touching at rest length of free travel) |
| `1` (and still a hit) | `L` (contact at ray end) |
| miss (no body) | forced to `L` (`restLen`), not the fraction formula |

Signed length: **negative ⇒ compressed** relative to the free suspension length (used later by suspension impulse / chassis lift — see `0.4-suspension.md`, `0.8-struct-offsets.md`).

---

## 7. Constants (`read_memory`)

| Symbol | Address | Raw LE | float32 | Role |
|---|---|---|---|---|
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Default ray fraction before cast; miss-side normalization |
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **−1.0** | Suspension rate numerator at `wheel+0xb4` (post-contact helper path; not compression) |
| `"TtPhantom::castRay"` | `0x009d4574` | ASCII | — | Names `0x580ed0` |

No additional float literals inside `0x64bbd0` itself (pure pack + call).

---

## 8. Struct anchors

| Location | Meaning |
|---|---|
| Framework `+0x0c` | Wheels / vehicle-data container (`param_1[3]`) |
| `*(fw+0xc) + 0x80` | Wheel slot base |
| Wheel stride | `0xC0` |
| Framework `+0x1f8` | **TtPhantom\*** used as cast `this` |
| Phantom `+0x80` / `+0x84` | Broadphase overlap collidable array / count |
| Wheel `+0xb0` | Compression length (output of formula in §6) |
| Wheel `+0x80` (byte) | In-contact flag |

---

## 9. Conflicts vs `0.5-wheel-collide.md`

| Item | Phase `0.5-wheel-collide.md` | This re-verify | Verdict |
|---|---|---|---|
| Entry `0x64bbd0` packs ray + phantom, calls `0x580ed0` | yes | yes | **match** |
| Cast is Havok phantom broadphase, not terrain height | yes | yes (overlap list + shape castRay) | **match** |
| Phantom at `fw+0x1f8`; list `+0x80`/`+0x84` | yes | yes | **match** |
| Compression `(radius+restLen)·frac − radius` | yes | yes (preUpdate hit branch) | **match** |
| Miss → compression `restLen`, inContact `0` | yes | yes | **match** |
| Result `+0x14` fraction, `+0x20` body | yes | yes (seed + read in preUpdate) | **match** |
| Wheel stride `0xC0`, base `wheels+0x80` | yes | yes | **match** |

**No algorithm conflict.** Phase 0.5 remains a valid map; this file is the porting gate for wheel-collide / cast / compression.

### Ambiguities retained (non-blocking for compression)

1. Exact layout of the post-hit world-transform helper `FUN_005d6ae0` args is decompiler-noisy; normal ends up world-space before preUpdate copies it — sufficient for contact.
2. Optional phantom filter object (`param_2` flag path → `phantom+8 → +0xd0`) not fully named; does not change the compression formula.
3. Collide post-helper vtbl `+0x24` (`0x51e900` in phase map) was **not** re-resolved as a function symbol in this pass; preUpdate still invokes it on hit for secondary projection — out of scope for compression.

---

## 10. Port checklist

1. Query interface must accept **arbitrary world bodies** (retail) even if v1 implements heightfield only.
2. Ray = hardpoint → hardpoint + suspDir·(radius+restLen) in world space.
3. Hit fraction default **1.0**; miss detected by null hit body, not by fraction alone.
4. **Compression** = `(radius + restLen) * hitFraction - radius` on hit; `restLen` on miss.
5. Do not substitute terrain Y-delta for this formula without re-deriving an equivalent fraction along the same ray.
```
