# Raw decompiler capture: Client_NpcDialog_PrepareResponseOpcode

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_008abd70` |
| Binary | autoassault.exe |
| Address | `0x008abd70` |
| Original symbol | `Client_NpcDialog_PrepareResponseOpcode` |
| System | dialog-vendors |
| Decompiler | Ghidra MCP decompile_function |
| Timestamp | 2026-07-15T20:00:00Z |
| Capture version | v1 (original; do not overwrite) |

## Raw signature / notes

- Sets `*(dialog+0x650) = 0x206E` so `FUN_008ab8f0` will transmit MissionDialogResponse.
- Also binds selected mission pointer at `dialog+0x670` and refreshes dialog chrome widgets.
- Called from `Client_ShowNpcMissionDialogUI` (xref 0x00943a60).

## Raw pseudocode

```c

/* Client_NpcDialog_PrepareResponseOpcode: sets dialog+0x650 = 0x206E for C2S MissionDialogResponse.
   Payload body: missionId i32 + accepted bool + pad7 + npc TFID16. */

void __fastcall Client_NpcDialog_PrepareResponseOpcode(int param_1)

{
  char cVar1;
  undefined4 uVar2;
  int *piVar3;
  int *piVar4;
  int *piVar5;
  int iVar6;
  int unaff_ESI;
  undefined4 *puVar7;
  float fVar8;
  char local_208 [2];
  undefined4 local_206 [128];
  
  *(int *)(unaff_ESI + 0x670) = param_1;
  *(undefined4 *)(unaff_ESI + 0x650) = 0x206e;
  if (param_1 == 0) {
    if (*(int **)(unaff_ESI + 0x6dc) != (int *)0x0) {
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x1b0))();
      iVar6 = *(int *)(unaff_ESI + 0x6dc);
      fVar8 = (float)*(int *)(iVar6 + 0x1bc) * (float)DAT_00d1e81c * DAT_00aaa678;
      *(int *)(iVar6 + 0x170) =
           (int)((float)*(int *)(iVar6 + 0x1b8) * (float)DAT_00d1e818 * DAT_00aaa67c);
      *(int *)(iVar6 + 0x174) = (int)fVar8;
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x15c))();
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x1d8))(&DAT_00a1419b,1);
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x34c))();
    }
  }
  else {
    if (*(char *)(param_1 + 0x168) == '\0') {
      FUN_00547920();
    }
    if (*(int *)(unaff_ESI + 0x6dc) != 0) {
      local_208[0] = '\0';
      local_208[1] = '\0';
      puVar7 = local_206;
      for (iVar6 = 0x7f; iVar6 != 0; iVar6 = iVar6 + -1) {
        *puVar7 = 0;
        puVar7 = puVar7 + 1;
      }
      *(undefined2 *)puVar7 = 0;
      FUN_007a69d0();
      FUN_007a6de0();
      sprintf(local_208,"[%d] %s");
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x1b0))();
      iVar6 = *(int *)(unaff_ESI + 0x6dc);
      fVar8 = (float)*(int *)(iVar6 + 0x1bc) * (float)DAT_00d1e81c * DAT_00aaa678;
      *(int *)(iVar6 + 0x170) =
           (int)((float)*(int *)(iVar6 + 0x1b8) * (float)DAT_00d1e818 * DAT_00aaa67c);
      *(int *)(iVar6 + 0x174) = (int)fVar8;
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x1d8))();
      if (DAT_00d1b6d8 != 0) {
        (**(code **)(*(int *)(*(int *)(*(int *)(DAT_00d1b6d8 + 4) + 4) + 4 + DAT_00d1b6d8) + 0x27c))
                  ();
      }
      iVar6 = **(int **)(unaff_ESI + 0x6dc);
      uVar2 = FUN_0092d580();
      (**(code **)(iVar6 + 0x158))(1,uVar2);
      (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x34c))();
      piVar3 = (int *)(**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x140))(&stack0xfffffdc8,1);
      piVar5 = *(int **)(unaff_ESI + 0x6dc);
      piVar4 = (int *)(**(code **)(*piVar5 + 0x204))(&stack0xfffffdc8);
      if (*piVar3 - piVar5[0x5c] < *piVar4) {
        (**(code **)(**(int **)(unaff_ESI + 0x6dc) + 0x1b0))();
        piVar5 = (int *)(*(int *)(unaff_ESI + 0x6dc) + 0x174);
        *piVar5 = *piVar5 + (*(int *)(DAT_00d1e808 + 0x7c) - *(int *)(DAT_00d1e7e8 + 0x7c));
      }
    }
    if (*(int **)(unaff_ESI + 0x6e4) != (int *)0x0) {
      (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x268))();
      (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x1b0))();
      cVar1 = FUN_008ab9b0();
      if (cVar1 == '\0') {
        (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 4))();
        if (*(int **)(unaff_ESI + 0x68c) != (int *)0x0) {
          (**(code **)(**(int **)(unaff_ESI + 0x68c) + 4))();
          (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x34c))();
          return;
        }
      }
      else {
        (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x204))();
        piVar5 = (int *)(**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x140))();
        if ((*piVar5 < (int)&stack0xfffffddc) ||
           (iVar6 = (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x140))(&stack0xfffffdd8,1),
           *(int *)(iVar6 + 4) < 2)) {
          (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x268))();
          (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x1b0))(8);
          FUN_008ab9b0(*(undefined4 *)(unaff_ESI + 0x670));
        }
        (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 4))(1);
        if (*(int **)(unaff_ESI + 0x68c) != (int *)0x0) {
          (**(code **)(**(int **)(unaff_ESI + 0x68c) + 4))();
        }
      }
      (**(code **)(**(int **)(unaff_ESI + 0x6e4) + 0x34c))();
      return;
    }
  }
  return;
}
```

## Tool warnings

- Heavy UI layout code is not protocol-critical; the durable fact is `dialog+0x650 = 0x206E`.
