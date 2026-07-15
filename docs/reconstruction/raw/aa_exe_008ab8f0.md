# Raw decompiler capture: Client_SendMissionDialogResponse

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_008ab8f0` |
| Binary | autoassault.exe |
| Address | `0x008ab8f0` |
| Original symbol | `FUN_008ab8f0` |
| Canonical name | `Client_SendMissionDialogResponse` |
| System | dialog-vendors |
| Decompiler | Ghidra MCP decompile_function |
| Timestamp | 2026-07-15T20:00:00Z |
| Capture version | v1 (original; do not overwrite) |

## Raw signature / notes

- Sends C2S **0x206E** MissionDialogResponse.
- Buffer is **dialog object + 0x650** (`param_1 + 0x194` dword index × 4).
- Size **0x20** including opcode.
- Opcode written earlier by `Client_NpcDialog_PrepareResponseOpcode` (0x008abd70) → `*(dialog+0x650)=0x206E`.
- Payload filled by `Client_MissionDialogHandleButton` (+0x654 missionId, +0x658 accepted/button, +0x660 NPC TFID16).
- Live captures: retail often wires `Accepted=false` on OK (server grant still attempts).

## Raw pseudocode

```c

void __fastcall FUN_008ab8f0(int *param_1)

{
  char cVar1;
  int *piVar2;
  int iVar3;
  
  if ((param_1[0x194] != 0) && (g_pSectorNetConnection_INFERRED != (void *)0x0)) {
    (**(code **)(*(int *)g_pSectorNetConnection_INFERRED + 0x18))(0xffffffff,param_1 + 0x194,0x20,0)
    ;
  }
  if ((((DAT_00d1d8dc != (int *)0x0) &&
       (cVar1 = (**(code **)(*DAT_00d1d8dc + 0x3d8))(), cVar1 != '\0')) &&
      (cVar1 = (**(code **)(*DAT_00d1d8dc + 0xd0))(), cVar1 != '\0')) && (DAT_00d1d8dc[0x146] != 0))
  {
    iVar3 = 0;
    piVar2 = param_1 + 0x156;
    do {
      if ((*piVar2 == DAT_00d1d8dc[0x148]) && (piVar2[1] == DAT_00d1d8dc[0x149])) {
        DAT_00d1d8f4 = 1;
        DAT_00d1d8f5 = 0;
        (**(code **)(*DAT_00d1d8dc + 4))(0);
        break;
      }
      iVar3 = iVar3 + 1;
      piVar2 = piVar2 + 2;
    } while (iVar3 < 4);
  }
  FUN_008aa320();
  (**(code **)(*param_1 + 0x3ac))();
  FUN_00792490();
  return;
}
```

## Tool warnings

- Ghidra shows no static callers (likely vtable/indirect). Confirm live with break on 0x008ab8f0.
- Send only if `param_1[0x194] != 0` (opcode non-zero); PrepareResponse must have run.
