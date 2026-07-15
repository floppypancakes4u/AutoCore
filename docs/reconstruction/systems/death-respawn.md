# System: Death / INC / SpecialEvent Respawn

**ID:** SYS-RESPAWN  
**Priority:** 6  
**Status:** partial reconstruction (request + S2C + animation SM)  
**Updated:** 2026-07-15

## Scope

INC airlift request, SpecialEvent presentation, airlift state machine. Death UI entry still open (UF-001).

## Entry points

| Symbol | Address | Role |
|--------|---------|------|
| Client_SendRespawnInSector | 0x00935300 | C2S 0x2073 size 0x28 |
| Client_RecvSpecialEvent | 0x0080cc50 | S2C 0x20A9 type 0/1/2 |
| ClientSpecialEvent_Respawn_ctor | 0x00979650 | type 0 ctor |
| ClientSpecialEvent_Respawn_Update | 0x00979730 | phases 0..7 airlift SM |

## C2S RespawnInSector (0x2073)

| Offset | Field |
|--------|-------|
| +0 | opcode 0x2073 |
| +4 | float3 current position (NOT dest) |
| +0x10 | float4 quaternion |
| +0x20 | int64 COID from entity at game+0xe98 (character in live captures) |

Gates: game+0xe98 and +0x250 non-null; match selected id +0xd28.

## S2C SpecialEvent (0x20A9)

| Offset | Field |
|--------|-------|
| +4 | type: 0=Respawn, 1=TeleportOut, 2=TeleportIn |
| +8 | dest pos |
| +0x14 | dest quat |
| +0x28 | target TFID — must match local +0xe98 entity TFID for fast path |
| +0x40 | flag non-zero for full Respawn ctor |

**Preserved quirk:** vehicle COID as target silent-returns (no airlift). Character TFID required.

## State machine (Update +0x6d)

| Phase | Behavior (summary) |
|-------|---------------------|
| 0 | After time threshold: spawn cptest.geo ship, attach |
| 1–2 | lift timing / camera |
| 3 | teleport target to dest at +0x40; optional camera bind |
| 4–5 | lower / cleanup camera FUN_0090dd50 |
| 6–7 | finish / return 1 |

## Dependencies

- SYS-COMMS for dispatch case 0x20a9
- Server LastStation / MarkRepairStation (server-side; Documentation/RESPAWN_SYSTEM.md)

## Confidence

- Packet layouts: **confirmed**
- TFID match rule: **confirmed** (live + decompile)
- Phase timing constants: **probable** (named floats uncertain)

## INC contact path (UF-001 partial close)

| Symbol | Address | Role |
|--------|---------|------|
| Client_INC_ContactCountdownTick | 0x0091ee20 | Countdown UI; option 0→SendRespawnInSector |
| Client_SendRespawnInSector | 0x00935300 | C2S 0x2073 |
| Client_SendInstantRepairRequest | 0x0092ce00 | option 1 |

**UI fields:** +0xc20 last tick, +0xc24 remaining, +0xc28 total, +0xc30 option (0/1/2).

**Tutorial strings (client):** death → press INC on Quick Bar → airlift to last repair station.

**Still open:** exact function that opens INC UI from zero-HP/corpse flag (not yet symbolized); countdown→send is confirmed.

## Open

- UF-001 residual: corpse HP → show INC UI open
