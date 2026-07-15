# System: Actions / abilities / weapons

**ID:** SYS-ACTIONS  
**Priority:** 5  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15

## Entry points

| Symbol | Address | Role |
|--------|---------|------|
| Client_QuickBar_ActivateSlot | 0x009436c0 | slot/mode/page → skill/item/power |
| Input_TryFireSecondaryWeapons | 0x0091a550 | Secondary fire with heat gate |
| Client_CastSkillFromQuickBarSlot | 0x009418e0 | Skill cast from QB |
| Client_QuickBarActivateSkillSlot | 0x00921b50 | On-foot skill slot |

## QuickBar index

- `index = slot + page*10`; page=-1 uses UI+0x50c.
- Type at client+0x3220+idx*0x18: 1=skill, 2=item, 5=power.
- On-foot (player+0x6b9): slot0/1 special-case skill or fire.

## Secondary fire gates

- Local player exists; flags at entity(+4+4)+0xb8 & 0xd2 == 0
- Sector connection connected
- Vehicle +0x250 present
- `FUN_004f52e0` heat check else log "Failed to fire secondary weapons due to heat."
- Then `FUN_004f5110` fire; optional UI refresh if flags 0x6b8/0x6b9

## Confidence

- Slot type dispatch: **high**
- Fire packet internals of FUN_004f5110: **tentative**

## Residuals (not eligible high-pri)

| ID | Residual | Class |
|----|----------|-------|
| — | `FUN_004f5110` fire packet internals / cast packet leaf | optional depth (matrix Open on `009436c0` / `0091a550`) |
| WQ-RT-01 / UF-002 | Runtime dual-run | blocked |

High-pri QuickBar activate + secondary fire gate path is complete (static).
