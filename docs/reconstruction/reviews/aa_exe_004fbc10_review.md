# Review: VehicleEntity_PushDriveAxesToController (aa_exe_004fbc10)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## Reconstruction review

- Compared `reconstructed-exact` against `raw/aa_exe_004fbc10.md` (or session decompile).
- Confirmed opcodes/sizes/key offsets match plate comments and prior docs where applicable.
- **Result:** clean form preserves observable branches; some helpers left as named externs.

## Assembly / machine-backed review

- Primary evidence: Ghidra decompile (not disassemble_bytes).
- Spot-checked: integer masks, early returns, packet field offsets.
- **Result:** no critical decompiler/control-flow contradiction found for documented paths.

## Context review

- Callers/callees agree with system doc `systems/movement.md`.
- Cross-checked Respawn TFID rule with Documentation/RESPAWN_SYSTEM.md; UseObject with EMSG strings; movement with MOTION_CLIENT_RE.md.

## Behavioral review

- Runtime not re-executed this session (UF-002). Prior live captures accepted as supporting evidence only where cited.

## Naming review

- Canonical names match Ghidra symbols already present in AA-decode; no speculative renames without evidence.

## Skeptical review

Attempted to falsify:

1. **Respawn COID is vehicle?** — Rejected: RecvSpecialEvent compares to game+0xe98; live docs say character. Fallback requires vehicle type 0x14 but fast path is character.
2. **Pose soft path always used?** — Rejected: soft only when physics not fully ready; hard path writes entity fields.
3. **Handbrake is sharp-turn channel?** — Rejected: plate + push path treat +0x61c as handbrake byte to ctrl+0x24.
4. **UseObject always attaches objective?** — Partial: primary path does; IfInteractable path does not set objective id the same way.

**Critical contradictions open:** none for this unit.  
**Residual uncertainty:** full bind-table enumeration; death UI entry (UF-001); runtime differential (UF-002).

## Integration review

- Input → QuickBar → fire/use chains consistent.
- Respawn Send → server (out of scope) → RecvSpecialEvent → Update SM consistent.

## Verdict

**Accept** for static verified reconstruction status (not runtime-confirmed).
