# Review: Client_RecvGroupReactionCall

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw decompile `raw/aa_exe_008092a0.md`
- `GroupReactionCallPacket.cs` wire writer
- Vendor open path docs (OpenStore via 0x206C)

## Falsification attempts against raw

1. **Is this the bit-packed wire parser?**  
   - Raw reads `param_2+4` as count byte and fixed stride `0x28`. Wire writer emits BitStream (count 8 bits, reaction coid 19 bits, etc.).  
   - **Verdict:** this function is **post-decode apply**, not the bitstream unpacker. Claiming "wire layout is 0x28 stride" would be **false**. Reconstruction notes this explicitly.

2. **Does type!=0 always mean Variable?**  
   - Raw: non-zero at `param_2+0xc+i*0x28` → `CVOGMap_SetVariable`. Zero → reaction path.  
   - Matches server `LogicStateChangeType` Reaction=0 Variable=other.  
   - **Verdict:** consistent; unknown types >1 not differentiated in client.

3. **`entry+0x28` SingleClientOnly?**  
   - Raw uses `*(char*)(iVar1+0x28)` as a gate; when non-zero, compares activator TFID to local vehicle/character.  
   - Wire has SingleClientOnly flag after Global.  
   - **Risk:** decompiler might alias next-entry base (`i*0x28+0x28`). If true, gate semantics would be wrong.  
   - **Falsify later:** dump decoded buffer at break on 0x008092a0 with known SingleClientOnly reaction. Marked **probable**, not confirmed.

4. **Vendor OpenStore must be this path?**  
   - Server sends OpenStore success as GroupReactionCall; client Apply is `vt+0x2c0` on reaction object.  
   - This function is the only dispatch case for 0x206C.  
   - **Verdict:** OpenStore delivery path is this function; OpenStore **implementation** is the reaction Apply callee (not captured this pass).

## Verdict

Accept apply loop + Variable branch. Decoded field map for SingleClientOnly remains tentative. Need unpacker capture for full wire↔struct map.
