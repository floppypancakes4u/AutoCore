# Verified: `VehicleEngine_torqueCurve2D` @ `0x4a9750`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x004a9750` |
| Symbol | `VehicleEngine_torqueCurve2D` |
| Convention | MSVC `__thiscall` — `this` in `ECX` |
| Return | x87 `float10` carrying a float32 factor |
| Verified | Ghidra `decompile_function` + `read_memory` (re-gate) |

---

## Constants (`read_memory`, length 4)

| Symbol | Address | Raw LE bytes | float32 | Role |
|---|---|---|---|---|
| `DAT_00a0f298` | `0x00a0f298` | `00 00 00 3f` | **0.5** | bin origin: `base = scale * 0.5` |
| `g_flOne` / `DAT_00a0f2a0` | `0x00a0f2a0` | `00 00 80 3f` | **1.0** | engine-disabled return; also `1.0 / scale` numerator |

---

## Engine struct fields (from decompile)

| Offset | Type | Meaning |
|---|---|---|
| `+0x0c` | `int8` / `char` | **enabled** — `0` → early-out |
| `+0x10` | `int32` | **rows** — X (param2 / RPM) bin count; range check only |
| `+0x14` | `int32` | **cols** — Y (param3 / throttle) bin count **and** row stride |
| `+0x18` | `float32` | **range scale** (bin width) |
| `+0x344` | `float32[8]` | **factor table** — discrete torque-factor levels |
| `+0x3dc` | `int32` | **pointer** to byte LUT (`rows * cols` bytes) |

Call args: `param_2` = X axis (caller labels as RPM/speed), `param_3` = Y axis (throttle).

---

## Exact algorithm (from decompile pseudocode)

```
// Casts are C (int) float→int: TRUNCATE TOWARD ZERO (not floor).

if (*(char*)(engine + 0x0c) == 0)
    return g_flOne;                              // 1.0  — DISABLED fallback

float scale = *(float*)(engine + 0x18);
float base  = scale * DAT_00a0f298;              // scale * 0.5
float inv   = g_flOne / scale;                   // 1.0 / scale

int xbin = (int)((param_2 - base) * inv);        // toward zero
int ybin = (int)((param_3 - base) * inv);        // toward zero

int rows = *(int*)(engine + 0x10);
int cols = *(int*)(engine + 0x14);

if (xbin >= 0 && xbin < rows &&
    ybin >= 0 && ybin < cols)
{
    byte* lut = *(byte**)(engine + 0x3dc);

    // INDEXING: cols * xbin + ybin   — NOT rows * xbin
    // Decompile: *(byte*)( *(int*)(engine+0x14) * iVar3
    //                        + *(int*)(engine+0x3dc) + iVar2 ) & 7
    byte b = lut[cols * xbin + ybin] & 7;        // low 3 bits only

    // Unreachable after &7 (Ghidra keeps the assert path):
    //   if (b > 7) { VOG_DEBUG_STOP; return g_flOne; }
    // Do not port as live control flow.

    return *(float*)(engine + 0x344 + (signed char)b * 4);  // factors[b]
}

return *(float*)(engine + 0x344);                // factors[0] — OOR fallback
```

### Critical details

1. **Cast toward zero**, not floor. Values slightly below `base` still yield bin `0` while
   `(value - base) * inv > -1.0`. True low-side OOR needs `value <= base - scale = -0.5 * scale`.
2. **`& 7`** quantizes each LUT byte to one of 8 factor slots; upper 5 bits ignored.
3. **Stride is `cols` (`+0x14`)**, index `cols * xbin + ybin` (row-major, row = X bin).
   `rows` (`+0x10`) is **only** the X upper bound — never used as the multiply stride.
4. **Two distinct fallbacks:**
   - engine **disabled** (`+0x0c == 0`) → **`1.0`** (`g_flOne`)
   - bin **out of range** → **`factors[0]`** (`+0x344`)
   These are not interchangeable.
5. Grid: origin at `scale * 0.5`, each bin is exactly `scale` wide (same scale for X and Y).

---

## Conflicts vs `engine-torque-spec.md`

| Item | `engine-torque-spec.md` §2 | This re-verify | Verdict |
|---|---|---|---|
| Disabled → `1.0` | yes | yes | **match** |
| OOR → `factors[0]` | yes | yes | **match** |
| `base = scale * 0.5`, `inv = 1/scale` | yes | yes | **match** |
| Cast toward zero | yes | yes | **match** |
| Bounds: `0 ≤ x < rows`, `0 ≤ y < cols` | yes | yes | **match** |
| Index `cols * xbin + ybin` (not rows) | yes (`lut[engine.cols * xbin + ybin]`) | yes (`*(int*)(+0x14) * iVar3 + …`) | **match** |
| `& 7` then `factors[b]` | yes | yes | **match** |
| Unreachable `b > 7` assert → `1.0` | noted, do not port | present in decompile | **match** |
| Constants `0.5` / `1.0` at `0xa0f298` / `0xa0f2a0` | documented | re-read LE bytes confirmed | **match** |

**No algorithm conflict with `engine-torque-spec.md` §2.** Spec is bit-exact for this function.

### Non-spec note (Ghidra plate only)

The **plate comment** attached to the decompile incorrectly states:

> `byte = table[rows * iVar3 + iVar2] & 7`

The **pseudocode body** uses `*(int *)(param_1 + 0x14)` (cols), not `+0x10` (rows).  
`engine-torque-spec.md` already documents the correct cols stride. Trust the body / this note, not the plate string.

---

## Golden vectors (unchanged from spec; hand-derived)

Config: `enabled=1, rows=4, cols=4, scale=100` → `base=50, inv=0.01`.  
`factors = [0.10, 0.20, 0.40, 0.60, 1.00, 1.10, 1.20, 1.60]`.  
LUT row-major bytes: row0 `0,1,2,3`; row1 `4,5,6,7`; row2 `0,1,2,3`; row3 `4,5,6,7`.

| rpm | thr | xbin | ybin | idx=`4*x+y` | &7 | return | path |
|----:|----:|-----:|-----:|------------:|---:|-------:|---|
| 240 | 350 | 1 | 3 | 7 | 7 | 1.60 | hit |
| 50 | 50 | 0 | 0 | 0 | 0 | 0.10 | origin |
| 250 | 250 | 2 | 2 | 10 | 2 | 0.40 | hit |
| 30 | 350 | 0 | 3 | 3 | 3 | 0.60 | trunc→0 (not OOR) |
| 600 | 350 | 5 | 3 | — | — | 0.10 | OOR → factors[0] |
| * | * | — | — | — | — | 1.00 | disabled |

Emulation remains impractical (pointer-heavy `__thiscall` + indirect LUT + x87 return); goldens stay hand-derived from this verified decompile.
