# Verified: `CVOGVehicle::MoveToTarget3DPoint` @ `0x004fc650`

| Field | Value |
|-------|-------|
| Program | `autoassault.exe` (image base `0x00400000`) |
| Ghidra name | `FUN_004fc650` |
| String tag | `"CVOGVehicle::MoveToTarget3DPoint"` (profiler entry) |
| Body | `004fc650` – `004fca6d` |
| Calling conv | `__thiscall` |
| Tooling | ghidra-mcp `decompile_function` + `read_memory` (this pass) |
| Spec reconciled | `docs/reconstruction/physics/drive-controller-spec.md` |
| Date | 2026-07-15 |

**Result: MATCH** — decompile + raw constant memory agree with the port spec. No binary conflict.

---

## 1. Purpose

AI **drive-axis generator only**. Reads aim point + chassis pose/velocity; writes:

| Offset | Axis | Type |
|-------:|------|------|
| `+0x614` | throttle | float |
| `+0x618` | steer | float (gated) |
| `+0x61c` | sharp / handbrake-assist | byte 0/1 |
| `+0x101`, `+0x109` | cleared to 0 on drive path | byte |

**Never writes chassis position.** Downstream `VehicleEntity_PushDriveAxesToController` (`FUN_004fbc10`) pushes axes into the controller / Havok path.

---

## 2. Signature (from decompile)

```c
// __thiscall  —  Ghidra shows FUN_004fc650(int param_1, float param_2, float param_3, undefined4 param_4, char param_5)
undefined4 CVOGVehicle::MoveToTarget3DPoint(
    int   this,          // param_1
    float acceptDist,    // param_2
    float cruiseScale,   // param_3
    void* aim_UNUSED,    // param_4 — NOT read; aim is this+0x190..0x198
    char  allowReverse); // param_5
```

Return: `1` on drive path, `0` on no-op / arrival brake.

---

## 3. Constants — `read_memory` this pass

| Address | LE hex | Decoded | Role in formula |
|---------|--------|--------:|-----------------|
| `0x00a0f718` | `0ad7233c` | **0.01** f32 | steer deadband: `\|lateral\| >= 0.01` |
| `0x00a10e74` | `00000040` | **2.0** f32 | steer gain (decomp symbol `g_flLevelUpUiBase_Inferred`) |
| `0x00aaa668` | `000080bf` | **−1.0** f32 | forward `base`, clamp floor, cruise negate |
| `0x009cd238` | `9a9999999999d9bf` | **−0.4** f64 | reverse gate: `fAlign < −0.4` |
| `0x00aaa688` | `0000a040` | **5.0** f32 | speed gate before throttle scale |
| `0x00a0f694` | `0000f041` | **30.0** f32 | near-target distance for ease |
| `0x00a0f730` | `cdcccc3d` | **0.1** f32 | min `cruiseScale` (`g_flMultiKillCountBlend`) |
| `0x00aaa7a4` | `00007041` | **15.0** f32 | sharp speed gate |
| `0x00a0f710` | `3333333f` | **0.7** f32 | sharp `\|lateral\|` threshold |

Also used in decompile (not on the required list; confirmed for completeness):

| Address | LE hex | Decoded | Role |
|---------|--------|--------:|------|
| `0x00aaab14` | `8988083d` | **≈0.033333** (`1/30`) | near-target ease scale `_DAT_00aaab14` |
| `g_flOne` | (symbol) | **1.0** | reverse `base` / clamp high |

All required plate addresses match `drive-controller-spec.md` §2.

---

## 4. Decompile-derived control flow

### 4.1 Preconditions

```
if (this+8 == 0 || this[0x101] != 0) → return 0;   // no physics obj / disabled
```

Profiler: `FUN_0076cf00("CVOGVehicle::MoveToTarget3DPoint")` / `FUN_0076cef0` + SEH — ignore for port.

### 4.2 Geometry

```
P   = *(this+8)+0x3c → +0xb0     // chassis position
A   = this+0x190 / +0x194 / +0x198
d   = A − P
distXZ = sqrt(d.x² + d.z²)       // Y ignored for planar distance (fVar2)
```

Arrival (else branch of drive gate):

```
if (!(acceptDist < |distXZ| || this[0x103] != 0)) {
    this[0x61c] = 1;
    VehicleEntity_SetLongitudinalInput(0);   // throttle → 0
    VehicleEntity_PushDriveAxesToController();
    return 0;   // +0x618 NOT touched
}
```

### 4.3 Normalize + projections

```
inv    = (m2 != 0) ? 1/sqrt(d·d) : 0     // full 3D, m2 = |d|²
R      = FUN_004e8ad0(basis)             // right  (local_30)
F      = FUN_004e8a40(basis)             // forward (local_40)
basis  = *(this+8)+0x3c + 0x30

lateral  = R · (d * inv)                 // fVar9
fAlign   = F · (d * inv)                 // fVar10
V        = *(this+8)+0x3c + 0x40..+0x48
speed    = |V|                           // fVar6
fwdSpeed = V · F                         // fVar5
```

### 4.4 **BASE DIRECTION — throttle sign inverted**

Decompile (comma-operator form preserved):

```c
// fVar10 = fAlign; DAT_00aaa668 = -1; g_flOne = +1; _DAT_009cd238 = -0.4 (double)
if ((param_5 == '\0') || (fVar8 = g_flOne, (float)_DAT_009cd238 <= fVar10)) {
    fVar8 = DAT_00aaa668;   // -1.0
}
// else: fVar8 stays g_flOne (+1.0)
```

Equivalent:

```
base = (allowReverse != 0 && fAlign < -0.4f) ? +1.0f   // reverse
                                              : -1.0f;  // forward (NORMAL)
```

| Mode | `base` | Typical throttle at `+0x614` |
|------|-------:|------------------------------|
| **Forward (default)** | **−1.0** | **negative** |
| Reverse (allowed + aim behind ~114°) | **+1.0** | **positive** |

> **Port rule:** do **not** normalize forward throttle to +1. Retail physics consumes the inverted sign.
> Steering proportional path is `clamp(base * lateral * 2.0, −1, +1)` — same `base`, so forward
> steering is effectively `−lateral * 2` before clamp.

### 4.5 Steering

Outside deadband (`|lateral| >= 0.01`):

```
steer = clamp(base * lateral * 2.0, -1.0, +1.0)
// write +0x618 only if steer-lock object null OR (byte+0xb4 & 0xC7) == 0
// bits checked individually: 0x01|0x02|0x04|0x40|0x80
wobj = *(*(this+4)+4 + this + 0xb0)
```

Inside deadband:

```
if (fAlign >= 0)  VehicleEntity_SetSteerInput(0);          // straighten
else              VehicleEntity_SetSteerInput(lateral > 0 ? +1 : -1);  // reverse-align spin
// deadband path NOT subject to the 0xC7 gate
```

(`VehicleEntity_SetSteerInput` / `SetLongitudinalInput` appear without `this` in decompiler — thiscall artifact.)

### 4.6 Throttle

```
thr = base;                                    // starts at ±1
if (speed > 5.0f) {
    if (distXZ > 0.0f && distXZ < 30.0f)
        thr *= distXZ * (1.0f/30.0f);          // fVar7 is 0.0 after steer block → "0 < distXZ"
    if (cruiseScale > 0.1f) {
        if (fwdSpeed < 0.0f)
            cruiseScale *= -1.0f;              // fVar11 was saved DAT_00aaa668
        thr *= cruiseScale;
    }
}
this[0x101] = 0;
this[0x109] = 0;
this[0x614] = thr;
```

Low speed (`speed <= 5`): throttle is exactly `base` (±1), no ease / no cruise mul.

### 4.7 Sharp / handbrake-assist

```
this[0x61c] = (speed > 15.0f && fabs(lateral) > 0.70f) ? 1 : 0;
```

Then `VehicleEntity_PushDriveAxesToController(); return 1;`

---

## 5. Reconciliation vs `drive-controller-spec.md`

| Item | Spec | This pass | Status |
|------|------|-----------|--------|
| Forward `base` | −1.0 | −1.0 (`DAT_00aaa668` when reverse gate fails) | **MATCH** |
| Reverse `base` | +1.0 when `allowReverse && fAlign < −0.4` | same | **MATCH** |
| Steer gain | 2.0 @ `0xa10e74` | LE `00000040` → 2.0 | **MATCH** |
| Deadband | 0.01 @ `0xa0f718` | LE `0ad7233c` → 0.01 | **MATCH** |
| Speed / near / cruise / sharp gates | 5 / 30 / 0.1 / 15 / 0.7 | all `read_memory` match | **MATCH** |
| Reverse double | −0.4 @ `0x9cd238` | LE double `…d9bf` → −0.4 | **MATCH** |
| Steer lock mask | 0xC7 bits | decompile checks 1\|2\|4\|0x40\|0x80 | **MATCH** |
| Arrival leaves steer | unchanged | decompile only sets thr=0 + sharp=1 | **MATCH** |

**Note:** Older narrative in `docs/NPCDriving.md` §5 used a non-inverted “revSign” sketch. Binary + this decompile + `drive-controller-spec.md` are authoritative: **forward base = −1**.

---

## 6. Emulation

**Not performed.** Pointer graph (`this+8 → +0x3c → basis/vel/pos`, aim, steer-lock chain) + SEH/profiler + two basis extractors make `emulate_function` impractical; outputs land in memory, not registers. Hand-derived goldens in `drive-controller-spec.md` §5 remain valid for the §4 model.

---

## 7. Callees (drive path)

| Address / name | Role |
|----------------|------|
| `FUN_0076cf00` / `FUN_0076cef0` | profiler enter/leave |
| `FUN_004e8ad0` | right vector from basis |
| `FUN_004e8a40` | forward vector from basis |
| `VehicleEntity_SetSteerInput` (`0x004f5620`) | deadband steer write |
| `VehicleEntity_SetLongitudinalInput` (`0x004f5650`) | arrival thr=0 |
| `VehicleEntity_PushDriveAxesToController` (`0x004fbc10`) | axes → controller |

---

## 8. Port checklist (no C# in this pass)

1. Preserve **negative throttle for forward** (`base = −1`).
2. Clamp order: compute `base * lateral * 2`, then clamp `[−1,+1]`.
3. Apply near-target ease and cruise only when `speed > 5`.
4. Sharp independent of thr/steer: `speed > 15 && |lateral| > 0.7`.
5. Do not invent sign flips when streaming ghost / soft-path inputs — feed retail-signed axes.
