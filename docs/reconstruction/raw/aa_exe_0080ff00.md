# Raw decompiler capture: Client_RecvCompleteDynamicObjective

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_0080ff00` |
| Binary | autoassault.exe |
| Address | `0x0080ff00` |
| Original symbol | `Client_RecvCompleteDynamicObjective` |
| System | missions |
| Decompiler | Ghidra MCP batch_decompile |
| Timestamp | 2026-07-15T12:30:00Z |
| Capture version | v1 |

## Raw pseudocode

```c

/* Client_RecvCompleteDynamicObjective  (opcode 0x2070)
   
   WAS misnamed Client_RecvObjectiveState. Dispatch in Client_PacketDispatch case 0x2070.
   
   Algorithm:
     1. Clear some mission UI state (FUN_0052d8b0)
     2. Lookup objective id at packet+0x10 in active-objectives hash
     3. ALWAYS CVOGReaction_CompleteObjective(id, -1, -1, force=1)
     4. Hide/refresh mission dialog chrome
     5. Optional Client_SendUseObject if world target matches
     6. Client_RefreshOpenMissionUiWindows
   
   AutoCore: do NOT send 0x2070 on dialog deliver turn-in (client already completed locally). */

void Client_RecvCompleteDynamicObjective(int param_1)

{
  int *piVar1;
  char cVar2;
  void *pvVar3;
  int iVar4;
  int iVar5;
  uint uVar6;
  int unaff_EDI;
  int local_4;
  
  FUN_0052d8b0(0,0xffffffff);
  pvVar3 = CNDHash_LookupByKey(*(void **)(*(int *)(unaff_EDI + 0xe98) + 0x548),
                               *(uint *)(param_1 + 0x10));
  local_4 = -1;
  if (pvVar3 != (void *)0x0) {
    uVar6 = 0;
    while( true ) {
      iVar4 = *(int *)((int)pvVar3 + 0x158);
      if ((iVar4 == 0) || ((uint)(*(int *)((int)pvVar3 + 0x15c) - iVar4 >> 2) <= uVar6))
      goto LAB_0080ff80;
      piVar1 = *(int **)(iVar4 + uVar6 * 4);
      iVar4 = (**(code **)(*piVar1 + 0x50))();
      if (iVar4 == 3) break;
      uVar6 = uVar6 + 1;
    }
    local_4 = piVar1[6];
  }
LAB_0080ff80:
  iVar4 = local_4;
  CVOGReaction_CompleteObjective(*(undefined4 *)(param_1 + 0x10),0xffffffff,0xffffffff,1);
  if ((*(int *)(unaff_EDI + 0x107c) != 0) &&
     (cVar2 = (**(code **)(**(int **)(unaff_EDI + 0x107c) + 0x3d8))(), cVar2 != '\0')) {
    (**(code **)(**(int **)(unaff_EDI + 0x107c) + 0x448))();
    (**(code **)(**(int **)(unaff_EDI + 0x107c) + 0x34c))();
  }
  if (((*(int *)(unaff_EDI + 0x10b0) != 0) &&
      (cVar2 = (**(code **)(**(int **)(unaff_EDI + 0x10b0) + 0x3d8))(), cVar2 != '\0')) &&
     (iVar5 = *(int *)(unaff_EDI + 0x10b0), *(int *)(iVar5 + 0x684) != 0)) {
    FUN_008af180(0);
    FUN_008a0370();
    if (*(int *)(iVar5 + 0x664) != 0) {
      (**(code **)(**(int **)(iVar5 + 0x664) + 0x480))();
    }
  }
  if (iVar4 != -1) {
    param_1 = FUN_009197a0(0x41700000);
    if ((param_1 == 0) || (*(int *)(*(int *)(param_1 + 0xa8) + 0x34) != iVar4)) {
      local_4 = 0;
      FUN_004294f0();
      iVar5 = FUN_004022a0(&local_4,&param_1);
      while (iVar5 != 0) {
        if ((param_1 != 0) && (*(int *)(*(int *)(param_1 + 0xa8) + 0x34) == iVar4)) {
          Client_SendUseObject();
          break;
        }
        iVar5 = FUN_004022a0(&local_4,&param_1);
      }
      iVar4 = *(int *)(*(int *)(*(int *)(*(int *)(*(int *)(*(int *)(unaff_EDI + 0xe98) + 4) + 4) +
                                         0xa8 + *(int *)(unaff_EDI + 0xe98)) + 0xe4e8) + 0x1c);
      if (*(char *)(iVar4 + 0x28) != '\0') {
        *(undefined1 *)(iVar4 + 0x28) = 0;
        LeaveCriticalSection((LPCRITICAL_SECTION)(iVar4 + 4));
      }
    }
    else {
      Client_SendUseObject();
    }
  }
  Client_RefreshOpenMissionUiWindows(unaff_EDI);
  if ((*(int *)(unaff_EDI + 0x1034) != 0) &&
     (cVar2 = (**(code **)(**(int **)(unaff_EDI + 0x1034) + 0x3d8))(), cVar2 != '\0')) {
    FUN_0090cbc0();
  }
  return;
}
```

## Tool warnings

- Ghidra may mis-type stack locals and calling conventions.
- Trust machine code over decompiler for signedness/widths when they disagree.
