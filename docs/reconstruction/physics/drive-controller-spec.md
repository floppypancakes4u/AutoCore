# Drive Controller Port Spec — `CVOGVehicle::MoveToTarget3DPoint` @ `0x004fc650`

**Target:** `autoassault.exe` (retail Auto Assault client), image base `0x00400000`.
**Tooling:** Ghidra project *AA-decode* via `ghidra-mcp`. Read-only.
**Scope:** Bit-exact reconstruction of the AI drive-axis generator. This function is **pure input
generation** — it reads an AI aim point + chassis pose/velocity and writes three drive axes
(`throttle +0x614`, `steer +0x618`, `sharp/handbrake +0x61c`). It never writes chassis position.
Everything below was re-pulled fresh (decompile + disassembly + raw constant memory reads).

> **Sign-convention warning (correction to `docs/NPCDriving.md`):** in this function the internal
> `base` direction is **-1.0 for normal forward driving** and **+1.0 for reverse**. Forward motion
> therefore produces a **negative** throttle value at `+0x614`, and steering is `-1 * lateral * 2`.
> These are the raw values the retail physics layer consumes, so a port that streams/feeds these
> axes **must preserve the exact signs** (do not "normalize" forward to +1).

---

## 1. Signature & I/O

```c
// __thiscall
undefined4 CVOGVehicle::MoveToTarget3DPoint(
    int   this,          // param_1  — vehicle entity
    float acceptDist,    // param_2  — arrival/accept planar radius
    float cruiseScale,   // param_3  — cruise speed multiplier (corner slowdown etc.)
    void* aim_UNUSED,    // param_4  — NOT read; aim comes from this+0x190 (caller pre-writes it)
    char  allowReverse); // param_5  — enable reverse when target is behind
```

**Reads**

| Source | Meaning |
|--------|---------|
| `this+0x08` (`iVar4`) | physics/motion object ptr. Must be non-null to run. |
| `this+0x101` | disabled flag — must be `0` to run. |
| `this+0x103` | "always drive" override — if `!=0`, skip the arrival brake even inside `acceptDist`. |
| `this+0x190 / +0x194 / +0x198` | **aim point** `A = (Ax, Ay, Az)`. |
| `*(this+8)+0x3c` then `+0xb0` | chassis **position** `P`. |
| `*(this+8)+0x3c` then `+0x30` | chassis **basis/transform** (fed to `FUN_004e8ad0`/`FUN_004e8a40`). |
| `*(this+8)+0x3c` then `+0x40/+0x44/+0x48` | chassis **velocity** `V = (Vx,Vy,Vz)`. |
| `*(*(this+4)+4)+this+0xb0` (`+0xb4` byte) | steer-lock flags (gate on writing `+0x618`). |

**Writes**

| Offset | Axis | Type |
|-------:|------|------|
| `+0x614` | throttle | float |
| `+0x618` | steer | float (gated — see §4) |
| `+0x61c` | sharp / handbrake-assist | byte (0/1) |
| `+0x101`, `+0x109` | cleared to 0 on the drive path | byte |

Basis extractors: `FUN_004e8ad0(basis,&out)` → **right** vector `R`; `FUN_004e8a40(basis,&out)` →
**forward** vector `F` (both unit). (Entry also calls profiler `FUN_0076cf00("CVOGVehicle::
MoveToTarget3DPoint")` / `FUN_0076cef0`; SEH frame — ignore for the port.)

---

## 2. Constants (verified — raw memory reads)

| Address | Symbol (Ghidra) | Value | Role |
|---------|-----------------|------:|------|
| `0x00a0f718` | `DAT_00a0f718` | `0.01` | steer deadband on `|lateral|` |
| `0x00a10e74` | (steer gain) | `2.0` | steer gain: `steer = base*lateral*2.0` |
| `0x00a0f2a0` | `g_flOne` | `1.0` | +clamp bound / base(+) |
| `0x00aaa668` | `DAT_00aaa668` | `-1.0` | -clamp bound / base(-) / cruise negate |
| `0x009cd238` | `_DAT_009cd238` | `-0.4` (double) | reverse gate on forward-alignment |
| `0x00aaa688` | `DAT_00aaa688` | `5.0` | speed gate to enable throttle scaling |
| `0x00a0f694` | `DAT_00a0f694` | `30.0` | near-target distance for throttle ease |
| `0x00aaab14` | `_DAT_00aaab14` | `0.033333` (`1/30`) | near-target ease scale |
| `0x00a0f730` | `g_flMultiKillCountBlend` | `0.1` | min `cruiseScale` to apply cruise mul |
| `0x00aaa7a4` | `DAT_00aaa7a4` | `15.0` | sharp speed gate |
| `0x00a0f710` | `DAT_00a0f710` | `0.70` | sharp `|lateral|` threshold |

Note: `0x00a10e74` (`2.0`) is the *steer gain* here; it is the same constant the physics layer
reuses elsewhere as a throttle ramp rate — do not conflate roles.

---

## 3. Exact reconstruction (port-ready pseudocode)

```c
// Preconditions: this+8 != 0 AND this[0x101]==0. Otherwise return 0 (no-op).

vec3 d      = A - P;                                  // aim - chassisPos
float distXZ = sqrt(d.x*d.x + d.z*d.z);              // planar distance (Y ignored)

// ---- ARRIVAL GATE ----
if (distXZ <= acceptDist && this[0x103] == 0) {
    this[0x61c] = 1;                 // handbrake on
    VehicleEntity_SetLongitudinalInput(this, 0);      // throttle input -> 0  (FUN_004f5650)
    VehicleEntity_PushDriveAxesToController(this);
    return 0;                        // steer (+0x618) left UNCHANGED
}
// (strictly: gate is `acceptDist < |distXZ|  ||  this[0x103]!=0` -> drive)

// ---- NORMALIZE (full 3D) ----
float m2  = d.x*d.x + d.y*d.y + d.z*d.z;
float inv = (m2 != 0.0f) ? 1.0f/sqrt(m2) : 0.0f;
vec3  dir = d * inv;                                   // may be zero vector if m2==0

// ---- PROJECTIONS ----
float lateral  = dot(R, dir);      // steer signal            (fVar9)
float fAlign   = dot(F, dir);      // forward alignment [-1,1](fVar10)
float speed    = length(V);        // |velocity|              (fVar6)
float fwdSpeed = dot(V, F);        // signed forward speed    (fVar5)

// ---- BASE DIRECTION SIGN ----
float base = (allowReverse != 0 && fAlign < -0.4f) ? +1.0f    // reverse (aim >~114deg behind)
                                                    : -1.0f;   // forward (normal)

// ---- STEERING ----
if (fabs(lateral) >= 0.01f) {                         // outside deadband
    float steer = clamp(base * lateral * 2.0f, -1.0f, +1.0f);
    // write gated: skip if steer-lock flags set
    int wobj = *(int*)(*(int*)(this+4)+4 + this + 0xb0);
    if (wobj == 0 || (*(byte*)(wobj+0xb4) & 0xC7) == 0)   // bits 0x1|0x2|0x4|0x40|0x80
        this[0x618] = steer;
} else {                                              // inside deadband
    if (fAlign >= 0.0f)
        VehicleEntity_SetSteerInput(this, 0);         // straighten
    else
        VehicleEntity_SetSteerInput(this, (lateral > 0.0f) ? +1.0f : -1.0f); // reverse-align spin
}

// ---- THROTTLE ----
float thr = base;
if (speed > 5.0f) {                                   // only scale once actually moving
    if (distXZ > 0.0f && distXZ < 30.0f)
        thr *= distXZ * (1.0f/30.0f);                 // ease down near target
    if (cruiseScale > 0.1f) {
        float cs = (fwdSpeed < 0.0f) ? -cruiseScale : cruiseScale; // face-away -> reverse cruise
        thr *= cs;
    }
}
this[0x101] = 0;
this[0x109] = 0;
this[0x614] = thr;

// ---- SHARP / HANDBRAKE-ASSIST ----
this[0x61c] = (speed > 15.0f && fabs(lateral) > 0.70f) ? 1 : 0;

VehicleEntity_PushDriveAxesToController(this);
return 1;
```

### Branch-structure notes
- **Deadband vs steer:** `|lateral| >= 0.01` → proportional clamped steer. Inside the deadband,
  steer is either zeroed (facing toward aim, `fAlign>=0`) or driven **hard to ±1** in the direction
  of `lateral`'s sign when facing away (`fAlign<0`) — this is the pivot/reverse-realign spin.
- **Reverse handling:** only engages when `allowReverse` **and** the aim is more than ~114° behind
  (`fAlign < -0.4`). That flips `base` to `+1`. The cruise block separately negates `cruiseScale`
  when the vehicle's *current* forward velocity is negative (`fwdSpeed < 0`).
- **Near-target ease:** below 30 u planar distance the throttle magnitude scales by `distXZ/30`
  (linear taper to ~0 at the target). Applied only when `speed > 5`.
- **Cruise scale:** applied only when `cruiseScale > 0.1` and `speed > 5`.
- **Sharp:** independent of the above; pure `speed > 15 && |lateral| > 0.7`.
- **Low speed (`speed <= 5`):** throttle is exactly `base` (±1, full) — no ease, no cruise.

---

## 4. Steering write gate (`+0x618`)

The proportional steer value is written to `+0x618` **only** if the steer-control object
(`*(*(this+4)+4)+this+0xb0`) is null, or its flag byte at `+0xb4` has **none** of bits
`0x01|0x02|0x04|0x40|0x80` (mask `0xC7`) set. If any of those bits are set the steer output is
suppressed (wheel/steer locked or externally overridden). The deadband branch instead calls
`VehicleEntity_SetSteerInput` (`0x004f5620`) directly and is **not** subject to this gate.

---

## 5. Golden / expected vectors

**Emulation status: NOT performed (impractical).** `emulate_function` would require a fully wired
pointer graph (`this+8 → +0x3c → {+0x30 basis, +0x40 vel, +0xb0 pos}`, aim at `this+0x190`, the
`+0x4/+0xb0` steer-object chain), the profiler/SEH entry calls, and two basis-extractor sub-calls to
execute — and the results are written to *memory* (`+0x614/+0x618/+0x61c`), not returned in
registers. Setup cost/fragility outweighs the value, so vectors below are **hand-derived** from the
§3 model and are exact for the stated inputs.

Common setup for all scenarios: chassis at origin `P=(0,0,0)`, right `R=(1,0,0)`, forward
`F=(0,0,1)`, `acceptDist=3`, `allowReverse=1`, `cruiseScale=1.0` (>0.1). Steer-lock flags clear.

| # | Scenario | aim `A` | vel `V` | lateral | fAlign | fwdSpd | speed | base | **throttle +0x614** | **steer +0x618** | **sharp +0x61c** |
|---|----------|---------|---------|--------:|-------:|-------:|------:|-----:|--------------------:|-----------------:|:---:|
| 1 | Straight ahead | (0,0,20) | (0,0,10) | 0.000 | +1.000 | +10 | 10 | -1 | **-0.6667** (`-1·20/30`) | **0** (deadband, fAlign≥0) | 0 |
| 2 | Hard left (45°) | (-14,0,14) | (0,0,8) | -0.707 | +0.707 | +8 | 8 | -1 | **-0.660** (`-1·19.8/30`) | **+1.000** (clamp `-1·-0.707·2=+1.41`) | 0 |
| 3 | Facing away → reverse | (0,0,-20) | (0,0,0) | 0.000 | -1.000 | 0 | 0 | +1 | **+1.000** (speed≤5 → no scale) | **-1.000** (deadband spin, lateral≤0) | 0 |
| 4 | High-speed sharp (right) | (20,0,10) | (0,0,20) | +0.894 | +0.447 | +20 | 20 | -1 | **-0.745** (`-1·22.36/30`) | **-1.000** (clamp `-1·0.894·2=-1.79`) | **1** |

Working:
- **#1** distXZ=20 (<30) → throttle `-1·20/30=-0.6667`; cruise `fwdSpeed>0` keeps +1.0; `|lateral|<0.01`
  and `fAlign≥0` ⇒ steer straightened to 0; speed 10≤15 ⇒ sharp 0.
- **#2** distXZ=√392≈19.799 → `-19.799/30=-0.660`; steer raw `+1.414`→clamp `+1.0`; sharp: speed 8≤15 ⇒ 0.
- **#3** `fAlign=-1 < -0.4` and reverse allowed ⇒ base `+1`; speed 0≤5 ⇒ throttle stays `+1`; deadband,
  `fAlign<0`, `lateral(0) not >0` ⇒ steer `-1`; sharp 0.
- **#4** distXZ=√500≈22.361 → `-22.361/30=-0.745`; steer raw `-1.789`→clamp `-1.0`; sharp:
  speed 20>15 **and** `|lateral|=0.894>0.7` ⇒ 1.

Port validation: feed these six input fields (P,R,F,A,V + params) through the §3 pseudocode and
assert the three outputs match to ~1e-4. Because outputs are deterministic float arithmetic with the
§2 constants, a correct port reproduces them bit-for-bit (modulo float rounding order in the dot
products / sqrt).
