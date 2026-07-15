# Review: Client_AwardKillExperience (aa_exe_0080ae70)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## Reconstruction review

- Compared `reconstructed-exact/Client_AwardKillExperience.cpp` to `raw/aa_exe_0080ae70.md`.
- Wire offsets `Amount@+4`, `LevelHint@+8` match AutoCore `GiveXPPacket` and `docs/XP.md`.
- Confirmed `AddExperience(..., PacketOrNonKill)` — packet path does **not** run kill spree.
- Floater type constant **3** preserved.
- Floater layout gate (skeptic close-out): `GiveXpFloaterStack` size **0x34**, type field at **+0x30** (`static_assert(offsetof(..., uStack_8) == 0x30)`); clean form sets `frame.uStack_8 = 3`; experiments assert `len == 0x34` and `buf[0x30] == 3` before `Client_EnqueueCombatFloater_INFERRED`.
- **Result:** control flow matches decompile; register-based ESI/EDI dispatch documented honestly; floater theater layout pinned.

## Assembly / machine-backed review

- Evidence: Ghidra `batch_decompile` only (no `disassemble_bytes`).
- Spot-checked: null character early-out; LevelHint `!= -1` branch; vehicle gate `+0x250`.
- **Result:** no critical contradiction for documented paths.

## Context review

- Dispatch table `raw/aa_exe_00815710.md` maps `0x205f` → this symbol.
- Distinct from `0x2017` absolute snapshot and `0x205e` credits.
- Callers of apply kernel include mission complete and local kill calc (not this handler alone).

## Behavioral review

- Runtime not executed (UF-002 / no Launcher).
- Floater presentation not dual-run verified.

## Naming review

- Kept retail symbol `Client_AwardKillExperience` despite misnomer; plate/docs note “all GiveXP”.
- Did not invent `Client_RecvGiveXP` as rename without registry consensus — listed as behavioral alias in function doc.

## Skeptical review

Attempted to falsify:

1. **Does GiveXP run kill-path spree?** — **Rejected.** Explicit `PacketOrNonKill` argument.
2. **Is LevelHint the new character level?** — **Unproven.** Writes the same byte used as spree/hint (`+0x738`); not the level field `+0x6c8`. Server may use it as spree seed / UI hint; do not assume it sets level.
3. **Is floater amount pre-scalar or post-scalar?** — Floater uses packet `ESI+4` raw amount, not the scaled result inside `AddExperience`. Personal gain may make bar != floater if scalar ≠ 1.0.
4. **Historical name means kills only?** — **Rejected.** Dispatch is all `0x205F`.

**Critical contradictions open:** none for static path.  
**Residual:** personal-gain vs floater mismatch; LevelHint semantics beyond byte write.

## Integration review

- Consistent with `systems/progression-xp.md` and `docs/XP.md` multiplayer authority model (server computes amount).

## Verdict

**Accept** for static verified reconstruction of the S2C GiveXP entry (not runtime-confirmed).
