# Review: CVOGReaction_AddExperience (aa_exe_00533c30)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## Reconstruction review

- Full decompile captured to `raw/aa_exe_00533c30.md`.
- Exact control flow (kill branch, soft cap, level up/down loops, zero early-out) reconstructed in `reconstructed-exact/CVOGReaction_AddExperience.cpp`.
- Character offsets match `docs/XP.md` field map and LevelUp writers.
- **Result:** core apply path high confidence.

## Assembly / machine-backed review

- Ghidra decompile primary evidence.
- Float table constants via `read_memory`: `0x00aaa7b8≈0.02`, `0x00aaa8f4≈0.04`, `0x00aaa8f0≈0.06`.
- Threshold sentinel `0x7fffffff` confirmed in `Experience_GetCumulativeThreshold` decompile.
- **Result:** numeric anchors consistent; weapon-bonus index source remains partially opaque.

## Context review

- Callers: GiveXP packet, kill calc, mission complete, create-from-packet, mission prereq check.
- LevelUp/Down and threshold helpers decompiled in same pass.
- Aligns with AutoCore `ExperienceService` design notes in XP.md.

## Behavioral review

- No live dual-run. Soft-cap and multi-level-up loops not empirically stepped.

## Naming review

- Existing Ghidra symbols retained (`CVOGReaction_AddExperience`, `XpIsKillPath`, `PacketOrNonKill`).
- No speculative rename of nested `+0xe818` field without type evidence.

## Skeptical review

Attempted to falsify:

1. **Is personal scalar applied on packet grants?** — **Yes.** Scalar always multiplies before soft cap; only kill-path preamble is skipped for packets.
2. **Does max-level soft cap use level from `+0x6c8` or vfunc?** — Soft-cap branch uses **vfunc `+0x27c`** for player level; level-up loop compares `+0x6c8` to `+0xc50`. Possible divergence if vfunc and field disagree (residual).
3. **Is weapon bonus index the spree byte?** — **No** in decompile: index loads from nested object `+0xe818`, separate from `+0x738` spree update.
4. **Can negative XP de-level?** — **Yes**, with LevelDown loop when level ≥ 2 and total below previous threshold.
5. **Is client kill XP usable in this image?** — Separate issue: `g_flGlobalKillXpScalar` BSS 0 zeros local kill path before AddExperience (documented in XP.md); packet path unaffected.

**Critical contradictions open:** none blocking S2C grant semantics.  
**Residuals:** `+0xe818` typing; vfunc-level vs field-level consistency; full LevelDown body not fully re-pasted this pass.

## Integration review

- Packet GiveXP → this kernel (non-kill) → optional LevelUp → UI.
- Absolute CharacterLevel bypasses this kernel (writes `+0x730` directly).

## Verdict

**Accept** as the authoritative client apply kernel for reconstruction; server `GiveXp` should mirror personal scalar, soft cap, and cumulative level-up rules.
