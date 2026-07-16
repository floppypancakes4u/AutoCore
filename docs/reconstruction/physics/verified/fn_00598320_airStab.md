# VERIFIED — `VehicleAction_airStabilization` @ `0x598320`

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x598320`; `read_memory` @ `0xa110d8`, `0x9d54a8`, `0xb04eb0`;  
`get_xrefs_to` / `get_bulk_xrefs`; `analyze_dataflow` @ `0x5985ad`;  
`batch_decompile` of helpers + continuous AVD `0x64d810`; call-site in `applyAction` @ `0x598650`.

**Scope of this file:** collision-window recovery / re-ground path only.  
**Not this file:** continuous `hkAngularVelocityDamper` AVD (see §4).

---

## 1. Identity & call graph

| Item | Value |
|------|------:|
| Entry | `0x598320` |
| Symbol (Ghidra) | `VehicleAction_airStabilization` |
| Convention | `__thiscall` |
| `this` (`param_1`) | Havok `VehicleAction` |
| `param_2` | input block ptr (forwarded into entity vtbl build-impulse; same family as applyAction `{dt, throttle…}`) |
| Sole caller | `VehicleAction_applyAction` @ `0x598650` — **unconditional call** after `VehicleAction_calcWheelTorque` (xref `0x599227`) |

Order in `applyAction` (relevant tail):

```
… upright-restore impulse (non-mode-0x02 branch) …
VehicleAction_calcWheelTorque();
VehicleAction_airStabilization();   // ← this fn
… airborne aero / boost block …
```

This function is **not** the continuous angular-velocity damper. It is a **time-windowed collision response + one-shot recovery** on the VehicleAction layer.

---

## 2. Gate structure (exact control flow)

```
entity = *(VehicleAction + 0x44)

if (entity+0x103 != 0)            return;   // forced-stop / dead
if (disabledFlag[+0x7e] != 0)     return;   // fully-disabled via data+0xa8 chain
// disabled chain (decompile):
//   *( *(*(entity+4)+4) + 0xa8 + entity ) + 0x7e

delta = g_dwClientTickMs - *(u32*)(entity + 0x14)

if (delta < 0x1900) {
    // ===== COLLISION WINDOW (active) =====
    *(u8*)(VehicleAction + 0x1c) = 1;       // in-collision flag
    if (speed > DAT_009d54a8) {
        // gather chassis pose/vel and try corrective impulse
        // (details §3.1)
    }
} else if (*(u8*)(VehicleAction + 0x1c) != 0) {
    // ===== POST-WINDOW RECOVERY (edge: was in window, now expired) =====
    *(u8*)(VehicleAction + 0x1c) = 0;
    // stabilizer reset · zero lin/ang vel · SetDriveAxes(0) · re-ground (§3.2)
}
return;
```

### 2.1 Collision window length — `0x1900`

| Token | Value | Interpretation |
|-------|------:|----------------|
| Immediate `0x1900` | **6400** | compared against `g_dwClientTickMs − entity+0x14` |
| Units | ms (symbol `g_dwClientTickMs`) | **6400 ms ≈ 6.4 s** |

**Corrections vs older notes:**

| Source | Claim | Verdict |
|--------|-------|---------|
| Ghidra plate on `0x598320` | “6400-tick (~1.07s @ 60Hz)” | **WRONG** — 6400 ms, not 64 frames / not 1.07 s |
| `docs/NPCDriving.md` §6.3 | “~6400 ticks / ~1.07s @ 60Hz” | **WRONG** — same error |
| `avd-airstab-spec.md` | 6400 ms | **CONFIRMED** |

Cross-check: `applyAction` idle gate uses the same stamp with `0x77a1` (= **30625** ≈ 30.6 s) — consistent with **millisecond** timebase, not 60 Hz frame counts.

`entity+0x14` is the last-collision timestamp written elsewhere (collide path); this function only **reads** it.

---

## 3. Behavior branches

### 3.1 In collision window (`delta < 0x1900`)

1. Set `VehicleAction+0x1c = 1`.
2. `FUN_0053e0b0` → float\* velocity (lazy cache at arg+0x28). Speed gate:

   ```
   speed = sqrt(v[0]² + v[1]² + v[2]²)
   if (speed <= DAT_009d54a8)  // ≈ 1.192e-7  — essentially “not moving”
       skip impulse path
   ```

3. If moving, gather chassis state (helpers return raw body pointers):

   | Local pack | Source | Meaning |
   |------------|--------|---------|
   | `local_20..14` (4×f32) | `FUN_00404c90` → `rb+0xb0` (or entity fallback) | **position** |
   | `local_50..44` (4×f32) | `rb+0x40` if physics present, else `DAT_00b04eb0` (zeros) | **linear velocity** |
   | `local_30..24` (4×f32) | `rb+0x50..0x5c` | **angular velocity** |
   | `local_40..34` (4×f32) | `FUN_00404a20` → `rb+0x30` | **orientation quaternion** |

   Rigid body path: `rb = *(*(entity+8) + 0x3c)` when `entity+8 != 0`.

4. Call **entity vtable +0x3c** (build/validate corrective impulse):

   ```
   ok = (*entity_vtbl)[+0x3c](
           *param_2,          // input / dt block
           &pos,              // local_20
           &linVel,           // local_50
           &angVel            // local_30
        );
   // stack also carries: &quat, 1.0f (0x3f800000), 0, 1  (decompiler stack slots)
   ```

5. If `ok != 0`:
   - `CVOGPhysics_ApplyImpulseVector(...)` — applies via physics object vtbl **`+0x50`**
   - cleanup `FUN_00404dc0`, `FUN_0040d040(&DAT_00b04eb0)`
   - **return** (early exit; no recovery branch this tick)

**What this path does *not* do:**

- Does **not** read `rlAVDNormalSpinDamping` / `rlAVDCollisionSpinDamping` / `rlAVDCollisionThreshold`
- Does **not** touch `DAT_00a110d8`
- Does **not** scale angular velocity by `(1 − rate·dt)` (that is continuous AVD @ `0x64d810`)

Impulse **construction math lives in the entity vtbl +0x3c method**, not in this function body. This fn only gates, packs state, and applies when the builder returns success.

---

### 3.2 Post-collision recovery (window just expired)

Entered only when `delta >= 0x1900` **and** `VehicleAction+0x1c != 0` (was in-window last tick).

| Step | Code evidence | Effect |
|-----:|---------------|--------|
| 1 | `VA+0x1c = 0` | clear in-collision flag |
| 2 | loop `i = 0; i < 0xC; i += 4` over `*(entity+0x260)` | **3 stabilizer slots**; if slot ≠ 0 → `FUN_0056a260(…, 0)` |
| 3 | physics vtbl **`+0x50`** with `&DAT_00b04eb0` | zero **linear** velocity (same slot `CVOGPhysics_ApplyImpulseVector` uses) |
| 4 | physics vtbl **`+0x54`** with `&DAT_00b04eb0` | zero **angular** velocity (same slot continuous AVD uses to write `w`) |
| 5 | `VehicleEntity_SetDriveAxes(0)` @ `0x4fbec0` | throttle/steer/handbrake cleared (`ent+0x614/618/61c`) then push to controller |
| 6 | **Re-ground** | see §3.3 |
| 7 | physics vtbl **`+0x40`** with new position (if not blocked by `phys+0x40` / `phys+8` gates) | write snapped world position |

Optional `FUN_005070b0` / `FUN_005070d0` pairs around vtbl calls are the usual “physics write enable / dirty” guards — not gameplay math.

---

### 3.3 Re-ground — **only** use of `DAT_00a110d8` in this function

Decompile (recovery path):

```
rb = *(*( *(VA+0x44) + 8 ) + 0x3c)
yStart = *(float*)(rb + 0xb4) + DAT_00a110d8     // Y + 10.0
h = CVOGMap_CastTerrainHeight( *(rb+0xb0) /*x*/, *(rb+0xb8) /*z*/ )  // yStart on stack
// write back:
pos.x = *(rb+0xb0)
pos.y = (float)h
pos.z = *(rb+0xb8)
// then vtbl+0x40 set position
```

**Dataflow confirmation** (`analyze_dataflow` @ `0x5985ad`):

```
005985ad  ADDSS  XMM0, dword ptr [0x00a110d8]   // FLOAT_ADD: rb.y + 10.0
005985cd  CALL   0x004cfe60                     // CVOGMap_CastTerrainHeight
…
0059863b  CALL   dword ptr [EDX+0x40]           // set position
```

`DAT_00a110d8` is consumed solely as the **raise height before the down-cast**. It is **not** added to angular velocity, **not** an AVD rate, and **not** used in the in-window impulse branch.

---

## 4. Continuous AVD is a **different** mechanism

| | Continuous AVD | This function (`0x598320`) |
|--|----------------|----------------------------|
| Entry | `hkAngularVelocityDamper_update` **`0x64d810`** | `VehicleAction_airStabilization` **`0x598320`** |
| When | every Havok sim step (framework action list) | every applyAction tick, gated by collision timer |
| Gate | `\|w\|² ≟ collisionThreshold²` (angular **speed**) | `g_dwClientTickMs − ent+0x14 < 0x1900` (time) |
| Params | damper `+0x08` normal / `+0x0c` collision rates, `+0x10` threshold (from clonebase `rlAVD*`) | **none** of the `rlAVD*` fields |
| Math | `w *= max(0, 1 − rate·dt)` on `rb+0x50..5c` | corrective impulse (in window) **or** zero vel + terrain snap (recovery) |
| `DAT_00a110d8` | unused | recovery Y-raise only |

Continuous AVD body (re-decompiled `0x64d810`), abbreviated:

```
w = (rb+0x50, rb+0x54, rb+0x58)
if (wx²+wy²+wz² <= thr²)  d = normalSpinDamping * dt
else                       d = collisionSpinDamping * dt
f = max(0, 1 - d)
w *= f
rb.vtbl[+0x54](w)   // setAngularVelocity
```

**Plate comment on `0x598320` is stale/wrong** when it claims:

- “AVDCollisionSpinDamping applied ONLY within the 6400-tick window”
- “`DAT_00a110d8 = 10.0` is the additive damping”
- “AVDNormalSpinDamping applied CONTINUOUSLY as chassisBody.angularDamping” *from this fn*

Those statements describe the **wrong function** (or an earlier misread). Binary control flow and constant uses above supersede the plate.

---

## 5. Constants — `read_memory` verified

### `DAT_00a110d8` @ `0xa110d8`

| | |
|--|--:|
| Bytes (LE) | `00 00 20 41` … |
| float32 | **`10.0`** |
| Role **in this fn** | re-ground raise: `pos.y + 10.0` before `CVOGMap_CastTerrainHeight` |
| Role **not** | AVD additive, angVel scale, collision threshold |

Neighbor words (same read, not used by this fn):

| Addr | Bytes | float32 |
|------|-------|--------:|
| `0xa110dc` | `00 00 20 c1` | −10.0 |
| `0xa110e0` | `ef ff 7f 3f` | ≈ 0.9999989 |
| `0xa110e4` | `bd 37 86 35` | ~1e−6-class small float |

`0xa110d8` is a **widely shared** 10.0 constant (many xrefs outside this fn). Only the recovery path at **`0x5985ad`** binds it to airStabilization.

### `DAT_009d54a8` @ `0x9d54a8`

| | |
|--|--:|
| Bytes (LE) | `00 00 00 34` |
| float32 | **`≈ 1.1920929e−7`** (`0x34000000`) |
| Role | speed epsilon — in-window impulse only if `\|v\| >` this |
| Xrefs in this fn | sole read @ `0x598392` |

### `DAT_00b04eb0` @ `0xb04eb0`

| | |
|--|--:|
| Bytes | 16× `00` |
| Role | zero vec4 — fallback linVel when no physics body; arg to clear lin/ang vel on recovery |

### Immediates

| Imm | Value | Role |
|-----|------:|------|
| `0x1900` | 6400 | collision-window length (ms vs `g_dwClientTickMs`) |
| `0x3f800000` | 1.0f | stack arg into impulse builder |
| loop `0..0xC` step 4 | 3 slots | stabilizer array at `entity+0x260` |

---

## 6. Struct offsets touched (this fn)

### VehicleAction (`this`)

| Off | Access | Meaning |
|----:|--------|---------|
| `+0x1c` | R/W u8 | in-collision flag |
| `+0x44` | R ptr | entity back-ref |

### Entity (`*(VA+0x44)`)

| Off | Access | Meaning |
|----:|--------|---------|
| `+0x08` | R ptr | physics object → `+0x3c` = rigid body |
| `+0x14` | R u32 | last-collision timestamp |
| `+0x103` | R u8 | forced-stop / dead (skip all) |
| `+0x260` | R ptr | stabilizer slot base (3× ptr) |
| data `+0x7e` | R u8 | fully-disabled (skip all) |

### Rigid body (`*(*(entity+8)+0x3c)`)

| Off | Access | Meaning |
|----:|--------|---------|
| `+0x30..3c` | R | orientation quaternion |
| `+0x40..4c` | R | linear velocity |
| `+0x50..5c` | R | angular velocity |
| `+0xb0` | R | position X (cast arg) |
| `+0xb4` | R | position Y (**+ 10.0** → cast start) |
| `+0xb8` | R | position Z (cast arg) |

**Note:** older plate text that called `entity+0xb0..b8` “angular velocity” is wrong; angVel is on the **body** at `+0x50`, position at `+0xb0`.

### Physics object vtbl (via `*(phys+0x3c)`)

| Slot | Use here |
|-----:|----------|
| `+0x40` | set position (re-ground write) |
| `+0x50` | apply lin impulse / set lin vel (ApplyImpulseVector + recovery zero) |
| `+0x54` | set angular velocity (recovery zero) |

---

## 7. Helper index

| Addr | Name | Role in this path |
|-----:|------|-------------------|
| `0x53e0b0` | `FUN_0053e0b0` | velocity cache getter (speed gate) |
| `0x404c90` | `FUN_00404c90` | position ptr (`rb+0xb0` or fallback) |
| `0x404a20` | `FUN_00404a20` | quaternion ptr (`rb+0x30` or fallback) |
| entity vtbl `+0x3c` | (unnamed) | build/validate corrective impulse |
| `0x40d260` | `CVOGPhysics_ApplyImpulseVector` | apply via phys vtbl `+0x50` |
| `0x56a260` | `FUN_0056a260` | reset one stabilizer slot |
| `0x4fbec0` | `VehicleEntity_SetDriveAxes` | clear drive axes on recovery |
| `0x4cfe60` | `CVOGMap_CastTerrainHeight` | down-cast for re-ground Y |
| `0x5070b0` / `0x5070d0` | physics write guards | enable dirty writes |
| **`0x64d810`** | **`hkAngularVelocityDamper_update`** | **NOT called** — continuous AVD elsewhere |
| **`0x64d900`** | **`hkAngularVelocityDamper_ctor`** | **NOT called** — setup only |

---

## 8. Misread registry (superseded by this re-verify)

| Claim | Origin | Status |
|-------|--------|--------|
| `DAT_00a110d8 = 10.0` is AVD / angVel damping additive | Ghidra plate; `NPCDriving.md` §6.3; `0.8-struct-offsets.md` wording on `+0xb4` | **FALSE** — re-ground Y raise only |
| AVDCollisionSpinDamping applied inside the 6400 window here | plate / old §6.3 | **FALSE** — AVD rates used only in `0x64d810` |
| Window ≈ 1.07 s @ 60 Hz | plate / `NPCDriving.md` | **FALSE** — **6400 ms** on `g_dwClientTickMs` |
| This fn *is* continuous AVD | naming “airStabilization — AVD + collision” | **MISLEADING** — recovery path only; continuous AVD is separate Havok action |
| `entity+0xb0..b8` = angVel | plate | **FALSE** — body pos; angVel = body `+0x50` |

Aligned prior write-up: `docs/reconstruction/physics/avd-airstab-spec.md` §3 (collision-window recovery) matches this re-verify. Prefer **this file** as the per-function verified record for `0x598320`.

---

## 9. Port recipe (behavior only — no C# in this pass)

1. **Do not** implement `rlAVD*` damping inside this module. Wire continuous AVD from damper update (`0x64d810`).
2. On collision stamp `lastCollisionMs = nowMs`.
3. While `nowMs − lastCollisionMs < 6400`:
   - set `inCollision = true`
   - if chassis speed > ~1.19e−7, build/apply corrective impulse (entity impulse builder parity TBD if vtbl body not yet RE’d)
4. On the first tick where window expired and `inCollision` was true:
   - clear flag
   - reset 3 stabilizer slots
   - zero lin + ang velocity
   - clear drive axes
   - `y = CastTerrainHeight(x, z, startY = pos.y + 10.0)` → set position
5. Skip entirely if entity forced-stop (`+0x103`) or fully-disabled (`+0x7e`).

---

## 10. Verification checklist

- [x] Decompile `0x598320` (fresh)
- [x] `read_memory` `0xa110d8` → `0x41200000` = 10.0
- [x] `read_memory` `0x9d54a8` → `0x34000000` ≈ 1.19e−7
- [x] `read_memory` `0xb04eb0` → zero vec
- [x] Dataflow: `ADDSS [0xa110d8]` → `CVOGMap_CastTerrainHeight` only
- [x] Xref: sole caller `applyAction` @ `0x599227`
- [x] Continuous AVD re-decompiled at `0x64d810` — confirmed separate, no timer
- [x] No `rlAVD*` / damper field reads in this function body
)
