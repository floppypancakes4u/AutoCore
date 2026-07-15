# Review: Client_RecvUnlockRegion (aa_exe_00809550)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00809550.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_RecvUnlockRegion.cpp` |
| Dispatch map | `docs/reconstruction/raw/aa_exe_00815710.md` (case `0x205b`) |
| Related grant unlock | `CVOGReaction_GiveMission` / `UnlockContinentObject` (missions) |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x205B` | S2C UnlockRegion |
| Character | game `+0xe98` | null → silent return |
| ContinentId | packet `+4` (int) | relock/unlock key |
| UnlockFlag | packet `+8` (byte) | `0` → relock |
| ExploredBits | packet `+0xC` (uint) | area bitmask |
| Unlocked hash | character `+0x534` | `CNDHash_LookupByKey` |
| Entry prior bits | entry `+8` | compare to packet bits |
| Area loop | `i = 0 .. 0x1f` | areas numbered `i+1` (1..32) |

## Concrete raw ↔ clean matches (3+)

1. **Null character early out** — Both load character from `game+0xe98` and return if null.
2. **Relock branch** — Both: `*(char*)(packet + 8) == 0` → `CVOGReaction_RelockContinentObject(*(int*)(packet + 4))` and return (no bit walk).
3. **Missing local entry** — Both: `CNDHash_LookupByKey(character+0x534, continentId) == null` → `CVOGReaction_UnlockContinentObject(character, continentId)` only (packet ExploredBits not applied on create — raw plate: bits ignored / empty entry).
4. **Opcode wiring** — PacketDispatch `0x205b` → `Client_RecvUnlockRegion`; clean enum `kUnlockRegionOpcode = 0x205B`.

## Concrete mismatch (clean vs raw — drives partial)

- **Bit-walk semantics:** raw walks `i = 0..0x1f` and, when `(packetBits & mask) != (entryBits & mask)`, calls  
  `CVOGCharacter_SetAreaExploredBit(character, continentId, (byte)i + 1, bitMask != 0)`  
  — i.e. can **clear or set** per differing bit, and passes continent id.  
  Clean only applies when packet bit is set and previous bit is clear, omits continent id argument, and skips the equal-bits early structure slightly differently. Structural intent (1..32 areas) is right; exact call signature/diff semantics are not bit-exact.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: UnlockFlag==0 still applies ExploredBits then relocks.**  
   **Falsified:** relock returns immediately; bits unread on that path.

2. **Hypothesis: first unlock applies full ExploredBits from the packet.**  
   **Falsified by raw plate and body:** no-entry path only `UnlockContinentObject`; ExploredBits ignored until an entry exists and bits differ (AutoCore note: often send twice — bootstrap then bits).

3. **Hypothesis: areas are 0-indexed 0..31 in the SetArea API.**  
   **Falsified:** raw passes `(byte)i + 1` with `i < 0x20` → areas **1..32**.

4. **Hypothesis: this handler is the same as GiveMission’s continent unlock.**  
   **Falsified:** GiveMission calls `UnlockContinentObject` from a reaction with objective `+0x120` id; this is S2C `0x205B` with continent + explored bitmask and optional relock. Related helper, different entry point.

## Residual uncertainty (this unit)

- Exact layout / type of `USContinentUnlocked` entry beyond `+8` bits dword.
- Whether `SetAreaExploredBit` third/fourth args are (continent, area, bool) as decompiler shows — clean signature disagrees; **raw wins until re-decompile confirms**.
- Decompiler parameter naming (`pPacket` vs `in_stack_00000004`) is messy; field offsets used are still consistent within the body.
- Runtime double-send bootstrap (server/AutoCore) not re-verified here (UF-002).

## Verdict

**Partial** — opcode, character gate, relock, missing-entry unlock-only, and 32-area intent match; clean bit-diff / `SetAreaExploredBit` signature is not fully faithful to raw.
