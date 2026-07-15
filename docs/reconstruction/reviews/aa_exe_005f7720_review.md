# Review: VehicleNet_UnpackGhostVehicle (aa_exe_005f7720)

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical

## Inspected

- `raw/aa_exe_005f7720.md` (full large dump)
- `reconstructed-exact/VehicleNet_UnpackGhostVehicle.cpp` (BitStream flag pattern, combat apply, owner forms, drive)
- `tools/ghidra/vehicle_combat_pool.md` offsets/masks

## Concrete raw ↔ clean matches (3+)

1. **Heat apply:** `local_ed` → `vehicle+0x150` first in apply tail (raw 1473-1475).
2. **Shield before ShieldMax in apply:** `local_f5` clamp vs **current** +0x148 (1476-1488), then `local_fa` writes max and maybe lowers current (1490-1494). Dual dirty cur 40/50 + shield 80 + max 100 → shield **50** not 80.
3. **Stream read order differs from apply:** dirty section reads ed → fa → f5; apply is ed → f5 → fa. Clean documents both.
4. **Owner forms wired from initial path:** `Ghost_ReadOwnerBlockAndUnpack` (live call in `DAT_00d1798c` block) → flag → 64-bit TFID → key → form flag → `Ghost_UnpackOwnerForm` → `FUN_005f5ad0(1,1)`/`(1,0)`. Mechanical test: `assert_recon_call_site(..., "Ghost_UnpackOwnerForm")`.
5. **Drive:** `Ghost_ReadDriveDirty` (13×f32 + bytes) + `Ghost_ApplyDriveInputs` → `Vehicle_setDrivingInputs`.

## Skeptical review

1. **Hypothesis: unpack tests mask words 0x20000000 in stream.** Falsified for client unpack — sequential dirty flags; masks are pack/dirty-list side (plate). Clean documents this distinction.
2. **Hypothesis: combat apply always runs.** Falsified — requires bound vehicle and `vehicle+0x103==0`.
3. **Hypothesis: owner helper is dead code.** Falsified — `Ghost_ReadOwnerBlockAndUnpack` invokes `Ghost_UnpackOwnerForm` from the initial path; call-site gate fails if only defined.

## Residuals

- Full name table for every intermediate non-combat dirty flag (documentation depth).
- Exact health 18-bit packing helpers.

## Verdict

**Accept** high-pri ghost vertical (combat apply order, **wired** owner initial path, drive).
