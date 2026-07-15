# System: Entity / Ghost vehicle

**ID:** SYS-ENTITY  
**Priority:** 8  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15  
**SYSTEM_INDEX:** complete (static)** — combat dirty + owner forms + drive; non-combat flag names residual only

## Entry points

| Symbol | Address | Role |
|--------|---------|------|
| VehicleNet_UnpackGhostVehicle | 0x005f7720 | Ghost unpack initial/delta |
| VehicleNet_PackUpdate | 0x005f5de0 | Pack |
| Vehicle_setDrivingInputs | 0x00504c70 | Pose+axes consume |
| Vehicle_CreateCombatPoolAction | 0x004F7E10 | Combat regen heartbeat |

## Behavioral notes

- Large bitstream unpack; combat pool fields at vehicle+0x144..+0x154
- Creature-owner form lacks wheels path — server must not arm owner+activate without wheelset
- Related crash docs: docs/ghostPlan.md, docs/nullWheels.md

## Combat dirty apply (UF-006 high-pri)

| Dirty flag (decompile) | Payload | Apply |
|------------------------|---------|-------|
| local_ed | u32 | vehicle+0x150 heat |
| local_fa | u32 | vehicle+0x148 max shield; clamp current |
| local_f5 | u32 | vehicle+0x144 shield (Ghost_ClampShield) |
| local_141 | u32 | power via vtbl+0x214/+0xAC |
| local_f0 | floats | Vehicle_setDrivingInputs |

BitStream: flag at cursor then optional 32-bit payload; cursor `stream+0x18`, limit `+0x2c`, buf `+0x0c`.

## Confidence

- Combat dirty apply + clamp: **high**
- Owner forms: **high** (`Ghost_ReadOwnerBlockAndUnpack` live from `DAT_00d1798c` path; UF-006 closed / WQ-021 complete)
- Full non-combat dirty-flag taxonomy: **probable** (optional residual WQ-021-r1)
- Runtime: **blocked** UF-002 / WQ-RT-01

## Residuals (not eligible high-pri — see WORK_QUEUE Residual table)

| ID | Residual | Class |
|----|----------|-------|
| WQ-021-r1 | Intermediate non-combat dirty-flag name taxonomy | optional depth |
| WQ-RT-01 / UF-002 | Runtime dual-run | blocked |
