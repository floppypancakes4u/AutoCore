# Verified: `TtPhantom::castRay` @ `0x00580ed0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x00580ed0` … `0x005810fd` |
| Ghidra name | `FUN_00580ed0` |
| Profiler string | `"TtPhantom::castRay"` @ `0x009d4574` (`read_memory` → ASCII + NUL) |
| Convention | MSVC `__thiscall` — `ECX` = `TtPhantom*`; stack: ray packet, result; **`RET 8`** |
| Role | Cast a **world-space ray** against every collidable currently in the phantom’s **broadphase overlap list**; accumulate closest hit into a shared result; rotate hit normal to world |
| RE tools | `decompile_function` @ `0x580ed0`; `batch_decompile` of callers `0x64bbd0` / `0x64cf20` + helper `0x5d6ae0`; `get_xrefs_to`; `read_memory` on string + call site `0x5810b0` |
| Status | **Verified** (re-read) |
| Scope | **This function only** — geometry / cast backend. Compression formula lives in preUpdate (`0x64cf20`); packer is `0x64bbd0`. |

Related:

- Phase map: `docs/reconstruction/physics/0.5-wheel-collide.md`
- Wheel packer: `fn_0064bbd0_wheelCollide.md`
- PreUpdate consumer: `fn_0064cf20_preUpdate.md` (if present) / phase 0.5
- World-rotate helper: `fn_005d6ae0_steerBasis.md`
- **Different** path (map heightfield, not this): `fn_004cfe60_castTerrain.md`
- Port rule: `PORTING_RULES.md` § Collision geometry

No C# in this file (RE evidence only). Written for a **geometry-later** server design: freeze the **query contract** now; swap the backend later.

---

## 1. Callers

| Site | Function | Role |
|---|---|---|
| `0x0064bc61` | `FUN_0064bbd0` (`hkVehicleWheelCollide::collide`, fw vtbl `+0x20`) | **Vehicle wheel suspension cast** — primary port concern |
| `0x004c629f` | `CVOGCreature_FindTerrainHeight` | Creature ground snap when entity has a phantom path (`entity+0x254` / `param_1[0x95] != 0`); else uses `CVOGMap_CastTerrainHeight` |

Wheel chain:

```
hkVehicleFramework_preUpdate (0x64cf20)
  seeds result: fraction=1.0, hitBody=0
  builds world ray on wheel+0x00..0x1c
  framework->vtbl[+0x20](wheelIndex, &result)   →  0x64bbd0
    copies ray; this = *(fw+0x1f8)  (TtPhantom*)
    TtPhantom::castRay (0x580ed0)
  reads result → compression / in-contact / normal
```

---

## 2. Signature & packets

```c
// this / param_1 = TtPhantom*
// param_2       = ray packet (float*)
// param_3       = raycast result (byte/float buffer; shared accumulator)
void __thiscall TtPhantom_castRay(int phantom, float *ray, int result);
```

### 2.1 Ray packet (`param_2`) — as used by this function

| Off | Type | Field |
|---|---|---|
| `+0x00..0x08` | 3×`float` | World ray **start** xyz (`ray[0..2]`) |
| `+0x0c` | `float` | pad / w (copied by wheel packer; not read here) |
| `+0x10..0x18` | 3×`float` | World ray **end** xyz (`ray[4..6]`) |
| `+0x1c` | `float` | pad / w |
| `+0x20` | `uint8` | filter enable flag (`*(char*)(ray+8)`); wheel packer sets **1** |
| `+0x24` | `float` | saved to stack (`ray[9]`); not consumed by the cast body in the decompile |

Wheel packer (`0x64bbd0`) fills start/end from `wheel+0x00..0x1c`, sets flag `1`, attaches phantom via `ECX = *(fw+0x1f8)`.

### 2.2 Result (`param_3`) — fields this function / shapes write

| Off | Field | Who writes | Seed (wheel preUpdate) |
|---|---|---|---|
| `+0x00..0x0c` | contact **normal** (body-local during shape casts → **world** after post-hit) | shape `castRay`, then `FUN_005d6ae0` | (uninit / prior) |
| `+0x14` | hit **fraction** along ray ∈ (0,1] | shape `castRay` (closest-hit) | **`1.0`** (`g_flOne` @ `0xa0f2a0`) |
| `+0x20` | hit **collidable\*** | **this function** on shape hit-flag | **`0`** (null = miss) |

Miss detection for wheels: **`result+0x20 == 0`**, not “fraction == 1” alone (a hit at the ray end can still be fraction 1.0 with a non-null body).

---

## 3. Algorithm (decompile order)

### 3.1 Profiler bookends

If `DAT_00bc5644 < DAT_00bc5648`:

1. Entry: store pointer to `"TtPhantom::castRay"` (`0x9d4574`), `rdtsc`, advance ring by 3 dwords.
2. Exit: store `&DAT_009d2878` (ASCII `"Et"` marker family), `rdtsc`, advance.

No effect on cast math.

### 3.2 Optional filter object (side data)

```
if (ray.flag == 0) OR (*( *(phantom+8) + 0xd0 ) == 0)
    filter = 0;
else
    filter = *( *(phantom+8) + 0xd0 ) + 0x10;
```

Ghidra does **not** show `filter` passed into the shape virtual call (possible decompiler miss vs dead prep). **Not required** for the geometry-later contract: a heightfield / world-query backend has its own filter policy. Do not block porting on naming this object.

### 3.3 Overlap list walk

```
count = *(int*)(phantom + 0x84);
list  = *(collidable***)(phantom + 0x80);   // array of collidable*

for each collidable in list[0 .. count):
    shape = *(void**)collidable;            // collidable[0]
    if (shape == null) continue;

    bodyXform = collidable[2];              // *(collidable + 8) — rigid motion / transform owner
    // bodyXform +0x20..+0x48 = 3×3 rotation (column vectors, 0x10 stride)
    // bodyXform +0x50..+0x58 = translation T
```

**Geometry scope (retail):** every collidable currently overlapping the vehicle **TtPhantom** Aabb/broadphase set — static **and** dynamic rigid bodies. This is **not** `CVOGMap_CastTerrainHeight` (`0x4cfe60`).

### 3.4 World → body-local ray

For start and end independently:

```
d = worldPoint - T
// local = R^T * d   (dot with columns of R)
local.x = R_col0 · d = R[+0x20]*dx + R[+0x24]*dy + R[+0x28]*dz
local.y = R_col1 · d = R[+0x30]*dx + R[+0x34]*dy + R[+0x38]*dz
local.z = R_col2 · d = R[+0x40]*dx + R[+0x44]*dy + R[+0x48]*dz
```

Stack holds local start (`local_40/3c/38`) and local end (`local_30/2c/28`) as a contiguous local ray packet.

### 3.5 Per-shape cast (virtual)

```
// this = shape; vtbl slot +0x20 = hkShape::castRay (and Tthk overrides,
// e.g. string "TthkShapeCollection::castRay" @ 0xa0e2d4)
shape->vtbl[+0x20]( /* hit-flag local */, &localRay, result );

if (hitFlag != 0)
    *(collidable**)(result + 0x20) = collidable;
```

**Closest-hit model:** the same `result` buffer is passed to every shape. PreUpdate seeds `fraction = 1.0` once. Standard Havok shape `castRay` only updates the output when the new fraction is **stricter** than the current one — so the loop yields the closest hit across the overlap set. (Individual shape implementations not re-verified in this pass; wheel math only needs the aggregate fraction/normal/body.)

### 3.6 Post-loop: normal body-local → world

Call site bytes @ `0x5810b0` (not full-function disasm):

```
mov  eax, [edi+0x20]      ; result.hitCollidable
test eax, eax
jz   skip
mov  edx, [eax+8]         ; collidable+8 → bodyXform
push edi                  ; v = result (normal at +0)
add  edx, 0x20            ; R = bodyXform+0x20
push edx
mov  ecx, edi             ; this/out = result
call FUN_005d6ae0         ; out = R * v, out.w = 0
```

So when any hit was recorded:

```
result.normal.xyz = R_body * result.normal.xyz   // local → world
result.normal.w   = 0
```

Helper details: `fn_005d6ae0_steerBasis.md` (`out = R·v`, column-major, stride 16).

---

## 4. Geometry-later design contract

This function is the **retail geometry backend** for the wheel suspension ray. Server ports should **not** reimplement phantom lists + Havok shapes to get correct compression — they should implement an injectable query with the **same I/O contract**.

### 4.1 Stable interface (what preUpdate / compression depend on)

```
Input (per wheel, world space):
  rayStart = hardpoint
  rayEnd   = hardpoint + suspDir * (radius + restLen)
  // built in preUpdate; packed by 0x64bbd0; cast only consumes start/end

Output (into shared result):
  fraction  @ +0x14   // 1.0 if miss (unchanged seed); else hit t along ray
  normal    @ +0x00   // world-space unit contact normal (after R·n)
  body      @ +0x20   // non-null ⇔ hit; null ⇔ miss
```

Downstream compression (**not** in this function — consumer `0x64cf20`):

```
on hit:  compression = (radius + restLen) * fraction - radius
on miss: compression = restLen, inContact = 0
```

### 4.2 What may be substituted later

| Retail (`0x580ed0`) | Geometry-later OK substitute |
|---|---|
| Phantom overlap list (`+0x80`/`+0x84`) | Any world query that can hit the surfaces the vehicle should rest on |
| Per-body world→local + `hkShape::castRay` | Heightfield / mesh / BVH raycast in world space |
| `FUN_005d6ae0` local→world normal | Emit **world** normal directly from the substitute |
| Optional phantom filter (`+8→+0xd0`) | Server filter / layer mask as needed |
| Dynamic rigid-body hits | v1 may omit movables; keep `body` pointer optional / null on heightfield |

### 4.3 What must **not** be hard-coded into math

Per `PORTING_RULES.md` § Collision geometry:

1. Do **not** bake “terrain Y only” into the compression formula.
2. Do **not** replace `(r+L)·frac − r` with a vertical delta unless you re-derive an equivalent **fraction along the same suspension ray**.
3. Keep an `IVehicleCollisionQuery`-style injection point so world meshes / props can be added without touching suspension / friction code.
4. Ray direction is **suspension direction** (usually chassis down), not always world −Y.

### 4.4 Recommended server seam

```
// conceptual — not production C#
struct WheelRayQuery {
  Vec3 start, end;          // world
};
struct WheelRayHit {
  bool  hit;                // ⇔ retail result+0x20 != 0
  float fraction;           // retail +0x14; default 1.0
  Vec3  normalWorld;        // retail +0x00 after 0x5d6ae0
  void* bodyOrNull;         // optional; movables / friction coupling later
};

// Retail backend: TtPhantom::castRay semantics (§3)
// v1 backend:     heightfield cast along start→end, fill same fields
// v2+ backend:    full world geometry (+ optional dynamics)
WheelRayHit QueryWheelRay(const WheelRayQuery&);
```

Wheel collide packer + preUpdate stay identical; only `QueryWheelRay` grows.

### 4.5 Contrast: terrain height API (do not conflate)

| | `TtPhantom::castRay` `0x580ed0` | `CVOGMap_CastTerrainHeight` `0x4cfe60` |
|---|---|---|
| Input | 3D segment start→end | XZ + startY + flag |
| Output | fraction, normal, collidable | scalar terrain **Y** |
| Targets | Phantom **overlap collidables** (any shape) | Map **heightfield** only |
| Wheel use | **Yes** (suspension) | **No** |
| Creature snap | Secondary path when entity has phantom | Primary path |

---

## 5. Phantom / body structural anchors

| Location | Meaning |
|---|---|
| Framework `+0x1f8` | `TtPhantom*` passed as `this` from wheel collide |
| Phantom `+0x80` | `collidable**` overlap array |
| Phantom `+0x84` | `int` overlap count |
| Phantom `+0x08` → `+0xd0` | Optional filter object (see §3.2) |
| Collidable `+0x00` | `hkShape*` (vtbl, slot `+0x20` = castRay) |
| Collidable `+0x08` | Body / motion transform owner* |
| Body `+0x20..+0x48` | Rotation R (3 columns × float4) |
| Body `+0x50..+0x58` | Translation T |

\*Indexing: decompile uses `collidable[2]` on an `int*` view of the collidable = byte offset `+0x08`.

---

## 6. Constants

| Symbol | Address | Raw LE | Value | Role |
|---|---|---|---|---|
| `"TtPhantom::castRay"` | `0x009d4574` | ASCII | — | Names this function (profiler) |
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | Caller seeds hit fraction before cast (not loaded inside `0x580ed0`) |

**No float formula constants** inside `0x580ed0` itself (pure list walk + transforms + virtual calls).

---

## 7. Decompile (annotated, authoritative structure)

```c
void __thiscall FUN_00580ed0(int phantom, float *ray, int result)
{
  // profiler: "TtPhantom::castRay"

  // optional filter prep from ray[+0x20] + phantom+8→+0xd0  (§3.2)

  int count = *(int *)(phantom + 0x84);
  int **list = *(int ***)(phantom + 0x80);

  while (count-- > 0) {
    int *collidable = *list++;
    int *shape = (int *)*collidable;          // +0
    if (!shape) continue;

    int body = collidable[2];                 // +8 → transform

    // world start/end → local via R^T (p - T)   (§3.4)
    // stack: local_from, local_to

    // shape->castRay(..., &local_ray, result)   vtbl+0x20
    if (hitFlag)
      *(int *)(result + 0x20) = (int)collidable;
  }

  if (*(int *)(result + 0x20) != 0) {
    // ECX=result, R=*(hitCollidable+8)+0x20, v=result
    FUN_005d6ae0(/*out*/ result, /*R*/ bodyR, /*v*/ result);
  }

  // profiler end
}
```

---

## 8. Reconciliation

| Claim | Source | This re-verify | Verdict |
|---|---|---|---|
| Symbol / string `TtPhantom::castRay` | phase 0.5, wheelCollide doc | `read_memory` `0x9d4574` | **match** |
| Overlap list `phantom+0x80` / count `+0x84` | phase 0.5 | decompile | **match** |
| World→local via body `+0x20` R and `+0x50` T | phase 0.5 | decompile SSE body | **match** |
| Shape cast vtbl `+0x20` | phase 0.5 | decompile + call `[edx+0x20]` | **match** |
| Hit collidable @ `result+0x20` | phase 0.5 | decompile store | **match** |
| Post-hit normal world via `0x5d6ae0` | wheelCollide doc | call-site bytes + helper decompile | **match** (args: ECX=result, R, v=result) |
| Wheel cast ≠ terrain height cast | phase 0.5, PORTING_RULES | separate xrefs / algorithms | **match** |
| Creature also calls `0x580ed0` | — | xref `0x4c629f` | **new detail** (non-wheel consumer) |

**Binary wins if conflict:** none vs phase 0.5 / `fn_0064bbd0_wheelCollide.md` cast section.

### Ambiguities (non-blocking for geometry-later seam)

1. **Filter object** wiring into shape `castRay` not visible in decompile; ignore for v1 heightfield.
2. **Shape-level closest-hit** assumed Havok-standard (only improve fraction); not re-decompiled per shape subclass this pass.
3. **`ray[+0x24]`** loaded to stack and unused in body — unknown side channel / residual; wheel packer does not depend on it for compression.

---

## 9. Port checklist (geometry-later)

1. Define `QueryWheelRay(start, end) → {hit, fraction, normalWorld, body?}` matching §4.1.
2. Retail reference behavior = this file (§3); v1 may use heightfield but **same fields**.
3. Keep compression in preUpdate: `(r+L)·frac − r` / miss → `L` — **outside** this module.
4. Do not call `CVOGMap_CastTerrainHeight` as a drop-in for wheel cast without converting Y-hit → ray fraction + world normal along the suspension segment.
5. When world geometry ships later, expand only the query backend; suspension, friction, and aero stay untouched.
