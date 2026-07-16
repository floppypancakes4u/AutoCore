# Verified: `CVOGSectorMap::StepTo` @ `0x4d6c80`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x004d6c80` |
| Symbol | `FUN_004d6c80` (string plate: `"CVOGSectorMap::StepTo"` @ `0x009cc79c`) |
| Convention | MSVC `__thiscall` — `this` = `CVOGSectorMap*` in `ECX`; `param_2` = frame `dt` (float) |
| Body | `0x004d6c80` – `0x004d6e55` |
| RE tools | Ghidra `decompile_function` @ `0x4d6c80`; `disassemble_function` (math path); `read_memory` on constants |
| Status | **Verified** (re-read) |
| Focus | Frame-dt clamp, sub-step count `N`, per-substep `dt` |

Related phase note: `docs/reconstruction/physics/0.1-step-rate.md` (resolved; this file re-gates the binary).

---

## 1. Constants (`read_memory`, length 4)

| Symbol (Ghidra) | Address | Raw LE bytes | u32 | float32 | Role |
|---|---|---|---:|---:|---|
| `_DAT_009cc798` | `0x009cc798` | `ff ff ef 41` | `0x41EFFFFF` | **29.999998092651367** | Sub-step frequency cap (≈30 Hz). Sits immediately before the `"CVOGSectorMap::StepTo"` string |
| `g_flMultiKillCountBlend` | `0x00a0f730` | `cd cc cc 3d` | `0x3DCCCCCD` | **0.1** | Max frame-delta clamp (10 fps floor). Name is a Ghidra reuse of a shared float pool — **not** multi-kill semantics |
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | `0x3F800000` | **1.0** | Added after `floor` to form `N` |

Assembly loads:

* clamp: `MOVSS XMM0, [0x00a0f730]`
* mul: `FMUL dword ptr [0x009cc798]`
* +1: `FADD dword ptr [0x00a0f2a0]` / `MOVSS XMM1, [0x00a0f2a0]`
* `floor`: `CALL dword ptr [0x009c6598]` (CRT `floor` IAT)

---

## 2. Decompile (authoritative pseudocode, substep path)

```c
void __thiscall FUN_004d6c80(int param_1 /* this */, float param_2 /* frameDt */)
{
  float fVar1;   // N as float
  int iVar2;     // N as int (loop counter)
  double dVar3;  // floor result

  FUN_0076cf00("CVOGSectorMap::StepTo");
  FUN_004d3420(param_2);                         // pre-step (outside subloop)

  if (*(char *)(param_1 + 0x7d) == '\0') {       // normal physics path
    // --- dt clamp ---
    if (g_flMultiKillCountBlend < param_2) {     // if frameDt > 0.1
      param_2 = g_flMultiKillCountBlend;         // frameDt = 0.1
    }

    // --- N = floor(frameDt * ~30) + 1 ---
    dVar3 = floor((double)(param_2 * _DAT_009cc798));
    fVar1 = (float)dVar3 + g_flOne;              // N_float
    iVar2 = (int)ROUND(fVar1);                   // FISTP of integer-valued float

    if (0 < iVar2) {
      do {
        // accumSimTime += substep_dt
        *(float *)(param_1 + 0x70) =
            param_2 / fVar1 + *(float *)(param_1 + 0x70);

        FUN_00561910(/* island = *(this+0xe4a4), stepInfo = {substep_dt, 1/substep_dt} */);
        FUN_00561b60(/* same island / stepInfo pair */);
        FUN_004d4da0(/* substep_dt */);
        iVar2 = iVar2 + -1;
      } while (iVar2 != 0);
    }
    FUN_004d3980();
  }
  else {
    // flag this+0x7d != 0: single step, no sub-splitting
    FUN_004d4da0(param_2);
    FUN_00562bf0(param_2);
    *(float *)(param_1 + 0x70) = param_2 + *(float *)(param_1 + 0x70);
  }

  if (*(int *)(param_1 + 0xe4a8) != 0) {
    FUN_005d86f0(0);
    FUN_005d83f0();
  }
  FUN_0076cef0();
  return;
}
```

Ghidra labels `ROUND` on the `FISTP` path; because `fVar1 = floor(...) + 1.0` is already integer-valued in float, cast and round-nearest are equivalent.

---

## 3. Assembly math path (bit-exact order)

Critical block `0x004d6cd1` – `0x004d6d46` (after `this+0x7d == 0` branch):

```text
; clamp frameDt = min(frameDt, 0.1)
MOVSS   XMM0, [0x00a0f730]          ; 0.1
MOVSS   XMM1, [ESP+0x30]            ; frameDt
COMISS  XMM1, XMM0
JBE     skip_clamp
MOVSS   [ESP+0x30], XMM0            ; frameDt = 0.1
skip_clamp:

; N_float = floor(frameDt * 29.999998...) + 1.0
FLD     dword [ESP+0x30]            ; frameDt
FMUL    dword [0x009cc798]          ; * ~30
FSTP    qword [ESP]                 ; arg for floor (double)
CALL    [0x009c6598]                ; floor
FADD    dword [0x00a0f2a0]          ; + 1.0
FSTP    dword [ESP+0x10]            ; N_float

; substep_dt = frameDt / N_float
; inv_dt     = 1.0 / substep_dt
MOVSS   XMM0, [ESP+0x3c]            ; frameDt (post-clamp)
MOVSS   XMM1, [0x00a0f2a0]          ; 1.0
DIVSS   XMM0, [ESP+0x10]            ; substep_dt
DIVSS   XMM1, XMM0                  ; 1/substep_dt
MOVSS   [ESP+0x34], XMM0            ; keep substep_dt
MOVSS   [ESP+0x1c], XMM0            ; stepInfo[0]
MOVSS   [ESP+0x20], XMM1            ; stepInfo[1]

; loop count
FLD     dword [ESP+0x10]
FISTP   dword [ESP+0x14]
MOV     EBX, [ESP+0x14]
TEST    EBX, EBX
JLE     skip_loop
```

Loop body (each of `N` iterations):

```text
ADDSS   XMM0, [ESI+0x70]            ; this+0x70 += substep_dt
MOVSS   [ESI+0x70], XMM0
; ...
LEA     EAX, [ESP+0x1c]             ; &stepInfo {substep_dt, 1/substep_dt}
PUSH    EAX
MOV     ECX, [ESI+0xe4a4]           ; island / Lt world
CALL    0x00561910                  ; LtSimulate
CALL    0x00561b60                  ; post-sim companion
PUSH    EBP                         ; substep_dt
CALL    0x004d4da0
SUB     EBX, 1
JNZ     loop
```

---

## 4. Exact portable formula

```
frameDt_in = caller frame delta (seconds)

if (map.flag_0x7d == 0):                  // normal path
    frameDt = min(frameDt_in, 0.1f)       // clamp BEFORE N
    N_float = floor(frameDt * 29.999998092651367f) + 1.0f
    N       = (int)N_float                // integer-valued; FISTP / cast OK
    if (N > 0):
        substep_dt = frameDt / N_float
        inv_dt     = 1.0f / substep_dt
        for i in 0..N-1:
            map.accum_0x70 += substep_dt
            LtSimulate(island, {substep_dt, inv_dt})   // FUN_00561910
            companion(island, {substep_dt, inv_dt})    // FUN_00561b60
            post(map, substep_dt)                      // FUN_004d4da0
else:
    // no clamp / no split (single full frameDt)
    post(map, frameDt_in)
    FUN_00562bf0(frameDt_in)
    map.accum_0x70 += frameDt_in
```

### Invariants

| Property | Value |
|---|---|
| Max `frameDt` (normal path) | **0.1 s** (10 Hz floor) |
| Sub-step frequency target | **`0x41EFFFFF` ≈ 29.999998 Hz** (not exactly `30.0f`) |
| `N` | `floor(frameDt * cap) + 1` |
| Sub-step size | **Equal split** `frameDt / N` (never exceeds ≈ 1/30 s) |
| Min `N` for positive dt | **1** (`floor(small) + 1`) |
| Max `N` under clamp | `floor(0.1 * 29.999998…) + 1` = `floor(2.9999998…) + 1` = **3** |
| `stepInfo` to island | `{ substep_dt, 1/substep_dt }` — island stores dt @ `+0x150` (`FUN_00561910`) |

### Worked examples

| frameDt | product `dt*cap` | `floor` | `N` | `substep_dt` |
|--------:|-----------------:|--------:|----:|-------------:|
| 1/60 ≈ 0.0166667 | ≈ 0.500 | 0 | 1 | ≈ 0.0166667 |
| 1/30 ≈ 0.0333333 | ≈ 1.000 − ε | 0 | 1 | ≈ 0.0333333 |
| just over 1/30 | ≥ 1.0 | 1 | 2 | frameDt/2 ≤ ≈1/30 |
| 0.1 (clamped max) | ≈ 2.9999998 | 2 | 3 | 0.1/3 ≈ 0.0333333 |
| 0.2 (input) | clamp → 0.1 | 2 | 3 | same as 0.1 |

---

## 5. Call chain (substep → vehicle action)

```
CVOGSectorMap::StepTo          0x004d6c80
  ├ FUN_004d3420               pre-step once per frame
  └ (loop N×, normal path)
      ├ FUN_00561910 "LtSimulate"   island @ this+0xe4a4
      │    stores *stepInfo → island+0x150; stepInfo[1] → +0x154
      │    └ FUN_00629d90          integrate / applyActions
      │         └ VehicleAction::applyAction  0x00598650   (param_2[0] = substep_dt)
      ├ FUN_00561b60               post-simulate companion (same stepInfo)
      └ FUN_004d4da0               map post with substep_dt
  └ FUN_004d3980               once after loop
```

---

## 6. Struct offsets touched here

| Offset on `this` (`CVOGSectorMap`) | Role |
|---|---|
| `+0x70` | Accumulated sim time; `+= substep_dt` each substep (or full `frameDt` on flag path) |
| `+0x7d` | `char` — **0** = substepped physics path; **≠0** = single full-dt path (no clamp/N) |
| `+0xe4a4` | Pointer to Lt island / world passed as `this` to `FUN_00561910` / `FUN_00561b60` / `FUN_00562bf0` |
| `+0xe4a8` | Optional post hook (`FUN_005d86f0` / `FUN_005d83f0`) |

---

## 7. Conflicts vs `0.1-step-rate.md`

| Item | `0.1-step-rate.md` | This re-verify | Verdict |
|---|---|---|---|
| `frameDt = min(frameDt, 0.1)` | yes (`g_flMultiKillCountBlend`) | yes (`COMISS` / `0xa0f730`) | **match** |
| Cap constant `0x9cc798` = `0x41EFFFFF` ≈ 29.9999998 | yes | `read_memory` → 29.999998092651367 | **match** (same bits) |
| `N = floor(frameDt * cap) + 1` | yes | yes (`floor` IAT + `g_flOne`) | **match** |
| `substep_dt = frameDt / N` | yes | yes (`DIVSS`); also builds `1/substep_dt` | **match** (+ extra inv for island) |
| 60 fps → N=1, dt=1/60 | yes | yes | **match** |
| 30 fps → N=1, dt=1/30 | yes | yes (product stays just under 1.0) | **match** |
| Max sub-step ≤ 1/30 s | yes | yes (max N=3 at dt=0.1) | **match** |
| Flag path `this+0x7d` | not detailed | single full-dt, no clamp | **additive detail** |
| stepInfo pair `{dt, 1/dt}` | not detailed | confirmed in asm + `FUN_00561910` | **additive detail** |

**No algorithm conflict with `0.1-step-rate.md`.** Binary confirms the documented substep rule. Prefer the **exact** float32 `0x41EFFFFF` (not a rounded `30.0f`) for bit-exact ports; `floor(dt*30)+1` is an acceptable engineering shorthand only if tests accept the tiny edge difference near exact multiples of 1/30.

### Port notes

1. Clamp **before** computing `N`.
2. Use **true `floor`** (toward −∞), not truncate-toward-zero, if negative `dt` is ever possible (retail path expects non-negative frame deltas).
3. Divide by **`N` as float** (`frameDt / N_float`); do not integer-divide.
4. Pass both `substep_dt` and `1/substep_dt` into the island step if mirroring Havok island fields `+0x150` / `+0x154`.
5. Do **not** invent a fixed 60 Hz step as the only mode; 60 Hz is simply the common N=1 case.

---

## 8. Capture metadata

| Field | Value |
|---|---|
| Decompile | Ghidra MCP HTTP `/decompile_function?address=0x4d6c80&program=autoassault.exe` |
| Disassembly | `/disassemble_function?address=0x4d6c80&program=autoassault.exe` |
| Memory | `/read_memory` @ `0x9cc798`, `0xa0f730`, `0xa0f2a0` (length 4) |
| Date | 2026-07-15 |
