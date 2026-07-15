# Raw decompiler capture: Client_RecvGiveCredits

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_0080cac0` |
| Binary | autoassault.exe |
| Address | `0x0080cac0` |
| Original symbol | `Client_RecvGiveCredits` |
| System | progression |
| Decompiler | Ghidra MCP batch_decompile |
| Timestamp | 2026-07-15T20:00:00Z |
| Capture version | v1 |

## Notes

- S2C **`0x205E`** (`EMSG_Sector_GiveCredits`).
- **Additive** currency delta via `CVOGCharacter_AddCredits` → char money `+0x720` (int64).
- Wire: pad/reserved at `+0x04`, `int64` amount at `+0x08`.
- Positive amount plays `"credits"` UI sound; combat floater type **`4`** when vehicle present and gate `char+0xd6c == 0`.
- Contrast: `0x2017 CharacterLevel` sets **absolute** currency; do not double-count after mission complete paths that already applied credits.

## Raw pseudocode

```c

/* Client_RecvGiveCredits — S2C 0x205E (Packet_GiveCredits).
   
   Algorithm:
     pChar = game+0xe98 local character; abort if null
     CVOGCharacter_AddCredits(pChar, pPacket->llAmount @ +0x08)
     if amount > 0: play "credits" UI sound
     optional combat floater type 4; refresh money HUD panel
   
   Parameters (dispatch convention — often ESI=game, EDI=packet rather than stack):
     pGameClient, pPacket (Packet_GiveCredits*)
   Returns: void.
   
   Do not use for mission complete if CompleteObjective already ran (double-count). */

void __cdecl Client_RecvGiveCredits(void *pGameClient,Packet_GiveCredits *pPacket)

{
  int iVar1;
  int unaff_ESI;
  int unaff_EDI;
  char *pcVar2;
  undefined4 uVar3;
  undefined4 uVar4;
  undefined4 uVar5;
  undefined4 uVar6;
  undefined4 uVar7;
  undefined4 uVar8;
  undefined4 uVar9;
  undefined4 uStack_38;
  undefined4 uStack_34;
  undefined4 uStack_30;
  undefined4 uStack_2c;
  undefined4 uStack_28;
  undefined4 uStack_24;
  undefined4 uStack_20;
  undefined4 uStack_1c;
  undefined4 uStack_18;
  undefined1 uStack_10;
  undefined4 uStack_8;
  
                    /* S2C GiveCredits 0x205E entry — ESI=game client, EDI=packet */
  if (*(void **)(unaff_ESI + 0xe98) == (void *)0x0) {
    FUN_007a4480(0,"VOG_DEBUG_STOP");
    return;
  }
                    /* ADD money delta: packet+8 (lo) / +0xc (hi) → char+0x720 */
  CVOGCharacter_AddCredits(*(void **)(unaff_ESI + 0xe98),*(longlong *)(unaff_EDI + 8));
                    /* Skip floater/sound when amount is zero or negative high */
  if ((-1 < *(int *)(unaff_EDI + 0xc)) &&
     ((0 < *(int *)(unaff_EDI + 0xc) || (*(int *)(unaff_EDI + 8) != 0)))) {
    uVar9 = 0;
    uVar8 = 0x1e;
    uVar7 = 0;
    uVar6 = 0;
    uVar5 = 0xffffffff;
    uVar4 = 0xffffffff;
    uVar3 = 0;
    pcVar2 = "credits";
    Client_GetMissionCompleteAudioTable("credits",0,0xffffffff,0xffffffff,0,0,0x1e,0);
    Client_PlayNamedInterfaceSound(pcVar2,uVar3,uVar4,uVar5,uVar6,uVar7,uVar8,uVar9);
  }
  iVar1 = *(int *)(*(int *)(unaff_ESI + 0xe98) + 0x250);
  if (iVar1 != 0) {
    iVar1 = (**(code **)(*(int *)(*(int *)(*(int *)(iVar1 + 4) + 4) + 4 + iVar1) + 0x1c8))();
    if (*(int *)(*(int *)(unaff_ESI + 0xe98) + 0xd6c) == 0) {
      uStack_38 = DAT_00a1e840;
      uStack_34 = DAT_00a1e844;
      uStack_30 = DAT_00a1e848;
      uStack_2c = DAT_00a1e84c;
      iVar1 = iVar1 + *(int *)(*(int *)(iVar1 + 4) + 4);
      uStack_28 = *(undefined4 *)(iVar1 + 0x164);
      uStack_24 = *(undefined4 *)(iVar1 + 0x168);
      uStack_20 = *(undefined4 *)(iVar1 + 0x16c);
      uStack_1c = *(undefined4 *)(iVar1 + 0x170);
      uStack_18 = *(undefined4 *)(unaff_EDI + 8);
      uStack_8 = 4;
      uStack_10 = 0;
      Client_EnqueueCombatFloater_INFERRED(&uStack_38);
    }
    iVar1 = *(int *)(unaff_ESI + 0x1040);
    if ((iVar1 != 0) && (*(int *)(iVar1 + 0x50c) != 0)) {
      (**(code **)(**(int **)(iVar1 + 0x50c) + 0x448))();
    }
  }
  return;
}
```
