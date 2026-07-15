# Raw decompiler capture: NetworkPoseApply_FUN_0053eec0

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_0053eec0` |
| Binary | autoassault.exe |
| Address | `0x0053eec0` |
| Original symbol | `NetworkPoseApply_FUN_0053eec0` |
| System | movement |
| Decompiler | Ghidra MCP decompile_function |
| Timestamp | 2026-07-15T12:00:00Z |
| Capture version | v1 (do not overwrite; amend as v2+) |

## Raw pseudocode

```c
/* FUN_0053eec0 — network pose apply soft vs hard.
   Soft when (graphics+0x40==0)||(graphics+8==0). Teleport if dist > DAT_009d000c (15.0f).
   Hard writes entity +0x84 pos / +0x94 rot. */

void __thiscall
FUN_0053eec0(int *param_1,float *param_2,undefined4 *param_3,float *param_4,undefined4 *param_5,
            float param_6)
{
  int iVar5 = param_1[2];
  if (((iVar5 != 0) && (*(float *)(*(int *)(iVar5 + 0x3c) + 0x2c) != 0.0)) &&
     (g_flOne / *(float *)(*(int *)(iVar5 + 0x3c) + 0x2c) != 0.0)) {
    bool bVar3 = (*(char *)(iVar5 + 0x40) == '\0') || (*(int *)(iVar5 + 8) == 0);
    if (bVar3) {
      /* soft buffer path: alloc/reuse param_1[10], copy pos/rot/vel/angVel,
         teleport if dist > 15, integrate if param_6 != 0 via FUN_0053eb90 */
      return;
    }
  }
  /* hard path: write +0x84 / +0x94 if |pos| large */
  return;
}
```

## Tool warnings

- Ghidra may mis-type stack locals and calling conventions.
- Trust machine code over decompiler for signedness/widths when they disagree.
- Some bodies abbreviated only when full text exceeded session packaging; re-run `decompile_function` at address for full dump. First-batch full dumps are in sibling files from batch JSON.
