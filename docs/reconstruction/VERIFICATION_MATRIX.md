# VERIFICATION_MATRIX

**Updated:** 2026-07-15

Status codes: Y=yes, N=no, P=partial, B=blocked

| Stable ID | Name | System | Pri | Raw | Boundary | Sig | Types | CF | DF | SE | Callers | Callees | Layout | State | Comms | Persist | Runtime | Diff | IndRev | Skep | Docs | Overall | Open |
|-----------|------|--------|-----|-----|----------|-----|-------|----|----|----|---------|---------|--------|-------|-------|---------|---------|------|--------|------|------|---------|------|
| aa_exe_00911030 | Client_Input_OnKeyDown_MatchAction | input | 95 | Y | Y | P | P | Y | P | Y | P | P | P | P | N | N | B | P | Y | Y | Y | P | bind table |
| aa_exe_00925d60 | Client_Input_PollBoundActions | input | 94 | Y | Y | P | P | P | P | Y | P | P | N | P | P | N | B | N | Y | Y | Y | P | full branch map |
| aa_exe_009223b0 | Client_Input_DriveControlTick | input | 93 | Y | Y | P | P | P | P | Y | P | P | P | P | N | N | B | N | Y | Y | Y | P | full annotate |
| aa_exe_00504c70 | Vehicle_setDrivingInputs | movement | 92 | Y | Y | Y | P | Y | Y | Y | Y | Y | Y | Y | Y | N | B | P | Y | Y | Y | Y | activate path |
| aa_exe_004fbc10 | VehicleEntity_PushDriveAxesToController | movement | 92 | Y | Y | Y | P | Y | Y | Y | Y | P | Y | Y | N | N | B | P | Y | Y | Y | Y | speed-cap floats |
| aa_exe_0053eec0 | NetworkPoseApply_FUN_0053eec0 | movement | 92 | Y | Y | Y | P | Y | Y | Y | Y | P | Y | Y | Y | N | B | P | Y | Y | Y | Y | integrateDt source |
| aa_exe_00916740 | Client_SendUseObject | interaction | 90 | Y | Y | Y | Y | Y | Y | Y | P | Y | Y | Y | Y | N | B | Y | Y | Y | Y | Y | none critical |
| aa_exe_00930d70 | Client_SendUseObject_IfInteractable | interaction | 90 | Y | Y | Y | P | Y | Y | Y | P | P | P | P | Y | N | B | Y | Y | Y | Y | Y | FUN_00524520 |
| aa_exe_00860e20 | Client_SendInventoryGrab_FromGrid | inventory | 88 | Y | Y | Y | P | Y | Y | Y | P | P | P | Y | Y | N | B | Y | Y | Y | Y | Y | equip chain |
| aa_exe_008151a0 | Client_RecvInventoryAddItem | inventory | 87 | Y | Y | P | P | Y | P | Y | P | P | P | Y | Y | N | B | N | Y | Y | Y | P | place helpers |
| aa_exe_009436c0 | Client_QuickBar_ActivateSlot | actions | 86 | Y | Y | Y | P | Y | Y | Y | Y | Y | P | Y | P | N | B | Y | Y | Y | Y | Y | cast packet |
| aa_exe_0091a550 | Input_TryFireSecondaryWeapons | actions | 86 | Y | Y | Y | P | Y | Y | Y | Y | P | P | Y | P | N | B | Y | Y | Y | Y | Y | FUN_004f5110 |
| aa_exe_00935300 | Client_SendRespawnInSector | respawn | 85 | Y | Y | Y | Y | Y | Y | Y | P | Y | Y | Y | Y | N | B | Y | Y | Y | Y | Y | UF-001 |
| aa_exe_0080cc50 | Client_RecvSpecialEvent | respawn | 85 | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | N | B | Y | Y | Y | Y | Y | UF-001 |
| aa_exe_00979730 | ClientSpecialEvent_Respawn_Update | respawn | 85 | Y | Y | P | P | Y | P | Y | P | P | P | Y | N | N | B | N | Y | Y | Y | P | phase timings |
| aa_exe_00979650 | ClientSpecialEvent_Respawn_ctor | respawn | 85 | Y | Y | P | P | Y | Y | Y | Y | P | P | Y | N | N | B | N | Y | Y | Y | P | fastcall map |
| aa_exe_005327c0 | CVOGReaction_GiveMission | missions | 80 | Y | Y | Y | P | Y | Y | Y | P | Y | Y | Y | P | P | B | N | Y | Y | Y | Y | hash internals |
| aa_exe_00533f90 | CVOGReaction_CompleteObjective | missions | 80 | Y | Y | P | P | P | P | Y | P | Y | P | Y | P | P | B | N | Y | Y | Y | P | full rewards |
| aa_exe_00813f40 | Client_RecvInventoryEquip | inventory | 78 | Y | Y | Y | P | Y | P | Y | P | Y | Y | Y | Y | N | B | P | Y | Y | Y | Y | type switch |
| aa_exe_00862c00 | Client_SendInventoryUnequip | inventory | 78 | Y | Y | Y | P | Y | Y | Y | P | P | P | Y | Y | N | B | Y | Y | Y | Y | Y | free space |
| aa_exe_00863430 | Client_SendInventoryDrop_Hardpoint | inventory | 78 | Y | Y | Y | P | Y | Y | Y | P | P | P | Y | Y | N | B | Y | Y | Y | Y | Y | town gate |
| aa_exe_005f7720 | VehicleNet_UnpackGhostVehicle | entity | 75 | Y | Y | Y | P | Y | Y | Y | Y | Y | Y | Y | Y | N | B | Y | Y | Y | Y | Y | non-combat flags residual |
| aa_exe_00809550 | Client_RecvUnlockRegion | world | 65 | Y | Y | Y | P | Y | Y | Y | P | Y | P | Y | Y | N | B | N | Y | Y | Y | Y | — |
| aa_exe_0091ee20 | Client_INC_ContactCountdownTick | respawn | 85 | Y | Y | Y | P | Y | Y | Y | Y | Y | P | Y | Y | N | B | Y | Y | Y | Y | Y | corpse?UI |
| aa_exe_00815710 | Client_PacketDispatch | comms | 60 | Y | Y | Y | P | Y | Y | Y | Y | Y | N | N | Y | N | B | Y | Y | Y | Y | Y | FUN_* leaves |
| aa_exe_0080ae70 | Client_AwardKillExperience | xp | 55 | Y | Y | Y | P | Y | Y | Y | Y | Y | P | Y | Y | N | B | Y | Y | Y | Y | Y | — |
| aa_exe_00815070 | Client_RecvNpcMissionDialog | dialog | 70 | Y | Y | P | P | Y | P | Y | P | Y | P | Y | Y | N | B | N | Y | Y | Y | Y | none critical (WQ-020 complete; UF-003 closed) |
| aa_exe_0088e180 | Client_SendStoreTransactionBuy | dialog | 70 | Y | Y | Y | P | Y | Y | Y | P | Y | Y | Y | Y | N | B | Y | Y | Y | Y | Y | none critical (WQ-020 / UF-003) |
| aa_exe_00810670 | Client_RecvStoreTransactionResponse | dialog | 70 | Y | Y | Y | P | Y | Y | Y | P | Y | Y | Y | Y | N | B | Y | Y | Y | Y | Y | buy sub-branch labels probable; live UF-002 |
| aa_exe_00860a50 | Client_UI_InventoryDropToGrid_store | dialog | 70 | Y | Y | Y | P | Y | Y | Y | P | Y | P | Y | Y | N | B | Y | Y | Y | Y | Y | none critical (sell 0x2027; WQ-020) |
