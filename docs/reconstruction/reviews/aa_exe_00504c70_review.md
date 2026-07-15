# Review: Vehicle_setDrivingInputs (aa_exe_00504c70)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00504c70.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Vehicle_setDrivingInputs.cpp` |
| Downstream push | `VehicleEntity_PushDriveAxesToController` (`aa_exe_004fbc10`) |
| Downstream pose | `NetworkPoseApply_FUN_0053eec0` (`aa_exe_0053eec0`) |
| Motion notes | plate references `docs/MOTION_CLIENT_RE.md` |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Address | `0x00504c70` | `Vehicle_setDrivingInputs` |
| Gate | vehicle `+0x8 != 0` | early return if missing subsystem |
| Throttle | vehicle `+0x614` float | from param_6 |
| Steering | vehicle `+0x618` float | from param_7 |
| Handbrake | vehicle `+0x61c` **byte** | from param_8 |
| Controller missing | vehicle `+0x1a0 == 0` + `param_9 == 0` | may `Vehicle_ActivateEnterWorld` |
| Owner ptr | `+0xb0` via offset table | identity compare for activate |
| Pose apply | `FUN_0053eec0` / `NetworkPoseApply` | always after push when gate passes |

## Concrete raw ↔ clean matches (3+)

1. **Subsystem gate** — Both return immediately if `*(vehicle + 8) == 0`.
2. **Axis writes** — Both store throttle → `+0x614`, steer → `+0x618`, handbrake byte → `+0x61c`, then call `VehicleEntity_PushDriveAxesToController`.
3. **ActivateEnterWorld conditional** — Both: only when `param_9 == 0` **and** `*(vehicle + 0x1a0) == 0`, resolve owner at `+0xb0`, compare driver id vs self via vfuncs, then `Vehicle_ActivateEnterWorld`.
4. **Pose tail** — Both call network pose apply with remaining pose/velocity/dt parameters after axes push (`FUN_0053eec0` / clean `NetworkPoseApply_FUN_0053eec0`).
5. **Optional graphics branch** — Raw: if vfunc on `*(vehicle+8)+0x3c` returns `6`, `FUN_0053d970(0)` before axis writes; clean notes the branch (stubbed).

## Skeptical review (unit-specific falsification)

1. **Hypothesis: `+0x61c` is a float “sharp-turn” channel, not handbrake.**  
   **Falsified:** raw types param_8 as `undefined1` and stores with `*(undefined1 *)(param_1 + 0x61c) = param_8`. Push path (sibling unit) treats it as handbrake byte into controller `+0x24`. Clean names it `handbrake` correctly.

2. **Hypothesis: ActivateEnterWorld always runs on every setDrivingInputs.**  
   **Falsified:** requires `param_9 == 0` (skip flag clear) **and** missing controller at `+0x1a0`, **and** owner identity match. Network ghosts often skip activate via `param_9`.

3. **Hypothesis: pose apply can run without axis writes when +8 is null.**  
   **Falsified:** entire body is under `if (*(param_1 + 8) != 0)`.

4. **Hypothesis: this function is the ghost bitstream unpacker.**  
   **Falsified:** unpack is `VehicleNet_UnpackGhostVehicle` (`0x005f7720`); this unit **applies** already-decoded axes/pose to the entity.

## Residual uncertainty (this unit)

- Exact meaning of `param_9` skip flag across all callers (local input vs net).
- Graphics type `== 6` → `FUN_0053d970(0)` purpose (camera? FX?) not expanded.
- Clean leaves ActivateEnterWorld identity compare as comments rather than full vfunc calls — control condition preserved, call site detail partial.
- Runtime stick→axis differential (UF-002).

## Verdict

**Accept** — axis offsets, handbrake width, push+pose order, and activate gates match raw. Activate identity compare is summarized rather than fully typed in clean.
