# VERIFIED — Quaternion basis extractors @ `0x4e8ad0` / `0x4e8a40`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` / `force_decompile` @ `0x4e8ad0`, `0x4e8a40` (and sibling `0x4e8b60`);  
`audit_globals_in_function` @ `0x4e8ad0`; `read_memory` @ `0xa0f2a0`, `0xa10e74`;  
`get_function_xrefs` / call-site in `CVOGVehicle::MoveToTarget3DPoint` @ `0x4fc650`.

**Scope of this file:** pure math helpers that convert a unit quaternion into world-space
**right (+X)** and **forward (+Z)** basis axes (with the sibling **up (+Y)** extractor noted for completeness).

**Not this file:** drive-controller logic that *consumes* these axes — see
`docs/reconstruction/physics/drive-controller-spec.md` (`MoveToTarget3DPoint` @ `0x4fc650`).

---

## 1. Identity & call graph

| Item | Right | Forward | Up (sibling) |
|------|------:|--------:|-------------:|
| Entry | `0x004e8ad0` | `0x004e8a40` | `0x004e8b60` |
| Symbol (Ghidra) | `FUN_004e8ad0` | `FUN_004e8a40` | `FUN_004e8b60` |
| Convention | stack args (`param_1`, `param_2` at `[ebp+8]`, `[ebp+0xc]`) — **not** `__thiscall` | same | same |
| Leaf? | **yes** (no calls, single basic block, cyclomatic complexity 1) | same | same |
| Body end | `0x004e8b58` | `0x004e8acb` | (next after up) |

### Signature (both primary helpers)

```c
// param_1 = const float quat[4]  // XYZW unit quaternion
// param_2 = float out[4]         // XYZ direction, W forced to 0
void FUN_004e8ad0(float *param_1, float *param_2);  // → RIGHT  (+X)
void FUN_004e8a40(float *param_1, float *param_2);  // → FORWARD (+Z)
void FUN_004e8b60(float *param_1, float *param_2);  // → UP      (+Y)  [sibling]
```

Neither function normalizes the input quaternion. For a **unit** quaternion the output
XYZ is unit length; non-unit input yields a non-unit axis.

### Primary caller for this RE track — `MoveToTarget3DPoint` @ `0x4fc650`

Chassis orientation source (physics path):

```
rb   = *(*(vehicle + 8) + 0x3c)     // rigid body
quat = rb + 0x30                    // float[4] orientation (XYZW)
```

Fallback when `vehicle+8 == 0` uses entity-local transform storage
(`*( *(vehicle+4)+4 ) + vehicle + 0x94`), still treated as the same XYZW layout.

Call order in the drive path:

```
FUN_004e8ad0(quat, &right);     // xref 0x4fc7d2  → R used as lateral = dot(R, dir)
FUN_004e8a40(quat, &forward);   // xref 0x4fc82e  → F used as fAlign / fwdSpeed dots
```

Other notable callers of the forward extractor include
`VehicleEntity_PushDriveAxesToController` (`0x4fbc10`), `VehicleAction_applyAction`
(`0x598650`), and several AI heading helpers — same math, different consumers.

---

## 2. Constants (verified — raw memory)

| Address | Ghidra name | LE bytes | float32 | Role here |
|---------|-------------|---------|--------:|-----------|
| `0x00a0f2a0` | `g_flOne` | `00 00 80 3f` | **1.0** | diagonal term in `1 − 2·(…)` |
| `0x00a10e74` | `g_flLevelUpUiBase_Inferred` | `00 00 00 40` | **2.0** | quaternion factor **2** in every off-diagonal / scale term |

> **Naming note:** `g_flLevelUpUiBase_Inferred` is a Ghidra plate leftover from an unrelated
> UI path. In *this* code it is simply the shared **2.0f** pool constant (same address the
> drive controller reuses as steer gain — role differs by call site; value is always 2.0).

No other globals, no tables, no vtables.

---

## 3. Decompile (authoritative)

### 3.1 `FUN_004e8ad0` — right (+X)

```c
void FUN_004e8ad0(float *param_1, float *param_2)
{
  float fVar1;  // y
  float fVar2;  // z
  float fVar3;  // x
  float fVar4;  // w
  float fVar5;  // 2.0

  fVar5 = g_flLevelUpUiBase_Inferred;   // 2.0 @ 0xa10e74
  fVar1 = param_1[1];                   // y
  fVar2 = param_1[2];                   // z
  fVar3 = *param_1;                     // x
  fVar4 = param_1[3];                   // w

  *param_2     = g_flOne - (fVar2 * fVar2 + fVar1 * fVar1) * g_flLevelUpUiBase_Inferred;
  // out.x = 1 - 2*(z² + y²)

  param_2[2]   = (fVar2 * fVar3 - fVar1 * fVar4) * fVar5;
  // out.z = 2*(z*x - y*w)

  param_2[1]   = (fVar3 * fVar1 + fVar2 * fVar4) * fVar5;
  // out.y = 2*(x*y + z*w)

  param_2[3]   = 0.0;
  return;
}
```

### 3.2 `FUN_004e8a40` — forward (+Z)

```c
void FUN_004e8a40(float *param_1, float *param_2)
{
  float fVar1;  // x
  float fVar2;  // y
  float fVar3;  // z
  float fVar4;  // w
  float fVar5;  // 2.0

  fVar5 = g_flLevelUpUiBase_Inferred;   // 2.0
  fVar1 = *param_1;                     // x
  fVar2 = param_1[1];                   // y
  fVar3 = param_1[2];                   // z
  fVar4 = param_1[3];                   // w

  *param_2     = (fVar3 * fVar1 + fVar2 * fVar4) * g_flLevelUpUiBase_Inferred;
  // out.x = 2*(z*x + y*w)

  param_2[1]   = (fVar3 * fVar2 - fVar1 * fVar4) * fVar5;
  // out.y = 2*(z*y - x*w)

  param_2[2]   = g_flOne - (fVar1 * fVar1 + fVar2 * fVar2) * fVar5;
  // out.z = 1 - 2*(x² + y²)

  param_2[3]   = 0.0;
  return;
}
```

### 3.3 Sibling `FUN_004e8b60` — up (+Y) (not used by MoveToTarget; documented for the set)

```c
void FUN_004e8b60(float *param_1, float *param_2)
{
  // q = (x,y,z,w) = (*p, p[1], p[2], p[3])
  *param_2     = (x*y - z*w) * 2.0f;             // out.x
  param_2[1]   = 1.0f - (z*z + x*x) * 2.0f;      // out.y
  param_2[2]   = (z*y + x*w) * 2.0f;             // out.z
  param_2[3]   = 0.0f;
}
```

---

## 4. Closed-form (port-ready)

Quaternion layout: **`q = (x, y, z, w)`** at `float[4]` indices `0..3` (Havok / client chassis
orientation). Output is a **homogeneous direction** `(vx, vy, vz, 0)`.

Standard unit-quaternion → rotation-matrix **columns** (local axes in world space):

```
        | 1−2(y²+z²)    2(xy−wz)     2(xz+wy) |
  R  =  | 2(xy+wz)      1−2(x²+z²)   2(yz−wx) |
        | 2(xz−wy)      2(yz+wx)     1−2(x²+y²) |

  right   (+X) = column 0 = FUN_004e8ad0
  up      (+Y) = column 1 = FUN_004e8b60
  forward (+Z) = column 2 = FUN_004e8a40
```

Port pseudocode (exact match to decompile):

```c
// TWO = 2.0f @ 0xa10e74; ONE = 1.0f @ 0xa0f2a0

void ExtractRight(const float q[4], float out[4]) {
    float x=q[0], y=q[1], z=q[2], w=q[3];
    out[0] = ONE - (z*z + y*y) * TWO;   // 1 - 2(y²+z²)
    out[1] = (x*y + z*w) * TWO;         // 2(xy + zw)
    out[2] = (z*x - y*w) * TWO;         // 2(xz - yw)
    out[3] = 0.0f;
}

void ExtractForward(const float q[4], float out[4]) {
    float x=q[0], y=q[1], z=q[2], w=q[3];
    out[0] = (z*x + y*w) * TWO;         // 2(xz + yw)
    out[1] = (z*y - x*w) * TWO;         // 2(yz - xw)
    out[2] = ONE - (x*x + y*y) * TWO;   // 1 - 2(x²+y²)
    out[3] = 0.0f;
}
```

### Hand checks (identity + 90° yaw)

| Input quat `(x,y,z,w)` | Right | Forward |
|------------------------|-------|---------|
| Identity `(0,0,0,1)` | `(1,0,0,0)` | `(0,0,1,0)` |
| 90° about +Y: `(0, √½, 0, √½)` | `(0,0,−1,0)` | `(1,0,0,0)` |

Matches local **+Z forward**, **+X right**, **+Y up** (left-handed or right-handed is
determined by the cross product `right × up = forward` with these columns — consistent
with the MoveToTarget / AI notes in `docs/NPCDriving.md` and `docs/NPC_DRIVING_FIX_RE.md`).

---

## 5. How MoveToTarget uses the outputs

From `0x4fc650` (already reconstructed in `drive-controller-spec.md`):

```
dir      = normalize(aim − pos)          // full 3D
lateral  = dot(right,   dir)             // FUN_004e8ad0 result → steer signal
fAlign   = dot(forward, dir)             // FUN_004e8a40 result → reverse gate / thr
fwdSpeed = dot(velocity, forward)        // signed speed along chassis +Z
```

Only **XYZ** of each output is dotted; **W is written 0** and ignored by consumers that
treat the buffer as `float3` (or `float4` with unused W).

---

## 6. Corrections / confirmed claims

| Source | Claim | Verdict |
|--------|-------|---------|
| `docs/NPCDriving.md` | `004e8ad0` = right (+X), `004e8a40` = forward (+Z) | **CONFIRMED** |
| `docs/NPC_DRIVING_FIX_RE.md` | Local **+Z forward**, **+X right**, Y up | **CONFIRMED** (matrix columns) |
| `drive-controller-spec.md` | basis at `rb+0x30` fed to these helpers | **CONFIRMED** |
| Ghidra name `g_flLevelUpUiBase_Inferred` | “level-up UI base” | **MISNOMER here** — value is **2.0f** quaternion factor |

---

## 7. Port checklist

- Input must be **XYZW** (not WXYZ). Swapping component order silently rotates wrong.
- Do **not** re-normalize the output unless the source quat is known non-unit; retail does not.
- Output W is always **0** — keep a 16-byte write if matching retail stack packs.
- Prefer extracting **right** and **forward** from the same quat snapshot (MoveToTarget
  re-reads the pointer twice; under concurrent physics that can race — server ports should
  sample once).
- Sibling up-extractor `0x4e8b60` is available when a full orthonormal basis is needed;
  MoveToTarget does not call it.

---

## 8. Status

| Item | State |
|------|-------|
| Decompile both primary helpers | **Verified** |
| Constant addresses / values | **Verified** (`read_memory`) |
| Quaternion → matrix column mapping | **Verified** (closed form match) |
| MoveToTarget call sites | **Verified** (xrefs `0x4fc7d2`, `0x4fc82e`) |
| Up sibling | Documented from decompile; not on MoveToTarget hot path |
| C# / production port in this change | **Out of scope** (doc only) |
|Status** | **Verified** |
