# Review: Client_MissionDialogHandleButton

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw `raw/aa_exe_008ae7c0.md`
- GiveMission / CompleteObjective prior reconstruction
- Server notes: Accepted=false on OK

## Falsification attempts against raw

1. **Does button index 0 mean Accept=true?**  
   - Raw stores `pDialogContext` into dialog+0x658; GiveMission runs when `pDialogContext == 0`.  
   - Live server: Accepted often false on OK.  
   - **Verdict:** first parameter is button index; value 0 is the primary OK path and maps to Accepted=false/0 on the wire. Do **not** gate server grants solely on Accepted==true.

2. **Turn-in always Completes locally without server?**  
   - Raw state1 + turnIn flag: `CVOGReaction_CompleteObjective(..., force=0)` before hide.  
   - Also fills 0x206E fields (mission id, reward tfid pair into +0x658 for turn-in).  
   - Server still processes 0x206E for authority/persistence.  
   - **Verdict:** local complete is real; server must not double-complete via 0x2070 (existing mission invariant).

3. **State 0 0x206F size 0x18 layout fully known?**  
   - Raw: opcode, two dwords from dialog+0x678/+0x67c, button byte.  
   - **Verdict:** size and opcode confirmed; field semantics of +0x678 pair **probable** only.

4. **Calling convention / which register is dialog?**  
   - Decompiler uses `in_EAX` heavily; formal params look wrong (`int *pDialogContext` used as button).  
   - **Risk:** reconstructed signature names are interpretive. Control flow on state values is trustworthy.

## Verdict

Accept state machine. Treat Accepted wire field as non-authoritative for grant/reject.
