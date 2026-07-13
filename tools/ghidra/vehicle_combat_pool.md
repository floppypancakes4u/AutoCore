# Vehicle combat-pool (session RE cleanup)

Human-readable map of Ghidra analysis for **this session only** (tag
`session-combat-pool-re`). Addresses: `autoassault.exe`.

Types prefixed `RE_` or `_Inferred` are **inferred layouts**, not full PDB types.

## Call graph

```
Vehicle_ActivateEnterWorld (0x00503F30)          [caller; not fully cleaned]
  └─ Vehicle_CreateCombatPoolAction (0x004F7E10)
       ├─ VehicleCombatPoolAction_ctor (0x005FBDB0)
       ├─ CVOGHBList_Enqueue (0x005078F0)
       └─ CVOGHBBase_Start (0x005081C0)
            └─ VehicleCombatPool_OnTick (0x005FBEA0)  // every 3s or 5s
                 ├─ Vehicle_GetPowerRegenRate (0x004F3870)
                 ├─ Vehicle_GetCoolRate (0x004F3840)
                 ├─ Vehicle_GetHpRegenRate (0x004FB630) / Creature_GetHpRegen...
                 ├─ Vehicle_GetShieldRegenRate (0x004FB600)
                 ├─ Vehicle_AddHeat (0x004F7210)
                 ├─ Vehicle_IsAnyWeaponFiring (0x004F7480)
                 ├─ Vehicle_SetCurrentShield (0x00419140)
                 ├─ NetObject_SetMaskBits (0x00786C10)
                 └─ CVOGHBBase_RescheduleAfterFire (0x00508350)

Weapon fire cost (related):
  Weapon_ApplyShotHeatAndPowerCost (0x0056AD00)
    └─ Vehicle_AddHeat

Skill hotbar cooldown (related):
  CVOGHBOKToCastAgain_ctor (0x0051E240)
    └─ Vehicle_GetSkillCooldownModifier (0x0052A9B0)
```

## Layout (INFERRED field names)

| Offset | Field | Notes |
|--------|-------|-------|
| vehicle+0x144 | nCurrentShield | int |
| vehicle+0x148 | nMaxShield | int |
| vehicle+0x150 | nCurrentHeat | int |
| vehicle+0x154 | nCoolAccumulator | int; firing slows cool |
| vehicle+0x1E0 | nHeatAdjust | CalcHeatMaximum |
| vehicle+0x1E4 | sCoolRateAdjust | short |
| vehicle+0x244 | nMaxHeat | int |
| vehicle+0x268 | pPowerPlant | plant +0xB8 power regen, +0xBA cool, +0xCC skill CD |
| vehicle+0x270 | pRaceItem | clonebase +0x3FA HP, +0x4B6 shield (INFERRED names) |
| vehicle+0x27C | pCombatPoolAction / pRegenerationHeartbeat | one-shot slot |
| creature+0x12C | sCurrentPower | short |
| creature+0x12E | sMaxPower | short |
| action+0x08 | nPeriodMs | 3000 or 5000 |
| action+0x24 | bHeatAtMaxDebounce | arms to 2 |
| action+0x25 | bShieldEmptyDebounce | arms to 2 |

## Ghost masks (`VehicleGhostMaskBits` enum)

| Name | Value | Field |
|------|-------|-------|
| ShieldMask | `0x04000000` | current shield |
| PowerMask | `0x08000000` | current power |
| HeatMask | `0x20000000` | current heat |

## Globals cleaned

| Address | Name | Notes |
|---------|------|-------|
| `0x00D17990` | `g_pNetObjectDirtyListHead` | static dirty-list head (INFERRED) |
| `0x00A0F714` | `g_flOverheatCoolFrac` | ~0.3 → cool at 70% when over max |
| `0x009DD2D0` | `g_pVtbl_VehicleCombatPoolAction` | INFERRED vtable |
| `0x009CE1C4` | `g_pVtbl_CVOGHBOKToCastAgain` | INFERRED vtable |

## Data types created

| Type | Role |
|------|------|
| `VehicleGhostMaskBits` | enum masks |
| `VehicleCombatPoolPeriodMs` | enum 3000 / 5000 |
| `RE_NetObjectDirtyListNode` | +0x0C prev, +0x10 next, +0x18/1C mask |
| `RE_VehicleCombatPoolFields` | combat-pool vehicle offsets |
| `RE_CombatPoolAction` | action debounce / period |
| `RE_PowerPlantCombatFields` | plant rates |
| `RE_CreaturePowerFields` | creature power shorts |

(Concurrent RE may also define `VehicleCombatPools_Inferred`, `PowerPlantRuntime_Inferred`.)

## Function index (session tag)

| Address | Name |
|---------|------|
| `0x004F7E10` | `Vehicle_CreateCombatPoolAction` |
| `0x005FBDB0` | `VehicleCombatPoolAction_ctor` |
| `0x005FBEA0` | `VehicleCombatPool_OnTick` |
| `0x004F3870` | `Vehicle_GetPowerRegenRate` |
| `0x004F3840` | `Vehicle_GetCoolRate` |
| `0x004FB630` | `Vehicle_GetHpRegenRate` |
| `0x004FB600` | `Vehicle_GetShieldRegenRate` |
| `0x004F7210` | `Vehicle_AddHeat` |
| `0x00419140` | `Vehicle_SetCurrentShield` |
| `0x004F7360` | `Vehicle_CalcHeatMaximum` |
| `0x004F7480` | `Vehicle_IsAnyWeaponFiring` |
| `0x0056AD00` | `Weapon_ApplyShotHeatAndPowerCost` |
| `0x0052A9B0` | `Vehicle_GetSkillCooldownModifier` |
| `0x0051E240` | `CVOGHBOKToCastAgain_ctor` |
| `0x00786C10` | `NetObject_SetMaskBits` |
| `0x005078F0` | `CVOGHBList_Enqueue` (dependency) |
| `0x005081C0` | `CVOGHBBase_Start` (dependency) |
| `0x00508350` | `CVOGHBBase_RescheduleAfterFire` (dependency) |

See `vehicle_combat_pool.c` for source-like pseudocode.
