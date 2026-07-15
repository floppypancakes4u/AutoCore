# TYPE_REGISTRY

**Updated:** 2026-07-15

| Type | Kind | Size | System | Evidence | Confidence | Notes |
|------|------|------|--------|----------|------------|-------|
| TFID / TFID_16 | handle | 16 | global | packets | high | object identity |
| GameOpcode | enum u32 | 4 | networking | EMSG strings | high | |
| RespawnInSectorPacket | struct | 0x28 | respawn | Client_SendRespawnInSector | high | includes opcode |
| UseObjectPacket | struct | 0x20 | interact | Client_SendUseObject | high | 4-byte pad after obj id |
| InventoryGrabPacket | struct | 0x20 | inventory | Grab_FromGrid | high | |
| InventoryEquipPacket | struct | 0x40 | inventory | RecvInventoryEquip | high | S2C 0x203C |
| InventoryUnequipPacket | struct | 0x30 | inventory | Send/Recv 0x203E | high | bidirectional |
| InventoryDropPacket | struct | 0x20 | inventory | Drop_Hardpoint 0x2036 | high | type@end=2 hardpoint |
| SpecialEventPacket | struct | ≥0x44 | respawn | RecvSpecialEvent | high | type@+4 flag@+0x40 |
| UnlockRegionPacket | struct | ~12+ | world | RecvUnlockRegion | high | 0x205B |
| VehicleGhostMaskBits | enum | 4 | combat | tools/ghidra | high | shield/power/heat |
| SoftPoseBuffer | struct | 0x40 | movement | FUN_0053eec0 | high | pos/rot/vel/ang |
