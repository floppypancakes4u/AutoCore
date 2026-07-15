# Raw decompiler capture: Client_MissionDialogHandleButton

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_008ae7c0` |
| Binary | autoassault.exe |
| Address | `0x008ae7c0` |
| Original symbol | `Client_MissionDialogHandleButton` |
| System | dialog-vendors |
| Decompiler | Ghidra MCP decompile_function |
| Timestamp | 2026-07-15T20:00:00Z |
| Capture version | v1 (original; do not overwrite) |

## Raw signature / notes

- UI button router for mission dialog; dialog state at `dialog+0x648`.
- Does **not** itself call the 0x206E send; fills `dialog+0x650..` fields used by `FUN_008ab8f0`.
- State 0 sends **0x206F** (size 0x18) via `Client_SendSectorPacket` — separate opcode from 0x206E.
- State 1 turn-in (`dialog+0x64c != 0`) runs **local** `CVOGReaction_CompleteObjective` — do not also S2C 0x2070 on that path.

## Raw pseudocode

```c

/* Client_MissionDialogHandleButton
   
   Dialog button router; state at dialogCtx+0x648:
     0 = send sector 0x206F accept request
     1 = accept offer OR claim/complete deliver (CompleteObjective path)
     2 = abandon confirmation
     3 = re-show NPC mission dialog UI
   
   State 1 complete path (char+0x64c turn-in mode):
     reward selection checks → Finished Mission toast →
     Client_ShowMissionRewardChatToast → CVOGReaction_CompleteObjective →
     Client_RefreshMissionDialogChrome / Hide / RefreshOpenMissionUiWindows
   
   Sends C2S MissionDialogResponse (0x206E) via prepared dialog+0x650. */

char __cdecl Client_MissionDialogHandleButton(int *pDialogContext,int iButtonIndex)

{
  undefined4 *puVar1;
  char cVar2;
  int in_EAX;
  char *pcVar3;
  undefined4 uVar4;
  int iVar5;
  undefined4 uVar6;
  undefined4 uVar7;
  undefined4 uVar8;
  undefined1 uStack_21a;
  undefined1 uStack_219;
  undefined4 auStack_218 [2];
  undefined4 uStack_210;
  undefined4 uStack_20c;
  undefined1 uStack_208;
  char acStack_200 [512];
  
  if (DAT_00d1b6d8 == 0) {
    return '\0';
  }
  FUN_007a69d0();
  if (*(int *)(in_EAX + 0x708 + (int)pDialogContext * 4) != 0) {
    if ((*(int **)(in_EAX + 0x6e0) != (int *)0x0) &&
       (cVar2 = (**(code **)(**(int **)(in_EAX + 0x6e0) + 0x1f8))(), cVar2 != '\0')) {
      (**(code **)(**(int **)(in_EAX + 0x6e0) + 0x1fc))();
      return '\0';
    }
    iVar5 = *(int *)(in_EAX + 0x648);
    if (iVar5 == 2) {
      if (pDialogContext == (int *)0x1) {
        if (*(undefined4 **)(in_EAX + 0x670) == (undefined4 *)0x0) {
          DAT_00d1b4b4 = 0xffffffff;
        }
        else {
          DAT_00d1b4b4 = **(undefined4 **)(in_EAX + 0x670);
        }
        uVar7 = 0xffffffff;
        if (*(int **)(in_EAX + 0x6dc) == (int *)0x0) {
          pcVar3 = "this mission";
        }
        else {
          pcVar3 = (char *)(**(code **)(**(int **)(in_EAX + 0x6dc) + 0x1dc))(0xffffffff);
        }
        uVar7 = FUN_007a6de0(pcVar3,uVar7);
        uVar4 = FUN_007a6de0("Are you sure you wish to abandon",0xffffffff);
        sprintf(acStack_200,"%s \"%s\"?",uVar4,uVar7);
        FUN_007fdfb0(&DAT_00d1a840,acStack_200,0x4e47,1,0);
        return '\0';
      }
    }
    else {
      if (iVar5 == 0) {
        uStack_210 = *(undefined4 *)(in_EAX + 0x678);
        uStack_20c = *(undefined4 *)(in_EAX + 0x67c);
        uStack_208 = SUB41(pDialogContext,0);
        auStack_218[0] = 0x206f;
        Client_SendSectorPacket(&DAT_00d1a840,0x18,auStack_218);
        return '\x01';
      }
      if (iVar5 == 3) {
        Client_ShowNpcMissionDialogUI(&DAT_00d1a840,*(undefined4 *)(in_EAX + 0x644),0);
        return '\0';
      }
      if ((iVar5 == 1) && (*(int *)(in_EAX + 0x670) != 0)) {
        if ((*(char *)(in_EAX + 0x64c) != '\0') &&
           (((*(uint *)(in_EAX + 0x558) & *(uint *)(in_EAX + 0x55c)) != 0xffffffff &&
            ((*(uint *)(in_EAX + 0x578) & *(uint *)(in_EAX + 0x57c)) == 0xffffffff)))) {
          pcVar3 = "You need to select a reward first!";
LAB_008ae999:
          uVar6 = 1;
          uVar4 = 0xffffffff;
          uVar8 = 0;
          uVar7 = FUN_007a6de0(pcVar3,0xffffffff);
          FUN_007fdfb0(&DAT_00d1a840,uVar7,uVar4,uVar6,uVar8);
          return '\0';
        }
        if ((*(uint *)(in_EAX + 0x578) & *(uint *)(in_EAX + 0x57c)) != 0xffffffff) {
          if (DAT_00d1b6d8 == 0) {
            return '\0';
          }
          if (*(int *)(DAT_00d1b6d8 + 0x250) == 0) {
            return '\0';
          }
          if (*(int *)(*(int *)(DAT_00d1b6d8 + 0x250) + 0x2b0) == 0) {
            return '\0';
          }
          iVar5 = CVOGReaction_ResolveObjectTarget
                            (1,*(uint *)(in_EAX + 0x578),*(uint *)(in_EAX + 0x57c));
          if (iVar5 != 0) {
            uStack_21a = 0;
            uStack_219 = 0;
            cVar2 = FUN_005714e0(iVar5,&uStack_21a,&uStack_219,1,0xffffffff);
            if (cVar2 == '\0') {
              pcVar3 = 
              "Your inventory is full. You must have space in your inventory to receive the mission reward."
              ;
              goto LAB_008ae999;
            }
          }
        }
        iVar5 = *(int *)(in_EAX + 0x644);
        if (iVar5 == 0) {
          *(undefined1 *)(in_EAX + 0x668) = 0;
          *(undefined4 *)(in_EAX + 0x660) = 0xffffffff;
          *(undefined4 *)(in_EAX + 0x664) = 0xffffffff;
        }
        else {
          puVar1 = (undefined4 *)(*(int *)(*(int *)(iVar5 + 4) + 4) + 0x164 + iVar5);
          *(undefined4 *)(in_EAX + 0x660) = *puVar1;
          *(undefined4 *)(in_EAX + 0x664) = puVar1[1];
          *(undefined4 *)(in_EAX + 0x668) = puVar1[2];
          *(undefined4 *)(in_EAX + 0x66c) = puVar1[3];
        }
        puVar1 = *(undefined4 **)(in_EAX + 0x670);
        *(undefined4 *)(in_EAX + 0x654) = *puVar1;
        if (*(char *)(in_EAX + 0x64c) == '\0') {
          *(int **)(in_EAX + 0x658) = pDialogContext;
          *(int *)(in_EAX + 0x65c) = (int)pDialogContext >> 0x1f;
          if ((pDialogContext == (int *)0x0) && (DAT_00d1b6d8 != 0)) {
            CVOGReaction_GiveMission(*puVar1);
            if (((DAT_00d1b216 != '\0') ||
                ((*(short *)(*(int *)(in_EAX + 0x670) + 0xfa) != 0 ||
                 (*(int *)(DAT_00d1ad10 + 0x10) < 1)))) &&
               (*(char *)(*(int *)(in_EAX + 0x670) + 0x130) != '\0')) {
              FUN_0092fd00();
            }
            Client_HideMissionDialogIfOpen();
            Client_MaybeShowFirstTimeTip(2);
            FUN_008ac7a0();
          }
        }
        else {
          *(undefined4 *)(in_EAX + 0x65c) = *(undefined4 *)(in_EAX + 0x57c);
          *(undefined4 *)(in_EAX + 0x658) = *(undefined4 *)(in_EAX + 0x578);
          iVar5 = *(int *)(puVar1[0x4f] + -4 + (uint)*(byte *)(puVar1 + 0x4c) * 4);
          if (iVar5 != 0) {
            uVar7 = FUN_007a6de0(puVar1[0x53],0xffffffff);
            uVar4 = FUN_007a6de0("Finished Mission",0xffffffff);
            sprintf(acStack_200,"%s \"%s\"",uVar4,uVar7);
            if (DAT_00d1b8dc != 0) {
              FUN_008f8200(DAT_00d1b8dc,6,&DAT_00a156cc,acStack_200,0);
            }
            Client_ShowMissionRewardChatToast(iVar5);
            cVar2 = CVOGReaction_CompleteObjective
                              (*(undefined4 *)(iVar5 + 0x10),*(undefined4 *)(in_EAX + 0x578),
                               *(undefined4 *)(in_EAX + 0x57c),0);
            if (cVar2 == '\0') {
              return '\0';
            }
            Client_RefreshMissionDialogChrome();
            Client_HideMissionDialogIfOpen();
            Client_RefreshOpenMissionUiWindows(&DAT_00d1a840);
            return '\x01';
          }
        }
      }
    }
  }
  return '\x01';
}
```

## Tool warnings

- `in_EAX` is the dialog object; first param is button/choice index (not a dialog pointer).
- Plate comment "Sends C2S 0x206E" is partially true: this function **prepares** the buffer; `FUN_008ab8f0` performs the send on close/teardown.
