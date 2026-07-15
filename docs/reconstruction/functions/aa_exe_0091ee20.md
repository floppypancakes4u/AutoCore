# Function: Client_INC_ContactCountdownTick

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_0091ee20` |
| Address | `0x0091ee20` |
| System | death-respawn |
| Confidence | high (countdown + option 0); medium (fee math) |
| Updated | 2026-07-15 |

## Purpose

Drive INC contact UI countdown; on expiry dispatch option 0 (airlift/Respawn), 1 (instant repair), or 2 (transfer).

## Behavioral summary

- Requires remaining ms at UI+0xc24 > 0 and local player+vehicle.
- Cancels if hardpoint flags set or movement/combat gates fail → FUN_0091edd0.
- Mid-countdown chat: "Contacting INC... Please do nothing for 5 more seconds!"
- Option 0: toast repair-station string → Client_SendRespawnInSector (0x2073).
- Option 1: fee check → Client_SendInstantRepairRequest.
- Option 2: fee/map check → FUN_008ed650.

## Death UI entry (UF-001)

Tutorial strings say: press INC button on Quick Bar when dead/stuck. Countdown path is confirmed. Exact corpse-HP → show-INC-UI open function is still residual (not this function).

## Artifacts

- raw/aa_exe_0091ee20.md
- reconstructed-exact/Client_INC_ContactCountdownTick.cpp
