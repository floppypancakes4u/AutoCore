# Review: Client_RecvNpcMissionDialog

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw decompile `raw/aa_exe_00815070.md`
- Dispatch map `raw/aa_exe_00815710.md` case 0x206d
- AutoCore `NpcMissionDialogPacket.cs`
- Prior notes claiming EMSG MissionDialog == 0x206C (MISSION_DIALOG_CLIENT_ANALYSIS.md)

## Falsification attempts against raw

1. **Is 0x206D actually MissionDialog_Response (C2S), not S2C?**  
   - EMSG string table maps index 0x6D → `EMSG_Sector_MissionDialog_Response`.  
   - **Counter-evidence from raw:** `Client_PacketDispatch` installs **recv** handler `Client_RecvNpcMissionDialog` for case **0x206d**. C2S response is prepared as **0x206e** at dialog+0x650 (`Client_NpcDialog_PrepareResponseOpcode`).  
   - **Verdict:** Dispatch + prepare opcode win over EMSG string naming. EMSG names are off-by-one relative to modern symbolization; do not flip 0x206D/0x206E without re-proving dispatch.

2. **Entry stride 40 / mission id at +0x20?**  
   - Raw: loop `puVar2 += 10` (10 dwords = 40), mission id `puVar2[8]` with cursor starting at packet base → first id at +0x20. Item coids `puVar2+10` → +0x28.  
   - Matches `NpcMissionDialogPacket` FirstMissionOffset=32, EntryStride=40.  
   - **Verdict:** confirmed.

3. **Does client filter completed missions before UI?**  
   - Raw: hash lookup for mission def + item slots only; no completed-mission check before `Client_ShowNpcMissionDialogUI`.  
   - **Verdict:** server must gate offers (consistent with missionWork.md).

4. **Count is full i32?**  
   - Raw compares against `(uint)*(byte*)(unaff_EBX+6)` — low byte only.  
   - **Risk:** server sending count > 255 would truncate client-side.  
   - **Verdict:** document as low-byte; AutoCore counts are small.

## Verdict

Accept static reconstruction for opcode, layout, and open-UI call. Runtime blocked UF-002.
