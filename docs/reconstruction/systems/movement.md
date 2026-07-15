# System: Vehicle movement / network pose

**ID:** SYS-MOVEMENT  
**Priority:** 2  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15

## Scope

Local and ghost drive inputs, controller push, and network pose soft/hard apply.

## Known entry points

| Symbol | Address | Role |
|--------|---------|------|
| Vehicle_setDrivingInputs | 0x00504c70 | Write throttle/steer/handbrake; push; optional activate; pose apply |
| VehicleEntity_PushDriveAxesToController | 0x004fbc10 | entity+0x614/0x61c → VehicleAction controller |
| FUN_0053eec0 (NetworkPoseApply) | 0x0053eec0 | Soft buffer vs hard entity write; teleport if error > 15 |
| VehicleEntity_SetLongitudinalInput | 0x004f5650 | Set throttle axis |
| VehicleEntity_SetSteerInput | 0x004f5620 | Set steer axis |

## Behavioral flow

```
setDrivingInputs(this, pos*, rot*, vel*, angVel*, throttle, steering, handbrake, skipActivate, integrateDt)
  if vehicle+8:
    optional scale/type gate
    +0x614 = throttle; +0x618 = steer; +0x61c = handbrake
    PushDriveAxesToController()
    if !skipActivate && +0x1a0==0: maybe Vehicle_ActivateEnterWorld
    FUN_0053eec0(pos, rot, vel, angVel, integrateDt)
```

### Soft vs hard pose (FUN_0053eec0)

- Soft when graphics exists but physics not fully ready: `(+0x40==0) || (physics+8==0)`.
- Soft: buffer 0x40 at vehicle[10]; teleport+impulse if |pos-current| > **15.0** (`DAT_009d000c`); integrate if dt≠0 via FUN_0053eb90.
- Hard: write entity +0x84 pos / +0x94 rot if |pos| large enough.

## State owners

- Entity drive axes +0x614/+0x618/+0x61c
- VehicleAction at +0x1a0 → controller +8 fields +0x20 throttle, +0x24 handbrake
- Soft pose buffer pointer at this+0x28 (param_1[10])

## Evidence

- Ghidra decompile (this pass)
- `docs/MOTION_CLIENT_RE.md` live PathAHook facts

## Confidence

- Axis write + push: **high / confirmed** (matches prior RE)
- Soft threshold 15.0: **high**
- Full Havok between packs: **probable**

## Residuals (not eligible high-pri — see WORK_QUEUE Residual table)

| ID | Residual | Class |
|----|----------|-------|
| WQ-MOV-r1 | Ghost +0xBC ms / integrateDt source annotation (MOTION_CLIENT_RE open item) | optional depth |
| WQ-RT-01 / UF-002 | Runtime dual-run of drive/pose | blocked |
