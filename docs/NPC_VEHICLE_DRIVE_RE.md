# NPC vehicle driving — client RE (2026-07-13)

## Question

Why do server-driven path NPCs look like they **lerp** / **circle** / **don’t turn wheels**?

## Answer (Ghidra)

Retail client AI does **not** lerp vehicle pose. It sets **drive axes** and lets **Havok + VehicleAction** move the chassis and spin wheels.

### Core client pipeline

| Piece | Address | Role |
|-------|---------|------|
| `CVOGHBAIDriver::DoLogic` | `005d7750` | Idle / engage / combat; path via `ReturnToNormalLocation`; combat pursue only if that returns false; **always** `FireWeapons` |
| `ReturnToNormalLocation` | `005d6e80` | Active path (`waypoint+0x52`) keeps steering to waypoint → **no pursue** |
| `CVOGVehicle::MoveToTarget3DPoint` | **`004fc650`** | **Real “drive”**: from chassis orient + target point at vehicle `+0x190..`, writes **throttle `+0x614`**, **steer `+0x618`**, **sharp `+0x61c`**, then `FUN_004fbc10` |
| `Vehicle_setDrivingInputs` | `00504c70` | Network entry: same thr/steer/sharp + `FUN_0053eec0` pose apply |
| `FUN_004fbc10` | `004fbc10` | Push thr into **VehicleAction** (`vehicle+0x1a0`); **no-op if action null** |
| `VehicleAction_applyAction` | `00598650` | Per-frame Havok vehicle: steer ramp, **`calcWheelTorque`**, aerodynamics |
| `FUN_0053eec0` | `0053eec0` | Soft network buffer if physics not fully ready; if fully ready, **hard-writes** pos/rot and relies on thr/steer + Havok between packs |
| Path heartbeat teleport | `005ce990` | Client-only path step / teleport helper when local AI owns path |

### MoveToTarget3DPoint (drive math)

```
toTarget = aim - position
forward / right from chassis rotation
steer  ∝ lateral (right · toTarget)   → vehicle+0x618
thr    ∝ forward alignment / distance → vehicle+0x614
sharp  when fast + large heading error → vehicle+0x61c
FUN_004fbc10()  // into VehicleAction
```

**No position lerp in this function.** Wheels turn because `calcWheelTorque` runs on the action with thr/steer.

### Implications for AutoCore

1. Server **cannot call** client functions; it must **pack** thr/steer/vel/angVel on the ghost.
2. Deriving steer only from **pose dYaw** fails when pose already faces the path (steer ≈ 0 → **wheels look locked**).
3. **Hard path XZ toward waypoint** without thr/steer = **lateral lerp**.
4. Full car-kinematics without look-ahead **orbits** AcceptDistance nodes.
5. `VehicleAction` must exist on the client (owner activate / wheels) or thr never reaches Havok (`004fbc10` early-out).

### Server design (implemented)

| Layer | Behavior |
|-------|----------|
| `NpcPathFollower` | Waypoint index / AcceptDistance / reactions |
| `SoftNpcPathMotion` | Pure-pursuit look-ahead + face-limited motion; **`VehicleDriveInputs`** thr/steer |
| `Vehicle.ApplyServerMove` | Packs thr/steer on ghost (`Acceleration`/`Steering`) |
| Terrain | `SnapToTerrain` pure Y |
| Combat | Path COID → no combat lunge; fire only |

### Still open

- Ensure every foreign NPC gets **VehicleAction** (activate + wheelset) so thr reaches Havok.
- Ghost integrate `dt` (`ghost+0xBC`) non-zero for soft buffer path.
- Optional: pack sharp-turn if a dedicated wire nibble is confirmed (do **not** set Handbrake bit).

## Related

- `docs/MOTION_CLIENT_RE.md` — pose soft/hard apply  
- `docs/nullWheels.md` — owner/wheel activate race  
- `docs/NPC.md` §15.7 path/terrain  
