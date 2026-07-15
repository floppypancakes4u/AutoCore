# Review: Client_SendInventoryUnequip (aa_exe_00862c00)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00862c00.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_SendInventoryUnequip.cpp` |
| Dispatch map | `docs/reconstruction/raw/aa_exe_00815710.md` (`0x203e` / `0x203f` S2C counterparts) |
| System doc | `docs/reconstruction/systems/inventory.md` |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x203E` | C2S InventoryUnequip |
| Packet size | `0x30` | `Client_SendSectorPacket(..., 0x30, ...)` |
| Player gate | `DAT_00d1b6d8 != 0` and player `+0x250 != 0` | must have vehicle |
| Item resolve vfunc | UI widget `+0x3ac` | selected equipment object |
| Blocked unequip | `FUN_004f6a80` non-zero → `FUN_00931db0` | abort, return `0` |
| Free-slot search | `FUN_005714e0(item, &x, &y, 1, -1)` | dest grid X/Y |
| Alt cargo retry | `FUN_004ce5c0(player)` then second free-slot | space recovery |
| Item TFID fields | item `+0x160` / `+0x164`, region byte `+0x168` | packet payload |
| Dest cells | packet `+0x28` / `+0x29` | free-slot result |

## Concrete raw ↔ clean matches (3+)

1. **Gates** — Both require global player `DAT_00d1b6d8`, vehicle at `+0x250`, and non-null item from vfunc `+0x3ac`; else return `0`.
2. **Blocked-unequip early out** — Both: `FUN_004f6a80(item) != 0` → `FUN_00931db0()` → return `0` (no packet).
3. **Free space algorithm** — Primary `FUN_005714e0`; on failure, if `FUN_004ce5c0(DAT_00d1b6d8)` then retry free-slot; still fail → localized string `"There is not enough space in your inventory for this equipment."` via `FUN_007fdfb0`, return `0`.
4. **Packet build** — Opcode `0x203e` / `0x203E`, size `0x30`; TFID from item `+0x160`/`+0x164`; region/type byte from `+0x168` into stack slot corresponding to packet `+0x10`; dest X/Y from free-slot into `+0x28`/`+0x29`; `Client_SendSectorPacket(&DAT_00d1a840, 0x30, ...)`.
5. **Pre-send UI refresh** — Both call widget vfunc `+0x34c` immediately before building the packet.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: unequip C2S is `0x203C` (equip opcode) or drop `0x2036`.**  
   **Falsified:** raw sets `auStack_30[0] = 0x203e`. S2C equip is a different opcode; unequip request is bidirectional family `0x203E` (this builder) / notify-response handlers on dispatch.

2. **Hypothesis: builder fills vehicle TFID fields like equip packet.**  
   **Falsified by raw plate:** “Vehicle TFID fields not filled by this builder.” Clean only packs item TFID + dest cells + size `0x30` — no vehicle TFID writes.

3. **Hypothesis: free-slot failure still sends unequip and lets the server reject.**  
   **Falsified:** failure path shows UI error and `return 0` without `Client_SendSectorPacket`.

4. **Hypothesis: dest X/Y come from the item’s current equipped slot, not a search.**  
   **Falsified:** X/Y are outputs of `FUN_005714e0` (and optional retry), written into `uStack_8`/`uStack_7` after search succeeds.

## Residual uncertainty (this unit)

- Exact ABI of vfunc `+0x3ac` when called with `(outX, outY, 1, -1)` vs no-args (decompiler shows both patterns) — clean models both call shapes but item pointer provenance is slightly fuzzy.
- Full layout of the middle of the `0x30` packet (bytes between TFID and dest) beyond the fields the builder writes.
- What `FUN_004f6a80` considers “blocked” (combat lock? soulbound? tutorial?) — not expanded.
- Runtime free-slot edge cases (UF-002).

## Verdict

**Accept** — C2S `0x203E` size `0x30`, player/vehicle gates, free-space fail/retry, and TFID+dest packing match raw. Helper semantics for free-slot remain extern.
