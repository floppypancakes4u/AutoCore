# Review: Client_SendUseObject (aa_exe_00916740)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00916740.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_SendUseObject.cpp` |
| Alt path note | raw/clean comments for `Client_SendUseObject_IfInteractable` @ `0x00930d70` |
| System doc | `docs/reconstruction/systems/interaction.md` |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x2072` | C2S UseObject |
| Packet size | `0x20` | send buffer |
| Selection store | `game + 0xd28 = object` | selected interact target |
| TFID source | object `+0x160..+0x16c` (16 bytes) | packet `+8` |
| Objective id | packet `+0x18` | from matching objective or `-1` |
| Objective key | `*(object+0xa8)+0x34` | `Client_FindObjectiveMatchingTarget` |
| Objective id field | match record `+0x10` | packed when found |
| Net send | `g_pSectorNetConnection` vfunc `+0x18` | `( -1, buf, 0x20, 0 )` |

## Concrete raw ↔ clean matches (3+)

1. **Selection write** — Both: `*(game + 0xd28) = object` (raw `param_1` / `in_EAX` pairing).
2. **TFID copy** — Both copy four dwords from object `+0x160`..`+0x16c` into packet starting at `+8` (`local_18`..`local_c`).
3. **Opcode / size** — Opcode `0x2072`, send length `0x20`.
4. **Objective attachment** — `Client_FindObjectiveMatchingTarget(*(object+0xa8)+0x34)`: null → `local_8 = -1`; else `local_8 = *(match + 0x10)`.
5. **Connection guard** — Send only if sector net connection non-null; vfunc offset `+0x18` with size `0x20`.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: UseObject always attaches a real objective id.**  
   **Falsified:** when `Client_FindObjectiveMatchingTarget` returns 0, objective field is **`-1`**. Primary path *tries* to attach; it does not require a match to send.

2. **Hypothesis: the IfInteractable alternate path is the same builder.**  
   **Falsified (partial, documented):** clean/raw notes `0x00930d70` gates on interactable / clone type 4 and does **not** set objective id the same way. This review is only for `0x00916740` primary path.

3. **Hypothesis: packet is 0x28 like RespawnInSector.**  
   **Falsified:** size is `0x20` (opcode + pad + TFID16 + objective int).

4. **Hypothesis: this is S2C dialog `0x206D`.**  
   **Falsified:** this is C2S `0x2072`. Server may reply with dialog `0x206D` / response `0x206E` (plate); those are separate dispatch cases.

## Residual uncertainty (this unit)

- Full TFID_16 field names inside the 16 bytes (coid vs serial) — treated as opaque 16-byte blob from object.
- `Client_FindObjectiveMatchingTarget` matching rules (range? mission filter?) not expanded.
- Whether send uses guaranteed flag `-1` semantics consistently across builds.
- IfInteractable path (`0x00930d70`) needs its own review if promoted — not this unit.
- Runtime interact captures (UF-002).

## Verdict

**Accept** — C2S `0x2072` size `0x20`, selection store, TFID copy, and objective-or-`-1` packing match raw. Alternate interactable path intentionally separate.
