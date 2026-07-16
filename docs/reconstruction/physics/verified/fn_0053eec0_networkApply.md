# VERIFIED — `FUN_0053eec0` network pose/velocity apply @ `0x53eec0`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x53eec0`, `0x53eb90`, `0x53e020`, `0x504c70`, `0x5057c0`,
`0x40d2a0`, `0x404dc0`, `0x40d040`, `0x40d260`, `0x568100`, `0x53f1f0`, `0x4c6360`;  
`read_memory` @ `0x9d000c`, `0x9d0010`, `0xa0f718`, `0xb04610`, `0xa0f298`, `0x9d2f1c`;  
`get_function_xrefs` @ `0x53eec0` / `0x53eb90`.

**Did not use** `disassemble_bytes`. Emulation skipped (pointer-heavy entity / physics graph).

**Scope:** client-side application of **network pose + linear/angular velocity** for entities that
own a physics shell (vehicles). Phase 6 ghost streaming must emit values this function can apply.

**Not this file:** server `GhostVehicle.PackUpdate` layout (see
[`server_ghost_pack_notes.md`](server_ghost_pack_notes.md)); thr/steer push
(`VehicleEntity_PushDriveAxesToController` @ `0x4fbc10`).

---

## 1. Identity & call graph

| Item | Value |
|------|------:|
| Entry | `0x53eec0` |
| Symbol (Ghidra) | `FUN_0053eec0` |
| Convention | `__thiscall` |
| `this` (`param_1`) | entity (`CVOGVehicle` / shared CVOG object) as `int*` |
| Role | Soft-buffer **or** hard-write network **pos / rot / vel / angVel** |

### 1.1 Signature (decompile)

```text
void __thiscall FUN_0053eec0(
    int   *this,          // entity
    float *param_2,       // position  xyzw (w usually 0)
    float *param_3,       // rotation  xyzw quaternion
    float *param_4,       // linear velocity xyzw
    float *param_5,       // angular velocity xyzw
    float  param_6)       // integrateDt (seconds); 0 = no soft integrate
```

### 1.2 Callers (code xrefs)

| Caller | Addr | Role |
|--------|------|------|
| `Vehicle_setDrivingInputs` | `0x504c70` | **Primary vehicle ghost path** — writes thr/steer/handbrake, `PushDriveAxesToController`, then this |
| `FUN_005057c0` | `0x5057c0` | Same shell; forces thr=0, steer=0, handbrake=1, then apply |
| `FUN_004c6360` | `0x4c6360` | Reaction / teleport-style apply (pos/rot/vel/ang from reaction block + `param_3` as dt) |

`Vehicle_setDrivingInputs` (abbreviated):

```text
entity+0x614 = thr
entity+0x618 = steer
entity+0x61c = handbrake/sharp byte
VehicleEntity_PushDriveAxesToController()
FUN_0053eec0(pos, rot, vel, angVel, integrateDt)   // integrateDt = pack param_10
```

Gate: only runs if `entity+0x08 != 0` (physics object pointer present). Type-6 body may call
`FUN_0053d970(0)` first (motion-mode cleanup).

---

## 2. Constants (`read_memory`)

| Symbol / address | LE bytes | float32 / int | Role |
|------------------|----------|---------------|------|
| `DAT_009d000c` @ `0x009d000c` | `00 00 70 41` | **`15.0`** | Soft-path **teleport** if `‖pos_net − pos_live‖ > 15` |
| `_DAT_009d0010` @ `0x009d0010` | `00 00 00 34` | **`≈1.192e-7`** (f32 ε) | Hard-path gate: write only if `‖pos‖ > ε` |
| `DAT_00a0f718` @ `0x00a0f718` | `0a d7 23 3c` | **`0.01`** | Soft-path: if `‖vel‖ < 0.01`, buffer linVel ← **zero** |
| `DAT_00b04610..1c` @ `0x00b04610` | 16× `00` | **`(0,0,0,0)`** | Zero vec4 used for sub-threshold vel / clears |
| `DAT_00a0f298` @ `0x00a0f298` | `00 00 00 3f` | **`0.5`** | Soft integrate: `½ ω` in quaternion step (`FUN_0053eb90`) |
| `_DAT_009d2f1c` @ `0x009d2f1c` | `6f 12 83 3a` | **`0.001`** | Unit-quat gate in `FUN_00568100`: `||q|−1| < 0.001` |
| Immediate `0x18ff` | — | **6399** | Soft-buffer age gate in `FUN_0053eb90` vs `g_dwClientTickMs − entity+0x14` (ms) |
| `g_flZero` / `g_flOne` | named | `0` / `1` | dt-zero early out; mass = `1/invMass` |

**Teleport distance is 15 world units** — matches prior NPC notes.  
**Velocity floor is 0.01**, not zero — tiny residual wire vel is discarded into the zero plate.

---

## 3. Control flow (exact)

```text
phys = entity[2]                    // entity+0x08 physics object*
bodyInvMass = *(float*)(*(phys+0x3c) + 0x2c)   // chassis RB +0x2c

if (phys != 0
    && bodyInvMass != 0
    && (1.0 / bodyInvMass) != 0) {

    notFullyReady = (phys+0x40 == 0) || (phys+0x08 == 0)

    if (notFullyReady) {
        // ========== SOFT PATH ==========
        // stamp entity+0x14 = g_dwClientTickMs; entity+0x10 byte = 1
        // ensure dead-reckon buffer at entity+0x28 (FUN_0053e020 → 0x40 bytes)
        // buffer.pos  = net pos
        // buffer.vel  = (|netVel| >= 0.01) ? netVel : 0
        // buffer.rot  = net rot   IFF FUN_00568100(rot) says unit quat OK
        // buffer.ω    = net angVel   (always)
        // if ‖netPos − livePos‖ > 15:
        //     entity vtbl+0x40()
        //     phys setPosition(netPos)      // FUN_0040d2a0 → body vtbl+0x40
        //     ApplyImpulseVector(netVel)    // CVOGPhysics @0x40d260 → body vtbl+0x50
        //     phys setRotation(netRot)      // FUN_00404dc0 → body vtbl+0x44
        //     phys setAngVel(netAngVel)     // FUN_0040d040 → body vtbl+0x54
        // if integrateDt == 0: return
        // FUN_0053eb90(..., integrateDt)    // advance buffer by vel/ω
        return
    }
    // fully ready → fall through
}

// ========== HARD PATH ==========
if (‖netPos‖ > ~1.19e-7) {
    entityVisualPos (+0x84 chain) = net pos   // xyzw
    entityVisualRot (+0x94 chain) = net rot   // xyzw
}
// no vel / angVel write on hard path
return
```

### 3.1 “Fully ready” vs soft

| Condition | Path |
|-----------|------|
| No physics object, or invMass 0, or mass `1/invMass` is 0 | **Hard** |
| Physics present + valid mass, and (`phys+0x40==0` **or** `phys+0x08==0`) | **Soft** |
| Physics present + valid mass, and `phys+0x40≠0` **and** `phys+0x08≠0` | **Hard** (fall-through) |

So when Havok is **fully ready**, network apply **does not** soft-buffer and **does not** push
vel/ω into the rigid body. It only overwrites the **entity visual pose slots**. Between packs,
motion is whatever thr/steer + VehicleAction + Havok produce.

When physics exists but is **not** fully ready, the soft buffer is the interpolation target;
large errors hard-snap the body; optional `integrateDt` dead-reckons the buffer.

### 3.2 Live position source for the 15u test

```text
if (entity+0x08 == 0)
    livePos = *( *(entity+4)+4 + entity + 0x84 )   // entity visual pos
else
    livePos = *( *(phys+0x3c) + 0xb0 )             // chassis body world pos
```

Distance is **3-component** Euclidean on xyz (local copies of net x/y/z only).

---

## 4. Soft-buffer layout (`entity+0x28`, alloc `FUN_0053e020`)

Allocator zeros `0x40` bytes and sets **rotation.w = 1** (identity quat).

| Buffer off | Field | Written by soft path |
|-----------:|-------|----------------------|
| `+0x00..0x0c` | position xyzw | always from `param_2` |
| `+0x10..0x1c` | rotation xyzw | only if unit-quat gate passes |
| `+0x20..0x2c` | linear velocity xyzw | net vel if `‖v‖≥0.01`, else zero plate |
| `+0x30..0x3c` | angular velocity xyzw | always from `param_5` |

Entity side-effects on soft entry:

| Entity off | Write |
|-----------:|-------|
| `+0x10` (byte via `param_1+4`) | `1` — soft-apply active flag |
| `+0x14` | `g_dwClientTickMs` — freshness stamp (also used by airStab / integrate age) |
| `+0x28` | buffer pointer (`param_1[10]`) |

### 4.1 Rotation gate `FUN_00568100` @ `0x568100`

Accepts buffer rotation only when:

1. Internal validity helper `FUN_005d68b0` returns true, and  
2. `abs(‖q‖ − 1) < 0.001` (`_DAT_009d2f1c`).

**Non-unit wire quaternions are dropped from the soft buffer** (pos/vel/ω still apply). Hard path
does **not** run this gate — it writes rot blindly to `+0x94`.

---

## 5. Soft teleport helpers (error > 15)

All operate on the **physics object** (layout `+0x3c` body, `+0x40` / `+0x08` ready flags):

| Helper | Addr | Body vtbl slot | Meaning |
|--------|------|----------------|---------|
| `FUN_0040d2a0` | `0x40d2a0` | `+0x40` | set position (only if not fully ready) |
| `FUN_00404dc0` | `0x404dc0` | `+0x44` | set rotation (only if not fully ready) |
| `CVOGPhysics_ApplyImpulseVector` | `0x40d260` | `+0x50` | apply / set **linear** velocity vector from net vel |
| `FUN_0040d040` | `0x40d040` | `+0x54` | set **angular** velocity (no ready gate) |

Also: `(*entity_vtbl)[+0x40]()` before the phys writes (entity-level sync hook; decompile shows no
explicit args).

**Note:** vtbl `+0x50` is shared with “zero linear velocity” recovery paths; here the argument is
the **network velocity vector**, not a pure impulse magnitude. Treat as **snap linVel to wire**.

---

## 6. Soft integrate `FUN_0053eb90` @ `0x53eb90`

Sole other caller: `FUN_0053f1f0` @ `0x53f1f0` (periodic soft catch-up / blend toward buffer).

### 6.1 Algorithm (buffer, not live body)

```text
// half-ω
hx,hy,hz = buffer.ω.xyz * 0.5

// quaternion derivative step (ω ⊗ q), then
q ← normalize( q + (ω⊗q) * dt )

// position Euler
buffer.pos += buffer.vel * dt
```

Return `1` on success, `0` if skipped by age gate.

Age gate (when second float arg decompiles as `0`):

```text
if (flag == 0 && (g_dwClientTickMs - entity+0x14) > 0x18ff)  // 6399 ms
    return 0;   // buffer too stale — caller may zero vel/ω
```

### 6.2 Call-site arg order caveat

| Site | Decompile call | Body uses 1st float as |
|------|----------------|------------------------|
| `FUN_0053f1f0` | `FUN_0053eb90(dt, 0)` | **`dt`** — confirmed useful |
| `FUN_0053eec0` | `FUN_0053eb90(0, param_6)` after `if (param_6==0) return` | would no-op if literal |

**Phase 6 interpretation:** net-apply intends to advance the soft buffer by **`integrateDt`**
when non-zero (client unpack uses `ghostObj+0xBC × 0.001` ms→s per existing RE). The sibling
call in `FUN_0053f1f0` proves the integrate math is `pos+=v·dt` and `q+=½ω⊗q·dt`. If live soft
dead-reckon from ghost packs misbehaves, **confirm stack order at `0x53f150` in assembly** before
changing server dt encoding — do not invent a second formula.

---

## 7. Hard path — pose only

When hard path runs and `‖pos‖ > float ε`:

```text
visualPos = *( *(entity+4)+4 + entity + 0x84 )   // float[4]
visualRot = *( *(entity+4)+4 + entity + 0x94 )   // float[4]
*visualPos = *param_2   // full xyzw copy
*visualRot = *param_3
```

| Written | Not written |
|---------|-------------|
| Entity visual **position** | Chassis body `+0xb0` pos |
| Entity visual **rotation** | Body linVel `+0x40`, angVel `+0x50` |
| | Soft buffer at `+0x28` |
| | Network vel / angVel at all |

**Phase 6 consequence:** for foreign NPCs with fully ready Havok, **the wire quaternion is the
visible chassis orientation every pack**. Yaw-only server quats **overwrite** suspension-derived
pitch/roll. Pitch/roll **must** be on the streamed rotation (or the car will look flat on slopes).

Velocity on the wire still matters for:

1. Soft path (not fully ready) dead-reckon and 15u snap linVel  
2. Any other consumers of ghost vel before apply  
3. Server-side `IsMovingForPoseStream` / priority — not this function

---

## 8. Phase 6 notes — what the server must stream

Aligned with [`server_ghost_pack_notes.md`](server_ghost_pack_notes.md) + this apply function:

| Wire field | Soft path consumer | Hard path consumer | Server requirement |
|------------|--------------------|--------------------|--------------------|
| Position XYZ | buffer `+0x00`; 15u teleport ref | entity `+0x84` | Grounded chassis; keep packs dense enough that soft error ≪ **15u** |
| Rotation XYZW | buffer `+0x10` if **unit** | entity `+0x94` always | **Full stance** (yaw+pitch+roll); normalize (`‖q‖≈1` within 0.001) for soft accept |
| Velocity XYZ | buffer `+0x20` if `‖v‖≥0.01` | *ignored by apply* | Facing × speed for soft/dead-reckon; sub-0.01 treated as stop |
| Angular velocity XYZ | buffer `+0x30` always | *ignored by apply* | Full **ω** (pitch/roll rates on ramps); zeros freeze soft orientation integrate |
| integrate dt | `FUN_0053eb90` | n/a | Non-zero ms on ghost (`+0xBC`) so soft buffer advances between packs |
| thr / steer / flags | *not this fn* | *not this fn* | Via `Vehicle_setDrivingInputs` → `+0x614/618/61c` + PushDrive |

### 8.1 Soft vs hard — operational summary for NPC ghosts

```text
                    ┌─ phys not fully ready ──► SOFT buffer + optional 15u body snap
network pack ───────┤                            + optional integrate(dt)
                    └─ phys fully ready ──────► HARD entity +0x84/+0x94 only
                                                 thr/steer drive Havok until next pack
```

### 8.2 Corrections vs older map notes

| Source | Claim | This verification |
|--------|-------|-------------------|
| `0.8-struct-offsets.md` §5 | Soft when “authoritative / owns body” | Soft when phys exists **and not fully ready** (`+0x40==0` or `+8==0`) |
| `0.8-struct-offsets.md` §5 | Hard is only “physicsObj null” fallback | Hard also when **fully ready** |
| `NPCDriving.md` §7.2 | Soft if “usable physics shell” (ambiguous) | Soft = shell with mass **and** not-ready flags; ready shell → hard |
| Older “15u” | teleport threshold | **Confirmed `15.0`** @ `DAT_009d000c` |
| Vel gate | sometimes omitted | **Confirmed `0.01`** @ `DAT_00a0f718` |

---

## 9. Related addresses (quick index)

| Addr | Name / role |
|------|-------------|
| `0x53eec0` | **This function** — network apply |
| `0x53eb90` | Soft buffer integrate (`v·dt`, `½ω⊗q·dt`) |
| `0x53e020` | Alloc/init 0x40-byte soft buffer (identity quat) |
| `0x53f1f0` | Soft blend / catch-up toward buffer (calls integrate) |
| `0x504c70` | `Vehicle_setDrivingInputs` — thr/steer + apply |
| `0x4fbc10` | `VehicleEntity_PushDriveAxesToController` |
| `0x5f7720` | `VehicleNet_UnpackGhostVehicle` (upstream unpack; not re-decompiled this pass) |
| `0x568100` | Unit-quaternion accept gate |
| `0x40d260` | `CVOGPhysics_ApplyImpulseVector` (body vtbl `+0x50`) |

---

## 10. Confidence

| Claim | Level |
|-------|-------|
| Soft vs hard branch predicates (phys / invMass / ready bytes) | **Verified** decompile |
| Buffer field offsets and vel/rot gates | **Verified** decompile + constants |
| Teleport threshold 15.0, vel floor 0.01, f32 ε hard gate | **Verified** `read_memory` |
| Hard path writes only entity `+0x84` / `+0x94` | **Verified** decompile |
| Integrate formula `pos+=v·dt`, `q+=½ω⊗q·dt` | **Verified** via `FUN_0053eb90` |
| Net-apply → integrate stack arg order | **Suspect** in decompile; sibling call clear — flag for asm if needed |
| Exact semantics of entity vtbl `+0x40` at teleport | **Partial** (called; body not expanded) |
| `ghost+0xBC × 0.001` as `integrateDt` source | **Cross-ref** prior RE / Phase 6 pack notes (not re-traced in unpack this pass) |
