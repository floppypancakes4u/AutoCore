# VERIFIED — Upright-restore impulse (`dot(up,worldUp) < 0.7`)

**Program:** `autoassault.exe` (image base `0x400000`)  
**Host function:** `VehicleAction_applyAction` @ `0x598650` (body `0x598650`–`0x5994c4`)  
**Gate site (threshold load):** `0x598e33` — sole xref to `DAT_00af3380`  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x598650`, `0x5994e0`, `0x5d6870`;  
`read_memory` on all formula DATs; `get_xrefs_to` @ `0xaf3380` / `0xaf3378` / `0xaf337c`;  
`audit_globals_in_function` @ `0x598650`.

**No C# in this file** — RE evidence only.

---

## 0. What this is / is not

| | |
|--|--|
| **This** | Angular righting impulse when the chassis is tilted past ~45° but not fully inverted, on the **non-mode-`0x02`** path of `applyAction` |
| **Not this** | `VehicleAction_airStabilization` @ `0x598320` (collision-window impulse + re-ground) — see `fn_00598320_airStab.md` |
| **Not this** | Continuous AVD `hkAngularVelocityDamper_update` @ `0x64d810` — see `fn_0064d810_avd.md` |
| **Not this** | Traction upright falloff `\|dot\| < 0.8` in `calcWheelTorque` (`DAT_00a0f698`) — see `fn_00598040_uprightPow.md` |
| **Not this** | Sharp-turn lateral threshold `0.7` at `DAT_00a0f710` (drive controller) |

Older shorthand that said “airStab upright when `dot < 0.7`” is **misleading**: the **0.7 gate lives in `applyAction`**, *before* `calcWheelTorque` / `airStabilization`.  
Related orchestration: `fn_00598650_applyAction.md` §3 step 7 `else` / §7. Constant plate: `constants/c_af3380_steer_block.md`.

---

## 1. Call-graph placement

```
VehicleAction_applyAction (0x598650)
  …
  if movementMode == 0x02:
      // analog wheel steer ramp → setSteeringAngle  (NO upright-restore)
  else:
      // velocity-coupled basis from chassis quat
      // ★ UPRIGHT-RESTORE impulse when 0.1 < up_dot < 0.7
      //   validate → FUN_005994e0 (phys vtbl +0x64)
  VehicleAction_calcWheelTorque();      // 0x598040
  VehicleAction_airStabilization();     // 0x598320  — separate mechanism
  …
```

| Item | Value |
|------|------:|
| Movement-mode byte | `vehicleData+0x4ce` (chain via `entity+600` → data `+0xac` → `+0x3c`) |
| Upright path taken when | `mode != 0x02` |
| Impulse apply helper | `FUN_005994e0` @ `0x5994e0` |
| Finite check helper | `FUN_005d6870` @ `0x5d6870` |
| Angle | `_CIacos` |

Mode `0x02` is the common analog wheel-steer path; **most player cars never hit this gate**.  
NPC / velocity-coupled modes (non-`0x02`) do.

---

## 2. Threshold constant — `DAT_00af3380` = **0.7**

### 2.1 `read_memory`

| | |
|--|--:|
| Address | **`0x00af3380`** |
| Symbol | `DAT_00af3380` |
| LE bytes | `33 33 33 3f` |
| Bit pattern | `0x3F333333` |
| float32 | **`0.699999988…` → 0.7** |
| Geometric | `acos(0.7) ≈ 45.57°` tilt from world-up |

Neighbor plate (same 16-byte block; **do not confuse**):

| Address | Bytes | float32 | Role |
|---------|-------|--------:|------|
| `0xaf3380` | `33 33 33 3f` | **0.7** | **this gate** |
| `0xaf3384` | `9a 99 19 3f` | 0.6 | neighbor only — **unused** by upright path |
| `0xaf3388` | `00 00 a0 41` | 20.0 | mode-`0x02` speedFactor divisor (other branch) |
| `0xaf338c` | `00 00 00 00` | 0.0 | pad |

### 2.2 Sole code xref

```
get_xrefs_to 0xaf3380:
  From 00598e33 in VehicleAction_applyAction [READ]
```

**One consumer in the entire binary.** Threshold address for ports / breakpoints / patches: **`0x00af3380`**, load site **`0x00598e33`**.

---

## 3. Gate condition (exact)

Decompile (`0x598650`, non-`0x02` branch):

```
// fStack_{3c,38,40} = normalize( rotate(chassisQuat, worldUp) )
//   = body local-up expressed in world (Y-up world axis DAT_00af3390/94/98)
up_dot = fStack_3c * DAT_00af3394
       + fStack_38 * DAT_00af3398
       + fStack_40 * DAT_00af3390;
// with worldUp = (0, 1, 0) this is simply the world-Y of body-up = cos(tilt)

if ( (up_dot < DAT_00af3380) && (g_flMultiKillCountBlend < up_dot) ) {
    // righting impulse …
}
```

| Bound | Symbol | Address | Value | Meaning |
|-------|--------|---------|------:|---------|
| Upper | `DAT_00af3380` | `0xaf3380` | **0.7** | engage when more tilted than ~45° |
| Lower | `g_flMultiKillCountBlend` | `0xa0f730` | **0.1** (`cd cc cc 3d`) | skip when `up_dot ≤ 0.1` (~84°+ / near-inverted) |

Open interval:

```
0.1  <  up_dot  <  0.7
```

| `up_dot` | Behavior |
|---------:|----------|
| `≥ 0.7` | upright enough — **no** righting impulse |
| `(0.1, 0.7)` | **righting active** |
| `≤ 0.1` | too inverted — **no** righting (recovery elsewhere: AVD / airStab re-ground) |

`g_flMultiKillCountBlend` is a **shared float pool** name (also 0.1 frame-dt clamp elsewhere); here it is only the numeric lower guard.

---

## 4. World axes used in the basis

| Symbol | Address | LE / float | Role |
|--------|---------|------------|------|
| `DAT_00af3390` | `0xaf3390` | `00 00 00 00` → **0** | worldUp.x |
| `DAT_00af3394` | `0xaf3394` | `00 00 80 3f` → **1** | worldUp.y |
| `DAT_00af3398` | `0xaf3398` | `00 00 00 00` → **0** | worldUp.z |
| `DAT_00af33a0..ac` | `0xaf33a0` | **(0, 0, 1, 0)** | second basis axis (forward-ish) for corr / sign |

Chassis orientation: rigid body quaternion at `rb+0x30..0x3c`  
(`rb = *(*(entity+8)+0x3c)`). Expansion uses `2.0` (`g_flLevelUpUiBase_Inferred` @ `0xa10e74` = `00 00 00 40`) as the standard quaternion double-cover factor.

---

## 5. Righting math (from decompile)

Entered only when the gate in §3 fires.

### 5.1 Desired correction direction

```
// afStack_50 = second rotated basis (from world axis B = (0,0,1))
// remove component of worldUp along that basis, re-normalize → desired "up toward world"

proj = -( afStack_50[2]*worldUp.z
        + afStack_50[1]*worldUp.y
        + afStack_50[0]*worldUp.x )

desired = normalize( afStack_50.xyz * proj + worldUp )
```

### 5.2 Angle between desired and current body-up

```
d = clamp-ish: if |dot(desired, bodyUp)| < 1.0:
      angle = acos(dot)          // _CIacos
   else:
      angle = 0.0 if dot > 0 else π   // DAT_009d54a4 @ 0x9d54a4 = 0x40490fdb ≈ 3.141593

// sign: majority-axis compare of (desired × bodyUp) vs afStack_50
// if sign bits of dominant components disagree → angle = -angle
```

### 5.3 Impulse magnitude and vector

```
// rb+0x2c = inertia-like scalar used as 1/I (0 → invI = 0)
invI  = (rb[+0x2c] == 0.0) ? 0.0 : 1.0 / rb[+0x2c]

// param_2[0] = substep dt; param_2[1] = throttle input
m     = invI * dt * DAT_00af3378 * angle * throttle     // 0.8 scale
damp  = DAT_00af337c * throttle                          // 0.1 * throttle

// angVel at rb+0x50..0x5c
impulse.x = m * desired.x  -  angVel.x * damp
impulse.y = m * desired.y  -  angVel.y * damp
impulse.z = m * desired.z  -  angVel.z * damp
// (w component also formed from afStack_50[3] / rb+0x5c in decompile)
```

| Constant | Address | Bytes | Value | Role |
|----------|---------|-------|------:|------|
| `_DAT_00af3378` | `0xaf3378` | `cd cc 4c 3f` | **0.8** | magnitude scale |
| `DAT_00af337c` | `0xaf337c` | `cd cc cc 3d` | **0.1** | angVel damp × throttle |
| `DAT_009d54a4` | `0x9d54a4` | `db 0f 49 40` | **π** | acos saturated negative fallback |

**Xrefs (each sole-read in this fn for these DATs):**

| DAT | Code site |
|-----|-----------|
| `0xaf3380` (0.7) | `0x598e33` |
| `0xaf337c` (0.1 damp) | `0x599100` |
| `0xaf3378` (0.8 mag) | `0x599160` |

### 5.4 Throttle dependence

`m ∝ param_2[1]` and `damp ∝ param_2[1]`.  
**`throttle == 0` ⇒ zero righting and zero damp term** — coasting cars do not self-right via this path.

### 5.5 Apply / reject

```
ok = FUN_005d6870(impulse)   // 0x5d6870
// Rejects if any of first 3 floats has exponent bits == 0x7f800000 (Inf/NaN class)
if !ok:
    log "\n!&!&!&!&!&!&!&! Illegal Impulse Detected: A:%f X:%f, Y:%f, Z:%f\n"
else:
    FUN_005994e0(impulse)    // 0x5994e0
```

`FUN_005994e0` (decompiled):

```
// this = VehicleAction; param_2 = impulse ptr
dirty guards FUN_005070b0 / FUN_005070d0 if needed
(*(phys_obj+0x3c))->vtbl[+0x64](impulse)   // angular-impulse / set-ang-impulse slot
```

Physics object = entity path; `vtbl+0x64` is the apply slot used **only** by this helper in the upright path (distinct from `CVOGPhysics_ApplyImpulseVector` / lin slot `+0x50` used by airStab / boost).

---

## 6. Struct offsets (this path)

### Rigid body (`rb`)

| Off | Use |
|----:|-----|
| `+0x2c` | scalar inertia proxy → `invI = 1/x` |
| `+0x30..0x3c` | orientation quaternion |
| `+0x50..0x5c` | angular velocity (damp term) |

### Input block (`param_2`)

| Index | Use |
|------:|-----|
| `[0]` | substep `dt` |
| `[1]` | throttle — scales magnitude **and** damp |

### VehicleAction / entity

Only used to reach `rb` and movement-mode; no dedicated “upright flag” is written.

---

## 7. Relationship to airStab / AVD

| Mechanism | Entry | Gate | Effect |
|-----------|------:|------|--------|
| **Upright-restore** | `applyAction` non-`0x02` | `0.1 < cos(tilt) < 0.7`, throttle-scaled | angular impulse toward world-up |
| **airStabilization** | `applyAction` always (after torque) | collision timer `Δt < 6400 ms` | corrective impulse builder **or** re-ground |
| **Continuous AVD** | framework child in `tickSubsystems` | `\|ω\|` vs `AVDCollisionThreshold` | `ω *= max(0, 1 − rate·dt)` |

Ports that only implement AVD will damp spin but **will not** actively right a half-tilted chassis on non-`0x02` modes.  
Ports that only implement airStab re-ground will snap after collisions but **will not** apply the 0.7-gated cruise righting.

---

## 8. Port recipe (behavior only)

1. If movement mode is `0x02`, **skip** this entire module (use wheel steer path).
2. From chassis quat, build body-up in world; `up_dot = bodyUp · (0,1,0)`.
3. If `up_dot >= 0.7` or `up_dot <= 0.1`, skip.
4. Build `desired = normalize(worldUp − proj_onto_secondary_basis)`.
5. `angle = signed_acos(dot(desired, bodyUp))` (π fallback when anti-aligned and saturated).
6. `impulse = desired * (invI * dt * 0.8 * angle * throttle) − angVel * (0.1 * throttle)`.
7. Drop non-finite impulses; else apply as **angular** impulse on the chassis body.
8. Constants must be the plate addresses above — especially **`0xaf3380 = 0.7`**, not traction `0.8` and not sharp `0xa0f710`.

---

## 9. Verification checklist

- [x] Decompile `VehicleAction_applyAction` @ `0x598650` — upright block present in `else` of mode `0x02`
- [x] `read_memory` `0xaf3380` → `33 33 33 3f` = **0.7**
- [x] `get_xrefs_to` `0xaf3380` → **sole** READ @ **`0x598e33`**
- [x] `read_memory` `0xa0f730` → `cd cc cc 3d` = **0.1** lower gate
- [x] `read_memory` `0xaf3378` → **0.8** mag; `0xaf337c` → **0.1** damp
- [x] `read_memory` `0x9d54a4` → **π**
- [x] `read_memory` `0xaf3390..98` → worldUp **(0,1,0)**
- [x] Decompile `FUN_005994e0` → phys vtbl **`+0x64`**
- [x] Decompile `FUN_005d6870` → Inf/NaN reject on 3 components
- [x] Confirmed **not** inside `0x598320` airStabilization body
- [x] Confirmed distinct from traction upright `0xa0f698 = 0.8`

---

## 10. Evidence index

| Tool | Target | Result |
|------|--------|--------|
| `list_open_programs` | — | `autoassault.exe` current |
| `decompile_function` | `0x598650` | full applyAction; gate + impulse math |
| `decompile_function` | `0x5994e0` | apply via vtbl `+0x64` |
| `decompile_function` | `0x5d6870` | finite check |
| `get_xrefs_to` | `0xaf3380` | `0x598e33` only |
| `get_xrefs_to` | `0xaf3378` | `0x599160` only |
| `get_xrefs_to` | `0xaf337c` | `0x599100` only |
| `read_memory` | `0xaf3380` len 4 | `3333333f` = 0.7 |
| `read_memory` | `0xaf3378` / `7c` / `74` | 0.8 / 0.1 / 0.5 |
| `read_memory` | `0xa0f730` | 0.1 lower gate |
| `read_memory` | `0x9d54a4` | π |
| `read_memory` | `0xaf3390` cluster | (0,1,0) and (0,0,1,0) |
| `audit_globals_in_function` | `0x598650` | lists `DAT_00af3380`, MultiKill blend, illegal-impulse string |

---

## 11. Cross-refs

| Doc | Relation |
|-----|----------|
| `fn_00598650_applyAction.md` | Full tick orchestration; §7 is the summary of this path |
| `fn_00598320_airStab.md` | Collision-window recovery (not the 0.7 gate) |
| `fn_0064d810_avd.md` | Continuous spin damping |
| `fn_00598040_uprightPow.md` | Traction upright falloff at **0.8** |
| `constants/c_af3380_steer_block.md` | 16-byte plate 0.7 / 0.6 / 20.0 |
| `avd-airstab-spec.md` §4 | Earlier combined write-up (prefer this file for the 0.7 path) |
