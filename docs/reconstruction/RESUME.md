# RESUME

**Timestamp:** 2026-07-15  
**Current system:** idle (high-pri static exhausted after skeptic repairs)  
**Last completed step:** Mechanical gate + artifact consistency pass (task-1); 27/27 tests green  

## Exact next action (optional residual only)

1. UF-001: further string xrefs / UI constructors for “open INC on death” if new symbols appear.
2. Optional: enumerate remaining non-combat `local_f*` dirty flags in ghost unpack.
3. UF-002: runtime differential only with user-approved Launcher.

## Evidence

- Ghost apply region from Ghidra decompile (local_ed/fa/f5 → +0x150/+0x148/+0x144)
- GiveXP raw floater stack build; type 3 @+0x30 size 0x34; enqueue live call-site
- Owner path: `Ghost_ReadOwnerBlockAndUnpack` under `DAT_00d1798c` → `Ghost_UnpackOwnerForm`
- experiments tests green (27)
