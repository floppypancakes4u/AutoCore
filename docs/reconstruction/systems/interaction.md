# System: Interaction / UseObject

**ID:** SYS-INTERACT  
**Priority:** 3  
**Status:** partial reconstruction  
**Updated:** 2026-07-15

## Scope

Client → server UseObject (0x2072) for NPC/world interactables; objective id attachment.

## Entry points

| Symbol | Address | Role |
|--------|---------|------|
| Client_SendUseObject | 0x00916740 | Primary C2S 0x2072 size 0x20 |
| Client_SendUseObject_IfInteractable | 0x00930d70 | Gate on interactable or type==4 |

## Packet layout (0x2072)

| Offset | Size | Field |
|--------|------|-------|
| +0x00 | 4 | Opcode 0x2072 |
| +0x04 | 4 | pad |
| +0x08 | 16 | TFID of target object (from obj+0x160) |
| +0x18 | 4 | IDObjective or -1 |

## Flow

1. Store selected object id at game+0xd28.
2. Copy TFID from object +0x160..+0x16c.
3. `Client_FindObjectiveMatchingTarget(clone+0x34)` → objective id or -1.
4. Send via sector connection vtable+0x18 size 0x20.
5. Alt path: only if `FUN_00524520` interactable **or** clone type == 4; and flag at +0xe04+0xf6 == 0.

## Confidence

- Layout and opcode: **confirmed** (strings EMSG_Sector_UseObject; decompile)
- Interactable predicate internals: **tentative**
