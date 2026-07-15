# Review: Client_NpcDialog_PrepareResponseOpcode

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw `raw/aa_exe_008abd70.md`
- Xref from `Client_ShowNpcMissionDialogUI` @ 0x00943a60

## Falsification attempts against raw

1. **Is 0x206E written every open?**  
   - First durable statements: `*(esi+0x670)=mission`; `*(esi+0x650)=0x206e`.  
   - Even when mission param is null, opcode still set.  
   - **Verdict:** opening/selecting mission primes response opcode.

2. **Could 0x206e be a UI enum not a net opcode?**  
   - Same constant used by send buffer at +0x650 size 0x20.  
   - Matches `GameOpcode.MissionDialogResponse = 0x206E`.  
   - **Verdict:** net opcode.

3. **UI chrome code required for protocol?**  
   - Majority of body is widget layout.  
   - **Verdict:** ignore for server parity; only +0x650/+0x670 matter for wire.

## Verdict

Accept.
