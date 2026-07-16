# VERIFIED — Island `LtSimulate` step: `FUN_00561910` + `FUN_00629d90` (+ integrate companion)

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x561910`, `0x629d90`, `0x561b60`, `0x628f70`, `0x629990`, `0x629c40`, `0x6297e0`, `0x62a8a0`, `0x62a930`, `0x562bf0`;  
`get_assembly_context` at call sites `0x561a21` / `0x561c94` / `0x628f90`;  
`get_function_callees` / `get_bulk_xrefs` / `get_function_xrefs`;  
`read_memory` @ `0xa0f298`, `0x9cc798`, `0xa0f730`, `0x9d54c4`;  
`list_strings` for timer labels (`LtSimulate`, `LtBroadPhase`, `TtIntegrate`, …).

**Scope of this file:** how one **substep_dt** from `CVOGSectorMap::StepTo` is installed on the Havok island and how that leads to **`VehicleAction::applyAction`**.  
**Not this file:** substep **N / clamp** math (see `fn_004d6c80_stepTo.md`); body of `applyAction` itself.

Related phase note: `docs/reconstruction/physics/0.1-step-rate.md`.

---

## 1. Identity

| Item | Value |
|------|------:|
| Entry (collide half) | `0x00561910` |
| Ghidra name | `FUN_00561910` |
| Timer label | **`"LtSimulate"`** @ `0x009d2894` |
| Convention | MSVC `__thiscall` — `this` = Lt **world/island manager** in `ECX`; `param_2` = `float*` **stepInfo** `{ substep_dt, 1/substep_dt }` |
| Body | `0x00561910` – `0x00561b5b` |
| Sole companions | `FUN_00561b60` (integrate half, same label `"LtSimulate"`); pair invoked every substep from `StepTo` / `FUN_00562bf0` |

| Item | Value |
|------|------:|
| Entry (broadphase unit) | `0x00629d90` |
| Ghidra name | `FUN_00629d90` |
| Timer label | **`"LtBroadPhase"`** @ `0x009e335c` |
| Role | **Per-sub-island collision pipeline** (AABB expand → 3-axis sweep → pair examine → narrowphase) |
| Sole caller | `FUN_00561910` @ `0x00561a21` |

| Item | Value |
|------|------:|
| Entry (applyActions + integrate) | `0x00628f70` |
| Ghidra name | `FUN_00628f70` |
| Timer label | **`"TtIntegrate"`** @ `0x009e3304` (body path) |
| Role | **Action list first** (`vtbl+0x14` = `applyAction`), then rigid-body integrate |
| Sole caller | `FUN_00561b60` @ `0x00561c94` |

---

## 2. Call chain (authoritative — corrects `0.1-step-rate.md`)

```
CVOGSectorMap::StepTo                 0x004d6c80
  for each of N substeps:
    stepInfo = { substep_dt, 1/substep_dt }
    island   = *(map + 0xe4a4)

    FUN_00561910(island, stepInfo)    // "LtSimulate" — COLLIDE half
      store stepInfo → island fields (§3)
      for i in 0 .. island.subIslandCount-1:
        sub = island.subIslands[i]    // *(island+0x8)[i]
        FUN_00629d90(sub, …)          // "LtBroadPhase" — NOT applyActions
          StCalcAabbs / St3AxisSweep / StExamine
          TtNarrowPhase (FUN_00629990 or FUN_00629c40)
        optional TtIslandPostCollideCb callbacks
      StPostCollideCB callbacks

    FUN_00561b60(island, stepInfo)    // "LtSimulate" — INTEGRATE half
      re-store dt / inv_dt + derived scales
      for each sub-island:
        FUN_00628f70(sub, island+0x140)
          ★ action list: (*action->vtbl)[+0x14](island+0x150)
            → VehicleAction::applyAction  0x00598650
              param_2[0] = substep_dt   (island+0x150)
          TtIntegrate rigid bodies
        optional TtPostIntegrateCb
      TtPostSimulateCb callbacks
```

**Correction vs `0.1-step-rate.md` / older chain text:**

| Claim | Verdict |
|-------|---------|
| `FUN_00629d90` = “island integrate / applyActions” | **WRONG** — it is **broadphase + narrowphase only** (timer `LtBroadPhase`) |
| `VehicleAction::applyAction` under `FUN_00629d90` | **WRONG** — applyActions live in **`FUN_00628f70`**, called from **`FUN_00561b60`** (second `LtSimulate` half) |
| `FUN_00561910` stores dt at `island+0x150` | **CONFIRMED** |
| `param_2[0]` to applyAction is `substep_dt` | **CONFIRMED** — applyAction is invoked with pointer **`island+0x150`** |

Same substep pair is also used on the non-split path: `FUN_00562bf0(frameDt)` builds `{dt, 1/dt}` and calls `FUN_00561910` then `FUN_00561b60`.

---

## 3. `FUN_00561910` — install stepInfo, then collide

### 3.1 Signature & prologue (asm-backed)

```text
; thiscall: ECX = island, [ESP+4] = stepInfo*
PUSH … ; MOV ESI, ECX
MOV  EBP, stepInfo*
MOV  ECX, [EBP]           ; dt
MOV  [ESI+0x150], ECX
MOV  EDX, [EBP+4]         ; inv_dt
MOV  [ESI+0x154], EDX
LEA  EBX, [ESI+0x140]     ; EBX = &island.stepBlock  (kept for callees)
; copy 4 dwords from *(island+0xcc) → island+0x140 .. +0x14c
; derived timestep scales → +0x170..+0x1a4 (§3.2)
```

### 3.2 Island fields written every substep

| Offset | Type | Written | Meaning |
|-------:|------|---------|---------|
| `+0x140`..`+0x14c` | 4×ptr/u32 | copy of `**(island+0xcc)` | Collision / broadphase **agent block** (vtable object used by sweep) |
| `+0x150` | f32 | `stepInfo[0]` | **`substep_dt`** — later handed to `applyAction` as `param_2[0]` |
| `+0x154` | f32 | `stepInfo[1]` | **`1/substep_dt`** |
| `+0x170` | f32 | `dt * (island+0x180)` | Scaled dt (world factor @ `+0x180`) |
| `+0x174` | f32 | `(float)(int)(island+0x17c) * inv_dt` | Scaled inv-dt (int factor @ `+0x17c`) |
| `+0x198` / `+0x19c` | f32 | copies of `+0x170` / `+0x174` | Working duplicates |
| `+0x1a0` | f32 | `(float)(int)(+0x17c) * (+0x170)` | Cross term |
| `+0x1a4` | f32 | `(+0x180) * (+0x174)` | Cross term |
| `+0x12c` | u8 | `1` during collide work, `0` after | In-simulate flag (`300` decimal) |
| `+0x12d` | u8 | `1` around sub-island work / post-cb, `0` after | Nested “busy” flag |

### 3.3 Control flow (pseudocode)

```
// profile "LtSimulate"
island+0x150 = stepInfo[0]   // dt
island+0x154 = stepInfo[1]   // inv_dt
island+0x140..0x14c = copy(* (island+0xcc) )   // agent
// compute +0x170..+0x1a4 as above
island+0x12c = 1
if (island+0x24 > 0)  FUN_00561320()   // flush pending island-pair glue

island+0x12d = 1
for i = 0 .. island.subCount-1:        // count @ +0xc, array @ +0x8
    // asm @ 0x561a13–0x561a21:
    //   ECX = *( *(island+8) + i*4 )     // sub-island
    //   PUSH EBX                         // island+0x140 stepBlock
    //   PUSH *(island+0xc4)              // extra context ptr (see §4)
    //   CALL FUN_00629d90
    if (island+0xac != 0):
        // "TtIslandPostCollideCb"
        FUN_0062a930(island, subIslands[i], stepInfo)

island+0x12d = 0
island+0x12c = 0
if (island+0x24 > 0)  FUN_00561320()
if (island+0xf4 != 0) FUN_005618b0()   // deferred delete list A
if (island+0x100 != 0) FUN_0055ec40()  // deferred delete list B
if (island+0x24 > 0)  FUN_00561320()

// "StPostCollideCB"
FUN_0062a8a0(island, stepInfo)         // vtbl+4 callbacks on list +0x90 / +0x94
return
```

### 3.4 Callers

| Caller | Address | Notes |
|--------|--------:|-------|
| `CVOGSectorMap::StepTo` | `0x004d6d90` | Normal substep loop; `ECX = *(map+0xe4a4)`, push `&stepInfo` |
| `FUN_00562bf0` | `0x00562c19` | Flag path: single `{dt, 1/dt}` then same pair |

---

## 4. `FUN_00629d90` — broadphase / narrowphase only

### 4.1 Calling convention (asm vs decompiler)

Ghidra decompile presents:

```c
void __thiscall FUN_00629d90(uint param_1, int *param_2);
// call site decompile incorrectly as:
//   FUN_00629d90(*(island+0xc4), island+0x140);
```

**Assembly at `0x00561a13`–`0x00561a21` is authoritative:**

```text
MOV  EAX, [ESI+0xc4]          ; extra context
MOV  ECX, [ESI+0x8]
MOV  ECX, [ECX+EDI*4]         ; this = subIslands[i]
PUSH EBX                      ; island+0x140  (stepBlock / agent+dt)
PUSH EAX                      ; *(island+0xc4)
CALL 0x00629d90
```

So:

| Register / slot | Value |
|-----------------|-------|
| `ECX` (true `this`) | **sub-island** from `island+8` array |
| stack | `*(island+0xc4)` + **`island+0x140` stepBlock** |

The decompiled body treats “param_1” as the sub-island-shaped object (`+0x3c` body array, `+0x40` body count, `+0x20` parent world) and “param_2” as the **stepBlock** used for 3-axis sweep (`(*param_2)[+0x14](...)`). Prefer asm binding for `this`; treat Ghidra’s thiscall parameter map as **partially wrong** at the call site.

### 4.2 Timed stages (profile strings)

| Label | Address (string) | Stage |
|-------|-----------------:|-------|
| `LtBroadPhase` | `0x009e335c` | Whole function |
| `StCalcAabbs` | `0x009e3350` | Expand AABBs per body |
| `St3AxisSweep` | (via profile stack) | Sweep / pair gen: `(*stepBlock.vtbl)[+0x14]` |
| `StExamine` | (via profile stack) | Pair filter / examine |
| `TtNarrowPhase` | (in `FUN_00629990` / `FUN_00629c40`) | Contact generation |

### 4.3 Behavior (high level)

```
// "LtBroadPhase"
// scratch alloc body AABB buffer (0x20 each) + ptr buffer
margin = *(float*)( *(parentWorld+0xcc) + 8 ) * DAT_00a0f298   // * 0.5
// "StCalcAabbs"
for each body in subIsland body list (+0x3c / +0x40):
    collect shape collidable; vtbl[+0x18](…, margin, aabbOut)

// "St3AxisSweep"
(*stepBlock)[+0x14](aabbPtrs, aabbBuf, bodyCount, &pairOut)

// "StExamine" + optional static-pair merge from subIsland+0x74/+0x78
FUN_006297e0(...)          // commit pair list into island collision storage
subIsland+0x30 = 1

if ( *(parentWorld + 0x23e) == 0 )
    FUN_00629c40(...)      // simple TtNarrowPhase
else
    FUN_00629990(...)      // continuous / TOI-style TtNarrowPhase
```

**No action-list walk. No `vtbl+0x14` on VehicleAction. No integrate.**

### 4.4 Constant

| Addr | LE bytes | float32 | Use |
|------|----------|--------:|-----|
| `0x00a0f298` (`DAT_00a0f298`) | `00 00 00 3f` | **0.5** | AABB expand margin scale in `StCalcAabbs` |

(Same pool constant appears as aero “0.5·ρ·A·…” scale elsewhere — shared float, not aero-specific here.)

---

## 5. Where `applyAction` actually runs — `FUN_00628f70`

### 5.1 Call from integrate half (`FUN_00561b60` @ `0x00561c8b`–`0x00561c94`)

```text
; EDI = subIsland
LEA  ECX, [ESI+0x140]     ; stepBlock
PUSH ECX
MOV  ECX, EDI             ; this = subIsland
CALL 0x00628f70
```

`FUN_00561b60` also re-writes `island+0x150/+0x154` and the `+0x170..+0x1a4` scales from the same `stepInfo` before the loop (does **not** re-copy `+0x140` agent from `+0xcc` — that was done in the collide half).

### 5.2 Action list then integrate (asm @ `0x00628f77`–`0x00628f9d`)

```text
; EDI = subIsland (this)
; EBP = stepBlock + 0x10  == island+0x150  == &{dt, inv_dt, …}

MOV  ESI, [EDI+0x4c]      ; action* array base
MOV  EAX, [EDI+0x50]      ; action count
LEA  EBX, [ESI + EAX*4]   ; end
loop:
  MOV  ECX, [ESI]         ; hkAction* / VehicleAction*
  MOV  EDX, [ECX]         ; vtable
  PUSH EBP                ; stepInfo @ island+0x150
  CALL [EDX+0x14]         ; ★ applyAction slot
  ADD  ESI, 4
  CMP  ESI, EBX
  JNZ  loop

; then "TtIntegrate" — per-body vtbl integrate, unless alternate solver @ +0x5c
```

Decompile equivalent:

```c
// param_1 = subIsland, param_2 = island+0x140
for (action in subIsland.actions[0 .. count):   // +0x4c / +0x50
    (*action->vtbl)[0x14 / 4]( param_2 + 0x10 );
// param_2+0x10 → island+0x150 → { substep_dt, inv_dt, ... }

if (subIsland+0x5c == 0) {
    // "TtIntegrate"
    for each body: motion vtbl[+8](step+0x10, world+0xe0); vtbl[+4](step+0x10);
} else {
    FUN_006511b0(...);   // alternate integrator batch
}
```

### 5.3 VehicleAction vtable slot

| Item | Value |
|------|------:|
| VehicleAction vtable base | `0x009d54c4` (ctor `FUN_00597e90`; note docs also cite nearby `0x9d54d8` as DATA xref to applyAction ptr) |
| Slot **+0x14** | `read_memory` → **`0x00598650`** = `VehicleAction::applyAction` |
| applyAction first float | `*(float*)(arg0 + 0)` = **`substep_dt`** |

Registration of VehicleAction into the world/island action list remains:  
`Vehicle_createVehicleAction @ 0x4fb660` → `FUN_0055fe50` → `FUN_006292a0` (world `+0x4c` family) — unchanged from `0.1-step-rate.md`.

---

## 6. Two-phase `LtSimulate` summary

| Phase | Function | Profile | Does applyAction? | Does collide? | Does integrate? |
|-------|----------|---------|-------------------|---------------|-----------------|
| 1 | `FUN_00561910` | `LtSimulate` | no | yes (via `00629d90`) | no |
| 1a | `FUN_00629d90` | `LtBroadPhase` | **no** | yes | no |
| 2 | `FUN_00561b60` | `LtSimulate` | via `00628f70` | no | yes |
| 2a | `FUN_00628f70` | `TtIntegrate` (body) | **yes (first)** | no | yes (second) |

Both halves are labeled `"LtSimulate"` in the RDTSC profiler (string appears twice: `0x9d2894`, `0x9d28d0`).

Order per substep for a vehicle:

1. Install `{dt, inv_dt}` on island.  
2. Broad/narrowphase contacts for each sub-island.  
3. Post-collide callbacks.  
4. **`applyAction(dt)` on every registered action** (vehicle driver, AVD, …).  
5. Rigid-body integrate.  
6. Post-integrate / post-simulate callbacks.

---

## 7. Struct offsets (island manager `this` of `FUN_00561910`)

| Offset | Role |
|-------:|------|
| `+0x08` | `subIsland**` array |
| `+0x0c` | sub-island count |
| `+0x20` | (on **sub-island**) parent world ptr |
| `+0x24` | pending pair-glue count (flushed by `FUN_00561320`) |
| `+0x3c` / `+0x40` | (sub-island) body ptr array / count |
| `+0x4c` / `+0x50` | (sub-island) **action** ptr array / count ← applyAction list |
| `+0x90` / `+0x94` | post-collide CB list (`FUN_0062a8a0`) |
| `+0xa8` / `+0xac` | island post-collide CB list (`FUN_0062a930`) |
| `+0x9c` / `+0xa0` | post-integrate CB list (`FUN_0062a8e0`, integrate half) |
| `+0x84` / `+0x88` | post-simulate CB list (`FUN_0062a840`) |
| `+0xc4` | extra context pushed into broadphase call |
| `+0xcc` | ptr to agent object copied into `+0x140` |
| `+0x140` | stepBlock / agent (16 bytes) + dt at `+0x10` relative |
| `+0x150` / `+0x154` | **dt / inv_dt** |
| `+0x17c` / `+0x180` | int / float factors for scaled timestep fields |
| `+0x12c` / `+0x12d` | simulate busy flags |
| `+0x23c` / `+0x23d` | integrate-half policy flags |
| `+0x23e` | (on **world**) selects narrowphase flavor |

`CVOGSectorMap` linkage (from StepTo): island pointer at **`map+0xe4a4`**.

---

## 8. Conflicts vs existing notes

| Source | Claim | This re-verify |
|--------|-------|----------------|
| `0.1-step-rate.md` | `FUN_00629d90` integrate/applyActions → applyAction | **Conflict** — broadphase only; applyAction is `FUN_00628f70` under `FUN_00561b60` |
| `fn_004d6c80_stepTo.md` §5 chain | same outdated `00629d90 → applyAction` line | **Conflict** (step math in that file is fine) |
| `0.1-step-rate.md` | dt stored at island `+0x150` | **Match** |
| `0.1-step-rate.md` | applyAction vtbl slot `+0x14` | **Match** (`0x598650` at `0x9d54c4+0x14`) |
| `fn_004d6c80_stepTo.md` | stepInfo `{dt, 1/dt}` into island | **Match** |

**Port implication:** server substep loop must still call vehicle driver **once per substep with `substep_dt`**, but mentally map that to the **integrate half** of the island step (after contact generation), not the broadphase function.

---

## 9. Checklist

- [x] `decompile_function` `0x561910` — stepInfo store + loop + `00629d90`
- [x] `decompile_function` `0x629d90` — `LtBroadPhase` stages, no actions
- [x] `decompile_function` `0x561b60` — integrate half, calls `00628f70`
- [x] `decompile_function` `0x628f70` — **action `vtbl+0x14` then `TtIntegrate`**
- [x] `get_assembly_context` `0x561a21` — `this = subIslands[i]`, stepBlock in `EBX`
- [x] `get_assembly_context` `0x561c94` / `0x628f90` — applyAction call shape
- [x] `read_memory` `0xa0f298` → `0x3f000000` = 0.5
- [x] `read_memory` `0x9d54c4` length 32 → slot `+0x14` = `0x00598650`
- [x] Xrefs: `00629d90` only from `00561910`; `00628f70` only from `00561b60`
- [x] No C# changes (docs only)

---

## 10. Capture metadata

| Field | Value |
|-------|-------|
| Decompile | Ghidra MCP `/decompile_function` for addresses above, `program=autoassault.exe` |
| Call-site asm | `/get_assembly_context` (not `disassemble_bytes`) |
| Memory | `/read_memory` @ `0xa0f298`, `0x9d54c4`, `0x9cc798`, `0xa0f730` |
| Date | 2026-07-15 |
