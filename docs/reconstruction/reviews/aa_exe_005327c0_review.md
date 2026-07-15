# Review: CVOGReaction_GiveMission (aa_exe_005327c0)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_005327c0.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/CVOGReaction_GiveMission.cpp` |
| System doc | `docs/reconstruction/systems/missions.md` |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Address | `0x005327C0` | `CVOGReaction_GiveMission` |
| Active missions hash | character `+0x540` | reject / insert gate |
| Active objectives hash | character `+0x548` | first objective insert |
| Completed hashes | character `+0x538`, `+0x53c` | prereq / kill-XP-bonus branches |
| Template enabled | template dword-index `+0x4c` (byte) | early fail if 0 |
| Template prereq short | template `+0x2b` | `-1` skips completed-hash prereq |
| First objective list | template `[0x4f]` → obj `+0x10` id, obj `+0x120` continent unlock |
| Runtime record size | `0x30` | `operator_new` + `FUN_004111f0` |
| Toast audio | `"gen_give_quest"` | when template short `@+0x3e == 0` |

## Concrete raw ↔ clean matches (3+)

1. **Template / enabled early outs** — Both require `FUN_0053fff0()` table non-null, `CNDHash_LookupByKey(table, missionId)` success, and `*(char*)(template + 0x4c) != 0`; otherwise return `0`.
2. **Already-active reject** — Both look up `character+0x540` by `missionId`; if present, outer grant body is skipped and function returns `0` (raw) / fails the outer gate (clean).
3. **Prereq completed-hash gates** — When `*(short*)(template + 0x2b) != -1`, both call `CVOGCharacter_WeaponAllowsKillXpBonus()` twice: bonus-off path checks `+0x538`, bonus-on path checks `+0x53c`; hit → `return 0`.
4. **First objective + continent unlock** — Both take `template[0x4f]` head, look up objective id at `*head+0x10` in `+0x548`, call `CVOGMission_AddActiveObjective` if missing, then `CVOGReaction_UnlockContinentObject(character, *(uint*)(*template[0x4f] + 0x120))`.
5. **Insert / toast path** — Second `+0x540` miss: `FUN_0053c360`, optional second `FUN_0053c360` under bonus+`short@+0x3e`/ `[0x40]`, `operator_new(0x30)`, copy `0xC` dwords from helper `+0x18`, `FUN_0053c660` / `FUN_0052d8b0`, and when `short@+0x3e == 0` play `"gen_give_quest"` and return `1`.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: GiveMission itself opens volume/map gates.**  
   **Falsified by raw plate:** comments and body only call `UnlockContinentObject` for the objective’s continent object id; no volume-gate walker. Map triggers re-eval after type-11 mission state changes (server/client elsewhere). Clean correctly does not invent gate opens.

2. **Hypothesis: “Already had mission” after objective work is a hard failure (`return 0`).**  
   **Falsified:** raw logs `"Already had mission %l..."` then falls through to `return 1`. Clean matches that success-with-log path.

3. **Hypothesis: completed-hash prereq always uses `+0x538` only.**  
   **Falsified:** raw dual-path depends on `CVOGCharacter_WeaponAllowsKillXpBonus()` selecting `+0x538` vs `+0x53c`. Clean preserves both.

4. **Hypothesis: toast always runs on grant.**  
   **Falsified:** toast gated on `*(short*)(template + 0x3e) == 0`. Non-zero skips UI toast but still returns `1` after insert path.

## Residual uncertainty (this unit)

- Exact semantics of `FUN_0053c360` / `FUN_00538b20` / `FUN_0053c660` (mission runtime hash node shape) — left as named externs; insert control flow is solid, node payload is probable.
- Whether decompiler `puVar3 + 0x4c` / `+0x2b` / `+0x3e` are byte offsets or dword-index offsets is taken from Ghidra as-is; widths trusted for control flow, not for a public C layout ABI.
- Runtime grant differential (UF-002) not re-run this session.

## Verdict

**Accept** — static control flow for grant/reject/objective/unlock/toast matches raw; helper bodies remain named externs by design. Not runtime-confirmed.
