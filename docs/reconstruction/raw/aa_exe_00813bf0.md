# Raw decompiler capture: Client_RecvInventoryUnequipNotify

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_00813bf0` |
| Binary | autoassault.exe |
| Address | `0x00813bf0` |
| Original symbol | `Client_RecvInventoryUnequipNotify` |
| System | inventory |
| Decompiler | Ghidra MCP batch_decompile |
| Timestamp | 2026-07-15T14:00:00Z |
| Capture version | v1 |

## Raw pseudocode

```c

/* S2C InventoryUnequip notify 0x203E size 0x30. item@+8 vehicle@+0x18 destX@+0x28 destY@+0x29
   invType@+0x2A. Distinct from C2S Client_SendInventoryUnequip which also uses 0x203E
   (bidirectional opcode). */

void __fastcall Client_RecvInventoryUnequipNotify(int param_1)

{
  TFID_16 *pTfid;
  char cVar1;
  int in_EAX;
  void *pvVar2;
  uint uVar3;
  undefined4 uVar4;
  int *piVar5;
  void *pvVar6;
  int iVar7;
  int iVar8;
  undefined4 extraout_EDX;
  undefined8 uVar9;
  undefined4 uVar10;
  undefined4 uVar11;
  undefined1 *puVar12;
  undefined4 uVar13;
  
  pTfid = (TFID_16 *)(in_EAX + 8);
  FUN_007a4480(0xffffffff,"Requesting InventoryUnequip: char:%I64d Old:%I64d\n",
               *(undefined4 *)(in_EAX + 0x18),*(undefined4 *)(in_EAX + 0x1c),pTfid->dwCoidLo,
               *(undefined4 *)(in_EAX + 0xc));
  pvVar2 = (void *)FUN_004bafe0(*(undefined1 *)(in_EAX + 0x20),*(undefined4 *)(in_EAX + 0x18),
                                *(undefined4 *)(in_EAX + 0x1c));
  if (pvVar2 == (void *)0x0) {
    pvVar2 = Object_ResolveFromTFID(pTfid);
    if (pvVar2 == (void *)0x0) {
      return;
    }
    FUN_009440e0(pvVar2,1,0,0xffffffff,0xffffffff);
    return;
  }
  iVar7 = *(int *)((int)pvVar2 + 4);
  piVar5 = *(int **)(*(int *)(iVar7 + 4) + 0xb0 + (int)pvVar2);
  if (piVar5 != (int *)0x0) {
    uVar9 = (**(code **)(*piVar5 + 0x1dc))();
    iVar7 = (int)((ulonglong)uVar9 >> 0x20);
    if (((int)uVar9 == 0) || ((int)uVar9 != *(int *)(param_1 + 0xe98))) goto LAB_00813d95;
    pvVar2 = Object_ResolveFromTFID(pTfid);
    uVar9 = FUN_00504f60(pvVar2);
    uVar4 = (undefined4)((ulonglong)uVar9 >> 0x20);
    piVar5 = (int *)uVar9;
    if (piVar5 != (int *)0x0) {
      (**(code **)(*piVar5 + 0x2ac))(*(undefined4 *)(param_1 + 0xd34));
      uVar4 = extraout_EDX;
    }
    iVar7 = CONCAT31((int3)((uint)uVar4 >> 8),*(byte *)(in_EAX + 0x2a));
    uVar3 = (uint)*(byte *)(in_EAX + 0x2a);
    switch(uVar3) {
    case 0:
      break;
    case 1:
      iVar7 = *(int *)(*(int *)(param_1 + 0xe98) + 0x250);
      iVar8 = *(int *)(iVar7 + 0x2b0);
      goto LAB_00813cff;
    case 2:
      FUN_0093d6e0(param_1,1);
      break;
    case 3:
      uVar3 = *(uint *)(param_1 + 0xe98);
      iVar8 = *(int *)(uVar3 + 0xcbc);
LAB_00813cff:
      if ((iVar8 != 0) &&
         (cVar1 = FUN_00571620(piVar5,CONCAT31((int3)(uVar3 >> 8),*(undefined1 *)(in_EAX + 0x28)),
                               CONCAT31((int3)((uint)iVar7 >> 8),*(undefined1 *)(in_EAX + 0x29)),1),
         cVar1 == '\0')) {
        FUN_007a69d0();
        uVar13 = 0;
        uVar11 = 1;
        uVar10 = 0xffffffff;
        uVar4 = FUN_007a6de0("This equipment cannot be changed at this time.",0xffffffff);
        FUN_007fdfb0(param_1,uVar4,uVar10,uVar11,uVar13);
        return;
      }
      break;
    default:
      FUN_007a4480(0,"VOG_DEBUG_STOP");
    }
    Client_RefreshOpenMissionUiWindows(param_1);
    piVar5 = *(int **)(param_1 + 0x1078);
    if (piVar5 == (int *)0x0) {
      return;
    }
    FUN_008801b0(piVar5);
    (**(code **)(*piVar5 + 0x34c))();
    return;
  }
LAB_00813d95:
  piVar5 = (int *)CVOGReaction_ResolveObjectTarget
                            (CONCAT31((int3)((uint)iVar7 >> 8),*(undefined1 *)(in_EAX + 0x10)),
                             pTfid->dwCoidLo,*(undefined4 *)(in_EAX + 0xc));
  if (piVar5 == (int *)0x0) {
    return;
  }
  (**(code **)(*piVar5 + 0x104))(0);
  switch(*(undefined4 *)(piVar5[0x2a] + 0x38)) {
  case 6:
    if (*(short *)(*(int *)(piVar5[0x2a] + 0x3c) + 0x3f4) != 10) {
      return;
    }
    piVar5 = (int *)0x0;
    FUN_004fe620(0,&stack0xfffffff8,0);
    break;
  default:
    return;
  case 10:
    pvVar6 = (void *)(**(code **)(*piVar5 + 500))();
    Vehicle_EquipPowerPlant(pvVar2,(void *)0x0,(void **)&stack0xfffffff8,false);
    goto LAB_00813e97;
  case 0xc:
    piVar5 = (int *)(**(code **)(*piVar5 + 0x1e0))();
    if (*(short *)(*(int *)(*(int *)(*(int *)(piVar5[1] + 4) + 0xac + (int)piVar5) + 0x3c) + 0x3f4)
        == 9) {
      FUN_004fe800(0,0,0);
    }
    else {
      puVar12 = &stack0xfffffff8;
      uVar4 = (**(code **)(*piVar5 + 0x60))(puVar12);
      FUN_004fe110(0,uVar4,puVar12);
    }
    break;
  case 0x10:
    piVar5 = (int *)(**(code **)(*piVar5 + 0x1f0))();
    FUN_004ff510(0,&stack0xfffffff8,0);
    break;
  case 0x1c:
    pvVar6 = (void *)(**(code **)(*piVar5 + 0x1f8))();
    FUN_00502180(0,&stack0xfffffff8,0);
LAB_00813e97:
    if (pvVar6 == (void *)0x0) goto LAB_00813ef0;
    iVar7 = *(int *)(*(int *)((int)pvVar6 + 4) + 4) + 4 + (int)pvVar6;
    goto LAB_00813ee2;
  }
  if (piVar5 != (int *)0x0) {
    iVar7 = *(int *)(piVar5[1] + 4) + 4 + (int)piVar5;
LAB_00813ee2:
    FUN_009440e0(iVar7,1,0,0xffffffff,0xffffffff);
  }
LAB_00813ef0:
  FUN_0092f120();
  return;
}
```
