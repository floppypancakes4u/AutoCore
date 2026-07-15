# Review: Client_RecvCharacterLevel (aa_exe_00810f00)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## Reconstruction review

- Existing raw v1 `raw/aa_exe_00810f00.md` preserved (not overwritten).
- Re-decompiled via Ghidra MCP; matched prior capture.
- New exact source `reconstructed-exact/Client_RecvCharacterLevel.cpp` + apply callee capture `raw/aa_exe_00531e90.md`.
- **Result:** handler + absolute apply path documented.

## Assembly / machine-backed review

- Decompile only. Packet offsets for Level/Currency/XP agree with AutoCore `CharacterLevelPacket.Write` comments and Apply plate.

## Context review

- Used for login restore, `/credits`-style absolute money UI, level-up snapshots after server grants.
- Must not be confused with additive `0x205F` / `0x205E`.

## Skeptical review

1. **Does CharacterLevel add XP?** — **No.** Apply **assigns** `char+0x730 = packet.nExperience`.
2. **Does missing Health zero vehicle HP?** — HP apply is **gated**; mana always applied. Incomplete packets still risk zeroing absolute currency/XP/pools — server builders must fill all absolute fields.
3. **Local UI only on TFID match?** — **Yes.** Apply still runs for any looked-up object; UI refresh is local-only.

## Verdict

**Accept** static reconstruction for absolute snapshot path.
