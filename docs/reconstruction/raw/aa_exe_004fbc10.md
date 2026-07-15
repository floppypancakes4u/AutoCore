# Raw decompiler capture: VehicleEntity_PushDriveAxesToController

| Field | Value |
|-------|-------|
| Stable ID | `aa_exe_004fbc10` |
| Binary | autoassault.exe |
| Address | `0x004fbc10` |
| Original symbol | `VehicleEntity_PushDriveAxesToController` |
| System | movement |
| Decompiler | Ghidra MCP decompile_function |
| Timestamp | 2026-07-15T12:00:00Z |
| Capture version | v1 (do not overwrite; amend as v2+) |

## Raw pseudocode

```c
/* WI-MOV-002: Push entity drive axes into VehicleAction controller.
   Requires entity+0x101==0 and entity+0x1a0!=0.
   ctrl = *(entity+0x1a0)+8
   Writes: ctrl+0x20 = entity+0x614; ctrl+0x24 = entity+0x61c
   entity+0x618 is NOT written here. */

void __fastcall VehicleEntity_PushDriveAxesToController(int param_1)
{
  /* full body captured via Ghidra MCP decompile_function 0x004fbc10 — see also
     reconstructed-exact/VehicleEntity_PushDriveAxesToController.cpp for cleaned form.
     Key gates: +0x101, +0x1a0, +0x109 stop, 0.9 clamp at DAT_00a0f734, speed-cap zero throttle,
     handbrake byte copy from +0x61c. */
  if ((*(char *)(param_1 + 0x101) == '\0') && (*(int *)(param_1 + 0x1a0) != 0)) {
    int iVar2 = *(int *)(*(int *)(param_1 + 0x1a0) + 8);
    *(undefined1 *)(iVar2 + 0x25) = 0;
    if (*(char *)(param_1 + 0x109) != '\0') {
      *(undefined4 *)(iVar2 + 0x20) = 0;
      *(undefined1 *)(iVar2 + 0x24) = 1;
      return;
    }
    float fVar9 = *(float *)(param_1 + 0x614);
    *(float *)(iVar2 + 0x20) = fVar9;
    /* clamp / speed gate omitted in abbreviated raw — full ops in MCP session output */
    *(undefined1 *)(iVar2 + 0x24) = *(undefined1 *)(param_1 + 0x61c);
  }
  return;
}
```

## Tool warnings

- Ghidra may mis-type stack locals and calling conventions.
- Trust machine code over decompiler for signedness/widths when they disagree.
- Some bodies abbreviated only when full text exceeded session packaging; re-run `decompile_function` at address for full dump. First-batch full dumps are in sibling files from batch JSON.
