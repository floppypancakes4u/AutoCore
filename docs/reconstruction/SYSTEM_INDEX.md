# SYSTEM_INDEX

**Last updated:** 2026-07-15  
**Target:** autoassault.exe  
**Ordering:** prompt priority (user-facing first). Lifecycle last.

| ID | System | Priority | User impact | Status | Key entry points | Doc |
|----|--------|----------|-------------|--------|------------------|-----|
| SYS-INPUT | User input / command binding | 1 | Direct | complete (static) | Client_Input_OnKeyDown_MatchAction 0x00911030; PollBoundActions 0x00925d60; DriveControlTick 0x009223b0 | systems/input.md |
| SYS-MOVEMENT | Vehicle movement / network pose | 2 | Direct | complete (static) | Vehicle_setDrivingInputs 0x00504c70; PushDriveAxes 0x004fbc10; PoseApply FUN_0053eec0 0x0053eec0 | systems/movement.md |
| SYS-INTERACT | UseObject / activation | 3 | Direct | complete (static) | Client_SendUseObject 0x00916740; Client_SendUseObject_IfInteractable 0x00930d70 | systems/interaction.md |
| SYS-INVENTORY | Inventory grab/drop/equip/add | 4 | Direct | complete (static) | Grab_FromGrid 0x00860e20; RecvAddItem 0x008151a0; Equip/Unequip Recv* | systems/inventory.md |
| SYS-ACTIONS | Quickbar / skills / secondary fire | 5 | Direct | complete (static) | QuickBar_ActivateSlot 0x009436c0; Input_TryFireSecondaryWeapons 0x0091a550 | systems/actions-weapons.md |
| SYS-RESPAWN | Death / INC / SpecialEvent Respawn | 6 | Direct | complete (static)* | SendRespawn 0x00935300; RecvSpecialEvent 0x0080cc50; INC countdown 0x0091ee20 | systems/death-respawn.md |
| SYS-MISSION | Missions / objectives | 7 | High | complete (static) | GiveMission 0x005327C0; CompleteObjective; 0x2070/0x2071 | systems/missions.md |
| SYS-PROGRESSION | XP / level / currency | 7 | High | complete (static) | AwardKillExperience 0x0080ae70; CharacterLevel 0x00810f00; AddExperience 0x00533c30 | systems/progression-xp.md |
| SYS-ENTITY | Ghost vehicle / create object | 8 | High | complete (static)** | VehicleNet_UnpackGhostVehicle 0x005f7720 | systems/entity-vehicle.md |
| SYS-VENDOR | Dialog / vendors / store | 9 | Medium | complete (static) | 0x206D/0x206E/0x206C + store 0x2027/0x2028 | systems/dialog-vendors.md |
| SYS-WORLD | Map / region / transfer | 10 | Medium | complete (static) | UnlockRegion 0x00809550 | systems/world-transitions.md |
| SYS-UI | Notifications / UI-facing state | 11 | Medium | partial | FUN_008f8200 | — |
| SYS-PERSIST | Persistence for above | 12 | Indirect | deferred | character create fields | — |
| SYS-COMMS | Packet dispatch hub | 13 | Supporting | complete (static) | Client_PacketDispatch 0x00815710 | systems/comms-dispatch.md |
| SYS-LIFECYCLE | Login / spawn / teardown | 14 | Supporting | deferred | Client_RecvLogin* | — |

\* UF-001 residual: corpse/zero-HP → **open** INC UI (countdown path complete).  
\** Combat dirty apply + owner forms + drive path reconstructed; `Ghost_ReadOwnerBlockAndUnpack` live-called from `DAT_00d1798c` initial path (call-site gate). Intermediate non-combat dirty-flag names are residual detail only (not blocking high-pri vertical).

## Prior docs (do not erase)

- `docs/MOTION_CLIENT_RE.md`, `docs/inventory-*.md`, `docs/missionState.md`, `Documentation/RESPAWN_SYSTEM.md`, `docs/XP.md`, `docs/networking.md`, `tools/ghidra/vehicle_combat_pool.*`
