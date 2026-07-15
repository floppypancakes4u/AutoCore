# System: Inventory

**ID:** SYS-INVENTORY  
**Priority:** 4  
**Status:** partial reconstruction  
**Updated:** 2026-07-15

## Scope

Grid cargo, grab/drop, equip/unequip, add-item notify, use-item responses.

## Entry points (sampled)

| Symbol | Address | Opcode / role |
|--------|---------|---------------|
| Client_SendInventoryGrab_FromGrid | 0x00860e20 | C2S 0x2034 size 0x20 |
| Client_RecvInventoryAddItem | 0x008151a0 | S2C add / loot notify |
| Client_SendInventoryUnequip | 0x00862c00 | C2S unequip |
| Client_RecvInventoryEquip | 0x00813f40 | S2C equip |
| InventoryGrid_* | 0x00570720+ | cell array / place / find free |
| Vehicle_CreateCargoInventoryFromPageCount | 0x004F3A30 | 6 × pages×13 grid |

## Grab packet (0x2034) from decompile

| Offset | Field |
|--------|-------|
| +0 | 0x2034 |
| +8 | item TFID-ish (from UI vfunc+0x3ac → +0x160) |
| +0x10 | inventory type byte (window+0x56c → +4) |
| +0x1c | quantity |

## Grid

- Width 6, height pages×13 (prior docs + cargo create).
- Place stamps all footprint cells (`FUN_00571620`).
- First-fit Y outer X inner (`FUN_005713a0`).

## Confidence

- Grab send path: **high**
- AddItem loot UI: **high**
- Full equip chain: **probable** (queued WQ-011)
