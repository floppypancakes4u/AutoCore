# System: Entity / Ghost vehicle

**ID:** SYS-ENTITY  
**Priority:** 8  
**Status:** partial reconstruction  
**Updated:** 2026-07-15

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
- Owner forms: **high**
- Full non-combat dirty-flag taxonomy: **probable** (optional residual)
- Runtime: **blocked** UF-002
