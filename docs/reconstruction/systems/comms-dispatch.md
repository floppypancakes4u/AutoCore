# System: Communication / packet dispatch

**ID:** SYS-COMMS  
**Priority:** 13 (supporting)  
**Status:** partial  

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

Progression vertical: `systems/progression-xp.md`. Full case table is WQ-016.
