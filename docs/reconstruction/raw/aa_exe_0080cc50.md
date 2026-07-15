# Raw decompiler capture: Client_RecvSpecialEvent

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_0080cc50` |
| Binary | autoassault.exe |
| Address | `0x0080cc50` |
| Original symbol | `Client_RecvSpecialEvent` |
| System | death-respawn |
| Decompiler | Ghidra MCP decompile_function / batch_decompile |
| Timestamp | 2026-07-15T00:00:00Z |
| Capture version | v1 (original; do not overwrite) |

## Raw signature / notes

See pseudocode header comments (may include prior RE plate comments from Ghidra DB).

## Raw pseudocode

```c

/* Client_RecvSpecialEvent — S2C presentation event (opcode 0x20A9).
   
   Dispatched from Client_PacketDispatch case 0x20a9 (this = packet buffer with opcode at +0).
   
   Layout (full packet):
     +0x00 uint32  opcode 0x20A9
     +0x04 byte    event type + 3 pad
                   0 = Respawn (ClientSpecialEvent_Respawn)
                   1 = TeleportOut
                   2 = TeleportIn
     +0x08 float3  destination position
     +0x14 float4  destination quaternion
     +0x24 pad 4
     +0x28 int64   target TFID coid (compared to local 0xe98 entity TFID)
     +0x30 ...     rest of TFID / pad
     +0x40 int     flag (non-zero required for full Respawn ctor path)
   
   IMPORTANT: Fast path requires packet TFID == local entity at game+0xe98.
   Live capture: 0xe98 is the CHARACTER coid. Sending vehicle coid silent-returns (no anim).
   Resolve fallback requires clone type 0x14 (Vehicle) and entity+0x250 non-null.
   
   AutoCore: SpecialEventPacket type Respawn after RespawnInSector */

void __thiscall Client_RecvSpecialEvent(int param_1,int param_2)

{
  undefined4 *puVar1;
  char cVar2;
  int iVar3;
  int *piVar4;
  int iVar5;
  void *pvVar6;
  undefined4 local_50;
  undefined4 local_4c;
  undefined4 local_48;
  undefined4 local_44;
  undefined4 local_40;
  undefined4 local_3c;
  undefined4 local_38;
  undefined4 local_34;
  int local_30;
  void *local_1c;
  undefined1 *puStack_18;
  undefined4 local_14;
  
  local_14 = 0xffffffff;
  puStack_18 = &LAB_009b32e0;
  local_1c = ExceptionList;
  local_44 = *(undefined4 *)(param_1 + 0x20);
  local_4c = *(undefined4 *)(param_1 + 0x18);
  local_50 = *(undefined4 *)(param_1 + 0x14);
  local_48 = *(undefined4 *)(param_1 + 0x1c);
  local_38 = *(undefined4 *)(param_1 + 0x10);
  local_40 = *(undefined4 *)(param_1 + 8);
  local_3c = *(undefined4 *)(param_1 + 0xc);
  local_34 = 0;
  if (*(int *)(param_2 + 0xe98) == 0) {
LAB_0080cd01:
    ExceptionList = &local_1c;
    piVar4 = (int *)CVOGReaction_ResolveObjectTarget
                              (1,*(undefined4 *)(param_1 + 0x28),*(undefined4 *)(param_1 + 0x2c));
    if (piVar4 == (int *)0x0) {
      ExceptionList = local_1c;
      return;
    }
    if (*(int *)(piVar4[0x2a] + 0x38) != 0x14) {
      ExceptionList = local_1c;
      return;
    }
    iVar5 = (**(code **)(*piVar4 + 0x1dc))();
  }
  else {
    iVar5 = *(int *)(param_2 + 0xe98);
    iVar3 = *(int *)(*(int *)(iVar5 + 4) + 4);
                    /* Compare packet TFID (+0x28/+0x2c) to local 0xe98 entity TFID (0x164/0x168)
                       — must match for fast path; live is CHARACTER coid */
    if ((*(int *)(iVar3 + 0x164 + iVar5) != *(int *)(param_1 + 0x28)) ||
       (*(int *)(iVar3 + 0x168 + iVar5) != *(int *)(param_1 + 0x2c))) goto LAB_0080cd01;
    iVar5 = *(int *)(param_2 + 0xe98);
    ExceptionList = &local_1c;
  }
  if (*(int *)(iVar5 + 0x250) == 0) {
    ExceptionList = local_1c;
    return;
  }
  if (iVar5 == 0) {
    ExceptionList = local_1c;
    return;
  }
                    /* event type byte at packet+0x04: 0=Respawn 1=TeleportOut 2=TeleportIn */
  cVar2 = *(char *)(param_1 + 4);
  if (cVar2 == '\0') {
    pvVar6 = operator_new(0x70);
    local_14 = 0;
    if (pvVar6 != (void *)0x0) {
                    /* packet+0x40 flag must be non-zero for Respawn ctor */
      local_30 = ClientSpecialEvent_Respawn_ctor(pvVar6,*(int *)(param_1 + 0x40) != 0,&local_50);
      goto LAB_0080cdeb;
    }
  }
  else if (cVar2 == '\x01') {
    pvVar6 = operator_new(0x34);
    local_14 = 1;
    if (pvVar6 != (void *)0x0) {
      local_30 = ClientSpecialEvent_TeleportOut_ctor(pvVar6,iVar5);
      goto LAB_0080cdeb;
    }
  }
  else {
    if (cVar2 != '\x02') {
      ExceptionList = local_1c;
      return;
    }
    pvVar6 = operator_new(0x50);
    local_14 = 2;
    if (pvVar6 != (void *)0x0) {
      local_30 = ClientSpecialEvent_TeleportIn_ctor(pvVar6);
      goto LAB_0080cdeb;
    }
  }
  local_30 = 0;
LAB_0080cdeb:
  local_14 = 0xffffffff;
  if (local_30 != 0) {
    puVar1 = (undefined4 *)(*(int *)(*(int *)(iVar5 + 4) + 4) + 0x164 + iVar5);
    local_40 = *puVar1;
    local_3c = puVar1[1];
    local_38 = puVar1[2];
    local_34 = puVar1[3];
    FUN_00403150(&local_40);
  }
  ExceptionList = local_1c;
  return;
}
```

## Tool warnings

- Ghidra may mis-type stack locals and calling conventions (thiscall vs fastcall vs unaff_ESI).
- Trust machine code over decompiler for signedness/widths when they disagree.
