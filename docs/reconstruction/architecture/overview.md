# Architecture overview (client reconstruction)

```
WM/DIK key → Client_Input_OnKeyDown_MatchAction → action table held/edge
                ↓
        Client_Input_PollBoundActions → QuickBar / UseObject / UI
                ↓
        Client_Input_DriveControlTick → entity axes → PushDriveAxes → VehicleAction
                ↓
        Network pose → FUN_0053eec0 soft/hard → render/physics

C2S helpers → Client_SendSectorPacket → g_pSectorNetConnection
S2C → Client_PacketDispatch → Recv* handlers (SpecialEvent, Inventory*, UnlockRegion, …)

Ghost stream → VehicleNet_UnpackGhostVehicle → vehicle combat pools + pose
Death/INC → Client_SendRespawnInSector → (server) → Client_RecvSpecialEvent → Respawn_Update SM
Missions → GiveMission / CompleteObjective (+ S2C 0x2070)
```

Updated: 2026-07-15
