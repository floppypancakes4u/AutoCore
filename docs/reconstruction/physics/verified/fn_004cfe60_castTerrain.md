# VERIFIED — `CVOGMap_CastTerrainHeight` @ `0x4cfe60`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x4cfe60`; `disassemble_function` @ `0x4cfe60`, `0x55e530`;  
`read_memory` @ `0xa0f718`, `0xa0f2a0`, `0xa0f518`; callees `FUN_005a58c0` / `FUN_0055e530` /  
`FUN_006cad80`; contrast decompile `TtPhantom::castRay` @ `0x580ed0`; `get_xrefs_to`.

**Scope of this file:** map terrain **down-cast height** query used for spawn / creature snap /  
air-stab re-ground.  
**Not this file:** per-wheel Havok suspension cast (`TtPhantom::castRay` / `0.5-wheel-collide.md`).

---

## 1. Identity

| Item | Value |
|------|------:|
| Entry | `0x004cfe60` |
| Body | `0x004cfe60` – `0x004cff6f` |
| Symbol (Ghidra) | `CVOGMap_CastTerrainHeight` |
| Plate | Terrain height raycast used by CreateCreature / FindTerrainHeight |
| Convention | MSVC `__thiscall` — `this` = `CVOGMap*` in `ECX`; **`RET 0x10`** (4 stack args) |
| Return | x87 `ST0` / decompiler `float10` — **terrain Y** (float32) |

### Signature (recovered from assembly + decompile)

```
float CVOGMap_CastTerrainHeight(
    CVOGMap* this,   // ECX
    float    x,      // [EBP+0x08]
    float    z,      // [EBP+0x0C]
    float    yStart, // [EBP+0x10]  ray start Y (cast down toward heightfield)
    char     flag    // [EBP+0x14]  collision-filter selector
);
```

Ghidra decompile names these `param_1..param_5` (`param_1` = this).

---

## 2. Map object fields used

| Offset | Role |
|--------|------|
| `map + 0xe4e0` | **Heightfield object*** — null → early-out `0.0` |
| `map + 0xe4a4` | **Map collision cast context*** — becomes `this` for `FUN_0055e530` |

\*Pointer fields; confirmed by:

```
MOV ESI, ECX                 ; map
MOV ECX, [ESI + 0xe4e0]      ; heightfield → this for FUN_005a58c0
…
MOV ECX, [ESI + 0xe4a4]      ; collision ctx → this for FUN_0055e530
CALL 0x0055e530
```

---

## 3. Constants (`read_memory`, length 4)

| Symbol | Address | LE bytes | float32 | Role in this fn |
|--------|---------|----------|--------:|-----------------|
| `DAT_00a0f718` | `0x00a0f718` | `0a d7 23 3c` | **0.01** | Ray **end Y** = heightfieldY + 0.01 (tiny overshoot past HF surface) |
| `g_flOne` / `DAT_00a0f2a0` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Default hit fraction before cast; lerp numerator |
| `g_flZero` / `DAT_00a0f518` | `0x00a0f518` | `00 00 00 00` | **0.0** | Early-out when heightfield ptr null |

`DAT_00a0f718` is a **shared** 0.01 literal elsewhere (steer deadband, wheel friction setup).  
Here it is **only** the down-ray end epsilon on heightfield Y.

---

## 4. Algorithm (exact control flow)

```
// this = CVOGMap*

hf = *(this + 0xe4e0)
if (hf == 0)
    return 0.0f;                                    // g_flZero

// Sample heightfield Y at (x, z). thiscall: ECX = hf.
hfY = FUN_005a58c0(hf, x, z);                       // bilinear HF sample / scale

endY = hfY + 0.01f;                                 // DAT_00a0f718
hitFraction = 1.0f;                                 // g_flOne
hitFlag = 0;

// Collision filter enum:
//   flag == 0 → 5
//   flag != 0 → 5 + 0xD = 18
filter = (flag != 0 ? 0xD : 0) + 5;                 // NEG/SBB/AND 0xD / ADD 5

// Build vertical ray on stack (start above → end at HF+ε):
//   start = (x, yStart, z, 0)
//   end   = (x, endY,   z, 0)
//   + filter byte/dword packing matches creature castRay path

ctx = *(this + 0xe4a4)
FUN_0055e530(ctx, &ray, &result);                   // map collision cast wrapper

if (hitFlag != 0)
    return yStart * (1.0f - hitFraction) + endY * hitFraction;  // standard ray lerp
else
    return hfY;                                     // miss → pure heightfield sample
```

### Notes

1. **Ray is vertical in XZ** — start/end share `x`/`z`; only Y differs (`yStart` → `hfY+0.01`).
2. **Miss falls back to heightfield**, not `yStart` and not a miss sentinel. Callers always get a Y.
3. **Hit interpolates toward endY** (HF + 0.01), so a hit fraction of 1.0 would yield `endY`; real hits update `hitFraction` / `hitFlag` inside the cast.
4. **Filter flag** matches the pattern in `CVOGCreature_FindTerrainHeight` when it builds a creature phantom ray (`(-(flag!=0) & 0xd) + 5`).

---

## 5. Callees

| Addr | Symbol / role | How used |
|------|---------------|----------|
| `0x5a58c0` | Heightfield sample (`FUN_005a58c0`) | `this` = `*(map+0xe4e0)`; args `(x, z)` → HF Y |
| `0x55e530` | Map collision cast wrapper | `this` = `*(map+0xe4a4)`; args `(ray*, result*)` |
| `0x6cad80` | Cast dispatcher (via `0x55e530`) | Uses `ctx+0xc4` (world/broadphase vtbl object) + `ctx+0xd0`; calls **vtbl +0x30** with packed ray |

### `FUN_0055e530` (thin wrapper)

```
// thiscall ECX = collision context (map+0xe4a4)
// stack: ray*, result*
// RET 8

FUN_006cad80(
    *(this + 0xc4),   // world / cast target with vtbl
    ray,
    *(this + 0xd0),   // filter-related
    0,
    result
);
```

`FUN_006cad80` packs start/end into a local ray and invokes  
`(**(world_vtbl + 0x30))(rayPacket, collector, 0)` — **map / world collision**, not the vehicle phantom.

### `FUN_005a58c0` (heightfield)

Scales `(x,z)` by HF metrics (`+0x30`, `+0x38`), clamps cell indices to grid (`+0xc`, `+0x10`),  
bilinear-samples via `FUN_005a5810`, divides by scale `+0x34`. Independent of Havok body list.

---

## 6. Callers (`get_xrefs_to`)

| From | Function | Role |
|------|----------|------|
| `0x4c631b` | `CVOGCreature_FindTerrainHeight` | Creature Y snap when creature has no phantom path |
| `0x56430b` | `CVOGSpawnPoint_CreateTemplateVehicle` | Spawn vehicle ground Y |
| `0x56536a` | `CVOGSpawnPoint_CreateCreature` | Spawn creature ground Y |
| `0x5985cd` | `VehicleAction_airStabilization` | Collision-recovery **re-ground** (`pos.y + 10` raise then cast) |
| `0x5ce905` | `FUN_005cd3b0` | Related placement / path helper |
| `0x6154eb` | `FUN_00615020` | (map/world path) |
| `0x620702` | `FUN_00620480` | (map/world path) |
| `0x9230c7` | `Client_Input_DriveControlTick` | Client drive-control ground query |
| `0x94f683` | `FUN_0094f2e0` | (client helper) |

**Vehicle physics air-stab usage** (from verified `fn_00598320_airStab.md`):

```
yStart = rb.y + 10.0f;                 // DAT_00a110d8
h = CVOGMap_CastTerrainHeight(x, z, yStart, …);
set position (x, h, z);
```

That is a **one-shot placement snap**, not a suspension contact sample.

---

## 7. Relation vs Havok wheel cast

| | `CVOGMap_CastTerrainHeight` `0x4cfe60` | Wheel cast path (`0.5-wheel-collide`) |
|--|----------------------------------------|--------------------------------------|
| Entry | Map API, `__thiscall` on `CVOGMap` | `hkVehicleWheelCollide::collide` `0x64bbd0` → `TtPhantom::castRay` `0x580ed0` |
| Who casts | Map collision context `*(map+0xe4a4)` → world vtbl **+0x30** | Vehicle **TtPhantom** overlap list (`phantom+0x80/+0x84`) |
| Geometry | Single **vertical** ray `(x,yStart,z) → (x, hfY+0.01, z)` | Per-wheel **suspension ray** hardpoint→end stored on wheel `+0x00..0x1c` |
| Also samples | Heightfield `*(map+0xe4e0)` via `FUN_005a58c0` | No heightfield sample in the wheel path |
| Hits | Map/static collision (+ filter 5/18) | **Each overlapping Havok shape** (`hkShape::castRay` vtbl **+0x20**), including movable RBs |
| Result | **Scalar Y** (lerp or HF fallback) | Contact **normal**, **fraction**, **body ptr** → wheel `+0x30`, `+0x80`, `+0xb0` compression |
| Used for | Spawn, creature foot snap, air-stab re-ground, some client drive helpers | Suspension compression / in-contact / friction every preUpdate |
| Movable bodies | Not the wheel-phantom body list | Yes — `result+0x20` collidable stored on wheel `+0xa4` |

### Creature path contrast (`CVOGCreature_FindTerrainHeight` `0x4c6100`)

When the creature **does** have a physics shell path (`creature[0x95] != 0`), FindTerrainHeight  
**skips** `CastTerrainHeight` and builds its own ray, calling **`FUN_00580ed0` (`TtPhantom::castRay`)**  
directly — same family as the wheel cast, different purpose (foot Y + offsets).

When `creature[0x95] == 0`, it uses **`CVOGMap_CastTerrainHeight`**.

### Porting implication

- **Do not** implement vehicle wheel–ground contact with `CastTerrainHeight`.
- Wheel stance / pitch / roll come from **Havok wheel collide + suspension**, not this map cast.
- Server terrain snap / re-ground / spawn Y may legitimately use heightfield ± a down-cast  
  analogous to this function (HF sample + optional collision ray, miss → HF Y).
- Full retail wheel collision requires an `IVehicleCollisionQuery` against **bodies**, not HF-only  
  (see `PORTING_RULES.md` § Collision geometry).

---

## 8. Decompiler pseudocode (cleaned)

```c
/* Terrain height raycast used by CreateCreature / FindTerrainHeight.
   Casts down from (x, z, yStart) against map collision. */

float10 __thiscall
CVOGMap_CastTerrainHeight(int map, float x, float z, float yStart, char flag)
{
  float10 fVar1;
  // locals: ray start/end, filter, result, hitFraction, hitFlag — see §4

  if (*(int *)(map + 0xe4e0) == 0)
    return (float10)g_flZero;

  fVar1 = (float10)FUN_005a58c0(x, z);              // ECX = *(map+0xe4e0)
  float endY = (float)fVar1 + DAT_00a0f718;         // + 0.01
  float hitFraction = g_flOne;                      // 1.0
  int filter = (-(uint)(flag != 0) & 0xd) + 5;
  int hitFlag = 0;

  // ray: start (x,yStart,z,0) → end (x,endY,z,0); filter packed
  FUN_0055e530(&ray, &result);                      // ECX = *(map+0xe4a4)

  if (hitFlag != 0)
    return (float10)(yStart * (g_flOne - hitFraction) + endY * hitFraction);

  return (float10)(float)fVar1;                     // heightfield Y
}
```

Assembly corrects Ghidra’s omitted `this` setup for both callees (see §2 / §5).

---

## 9. Verification checklist

- [x] Function present @ `0x4cfe60` (`CVOGMap_CastTerrainHeight`)
- [x] Decompile + full function disassembly (71 instructions, `RET 0x10`)
- [x] Constants verified: `0.01` / `1.0` / `0.0`
- [x] Heightfield ptr `map+0xe4e0`, cast ctx `map+0xe4a4`
- [x] Filter formula `(-(flag!=0)&0xD)+5` → 5 or 18
- [x] Hit → ray lerp; miss → HF Y; null HF → 0
- [x] Call graph: spawn / FindTerrainHeight / airStab re-ground / client helpers
- [x] Explicit contrast vs `TtPhantom::castRay` wheel path (`0x580ed0` / `0x64bbd0`)
- [x] Did **not** use `disassemble_bytes` as primary RE (used `disassemble_function` only to recover ECX setup Ghidra omitted)

---

## 10. Related docs

| Doc | Relation |
|-----|----------|
| `docs/reconstruction/physics/0.5-wheel-collide.md` | Havok wheel cast (different path) |
| `docs/reconstruction/physics/verified/fn_00598320_airStab.md` | Re-ground caller of this fn |
| `docs/reconstruction/physics/avd-airstab-spec.md` | Spec layer for air-stab + cast |
| `docs/NPCDriving.md` / `docs/NPC_DRIVING_FIX_RE.md` | Index entries for terrain layer |
| `docs/reconstruction/physics/PORTING_RULES.md` | Collision geometry porting rule |
