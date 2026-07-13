# Vehicle Combat Pools — Ghidra Symbol Map

Companion to `tools/ghidra/vehicle_combat_pool.md`. Tag **`combat-pools`** / **`timed-action`**.

Names with **`_Inferred`** are not PDB-verified.

## Period (corrected)

| Race id | Interval |
|---------|----------|
| 0 / 1 / 2 | **3000 ms** |
| other | **5000 ms** |

Clock: `g_dwActionSchedulerTickMs` = `GetTickCount()`.  
**Not** 16 ms (`g_dwTimedActionDefaultPeriodMs`).

## Entry points

```
Vehicle_ActivateEnterWorld
  → Vehicle_EnsureRegenerationHeartbeat (0x4F7E10)
      → VehicleCombatPoolAction_ctor (0x5FBDB0)
          → VehicleCombatPool_OnTick (0x5FBEA0) every 3s/5s
```

## Types

- `RE_VehicleCombatPoolFields` — vehicle combat fields
- `RE_CombatPoolAction` — heartbeat object
- `PowerPlantRuntime_Inferred` — plant heat/power/cool rates
- `GHOST_VEHICLE_MASK` / `RACE_ID_INFERRED` enums

## See also

Full tables: `tools/ghidra/vehicle_combat_pool.md`  
Server: `src/AutoCore.Game/Combat/VehicleCombatPool.cs`
