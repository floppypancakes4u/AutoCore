# System: Communication / packet dispatch

**ID:** SYS-COMMS  
**Priority:** 13 (supporting)  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15

## Hub

- `Client_PacketDispatch` @ 0x00815710 — central S2C opcode switch
- `Client_SendSectorPacket` @ 0x00807460 — C2S send helper (size + buffer)

## Sample opcode → handler (from symbols + prior docs)

| Opcode | Name | Handler |
|--------|------|---------|
| 0x2034 | InventoryGrab | send from UI |
| 0x2072 | UseObject | Client_SendUseObject |
| 0x2073 | RespawnInSector | Client_SendRespawnInSector |
| 0x20A9 | SpecialEvent | Client_RecvSpecialEvent |
| 0x2070 | CompleteDynamicObjective | Client_RecvCompleteDynamicObjective / ObjectiveState |
| 0x2017 | CharacterLevel (absolute XP/money) | Client_RecvCharacterLevel |
| 0x205E | GiveCredits (additive) | Client_RecvGiveCredits |
| 0x205F | GiveXP (additive) | Client_AwardKillExperience |
| 0x2027 / 0x2028 | StoreTransaction | send / `Client_RecvStoreTransactionResponse` |
| 0x206C / 0x206D | GroupReactionCall / NpcMissionDialog | recv handlers |

Progression vertical: `systems/progression-xp.md`. Case map hub is **WQ-016 complete**.

## Residuals (not eligible high-pri — see WORK_QUEUE Residual table)

| ID | Residual | Class |
|----|----------|-------|
| WQ-016-r1 | Remaining FUN_* leaf handlers beyond case map | optional depth |
| WQ-RT-01 / UF-002 | Runtime dual-run | blocked |
