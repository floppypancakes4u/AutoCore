# Review: Client_INC_ContactCountdownTick (aa_exe_0091ee20)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_0091ee20.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_INC_ContactCountdownTick.cpp` |
| Downstream airlift | `Client_SendRespawnInSector` (`aa_exe_00935300`) |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Address | `0x0091ee20` | INC contact countdown tick |
| Remaining ms | UI `+0xc24` | `< 1` → return |
| Last tick | UI `+0xc20` | `GetTickCount` delta |
| Total duration | UI `+0xc28` | progress + mid-toast gates |
| Snapshot | UI `+0xc2c` | cancel compare (vfunc `+0x1b0`) |
| Option | UI `+0xc30` | `0` airlift, `1` instant repair, `2` transfer |
| Hardpoint cancel | vehicle `+0x260` slots 0..2, flag byte `+199` | abort countdown |
| Speed cancel | vehicle `+0x138` vs `DAT_00aaabf4` | must stay below threshold |
| Entity flags | entity off `+0xb8 & 0xc3 == 0` | cancel if set |
| Mid toast | remaining/total `> 5000` and `rem-elapsed < 0x1389` | `"Contacting INC..."` |
| Option 0 string | airlift established text | then `Client_SendRespawnInSector` |
| Option 1 opcode family | Instant repair request `0x20B6` (plate) | fee-gated |
| Cancel helper | `FUN_0091edd0` | abort UI countdown |

## Concrete raw ↔ clean matches (3+)

1. **Idle / player gates** — Both: `+0xc24 < 1` return; `DAT_00d1b6d8 == 0` return.
2. **Hardpoint cancel loop** — Both scan 3 hardpoint pointers under vehicle `+0x260`; if object non-null and `*(obj + 199) != 0` → `FUN_0091edd0` cancel.
3. **Countdown math** — Both subtract `GetTickCount() - +0xc20` from remaining, clamp at 0, refresh `+0xc20`; mid-window toast `"Contacting INC... Please do nothing for 5 more seconds!"` under the 5000 / `0x1389` conditions.
4. **Option 0 completion** — When remaining hits 0 and option `== 0`: toast `"INC Contact Established!  Returning you to nearest repair station..."` then `Client_SendRespawnInSector()`.
5. **Option enum** — Clean `kIncOptionAirlift/InstantRepair/Transfer` = 0/1/2 matches raw `+0xc30` tests.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: any INC option completion always sends `0x2073` RespawnInSector.**  
   **Falsified:** only option `0`. Option `1` → instant repair request path; option `2` → transfer (`FUN_008ed650`) after map/station fee checks. Other values return without action.

2. **Hypothesis: countdown ignores combat hardpoint state.**  
   **Falsified:** any of three hardpoints with flag `+199` forces cancel via `FUN_0091edd0`.

3. **Hypothesis: this opens the death UI (corpse → INC panel).**  
   **Falsified / residual:** this unit **ticks** an already-active countdown (`+0xc24` already > 0). Clean plate: closes UF-001 **partially** (button → countdown → send); full corpse→UI open may still need xrefs.

4. **Hypothesis: moving at any speed is fine during contact.**  
   **Falsified:** gate requires vehicle speed at `+0x138 <= DAT_00aaabf4` (and other flags); failure cancels.

## Residual uncertainty (this unit)

- Full fee-check arithmetic for options 1 and 2 (raw capture abbreviated with `...` in places; clean stubs fee messages).
- Exact semantics of cancel gates using vfunc `+0x1b0` / `+0x19c` and bit at `+0x180>>3`.
- Who **starts** the countdown (sets `+0xc24`/`+0xc28`/`+0xc30`) — not this function.
- Runtime INC capture (UF-002).

## Verdict

**Partial** — option-0 airlift path, countdown math, hardpoint cancel, and UI field map are solid; fee branches for options 1/2 and death-UI entry remain incomplete (UF-001 partial).
