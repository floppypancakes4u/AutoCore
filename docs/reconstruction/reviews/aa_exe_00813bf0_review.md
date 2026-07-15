# Review: Client_RecvInventoryUnequipNotify

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw decompile / prior tools/ghidra notes
- Cross-system consistency (inventory equip-via-drop; ghost combat masks)

## Skeptical checks

- Equip C2S is not 0x203C — confirmed plate: equip via Drop HARDPOINT type 2
- Unequip 0x203E bidirectional — preserved
- Ghost creature-owner form omits wheels — preserved as risk note

## Verdict

Accept static reconstruction. Runtime blocked UF-002.
