# Review: Client_SendMissionDialogResponse (FUN_008ab8f0)

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw `raw/aa_exe_008ab8f0.md`, `aa_exe_008abd70.md`, `aa_exe_008ae7c0.md`
- `MissionDialogResponsePacket.cs` Read layout
- Server `NpcInteractHandler.HandleMissionDialogResponse` comments on Accepted=false

## Falsification attempts against raw

1. **Is size really 0x20?**  
   - Raw: send `(param_1 + 0x194, 0x20, 0)`.  
   - `0x194 * 4 = 0x650` — matches prepare opcode write site.  
   - Server Read: missionId(4)+bool+pad3+pad4+TFID16 = 4+1+3+4+16 = 28 body + 4 opcode = 32 = 0x20.  
   - **Verdict:** confirmed.

2. **Does HandleButton send 0x206E itself?**  
   - HandleButton plate says "Sends C2S MissionDialogResponse via prepared dialog+0x650".  
   - Raw HandleButton: fills +0x654/+0x658/+0x660; sends only **0x206F** in state 0; no `Client_SendSectorPacket` with 0x206E.  
   - Raw FUN_008ab8f0: actual connection send of dialog+0x650.  
   - **Verdict:** plate comment overstates HandleButton; send is teardown FUN_008ab8f0. Reconstruction documents both.

3. **Could param_1[0x194] be something other than opcode?**  
   - Prepare sets `*(esi+0x650)=0x206e`. Send checks `param_1[0x194] != 0` then transmits that buffer.  
   - If opcode were cleared before send, response would be suppressed — plausible cancel path.  
   - **Verdict:** opcode field interpretation is solid.

4. **Ghidra reports no callers**  
   - Suggests vtable/indirect call. Risk: wrong function renamed as send.  
   - Counter: only place that bulk-sends dialog+0x650 size 0x20 after opcode 0x206E is planted.  
   - **Verdict:** accept as send site; confirm live with BP if available.

## Verdict

Accept. Highest-value UF-003 C2S closure for mission dialog.
